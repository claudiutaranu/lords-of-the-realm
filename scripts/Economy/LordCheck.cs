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
		TheMuster(b);
		TheWalls();
		TheirWar();

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
			Mathf.FloorToInt(Definition("Ashenvale").InitialPopulation * b.MilitiaShare[(int)Difficulty.Medium]));

		int Militia(Difficulty skill) => new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown" }, "royal-crown", skill,
			new List<ProvinceDefinition> { Definition("Ashenvale") }).DefendersOf("Ashenvale").Men;
		Defenders town = free.DefendersOf("Ashenvale");
		Is("  and not only farmhands: the town's watch has bows", town.Field.GetValueOrDefault("bow") > 0, true);
		Is("  and spears", town.Field.GetValueOrDefault("spear") > 0, true);
		Is("  with the farmhands still the most of them",
			town.Field.GetValueOrDefault("peasant") > town.Field.GetValueOrDefault("bow") + town.Field.GetValueOrDefault("spear"), true);
		Is("  and more of them turn out the harder the game",
			Militia(Difficulty.Easy) < Militia(Difficulty.Medium) && Militia(Difficulty.Medium) < Militia(Difficulty.Hard), true);
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

	/// <summary>A rival raises his own men: a company out of what is in his armoury, more of them the
	/// harder he is, none from a county that has turned on him, and the surplus sent home when he has
	/// more out than his county carries.</summary>
	private void TheMuster(GameBalance b)
	{
		ProvinceDefinition def = Definition("Valmere");
		ProvinceEconomy county = County(def);
		county.Loyalty = 60f;
		county.Armoury["spear"] = 100;
		LordArms.Arm(county, b, Difficulty.Medium, _ => "north");
		Is("a lord with arms in the armoury raises a company", county.Armies.Count, 1);
		Is("  out of his own people", county.Population, def.InitialPopulation - county.FieldMen);
		Is("  and not all of them in one season",
			county.FieldMen <= Mathf.CeilToInt(def.InitialPopulation * b.LordLevyShare), true);

		Is("a hard lord keeps more men in the field than an easy one",
			LordArms.Room(County(def), b, Difficulty.Hard) > LordArms.Room(County(def), b, Difficulty.Easy), true);

		ProvinceEconomy sullen = County(def);
		sullen.Loyalty = b.LordLevyAbove - 1f;
		sullen.Armoury["spear"] = 100;
		LordArms.Arm(sullen, b, Difficulty.Hard, _ => "north");
		Is("a county that has turned on its lord gives him no sons", sullen.Armies.Count, 0);

		ProvinceEconomy swollen = County(def);
		swollen.Realm = "north";
		FieldArmy host = swollen.Raise(b.MarchReach);
		host.Men["spear"] = 400;
		int people = swollen.Population;
		LordArms.Arm(swollen, b, Difficulty.Medium, _ => "north");
		Is("more out than the county carries, and the surplus goes home",
			swollen.FieldMen, Mathf.FloorToInt(people * b.LordArmyShare[(int)Difficulty.Medium]));
		Is("  back to the fields it came from", swollen.Population, people + 400 - swollen.FieldMen);

		ProvinceEconomy away = County(def);
		away.Realm = "north";
		FieldArmy campaign = away.Raise(b.MarchReach);
		campaign.Men["spear"] = 400;
		campaign.County = "Southmoor";
		LordArms.Arm(away, b, Difficulty.Medium, county => county == "Southmoor" ? "crown" : "north");
		Is("  but never from men on campaign", away.FieldMen, 400);
	}

	/// <summary>A lord builds up his walls a rung at a time out of his own stores, no higher than the
	/// campaign lets him, holds back the timber for the next rung instead of selling it, and puts
	/// men on a wall once it stands.</summary>
	private void TheWalls()
	{
		var b = new GameBalance { LordBuildChance = new[] { 1f, 1f, 1f } };
		ProvinceDefinition def = Definition("Valmere");
		var dice = new RandomNumberGenerator { Seed = 1268 };

		ProvinceEconomy rich = County(def);
		rich.Wood = 1000;
		rich.Stone = 200;
		LordAI.TakeTurn(rich, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		Is("a lord with the timber orders the first palisade", rich.Building, "small-palisade");
		Is("  and pays for it on the order", rich.Wood <= 1000 - 200, true);

		ProvinceEconomy topped = County(def);
		topped.Fortification = "medium-fort";
		topped.Wood = 5000;
		topped.Stone = 5000;
		topped.Iron = 5000;
		LordAI.TakeTurn(topped, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Hard, dice, "medium-fort");
		Is("  but builds no higher than the campaign lets him", topped.Building, "");

		ProvinceEconomy unasked = County(def);
		unasked.Wood = 1000;
		LordAI.TakeTurn(unasked, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("  and without the dice to decide the day, never builds at all", unasked.Building, "");

		// Saving for it: the timber a palisade wants stays in the yard rather than going to market.
		var never = new GameBalance { LordBuildChance = new[] { 0f, 0f, 0f } };
		ProvinceEconomy saving = County(def);
		saving.Wood = 300;
		LordAI.TakeTurn(saving, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		ProvinceEconomy selling = County(def);
		selling.Wood = 300;
		LordAI.TakeTurn(selling, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a lord saving for a palisade keeps the timber for it", saving.Wood > selling.Wood, true);

		ProvinceEconomy manned = County(def);
		manned.Fortification = "small-palisade";
		manned.Raise(0f).Men["spear"] = 50;
		LordWalls.ManTheWalls(manned, b);
		Is("a wall that stands gets men on it", manned.CastleMen, b.LordWatch);
		Is("  out of the company at home", manned.FieldMen, 50 - b.LordWatch);
	}

	/// <summary>The rival marches, over ground handed to him the way the map hands it over, and takes
	/// what his difficulty lets him want. A straight road here instead of the map's: three villages
	/// in a row, a neutral one between his and the player's.</summary>
	private void TheirWar()
	{
		(TurnManager turns, FieldArmy host) Board(Difficulty skill, bool surveyed, int menInHost)
		{
			var b = new GameBalance { WorldEventChance = 0f, MercenaryChance = 0f };
			b.LordFirstMarch = new[] { 0, 0, 0 };
			var definitions = new List<ProvinceDefinition> { Definition("North"), Definition("South") };
			var middle = Definition("Middle");
			middle.InitialPopulation = 300; // a militia of forty-odd farmhands
			var realms = new Dictionary<string, string> { ["North"] = "north", ["South"] = "crown" };
			var board = new TurnManager(b, definitions, realms, "crown", skill, new List<ProvinceDefinition> { middle });
			var towns = new Dictionary<string, Vector2>
			{
				["North"] = new(100, 100),
				["Middle"] = new(300, 100),
				["South"] = new(500, 100),
			};
			if (surveyed)
			{
				board.Survey(Straight, pixel => pixel.X < 200 ? "North" : pixel.X < 400 ? "Middle" : "South",
					towns, 38f);
			}

			FieldArmy army = board.AnyProvince("North").Raise(b.MarchReach);
			army.Men["spear"] = menInHost;
			return (board, army);
		}

		(TurnManager war, FieldArmy _) = Board(Difficulty.Medium, surveyed: true, menInHost: 200);
		for (int season = 0; season < 3; season++)
		{
			war.AdvanceTurn();
		}

		Is("a lord marches on the empty country next to him and takes it", war.AnyProvince("Middle")?.Realm, "north");

		(TurnManager mapless, FieldArmy idle) = Board(Difficulty.Medium, surveyed: false, menInHost: 200);
		mapless.AdvanceTurn();
		Is("  and with no ground handed to him, nobody marches", idle.County, "North");

		(TurnManager gentle, FieldArmy _) = Board(Difficulty.Easy, surveyed: true, menInHost: 400);
		for (int season = 0; season < 8; season++)
		{
			gentle.AdvanceTurn();
		}

		Is("an easy lord takes the neutral county", gentle.AnyProvince("Middle")?.Realm, "north");
		Is("  and leaves the player's alone", gentle.AnyProvince("South")?.Realm, "crown");

		(TurnManager hard, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		Is("a lord with a county to his name has not fallen", hard.PlayerFallen, false);
		bool told = false;
		for (int season = 0; season < 8 && hard.AnyProvince("South")?.Realm == "crown"; season++)
		{
			hard.AdvanceTurn();
			told |= hard.News.Exists(item => item.Said.Id == "county-lost" && item.ProvinceName == "South");
		}

		Is("a hard lord comes for the player's county and takes it", hard.AnyProvince("South")?.Realm, "north");
		Is("  and the player is told", told, true);
		Is("  and with his only county gone, his reign is over", hard.PlayerFallen, true);

		// Defended this time, by thirty spears of his own standing at the gate: he is told what came,
		// what stood, and what it cost him, by kind — and no blank left in the line.
		(TurnManager held, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		held.AnyProvince("South").Raise(0f).Men["spear"] = 30;
		string report = "";
		for (int season = 0; season < 8 && report.Length == 0; season++)
		{
			held.AdvanceTurn();
			FiredEvent said = held.News.Find(item => item.ProvinceName == "South"
				&& (item.Said.Id == "county-lost" || item.Said.Id == "invaders-repelled" || item.Said.Id == "field-lost"));
			report = said?.Said.Text ?? "";
		}

		Is("the player hears how the fight at his gate went", report.Length > 0, true);
		Is("  with every number written in", report.Contains('{'), false);
		Is("  how many of his stood", report.Contains("our 30 "), true);
		Is("  and what he lost, by kind", report.Contains("spearmen"), true);
	}

	/// <summary>A road that runs straight, a step every twelve pixels, a pixel of march a pixel.</summary>
	private static List<(Vector2 At, float Spent)> Straight(Vector2 from, Vector2 to)
	{
		var road = new List<(Vector2, float)>();
		float far = from.DistanceTo(to);
		for (float along = 12f; along < far + 12f; along += 12f)
		{
			float step = Mathf.Min(along, far);
			road.Add((from.Lerp(to, step / Mathf.Max(far, 0.01f)), step));
		}

		return road;
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
