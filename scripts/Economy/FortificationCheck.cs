using System.Collections.Generic;
using Godot;

/// <summary>The one runnable check behind a province's walls: that what a lord built survives being
/// written to a save and read back. A castle costs seasons and a fortune in stone, and a field that
/// quietly fails to serialise loses both without a single error on screen.
///
/// Run it: Godot --headless --path . res://scene/checks/fortification-check.tscn</summary>
public partial class FortificationCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		var balance = new GameBalance();

		// A province part-way through raising a castle: one wall standing, a better one paid for.
		var province = new ProvinceEconomy
		{
			ProvinceName = "Kingsreach",
			Realm = "northern-watch", // taken from its old lord, which the file has to remember
			Gold = 500, Wood = 900, Stone = 1200, Iron = 300, Population = 1200,
			Fortification = "medium-fort",
			Building = "large-castle",
			BuildSeasonsLeft = 4,
		};
		province.Garrison["sword"] = 12;
		province.HappinessByYear = new List<float> { 70f, 64f }; // two years of a reign
		province.Armoury["bow"] = 30;

		// Through the real file, not a serializer called by hand: what matters is that a save on
		// disk carries it, and the options that decide so live inside SaveGame.
		// The realm's prices ride along in the same file: a market that forgets what the player did
		// to it is one he can launder a granary through by saving and reloading.
		var prices = new Dictionary<string, float> { ["grain"] = -0.4f };

		SaveGame.Write("Check", turn: 9, provinces: new() { province }, prices, Difficulty.Hard);
		SaveGame read = Newest();
		ProvinceEconomy back = read.Provinces[0];

		Is("the turn comes back", read.Turn, 9);
		Is("the wall standing comes back", back.Fortification, "medium-fort");
		Is("the wall being raised comes back", back.Building, "large-castle");
		Is("the seasons left come back", back.BuildSeasonsLeft, 4);
		Is("the stores come back", back.Stone, 1200);
		Is("the garrison comes back", back.Garrison.GetValueOrDefault("sword"), 12);
		Is("the armoury comes back", back.Armoury.GetValueOrDefault("bow"), 30);
		Is("a glutted market is still glutted", read.Prices.GetValueOrDefault("grain"), -0.4f);
		Is("the county comes back in the hands that took it", back.Realm, "northern-watch");
		// The chart cannot be worked out again later — the turns it is made of are gone — so if it
		// does not survive the file, the county's whole history is one save away from nothing.
		Is("and so do the years it remembers", back.HappinessByYear.Count, 2);
		Is("  with what they were worth", back.HappinessByYear[1], 64f);
		Is("and the campaign at the difficulty it was played at", read.Difficulty == Difficulty.Hard, true);
		// Out of the player's folder the moment it has been read: see SaveGame.Forget.
		SaveGame.Forget(read);

		// A province that has never built anything must read as open, not as null.
		SaveGame.Write("Check", turn: 1, provinces: new() { new ProvinceEconomy { ProvinceName = "Thornwatch" } },
			new Dictionary<string, float>(), Difficulty.Medium);
		SaveGame open = Newest();
		ProvinceEconomy openBack = open.Provinces[0];
		Is("an unbuilt province has no wall", openBack.Fortification, "");
		Is("an unbuilt province is raising nothing", openBack.Building, "");
		SaveGame.Forget(open);

		// And the turn is what finishes it: four seasons on, the new wall replaces the old.
		province.BuildSeasonsLeft = 1;
		EconomySimulation.RunTurn(province, new ProvinceDefinition { ProvinceName = "Kingsreach" },
			balance, Season.Spring);
		Is("the last season raises the wall", province.Fortification, "large-castle");
		Is("and clears the work in hand", province.Building, "");

		GD.Print(_failed == 0 ? "\nfortifications: all checks passed" : $"\nfortifications: {_failed} FAILED");
		GetTree().Quit(_failed);
	}

	/// <summary>The save just written, read back off disk.</summary>
	private static SaveGame Newest()
	{
		System.Collections.Generic.List<SaveGame> saves = SaveGame.List();
		saves.Sort((left, right) => right.SavedAtUnix.CompareTo(left.SavedAtUnix));
		return saves[0];
	}

	private void Is<T>(string what, T got, T wanted)
	{
		bool ok = Equals(got, wanted);
		if (!ok)
		{
			_failed++;
		}

		GD.Print($"{(ok ? "ok  " : "FAIL")} {what}{(ok ? "" : $" — got '{got}', wanted '{wanted}'")}");
	}
}
