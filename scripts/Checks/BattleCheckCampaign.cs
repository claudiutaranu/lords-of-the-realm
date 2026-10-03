using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>War through the turn: a realm of two counties, the companies left orphaned by a fallen
/// county, a hired band, and fields trampled by a march.</summary>
public partial class BattleCheck
{
	// --- scaffolding -------------------------------------------------------------------------------

	/// <summary>Two counties, two realms, nobody in either of them yet.</summary>
	private static TurnManager Realm(GameBalance b, out ProvinceEconomy crown, out ProvinceEconomy watch)
	{
		var turns = new TurnManager(b,
			new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string>
			{
				["Kingsreach"] = "royal-crown",
				["Valmere"] = "northern-watch",
			},
			"royal-crown", Difficulty.Medium);

		crown = turns.AnyProvince("Kingsreach");
		watch = turns.AnyProvince("Valmere");
		// Counties open untaxed, as in the original: the war is paid for out of the chest.
		crown.Gold = 20000;
		watch.Gold = 20000;
		crown.Armies.Clear();
		watch.Armies.Clear();
		watch.Castle.Clear();
		return turns;
	}

	/// <summary>What happens to a lord's other companies when the county that raised them falls.
	/// They are not in the battle — they are three counties away — and a campaign that killed them
	/// off with their home would make taking one seat worth more than beating an army.</summary>
	private void TheOrphans(GameBalance b)
	{
		// A lord with two counties, so that losing one is not losing everything.
		var turns = new TurnManager(b,
			new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere"), Definition("Frostgate") },
			new Dictionary<string, string>
			{
				["Kingsreach"] = "royal-crown",
				["Valmere"] = "northern-watch",
				["Frostgate"] = "northern-watch",
			},
			"royal-crown", Difficulty.Medium);

		ProvinceEconomy crown = turns.AnyProvince("Kingsreach");
		ProvinceEconomy watch = turns.AnyProvince("Valmere");
		crown.Armies.Clear();
		watch.Armies.Clear();
		watch.Castle.Clear();
		turns.AnyProvince("Frostgate").Armies.Clear();

		// Two of the Watch's companies: one standing at home, one off in our country.
		watch.Muster("peasant", 30);
		FieldArmy abroad = watch.Raise(b.MarchReach);
		abroad.Men["spear"] = 25;
		abroad.County = "Kingsreach";

		crown.Muster("sword", 200, b.MarchReach);
		turns.Attack(crown.Armies[0], "Valmere", new Vector2(900, 200), walls: false);

		Is("the seat falls", turns.AnyProvince("Valmere").Realm, "royal-crown");
		Is("  and the men who were standing on it are gone with it",
			turns.AnyProvince("Valmere").Armies.Count, 0);
		Is("  but the company that was away is still in the field", abroad.Strength, 25);
		Is("  on the books of another of its own lord's counties", abroad.Home, "Frostgate");
		Is("  and it is still his", turns.RealmOf(abroad), "northern-watch");
	}

