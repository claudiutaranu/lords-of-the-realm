using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>A campaign through the file and back into a turn built fresh, the way the load screen
/// does it: the country a lord took from nobody is still his, the old whole-percent labour is read
/// up once and never again, and a file with no counties in it is no save at all.</summary>
public partial class EconomyCheck
{
	private void Saves(GameBalance b)
	{
		TurnManager turns = Wilderness(b);
		ProvinceEconomy home = turns.GetProvince("Home");
		FieldArmy taker = home.Raise();
		taker.Men["spear"] = 40;
		Is("the empty country is taken", turns.Claim(taker, "Wild", new Vector2(300f, 0f)), true);
		ProvinceEconomy wild = turns.GetProvince("Wild");
		wild.Grain = 777;
		wild.Raise().Men["bow"] = 12;

		// Through the real file, as the Save button writes it.
		SaveGame written = SaveGame.Of("Check", turns);
		Is("the save is written", SaveGame.Write(written), true);
		SaveGame read = SaveGame.List().Find(save => save.SavedAtUnix == written.SavedAtUnix);
		SaveGame.Forget(read);

		TurnManager loaded = Wilderness(b);
		loaded.Restore(read);
		ProvinceEconomy wildBack = loaded.GetProvince("Wild");
		Is("a county taken from nobody is still held after a load", wildBack?.Realm, "royal-crown");
		Is("  with its barn as it was", wildBack?.Grain, 777);
		Is("  and the company raised in it", wildBack?.Armies.Sum(army => army.Strength), 12);
		Is("  and the company that took it still standing on it",
			loaded.GetProvince("Home").Armies.Any(army => army.County == "Wild" && army.Strength == 40), true);
		Is("  and it takes its turn", loaded.Provinces.Any(county => county.ProvinceName == "Wild"), true);
		Is("  and the purse it fell into is the realm's", wildBack?.Purse == loaded.GetProvince("Home").Purse, true);

		// Carried through the options screen rather than the file: the same snapshot, never written.
		TurnManager returned = Wilderness(b);
		returned.Restore(SaveGame.Of("Check", turns));
		Is("and through the options and back", returned.GetProvince("Wild")?.Grain, 777);

		Shares(b);
		Countyless();
	}

	/// <summary>A file of the days the shares were whole percents is read up into hundredths; a file
	/// written since is taken at its word, however small the share a lord left on a job.</summary>
	private void Shares(GameBalance b)
	{
		// The seat, divided as a lord who left grain and cattle a fraction of a percent each would
		// have it — or as a file from the whole-percent days wrote a sixty-forty split.
		SaveGame Divided(TurnManager turns, int version)
		{
			ProvinceEconomy seat = turns.GetProvince("Home");
			seat.Shares.Clear();
			seat.Shares[Labour.Grain] = 60;
			seat.Shares[Labour.Cattle] = 40;
			SaveGame save = SaveGame.Of("Check", turns);
			save.Version = version;
			return save;
		}

		TurnManager old = Wilderness(b);
		old.Restore(Divided(old, SaveGame.SharesInHundredthsFrom - 1));
		Is("an old file's whole percents are read as hundredths", old.GetProvince("Home").Shares[Labour.Grain], 6_000);

		TurnManager kept = Wilderness(b);
		kept.Restore(Divided(kept, new SaveGame().Version));
		kept.AdvanceTurn();
		Is("a lord's fraction of a percent stays a fraction, season after season",
			kept.GetProvince("Home").Shares[Labour.Grain], 60);
	}

	/// <summary>A file that parses but holds no counties is passed over like one that will not parse:
	/// let through, it opened the map and threw.</summary>
	private void Countyless()
	{
		long stamped = (long)Time.GetUnixTimeFromSystem() + 7;
		using (FileAccess file = FileAccess.Open($"user://saves/{stamped}.json", FileAccess.ModeFlags.Write))
		{
			file.StoreString($$"""{"Version":7,"CampaignName":"Check","Turn":2,"SavedAtUnix":{{stamped}},"Provinces":null}""");
		}

		bool listed = SaveGame.List().Any(save => save.SavedAtUnix == stamped);
		SaveGame.Forget(new SaveGame { SavedAtUnix = stamped });
		Is("a save with no counties is not offered", listed, false);
	}

	/// <summary>The crown's seat, a rival, and a county nobody holds.</summary>
	private static TurnManager Wilderness(GameBalance b) =>
		new(b,
			new List<ProvinceDefinition>
			{
				new() { ProvinceName = "Home", InitialPopulation = 600, InitialGrain = 1000 },
				new() { ProvinceName = "Rival", InitialPopulation = 600, InitialGrain = 1000 },
			},
			new Dictionary<string, string> { ["Home"] = "royal-crown", ["Rival"] = "northern-watch" },
			"royal-crown", Difficulty.Medium,
			new List<ProvinceDefinition> { new() { ProvinceName = "Wild", InitialPopulation = 300, InitialGrain = 200 } });
}
