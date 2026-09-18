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
			Gold = 500, Wood = 900, Stone = 1200, Iron = 300, Population = 1200,
			Fortification = "medium-fort",
			Building = "large-castle",
			BuildSeasonsLeft = 4,
		};
		province.Garrison["sword"] = 12;
		province.Armoury["bow"] = 30;

		// Through the real file, not a serializer called by hand: what matters is that a save on
		// disk carries it, and the options that decide so live inside SaveGame.
		SaveGame.Write("Check", turn: 9, provinces: new() { province });
		SaveGame read = Newest();
		ProvinceEconomy back = read.Provinces[0];

		Is("the turn comes back", read.Turn, 9);
		Is("the wall standing comes back", back.Fortification, "medium-fort");
		Is("the wall being raised comes back", back.Building, "large-castle");
		Is("the seasons left come back", back.BuildSeasonsLeft, 4);
		Is("the stores come back", back.Stone, 1200);
		Is("the garrison comes back", back.Garrison.GetValueOrDefault("sword"), 12);
		Is("the armoury comes back", back.Armoury.GetValueOrDefault("bow"), 30);

		// A province that has never built anything must read as open, not as null.
		SaveGame.Write("Check", turn: 1, provinces: new() { new ProvinceEconomy { ProvinceName = "Thornwatch" } });
		ProvinceEconomy openBack = Newest().Provinces[0];
		Is("an unbuilt province has no wall", openBack.Fortification, "");
		Is("an unbuilt province is raising nothing", openBack.Building, "");

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
