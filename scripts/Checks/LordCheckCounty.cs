using System.Collections.Generic;
using Godot;

/// <summary>The lord at home: hands set to work, bread, the tax, the market counter, the reserve
/// line, word getting around, and the larder he keeps.</summary>
public partial class LordCheck
{
	/// <summary>Difficulty is competence, so the same county in three pairs of hands has to come out
	/// three different ways. If this ever passes trivially — every lord deploying identically — the
	/// setting on the briefing page has quietly become decoration.</summary>
	private void Hands(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy good = County(def);
		LordAI.TakeTurn(good, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Hard);

		// Measured against the fields he chose: a spring lord rests some ground, and the work there
		// is to do is the work on what he left sown.
		ProvinceEconomy best = good.Copy();
		Labour.FarmsFirst(best, def, b, Season.Spring);
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
		// A county souring on its lord, still short of walking out. The good one reads the county and
		// eases off; the poor one reads his treasury and keeps squeezing until it is at the edge.
		ProvinceEconomy watched = County(def);
		watched.Loyalty = 45f;
		LordAI.TakeTurn(watched, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Hard);
		Is("a good lord eases the tax before the county rises", watched.Tax < b.FairTaxPercent, true);

		ProvinceEconomy squeezed = County(def);
		squeezed.Loyalty = 45f;
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

		// Spring, dear bread, and a barn with his reserve and one meal over — enough for him to set
		// the table double. He sold everything above the reserve and then fed the county double out
		// of the reserve itself: Valmere went from 360 sacks to 18 in its first season, bought them
		// back the next at any price, and starved in the twenty years after. What the county is about
		// to eat is not surplus.
		ProvinceEconomy feast = County(def);
		int need = LordAI.Meal(feast, RationLevel.Normal);
		int reserve = Mathf.CeilToInt(need * b.LordGrainSeasons[(int)Difficulty.Medium]);
		feast.Grain = reserve + need + 10;
		LordAI.TakeTurn(feast, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Medium);
		// The meal as the table serves it, worked out here from the rule and not from LordAI.
		int meal = Livelihood.DivCeil(Mathf.Max(0, Livelihood.Portions(feast.Population + feast.FieldMen, feast.Ration)
			- (feast.Cattle * Livelihood.FedByDairy)), Livelihood.FedBySack);
		Is("a lord does not sell the bread his county is about to eat", feast.Grain >= reserve + meal, true);
		Is("  and reckons his meal the way the table serves it", LordAI.Meal(feast, feast.Ration), meal);

		// A county short of bread buys it and does not haggle — the purse is the only ceiling.
		ProvinceEconomy hungry = County(def);
		hungry.Grain = 0;
		hungry.Gold = 5000;
		LordAI.TakeTurn(hungry, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Medium);
		Is("a lord short of bread buys it", hungry.Grain > 0, true);
		Is("  and pays for it", hungry.Gold < 5000, true);

		// Short of bread with an empty purse and a yard full of iron. Valmere's fields feed about
		// three fifths of its people, and its mines are the richest on the island; its lord never
		// sold an ingot, and starved on top of five hundred of them. What the county makes and does
		// not eat is what it buys its bread with.
		ProvinceEconomy miner = County(def);
		miner.Grain = 0;
		miner.Gold = 0;
		miner.Iron = 1000;
		LordAI.TakeTurn(miner, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Medium);
		Is("a lord with no silver sells his iron for bread", miner.Grain > 0, true);
		Is("  and keeps some to work with", miner.Iron > 0, true);
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
		// And of its people, season by season — five turns run, five seasons written, each as it ended.
		PeopleSeason lastRecorded = rival.PeopleBySeason[^1];
		Is("  and of its people, a season each", rival.PeopleBySeason.Count, 5);
		Is("  the last of them as the county stands", lastRecorded.Population, rival.Population);
		Is("  with the births and deaths the season made", lastRecorded.Born, turns.LastSeason("Valmere").Born);
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

		// The original's realm term: every county of the lord pays for every county's rate.
		float realm = Livelihood.EmpireTerm(squeezed.Tax) + Livelihood.EmpireTerm(quiet.Tax);
		Is("a county taxed fairly still hears about its neighbour", next.LoyaltyFromNeighbours, realm);
		Is("  and it is not charged to its own tax", next.LoyaltyFromTax, (float)Livelihood.TaxTerm(quiet.Tax));
		Is("  and the county being squeezed pays the same realm's term",
			told.Find(summary => summary.ProvinceName == "Kingsreach").LoyaltyFromNeighbours, realm);
	}

	/// <summary>Bread behind his gate. A rival who never carries any up is a rival whose castle falls
	/// to anybody who sits down in front of it for three seasons — and that is not a hard opponent
	/// playing badly, it is a mechanic the computer does not know exists.</summary>
	private void TheLarder(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy keep = County(def);
		keep.Fortification = "medium-castle";
		keep.Grain = 2000;
		keep.Castle["spear"] = 20;
		LordAI.TakeTurn(keep, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a lord with walls keeps bread behind them",
			keep.CastleStores, Fortifications.Of("medium-castle").Stores);
		Is("  and no more than they hold",
			keep.CastleStores <= Fortifications.Of("medium-castle").Stores, true);

		// Bread he has not got, he does not carry up: the larder comes out of what is over and above
		// what his people are going to eat.
		ProvinceEconomy thin = County(def);
		thin.Fortification = "medium-castle";
		thin.Grain = 0;
		LordAI.TakeTurn(thin, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a lord with nothing in the barn carries nothing up", thin.CastleStores, 0);

		// And nothing goes in through a siege line, or a siege would never end.
		ProvinceEconomy shut = County(def);
		shut.Fortification = "medium-castle";
		shut.Grain = 2000;
		shut.BesiegedFrom = "Kingsreach";
		LordAI.TakeTurn(shut, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("and nothing gets in past the men outside", shut.CastleStores, 0);

		// An open village has nowhere to put it.
		ProvinceEconomy village = County(def);
		village.Grain = 2000;
		LordAI.TakeTurn(village, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a county with no walls has nowhere to put any", village.CastleStores, 0);
	}
}
