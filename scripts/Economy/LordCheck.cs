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
		TheLarder(b, def);
		TheMarch(b);
		FiftyYears(b);

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
		ProvinceEconomy good = County(def);
		LordAI.TakeTurn(good, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Hard);

		// Measured against the fields he chose: a spring lord rests some ground, and the work there
		// is to do is the work on what he left sown.
		ProvinceEconomy best = good.Copy();
		EconomySimulation.Deploy(best, def, b, Season.Spring);
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

		// Spring, dear bread, and a barn with his reserve and one meal over — enough for him to set
		// the table double. He sold everything above the reserve and then fed the county double out
		// of the reserve itself: Valmere went from 360 sacks to 18 in its first season, bought them
		// back the next at any price, and starved in the twenty years after. What the county is about
		// to eat is not surplus.
		ProvinceEconomy feast = County(def);
		int need = Mathf.CeilToInt(feast.Population / b.PeoplePerGrain)
			+ Mathf.CeilToInt(feast.Soldiers / b.PeoplePerGrain * b.SoldierAppetite);
		int reserve = Mathf.CeilToInt(need * b.LordGrainSeasons[(int)Difficulty.Medium]);
		feast.Grain = reserve + need + 10;
		LordAI.TakeTurn(feast, def, b, Counter(b, Season.Spring), Season.Spring, Difficulty.Medium);
		int meal = Mathf.CeilToInt(feast.Population / b.PeoplePerGrain * b.RationFoodMultiplier[(int)feast.Ration])
			+ Mathf.CeilToInt(feast.Fed / b.PeoplePerGrain * b.SoldierAppetite);
		Is("a lord does not sell the bread his county is about to eat", feast.Grain >= reserve + meal, true);

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
		FieldArmy spears = home.Raise(b.MarchReach);
		spears.Men["spear"] = 60;

		Is("an army opens with a season's ground in hand", spears.MarchLeft, b.MarchReach);
		Is("the men march", turns.March(spears, "Redmoor", new Vector2(400, 300), 200f), true);
		Is("  and they are standing in the county they were sent to", spears.County, "Redmoor");
		Is("  on the ground they were sent to", spears.X, 400f);

		// The men are the company's, and the company is its own county's however far it walks: the
		// county that raised them is the one that goes on paying and feeding them.
		Is("  still on the roster of the county that feeds them", home.Soldiers, 60);
		Is("  and not on the roster of the one they are standing in",
			turns.GetProvince("Redmoor").Soldiers, 0);

		// What the season had left is the army's own and not the county's.
		Is("the march costs the ground it covered", spears.MarchLeft, b.MarchReach - 200f);

		// And no further than the season allows, however good the road looks.
		Is("a march further than the season is refused",
			turns.March(spears, "Kingsreach", new Vector2(100, 100), b.MarchReach), false);

		// A second company raised at home is a SECOND company: the barracks does not quietly pour
		// its intake into whatever is already standing in the field three counties away.
		FieldArmy bows = home.Raise(b.MarchReach);
		bows.Men["bow"] = 20;
		Is("a company raised at home is its own army", home.Armies.Count, 2);
		Is("  with the whole county behind both of them", home.FieldMen, 80);
		Is("  and its own legs", bows.MarchLeft, b.MarchReach);
		Is("one army's march is not the other's", turns.March(bows, "Redmoor", new Vector2(400, 300), 50f), true);
		Is("  and spends only its own ground", bows.MarchLeft, b.MarchReach - 50f);
		Is("  while the first army's is where the first march left it",
			spears.MarchLeft, b.MarchReach - 200f);

		// Standing in the same field is not being one army. That is an order the lord gives.
		Is("two companies in one county are still two", home.Armies.Count, 2);
		Is("joining them makes one", turns.Merge(spears, bows), true);
		Is("  with everybody in it", spears.Strength, 80);
		Is("  and one banner left standing", home.Armies.Count, 1);
		Is("  on the slower pair of legs", spears.MarchLeft, b.MarchReach - 200f);

		// Walking onto ground nobody holds does NOT take it any more: the county's own people stand
		// up for it, and men in the way have to be beaten rather than walked past.
		var free = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown" }, "royal-crown",
			Difficulty.Medium, new List<ProvinceDefinition> { Definition("Ashenvale") });

		FieldArmy walkers = free.GetProvince("Kingsreach").Raise(b.MarchReach);
		walkers.Men["spear"] = 40;
		Is("an unheld county is nobody's until somebody takes it", free.AnyProvince("Ashenvale") == null, true);
		Is("  and its own people are what stands in the way", free.DefendersOf("Ashenvale").Men,
			Mathf.FloorToInt(Definition("Ashenvale").InitialPopulation * b.MilitiaShare));
		Is("the men can still be walked onto it",
			free.March(walkers, "Ashenvale", new Vector2(500, 400), 150f), true);
		Is("  but it is nobody's still", free.AnyProvince("Ashenvale") == null, true);
		Is("  and they are standing on its ground", walkers.X, 500f);
		Is("  on the roster of the county that feeds them", free.GetProvince("Kingsreach").Soldiers, 40);

		// Beating them is what takes it, and it is the only thing that does.
		Is("beating them takes it", free.Claim(walkers, "Ashenvale", new Vector2(500, 400)), true);
		Is("  and it answers to the lord who took it", free.GetProvince("Ashenvale").Realm, "royal-crown");
		Is("  with his men standing on it", free.DefendersOf("Ashenvale").Men, 40);
		Is("  still fed by the county that raised them", free.GetProvince("Kingsreach").Soldiers, 40);
		Is("  drawing on the same purse as the rest of his realm",
			free.GetProvince("Ashenvale").Purse == free.GetProvince("Kingsreach").Purse, true);
		Is("  and it takes its turns from now on", free.AdvanceTurn().Count, 2);

		// And an empty company cannot march: there is nobody in it to go.
		FieldArmy nobody = free.GetProvince("Kingsreach").Raise(b.MarchReach);
		Is("a company with no men in it marches nowhere",
			free.March(nobody, "Ashenvale", new Vector2(500, 400), 10f), false);

		// Another lord's ground can be crossed. Crossing it is all it is — his county is his until
		// somebody takes its seat off him, and ground that changed hands for being walked over would
		// be the fastest way to win the campaign and the least interesting.
		var rival = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown", ["Valmere"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);

		FieldArmy crossing = rival.GetProvince("Kingsreach").Raise(b.MarchReach);
		crossing.Men["spear"] = 80;
		Is("a rival's border no longer stops an army",
			rival.March(crossing, "Valmere", new Vector2(900, 200), 100f), true);
		Is("  but crossing his county has not taken it", rival.AnyProvince("Valmere").Realm, "northern-watch");
		Is("  and his own county is none the worse for it", rival.AnyProvince("Valmere").Soldiers, 0);
		Is("  while our men are still ours to feed", rival.GetProvince("Kingsreach").Soldiers, 80);
		Is("  standing on his ground", crossing.X, 900f);

		// Ground inside a county's own borders: the men walk, and nothing changes hands.
		ProvinceEconomy walking = rival.GetProvince("Kingsreach");
		float had = crossing.MarchLeft;
		Is("men can walk their own county", rival.March(crossing, "Kingsreach", new Vector2(370, 500), 60f), true);
		Is("  and it still costs them ground", crossing.MarchLeft, had - 60f);
		Is("  and nobody has taken anything", walking.Realm, "royal-crown");

		// The walls and the field are two rosters. The county feeds, pays and resents both of them;
		// only one of them ever goes anywhere. Without the second, a lord could not leave men on his
		// own gate and march out with the rest — he could only choose — and every castle in the realm
		// would stand empty the season its county went to war.
		walking.Castle["spear"] = 20;
		Is("the gate watch is counted with the men the county keeps", walking.Soldiers, 100);
		Is("  but it is not what marches", walking.FieldMen, 80);
		walking.Armies.Clear();
		Is("a county with nobody but a gate watch has nothing to march",
			walking.Armies.Count, 0);
	}

	// --- scaffolding -------------------------------------------------------------------------------

	/// <summary>A lord left alone with a poor county for fifty years has to still have a county at
	/// the end of them. Every rival used to starve his own land to death without a blow struck: he
	/// never rested a field, ploughed up more than he could reap, sold his iron only once the barn
	/// was empty and kept a garrison his shrinking people would not stand. No weather and no plague
	/// here — only his own running of it, which is the part this check is about.</summary>
	private void FiftyYears(GameBalance b)
	{
		var def = new ProvinceDefinition
		{
			ProvinceName = "Valmere",
			InitialPopulation = 850,
			Fields = 9,
			InitialGrainFields = 4,
			InitialPastureFields = 2,
			InitialGrain = 360,
			InitialCattle = 50,
			GrainModifier = 0.85f,
			IronWorkerCapacity = 160,
			IronModifier = 1.4f,
			InitialFortification = "large-fort",
		};
		ProvinceEconomy county = County(def);
		county.Castle["spear"] = 60;
		EconomySimulation.Deploy(county, def, b, Season.Spring);
		var market = new Market(b);
		int hungry = 0;
		float goodwill = 0f;
		for (int turn = 0; turn < 200; turn++)
		{
			var season = (Season)(turn % 4);
			market.Turned(season);
			LordAI.TakeTurn(county, def, b, market, season, Difficulty.Medium);
			hungry += EconomySimulation.RunTurn(county, def, b, season).FoodShort > 0 ? 1 : 0;
			EconomySimulation.FitWorkforce(county);
			goodwill += county.Loyalty / 200f;
		}

		Is("fifty years of a poor county under a middling lord, and it is still a county",
			county.Population >= def.InitialPopulation / 2, true);
		// Over the fifty years and not on the last day of them: a county that outgrows its fields and
		// has a lean year is a county being run, not one being lost.
		Is("  that has not turned on him", goodwill > b.EmigrationBelow, true);
		Is("  and has gone hungry in fewer than one season in ten", hungry < 20, true);
	}

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