	/// <summary>A hired band keeps its own banner, even at the gate it holds beside the county's own
	/// men — and the dead of that day still come off both of them, not off a roll nobody stands in.</summary>
	private void TheHiredBand(GameBalance b)
	{
		var turns = new TurnManager(b,
			new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown", ["Valmere"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);
		ProvinceEconomy crown = turns.AnyProvince("Kingsreach");
		ProvinceEconomy watch = turns.AnyProvince("Valmere");
		crown.Armies.Clear();
		watch.Armies.Clear();
		watch.Castle.Clear();

		FieldArmy own = watch.Raise(b.MarchReach);
		own.Men["spear"] = 100;
		FieldArmy band = watch.Raise(b.MarchReach);
		band.Men["swiss"] = 100;
		Is("a hired band is never joined to the county's own men", turns.Merge(own, band), false);
		Is("  nor they to it", turns.Merge(band, own), false);

		crown.Muster("sword", 80, b.MarchReach);
		Battle.Result day = turns.Attack(crown.Armies[0], "Valmere", new Vector2(900, 200), walls: false);
		Is("  it holds the gate beside them under its own banner", watch.Armies.Count, 2);
		Is("  and the day's dead come off the two of them",
			200 - own.Strength - band.Strength, day.DefenderFell);
		Is("  and some of them off the band", band.Strength < 100 || day.DefenderFell == 0, true);
	}

	/// <summary>An army over another lord's land treads his corn and scatters his herd, a field once
	/// a season however many companies cross it, and a mine it halts on is shut three seasons. The
	/// player hears what a rival's march cost him.</summary>
	private void Trampled(GameBalance b)
	{
		var turns = new TurnManager(b,
			new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown", ["Valmere"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);
		// Valmere's field 0 lies at x 10, field 1 at x 20, its mine at x 30; Kingsreach's field 0 at x 40.
		turns.Survey((_, _) => new List<(Vector2, float)>(), _ => "", new Dictionary<string, Vector2>(), 38f,
			pixel => pixel.X switch
			{
				10 => ("Valmere", 0),
				20 => ("Valmere", 1),
				40 => ("Kingsreach", 0),
				_ => ("", -1),
			},
			pixel => pixel.X == 30 ? ("Valmere", Labour.Iron) : ("", ""));

		ProvinceEconomy watch = turns.AnyProvince("Valmere");
		watch.Fields[0] = FieldUse.Grain;
		watch.Fields[1] = FieldUse.Pasture;
		int grain = watch.FieldsUnder(FieldUse.Grain);
		int pastures = watch.FieldsUnder(FieldUse.Pasture);
		watch.StandingCrop = 100 * grain;
		watch.Cattle = 10 * pastures;

		FieldArmy ours = turns.AnyProvince("Kingsreach").Raise(b.MarchReach);
		ours.Men["spear"] = 50;
		var road = new List<Vector2> { new(5, 0), new(10, 0), new(20, 0), new(30, 0) };
		Is("a march is cut at the first of his fields on the way", turns.FirstSpoil(ours, road), 1);
		turns.Trample(ours, road.GetRange(0, 2));
		Is("  which treads that field's corn", watch.StandingCrop, 100 * (grain - 1));
		Is("  and lays the field itself waste", watch.Fields[0], FieldUse.Waste);
		Is("  and that is the company's season", ours.MarchLeft, 0f);
		Is("  one field a season: the next on the road is not its to spoil", turns.FirstSpoil(ours, road), -1);
		turns.Trample(ours, road.GetRange(0, 3));
		Is("  so the pasture beyond keeps its herd", watch.Cattle, 10 * pastures);

		FieldArmy second = turns.AnyProvince("Kingsreach").Raise(b.MarchReach);
		second.Men["spear"] = 50;
		Is("a second company passes the wasted field by", turns.FirstSpoil(second, road), 2);
		turns.Trample(second, road.GetRange(0, 3));
		Is("  and scatters the herd on the next", watch.Cattle, 10 * (pastures - 1));

		FieldArmy third = turns.AnyProvince("Kingsreach").Raise(b.MarchReach);
		third.Men["spear"] = 50;
		turns.Trample(third, road);
		Is("a company halted on his mine shuts it", watch.Occupied.GetValueOrDefault(Labour.Iron), b.OccupiedSeasons);
		Is("  so nobody is dealt to it", Labour.Ceiling(watch, Definition("Valmere"), b, Season.Summer, Labour.Iron), 0);

		watch.Grain = 1000;
		watch.GrainWorkers = 500;
		Husbandry.WorkTheFields(watch, Season.Winter, new TurnSummary());
		Is("the next sowing does not mend waste", watch.Fields[0], FieldUse.Waste);
		EconomySimulation.SetField(watch, 0, FieldUse.Reclaiming);
		watch.ReclaimWorkers = b.ReclaimPerSeason / 2;
		Is("  the reclaimers do: trodden ground is two seasons at half a field's most",
			Husbandry.SeasonsToReclaim(watch, b), 2);
		watch.ReclaimWorkers = b.ReclaimPerSeason;
		Husbandry.Reclaim(watch, b);
		Is("  and one at its most", watch.Fields[0], FieldUse.Fallow);

		ProvinceEconomy crown = turns.AnyProvince("Kingsreach");
		crown.Fields[0] = FieldUse.Grain;
		crown.StandingCrop = 100 * crown.FieldsUnder(FieldUse.Grain);
		int before = crown.StandingCrop;
		turns.Trample(ours, new List<Vector2> { new(40, 0) });
		Is("his own men tread nothing of his", crown.StandingCrop, before);

		FieldArmy theirs = watch.Raise(b.MarchReach);
		theirs.Men["spear"] = 50;
		turns.Trample(theirs, new List<Vector2> { new(35, 0), new(40, 0) });
		Is("a rival over the player's grain treads it", crown.StandingCrop < before, true);
		Is("  and the player is told", turns.RivalNews.Any(item => item.Said.Id == "fields-trampled"), true);
	}
}
