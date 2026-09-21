using System.Collections.Generic;
using Godot;

/// <summary>The one runnable check behind the lords the player is not. Two things are worth pinning
/// down and neither of them shows on screen as wrong: that a rival county is actually being RUN —
/// the quiet failure here is an enemy whose numbers never move and whose difficulty setting does
/// nothing — and that what belongs to him stays his side of the line. A rival's granary counted into
/// the crown's total, or his rats reported as the player's news, is the kind of bug a player never
/// reports because he cannot tell it from a game that is simply hard.
///
/// Run it: Godot --headless --path . res://scene/checks/lord-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class LordCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		var b = new GameBalance();
		ProvinceDefinition def = Definition("Valmere");

		Hands(b, def);
		Bread(b, def);
		TheTax(b, def);
		TheCounter(b, def);
		TheLine(b);
		WordGetsAround(b);
		TheMarch(b);

		GD.Print(_failed == 0
			? "\nthe other lords: all checks passed"
			: $"\nthe other lords: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	/// <summary>Difficulty is competence, so the same county in three pairs of hands has to come out
	/// three different ways. If this ever passes trivially — every lord deploying identically — the
	/// setting on the briefing page has quietly become decoration.</summary>
	private void Hands(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy best = County(def);
		EconomySimulation.Deploy(best, def, b, Season.Spring);

		ProvinceEconomy good = County(def);
		LordAI.TakeTurn(good, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Hard);
		Is("a good lord works his county as hard as it can be worked", good.AllocatedWorkers, best.AllocatedWorkers);

		ProvinceEconomy poor = County(def);
		LordAI.TakeTurn(poor, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Easy);
		Is("a poor one leaves hands standing about", poor.AllocatedWorkers < best.AllocatedWorkers, true);

		// He is not short of people, he is short of anybody telling them where to be — which is a
		// different thing and has to stay a different thing, or the easy setting is just a smaller
		// county and the player can see that on the map.
		Is("  but he has as many people as anybody", poor.Population, best.Population);
	}

	private void Bread(GameBalance b, ProvinceDefinition def)
	{
		// A barn that can carry the county to the next harvest and then some: he feeds them well,
		// because bread is the cheapest goodwill in the game.
		ProvinceEconomy full = County(def);
		full.Grain = 4000;
		LordAI.TakeTurn(full, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a lord with a full barn feeds his people well", full.Ration, RationLevel.Double);

		// And one that cannot: the ration is the only lever he has after the harvest is in.
		ProvinceEconomy thin = County(def);
		thin.Grain = 40;
		LordAI.TakeTurn(thin, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("one who cannot cuts it", thin.Ration, RationLevel.Half);

		// And the barn in between, which is where a well-run county spends most of its life. Worth
		// pinning: bands worked out from one reserve are easy to write so that the middle one can
		// never be reached, and nothing on screen would ever say so.
		ProvinceEconomy steady = County(def);
		steady.Grain = 200;
		LordAI.TakeTurn(steady, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("and the barn in between gets the ordinary ration", steady.Ration, RationLevel.Normal);
	}

	private void TheTax(GameBalance b, ProvinceDefinition def)
	{
		// A county that has had about enough of its lord. The good one reads the county and eases
		// off; the poor one reads his treasury and keeps squeezing.
		ProvinceEconomy watched = County(def);
		watched.Loyalty = 30f;
		LordAI.TakeTurn(watched, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Hard);
		Is("a good lord eases the tax before the county rises", watched.Tax < b.FairTaxPercent, true);

		ProvinceEconomy squeezed = County(def);
		squeezed.Loyalty = 30f;
		LordAI.TakeTurn(squeezed, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Easy);
		Is("a poor one keeps asking full price", squeezed.Tax, b.FairTaxPercent);

		// And a county that plainly has room: he is not a charity either.
		ProvinceEconomy content = County(def);
		content.Loyalty = 90f;
		LordAI.TakeTurn(content, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Hard);
		Is("and takes more where it will be borne", content.Tax > b.FairTaxPercent, true);
	}

	private void TheCounter(GameBalance b, ProvinceDefinition def)
	{
		// Autumn: every barn in the realm is full and wheat is worth about seven tenths of what it
		// is worth in an ordinary year. A lord who sells into that is giving his harvest away.
		ProvinceEconomy patient = County(def);
		patient.Grain = 6000;
		LordAI.TakeTurn(patient, def, b, Counter(b, Season.Autumn), Season.Autumn, Difficulty.Hard);
		Is("a good lord does not sell into the autumn glut", patient.Grain, 6000);

		ProvinceEconomy hasty = County(def);
		hasty.Grain = 6000;
		int purse = hasty.Gold;
		LordAI.TakeTurn(hasty, def, b, Counter(b, Season.Autumn), Season.Autumn, Difficulty.Easy);
		Is("a poor one takes what is offered", hasty.Grain < 6000, true);
		Is("  and has the silver for it", hasty.Gold > purse, true);

		// Spring, when the realm is hungry and the same grain is worth half again as much.
		ProvinceEconomy waited = County(def);
		waited.Grain = 6000;
		var spring = Counter(b, Season.Spring);
		LordAI.TakeTurn(waited, def, b, spring, Season.Spring, Difficulty.Hard);
		Is("and sells when the market wants it", waited.Grain < 6000, true);

		// A lot, and not the whole barn: the tail of that order would be sold into the hole the front
		// of it dug, and the grain price the player trades on would come down with it.
		Is("  and a lot rather than the granary", 6000 - waited.Grain < 600, true);

		// A county short of bread buys it and does not haggle — the purse is the only ceiling.
		ProvinceEconomy hungry = County(def);
		hungry.Grain = 0;
		hungry.Gold = 5000;
		LordAI.TakeTurn(hungry, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Medium);
		Is("a lord short of bread buys it", hungry.Grain > 0, true);
		Is("  and pays for it", hungry.Gold < 5000, true);
	}

	/// <summary>Where the player's realm ends. Everything here is a line the UI reads through the
	/// TurnManager, and every one of them used to mean "the only provinces there are".</summary>
	private void TheLine(GameBalance b)
	{
		var definitions = new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") };
		var realms = new Dictionary<string, string>
		{
			["Kingsreach"] = "royal-crown",
			["Valmere"] = "northern-watch",
		};
		var turns = new TurnManager(b, definitions, realms, "royal-crown", Difficulty.Medium);

		ProvinceEconomy rival = turns.AnyProvince("Valmere");
		List<TurnSummary> told = turns.AdvanceTurn();

		Is("the turn reports only the player's counties", told.Count, 1);
		Is("  and it is his", told[0].ProvinceName, "Kingsreach");

		// A year of it, so the rival has had a spring to sow and an autumn to reap.
		for (int season = 0; season < 4; season++)
		{
			turns.AdvanceTurn();
		}

		Is("a rival's county takes its own turns", rival.Population != definitions[1].InitialPopulation, true);

		// Five seasons is a full year and the first of the next, so the record has two years in it.
		// Kept on the province because the turn it belongs to is gone by the time anybody asks.
		Is("and every county keeps the record of its years", rival.HappinessByYear.Count, 2);
		Is("but it is not the player's to walk into", turns.GetProvince("Valmere") == null, true);
		Is("  though it is his to look at", turns.AnyProvince("Valmere") != null, true);
		Is("and the crown counts only its own", turns.RealmStore("gold"), turns.GetProvince("Kingsreach").Gold);

		// The rule that keeps an unclaimed county exactly as the campaign drew it: it is not in here
		// at all, so there is no turn to forget to skip.
		Is("a county nobody holds is not run at all", turns.AnyProvince("Ashenvale") == null, true);
	}

	/// <summary>A lord's counties know what he is doing to his other counties. This is the one thing
	/// in the turn that reaches across provinces, so it is also the one the simulation cannot check
	/// on its own — and a grievance charged to the wrong county would have the advisor telling a lord
	/// to cut a tax he never levied here.</summary>
	private void WordGetsAround(GameBalance b)
	{
		var definitions = new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Redmoor") };
		var realms = new Dictionary<string, string>
		{
			["Kingsreach"] = "royal-crown",
			["Redmoor"] = "royal-crown",
		};
		var turns = new TurnManager(b, definitions, realms, "royal-crown", Difficulty.Medium);

		ProvinceEconomy squeezed = turns.GetProvince("Kingsreach");
		ProvinceEconomy quiet = turns.GetProvince("Redmoor");
		squeezed.Tax = b.MostTaxPercent;
		squeezed.Grain = 4000; // nothing here is about hunger
		quiet.Tax = b.FairTaxPercent;
		quiet.Grain = 4000;

		List<TurnSummary> told = turns.AdvanceTurn();
		TurnSummary next = told.Find(summary => summary.ProvinceName == "Redmoor");

		Is("a county taxed fairly still hears about its neighbour",
			next.LoyaltyFromNeighbours, EconomySimulation.TaxSpill(b.MostTaxPercent, b));
		Is("  and it is not charged to its own tax", next.LoyaltyFromTax, 0f);
		Is("  while the county being squeezed hears nothing from next door",
			told.Find(summary => summary.ProvinceName == "Kingsreach").LoyaltyFromNeighbours, 0f);
	}

	/// <summary>An army leaving home. The men ARE the county's roster, so the thing to pin down is
	/// that they are only ever in one place: a march that copied them instead of moving them would
	/// give a lord two armies out of one and cost him nothing.
	///
	/// Where the ground is passable and what it costs to cross is the map's arithmetic and is checked
	/// against the map; what is checked here is the ledger's half — men, budget, and whose county is
	/// being walked into.</summary>
	private void TheMarch(GameBalance b)
	{
		var definitions = new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Redmoor") };
		var realms = new Dictionary<string, string>
		{
			["Kingsreach"] = "royal-crown",
			["Redmoor"] = "royal-crown",
		};
		var turns = new TurnManager(b, definitions, realms, "royal-crown", Difficulty.Medium);

		ProvinceEconomy home = turns.GetProvince("Kingsreach");
		home.Garrison["spear"] = 60;

		Is("a county opens with a season's ground in hand", home.MarchLeft, b.MarchReach);
		Is("the men march", turns.March("Kingsreach", "Redmoor", new Vector2(400, 300), 200f), true);
		Is("  and they are gone from where they were", home.Soldiers, 0);
		Is("  and standing where they went", turns.GetProvince("Redmoor").Soldiers, 60);
		Is("  on the ground they were sent to", turns.GetProvince("Redmoor").ArmyX, 400f);

		// What the season had left goes with the men and not with the county they walked out of.
		Is("the march costs the ground it covered",
			turns.GetProvince("Redmoor").MarchLeft, b.MarchReach - 200f);

		// And no further than the season allows, however good the road looks.
		Is("a march further than the season is refused",
			turns.March("Redmoor", "Kingsreach", new Vector2(100, 100), b.MarchReach), false);

		// Walking onto ground nobody holds takes it — there is nobody there to say otherwise, and
		// until an army stands on it an unclaimed county is not in the turn at all.
		var free = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown" }, "royal-crown",
			Difficulty.Medium, new List<ProvinceDefinition> { Definition("Ashenvale") });

		free.GetProvince("Kingsreach").Garrison["spear"] = 40;
		Is("an unheld county is nobody's until somebody walks in", free.AnyProvince("Ashenvale") == null, true);
		Is("walking in takes it", free.March("Kingsreach", "Ashenvale", new Vector2(500, 400), 150f), true);
		Is("  and it answers to the lord who took it", free.GetProvince("Ashenvale").Realm, "royal-crown");
		Is("  with his men standing on it", free.GetProvince("Ashenvale").Soldiers, 40);
		Is("  and it takes its turns from now on", free.AdvanceTurn().Count, 2);

		// And an empty county cannot march: there is nobody in it to go.
		Is("a county with no men marches nowhere",
			free.March("Kingsreach", "Ashenvale", new Vector2(500, 400), 10f), false);

		// Another lord's ground is not walked onto. There is no way to fight for it yet, and ground
		// that changes hands for nothing would be the fastest way to win the campaign.
		var rival = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown", ["Valmere"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);

		rival.GetProvince("Kingsreach").Garrison["spear"] = 80;
		Is("a rival's county is not walked into",
			rival.March("Kingsreach", "Valmere", new Vector2(900, 200), 100f), false);
		Is("  and his men are still his", rival.AnyProvince("Valmere").Realm, "northern-watch");
		Is("  while ours are still at home", rival.GetProvince("Kingsreach").Soldiers, 80);

		// Ground inside a county's own borders: the men walk, and nothing changes hands.
		ProvinceEconomy walking = rival.GetProvince("Kingsreach");
		float had = walking.MarchLeft;
		Is("men can walk their own county", rival.March("Kingsreach", "Kingsreach", new Vector2(370, 500), 60f), true);
		Is("  and it still costs them ground", walking.MarchLeft, had - 60f);
		Is("  and nobody has taken anything", walking.Realm, "royal-crown");
	}

	// --- scaffolding -------------------------------------------------------------------------------

	private static ProvinceDefinition Definition(string name) => new()
	{
		ProvinceName = name,
		InitialPopulation = 850,
		Fields = 9,
		InitialGrainFields = 4,
		InitialPastureFields = 2,
		InitialGrain = 400,
		InitialCattle = 40,
	};

	private static ProvinceEconomy County(ProvinceDefinition def) => ProvinceEconomy.FromDefinition(def);

	private static Market Counter(GameBalance b, Season season)
	{
		var market = new Market(b);
		market.Turned(season);
		return market;
	}

	private void Is<T>(string what, T got, T wanted)
	{
		if (EqualityComparer<T>.Default.Equals(got, wanted))
		{
			GD.Print($"ok	{what}");
			return;
		}

		_failed++;
		GD.PrintErr($"FAIL {what}: got {got}, expected {wanted}");
	}
}
