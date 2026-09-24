using System.Collections.Generic;
using Godot;

/// <summary>The one runnable check behind the province economy. Everything a player does for the
/// first hour of a campaign passes through these formulas, and none of it shows up on screen as
/// wrong — a harvest that is quietly a third short just looks like a hard year until the save is
/// already unwinnable.
///
/// Run it: Godot --headless --path . res://scene/checks/economy-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class EconomyCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		var b = new GameBalance();
		ProvinceDefinition def = Definition();

		Demands(b, def);
		Mending(b, def);
		Levy(b, def);
		LabourBar(b, def);
		Masons(b, def);
		Forge(b, def);
		OnePurse(b, def);
		TheArmy(b, def);
		TheLand(b, def);
		AsTheOriginal(b, def);
		Turning(b, def);
		Projection(b, def);
		TheYear(b);
		StoneOrIron();

		GD.Print(_failed == 0
			? "\nprovince economy: all checks passed"
			: $"\nprovince economy: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	// --- labour ----------------------------------------------------------------------------------

	private void Demands(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();

		// Four fields under grain: the sowing wants twelve hands a sack, five sacks to a man, ten sacks a
		// field; the herd wants a herdsman to three head, and takes twice that.
		Is("the sowing asks for the sowers of ten sacks a field", EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Winter), 96);
		Is("a herdsman to three head, and twice that helps", EconomySimulation.Demand(ResourceType.Cattle, p, def, b, Season.Spring), 2 * Husbandry.Herdsmen(p.Cattle));
		Is("the quarry takes all comers, as in the original", EconomySimulation.Demand(ResourceType.Stone, p, def, b, Season.Spring), Labour.Bottomless);

		// The pool and the figures drawn against it were rescaled together when the population
		// became the pool; the diggings are what catches it if only one of the two ever moves again.
		ProvinceEconomy digging = Province();
		digging.WoodWorkers = 200;
		int wood = digging.Wood;
		EconomySimulation.RunTurn(digging, def, b, Season.Spring);
		Is("two hundred at a practised woodpile are worth seventy-five a season", digging.Wood - wood, 75);

		// The same two hundred at a woodpile nobody has worked before bring in a fraction of it.
		ProvinceEconomy green = Province();
		green.WoodWorkers = 200;
		green.Efficiency[Labour.Wood] = b.SiteEfficiencyFloor;
		int greenWood = green.Wood;
		EconomySimulation.RunTurn(green, def, b, Season.Spring);
		Is("  and at a new one, what its practice allows", green.Wood - greenWood,
			Mathf.RoundToInt(75f * b.SiteEfficiencyFloor / 100f));

		Is("half the hands do half the work", EconomySimulation.Covered(30, 60), 0.5f);
		Is("more hands than work is still one job", EconomySimulation.Covered(600, 60), 1f);
		Is("no work is not short-handed", EconomySimulation.Covered(30, 0), 0f);

		// Hands on a job that cannot use them are wasted, and the waste is counted rather than
		// silently folded into the yield.
		p.StandingCrop = 2000; // a summer's crop two hundred tenders can keep
		p.GrainWorkers = 200;
		p.CattleWorkers = p.WoodWorkers = p.StoneWorkers = p.IronWorkers = 0;
		Is("hands past the work stand idle", EconomySimulation.Idle(p, def, b, Season.Summer), 1000 - 200);
	}

	// --- the farming year --------------------------------------------------------------------------

	private void OnePurse(GameBalance b, ProvinceDefinition def)
	{
		// Two counties of one realm spend out of the same purse: what is collected in one is there
		// to be spent in the other, which is the whole point of a treasury.
		var shared = new Treasury { Gold = 1000 };
		ProvinceEconomy here = Province();
		ProvinceEconomy there = Province();
		here.Purse = shared;
		there.Purse = shared;

		here.Gold -= 400;
		Is("what one county spends, the other is short", there.Gold, 600);
		there.Gold += 250;
		Is("and what one collects, the other can spend", here.Gold, 850);

		// Stores are not like that: grain rots where it was reaped.
		here.Grain = 100;
		there.Grain = 40;
		here.Grain -= 60;
		Is("a county's grain is its own", there.Grain, 40);

		// A projection reads the purse without spending it: the season played on a copy must not
		// take money out of the realm the lord is still standing in.
		ProvinceEconomy guess = here.Copy();
		guess.Gold -= 500;
		Is("a projection cannot spend the realm's money", here.Gold, 850);
		Is("  though it starts from what the realm has", guess.Gold, 350);
	}

	private void Masons(GameBalance b, ProvinceDefinition def)
	{
		// A wall nobody is building asks for nobody, and nobody standing on scaffolding that is not
		// there counts as idle.
		ProvinceEconomy quiet = Province();
		Is("no wall, no masons wanted", EconomySimulation.Masons(quiet), 0);

		// One priced at three seasons is three gangs' worth of work.
		ProvinceEconomy raising = Province();
		raising.Building = "palisade";
		raising.BuildLeft = 3 * b.MasonsPerBuildSeason;
		Is("a wall asks for what is left of it", EconomySimulation.Masons(raising), 3 * b.MasonsPerBuildSeason);

		// The nominal gang finishes it in the seasons it was quoted at.
		raising.BuildWorkers = b.MasonsPerBuildSeason;
		for (int season = 1; season <= 3; season++)
		{
			EconomySimulation.RunTurn(raising, def, b, Season.Winter);
			raising.BuildWorkers = b.MasonsPerBuildSeason;
		}

		Is("the nominal gang finishes on the quoted season", raising.Fortification, "palisade");

		// Twice the gang, half the seasons — which is the whole reason to have idle men on a wall.
		ProvinceEconomy hurried = Province();
		hurried.Building = "palisade";
		hurried.BuildLeft = 4 * b.MasonsPerBuildSeason;
		hurried.BuildWorkers = 2 * b.MasonsPerBuildSeason;
		EconomySimulation.RunTurn(hurried, def, b, Season.Winter);
		hurried.BuildWorkers = 2 * b.MasonsPerBuildSeason;
		EconomySimulation.RunTurn(hurried, def, b, Season.Winter);
		Is("twice the masons, half the seasons", hurried.Fortification, "palisade");

		// And a wall with nobody on it does not rise at all, however long the lord waits.
		ProvinceEconomy stalled = Province();
		stalled.Shares[Labour.Castle] = 0;
		stalled.Building = "palisade";
		stalled.BuildLeft = b.MasonsPerBuildSeason;
		stalled.BuildWorkers = 0;
		for (int season = 1; season <= 8; season++)
		{
			EconomySimulation.RunTurn(stalled, def, b, Season.Winter);
		}

		Is("a wall nobody is on does not rise", stalled.Fortification, "");
		Is("  and still asks for the same gang", EconomySimulation.Masons(stalled), b.MasonsPerBuildSeason);
	}

	private void Forge(GameBalance b, ProvinceDefinition def)
	{
		// A cold forge asks for nobody.
		ProvinceEconomy cold = Province();
		Is("a cold forge wants no smiths", EconomySimulation.Smiths(cold, b), 0);

		// Swords are ten iron and three timber apiece: ninety iron is nine swords, and the forge asks
		// for the hands to make all of them.
		ProvinceEconomy lit = Province();
		lit.Forging = "sword";
		lit.Iron = 90;
		lit.Wood = 1000;
		Is("a lit forge wants hands for what the stores pay for", EconomySimulation.Smiths(lit, b), 9 * b.SmithsPerWeapon);

		// Four swords' worth of smiths make four, every season, each paid for as it is made — out of
		// last season's iron: whatever the mine brings up this season is not on the anvil yet.
		lit.SmithWorkers = 4 * b.SmithsPerWeapon;
		lit.IronWorkers = def.IronWorkerCapacity;
		TurnSummary first = EconomySimulation.RunTurn(lit, def, b, Season.Spring);
		Is("four swords' worth of smiths make four", lit.Armoury.GetValueOrDefault("sword"), 4);
		Is("  and say so", first.Forged, 4);
		int dug = Mathf.RoundToInt(def.IronWorkerCapacity * b.IronYieldPerWorker * def.IronModifier * b.IronSeasonMultiplier[(int)Season.Spring]);
		Is("  paid for in iron as they were made", lit.Iron, 90 - 40 + dug);
		lit.SmithWorkers = 4 * b.SmithsPerWeapon;
		EconomySimulation.RunTurn(lit, def, b, Season.Spring);
		Is("  and four more the next season", lit.Armoury.GetValueOrDefault("sword"), 8);

		// The stores are the ceiling, however many are at the anvil.
		ProvinceEconomy scant = Province();
		scant.Forging = "sword";
		scant.Iron = 25;
		scant.Wood = 1000;
		scant.SmithWorkers = 40;
		Is("the smiths past what the iron pays for stand idle", EconomySimulation.Idle(scant, def, b, Season.Spring)
			>= 40 - EconomySimulation.Smiths(scant, b), true);
		EconomySimulation.RunTurn(scant, def, b, Season.Spring);
		Is("  and make no more than it pays for", scant.Armoury.GetValueOrDefault("sword"), 2);

		// A save from before the forge worked by the season had its order paid for; it lands.
		ProvinceEconomy owed = Province();
		owed.Forging = "spear";
		owed.ForgeBatch = 30;
		EconomySimulation.RunTurn(owed, def, b, Season.Spring);
		Is("an order an old save paid for is delivered", owed.Armoury.GetValueOrDefault("spear"), 30);
		Is("  once", owed.ForgeBatch, 0);

		// The labour bar sends hands to a lit forge like to any trade.
		ProvinceEconomy dealt = Province();
		dealt.Forging = "mace";
		dealt.Iron = 400;
		dealt.Wood = 400;
		Labour.Divide(dealt, def, b, Season.Summer, 50);
		Is("the deal puts smiths at a lit forge", dealt.SmithWorkers > 0, true);
		Is("  and every hand is still counted once", dealt.AllocatedWorkers <= dealt.Workers, true);
	}

	private void LabourBar(GameBalance b, ProvinceDefinition def)
	{
		// The bar hard over to the farm: the industry stands empty, the fields take what they can
		// use and whoever is left over stands idle rather than being lent to the mine.
		ProvinceEconomy farm = Province();
		Labour.Divide(farm, def, b, Season.Winter, 0);
		Is("the bar hard over to the farm empties the industry",
			farm.WoodWorkers + farm.StoneWorkers + farm.IronWorkers + farm.BuildWorkers + farm.SmithWorkers, 0);
		Is("  and no job has more than it can use", farm.GrainWorkers <= EconomySimulation.Demand(ResourceType.Grain, farm, def, b, Season.Winter), true);
		Is("  and the rest stand idle", EconomySimulation.Idle(farm, def, b, Season.Winter),
			farm.Workers - farm.AllocatedWorkers);

		// Hard over to the industry: nobody in the fields, and the diggings take every one of them.
		ProvinceEconomy works = Province();
		Labour.Divide(works, def, b, Season.Autumn, 100);
		Is("the bar hard over to the industry empties the farm",
			works.GrainWorkers + works.CattleWorkers + works.ReclaimWorkers, 0);
		Is("  and the diggings take all comers", works.AllocatedWorkers, works.Workers);

		// A quarter to the industry, the original's opening: each half is dealt by its shares.
		ProvinceEconomy quarter = Province();
		Labour.Divide(quarter, def, b, Season.Spring, 25);
		int industry = quarter.WoodWorkers + quarter.StoneWorkers + quarter.IronWorkers + quarter.BuildWorkers
			+ quarter.SmithWorkers;
		Is("a quarter of the county goes to the industry", industry, Labour.Pct(quarter.Workers, 25));

		// The leftovers of a job that cannot use its share walk on to the jobs with room...
		ProvinceEconomy herd = Province();
		herd.Shares[Labour.Grain] = Labour.Whole / 10;
		herd.Shares[Labour.Cattle] = Labour.Whole * 9 / 10;
		herd.Shares[Labour.Reclaim] = 0;
		Labour.Divide(herd, def, b, Season.Winter, 0);
		Is("a share the herd cannot use walks on to the fields",
			herd.CattleWorkers == EconomySimulation.Demand(ResourceType.Cattle, herd, def, b, Season.Winter)
			&& herd.GrainWorkers == EconomySimulation.Demand(ResourceType.Grain, herd, def, b, Season.Winter), true);

		// ...but never to one the lord has set at nothing: those stand idle instead.
		ProvinceEconomy none = Province();
		none.Shares[Labour.Grain] = 0;
		none.Shares[Labour.Cattle] = Labour.Whole;
		none.Shares[Labour.Reclaim] = 0;
		Labour.Divide(none, def, b, Season.Spring, 0);
		Is("  and a job set at nothing is given nobody", none.GrainWorkers, 0);
		EconomySimulation.RunTurn(none, def, b, Season.Spring);
		Is("  not even when the season turns", none.GrainWorkers, 0);

		// A shut site is dealt nobody, and its share goes to the sites still open.
		ProvinceEconomy shut = Province();
		Labour.Divide(shut, def, b, Season.Summer, 40);
		int woodBefore = shut.WoodWorkers;
		Labour.Toggle(shut, def, b, Season.Summer, Labour.Iron);
		Is("a shut mine is dealt nobody", shut.IronWorkers, 0);
		Is("  and the woods take its men", shut.WoodWorkers > woodBefore, true);
		Labour.Ask(shut, def, b, Season.Summer, Labour.Iron, 20);
		Is("asking for men at a shut mine opens it", shut.IsShut(Labour.Iron), false);

		// Asking for hands rewrites the shares, and the deal gives them.
		ProvinceEconomy asked = Province();
		Labour.Divide(asked, def, b, Season.Summer, 40);
		Labour.Ask(asked, def, b, Season.Summer, Labour.Wood, 60);
		Is("a figure moved by the lord is where he put it", Mathf.Abs(asked.WoodWorkers - 60) <= 2, true);

		// A figure taken off stands idle and stays idle; one put on comes from the idle first, and
		// nobody else on the county moves either way.
		ProvinceEconomy winter = Province();
		Labour.Divide(winter, def, b, Season.Winter, 25);
		int woodNow = winter.WoodWorkers;
		int ironNow = winter.IronWorkers;
		int idleNow = EconomySimulation.Idle(winter, def, b, Season.Winter);
		Labour.Ask(winter, def, b, Season.Winter, Labour.Grain, winter.GrainWorkers - 20);
		Is("a figure taken off the fields stands idle", EconomySimulation.Idle(winter, def, b, Season.Winter), idleNow + 20);
		Labour.Ask(winter, def, b, Season.Winter, Labour.Wood, woodNow + 20);
		Is("a figure put on the woods comes from the idle", EconomySimulation.Idle(winter, def, b, Season.Winter), idleNow);
		Is("  and not off the mine", winter.IronWorkers, ironNow);
		Is("  and is exactly one figure", winter.WoodWorkers, woodNow + 20);

		// With nobody idle, the figure comes from a job with more than it needs — never off a herd
		// that would fall below what it needs.
		ProvinceEconomy busy = Province();
		Labour.Divide(busy, def, b, Season.Spring, 25);
		int herdNow = busy.CattleWorkers;
		Labour.Ask(busy, def, b, Season.Spring, Labour.Grain, busy.GrainWorkers + 20);
		Is("a figure for the fields leaves the herd its need", busy.CattleWorkers, herdNow);

		// Torn ground has reclaimers of its own, and the reapers are not taken off the harvest for it.
		ProvinceEconomy torn = Province();
		torn.FieldRepair = 300;
		Labour.Divide(torn, def, b, Season.Summer, 0);
		Is("torn ground is dealt reclaimers", torn.ReclaimWorkers, b.ReclaimPerSeason);

		// Every county, every season, in a year of turns: nine jobs, and every hand on exactly one.
		ProvinceEconomy year = Province();
		Labour.Divide(year, def, b, Season.Spring, 25);
		bool counted = true;
		for (int season = 0; season < 8; season++)
		{
			EconomySimulation.RunTurn(year, def, b, (Season)(season % 4));
			counted &= year.AllocatedWorkers <= year.Workers
				&& EconomySimulation.Idle(year, def, b, (Season)((season + 1) % 4)) == year.Workers - year.AllocatedWorkers;
		}

		Is("every hand is on one job and no job has more than it can use, season after season", counted, true);

		// A site worked grows practised; one left empty falls back to where a new one starts.
		ProvinceEconomy practised = Province();
		practised.Efficiency[Labour.Wood] = b.SiteEfficiencyFloor;
		practised.WoodWorkers = 10;
		Labour.Practise(practised, b);
		Is("a worked site grows more practised", practised.EfficiencyOf(Labour.Wood) > b.SiteEfficiencyFloor, true);
		practised.WoodWorkers = 0;
		Labour.Practise(practised, b);
		Is("  and an abandoned one starts over", practised.EfficiencyOf(Labour.Wood), b.SiteEfficiencyFloor);
	}

	private void Levy(GameBalance b, ProvinceDefinition def)
	{
		// Hands standing about are what a levy is taken from first: the trades do not notice it.
		ProvinceEconomy spare = Province();
		spare.Population = 400;
		spare.GrainWorkers = 100;
		spare.IronWorkers = 50;
		EconomySimulation.Conscript(spare, 200);
		Is("a levy the idle can cover leaves the fields alone", spare.GrainWorkers, 100);
		Is("  and the mine too", spare.IronWorkers, 50);
		Is("  and the people are gone off the roll", spare.Population, 200);

		// One bigger than that comes off the trades, in proportion to what each of them holds.
		ProvinceEconomy pressed = Province();
		pressed.Population = 200;
		pressed.GrainWorkers = 120;
		pressed.IronWorkers = 60;
		pressed.WoodWorkers = 20;
		EconomySimulation.Conscript(pressed, 100);
		Is("a levy past the idle takes the fields", pressed.GrainWorkers, 60);
		Is("  and the mine, by what it held", pressed.IronWorkers, 30);
		Is("  and the woodyard", pressed.WoodWorkers, 10);

		// Whatever the arithmetic, the county never has more men at work than it has men.
		Is("nobody works who is not there", pressed.AllocatedWorkers <= pressed.Workers, true);

		ProvinceEconomy stripped = Province();
		stripped.Population = 50;
		stripped.GrainWorkers = 50;
		EconomySimulation.Conscript(stripped, 50);
		Is("a county emptied of people has nobody at work", stripped.AllocatedWorkers, 0);
	}

	private void Mending(GameBalance b, ProvinceDefinition def)
	{
		// Torn ground is the reclaimers' job, not the reapers': a field full of reapers mends nothing.
		ProvinceEconomy torn = Province();
		torn.FieldRepair = 400;
		torn.GrainWorkers = 520;
		Is("reapers mend nothing", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), -1);

		torn.ReclaimWorkers = 100;
		Is("a hundred reclaimers is four seasons", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), 4);

		torn.ReclaimWorkers = 200;
		Is("twice the reclaimers, half the seasons", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), 2);

		torn.ReclaimWorkers = 400;
		Is("  but no faster than a season's most", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), 2);

		torn.ReclaimWorkers = 200;
		EconomySimulation.RunTurn(torn, def, b, Season.Summer);
		Is("  and a season of them takes its share off", torn.FieldRepair, 200);

		// The flood takes one field under grain, and only that field's corn with it.
		ProvinceEconomy drowned = Province();
		int grainFields = drowned.FieldsUnder(FieldUse.Grain);
		drowned.StandingCrop = grainFields * 100;
		Is("the flood takes a field under grain", EconomySimulation.Flood(drowned, b), 0);
		Is("  which lies waste", drowned.Fields[0] == FieldUse.Waste, true);
		Is("  and one field fewer is under grain", drowned.FieldsUnder(FieldUse.Grain), grainFields - 1);
		Is("  and only its corn is lost", drowned.StandingCrop, (grainFields - 1) * 100);
		Is("  and it has to be mended", drowned.FieldRepair, b.FieldRepairWork);

		EconomySimulation.SetField(drowned, 0, FieldUse.Grain);
		Is("no order sows a flooded field", drowned.Fields[0] == FieldUse.Waste, true);

		// It is handed back on the turn the work is finished, not before it.
		drowned.ReclaimWorkers = b.ReclaimPerSeason;
		EconomySimulation.RunTurn(drowned, def, b, Season.Summer);
		Is("half the work leaves it waste", drowned.Fields[0] == FieldUse.Waste, true);
		drowned.ReclaimWorkers = b.ReclaimPerSeason;
		EconomySimulation.RunTurn(drowned, def, b, Season.Summer);
		Is("the last season closes the work", drowned.FieldRepair, 0);
		Is("  and hands the field back to rest", drowned.Fields[0] == FieldUse.Fallow, true);
	}

	// --- the herd ----------------------------------------------------------------------------------

	// --- the table ---------------------------------------------------------------------------------

	/// <summary>What a thousand people eat out of the granary in one winter season on this ration.
	/// Read off the turn rather than the formula, so it is the meal the game actually serves.</summary>
	private static int Eaten(GameBalance b, ProvinceDefinition def, RationLevel ration)
	{
		ProvinceEconomy province = Province();
		province.Ration = ration;
		province.Cattle = 0;      // no dairy, so the granary answers for all of it
		province.Grain = 4000;    // and enough in it that nothing goes short
		province.StandingCrop = 0;
		Labour.FarmsFirst(province, def, b, Season.Winter);
		int held = province.Grain;
		EconomySimulation.RunTurn(province, def, b, Season.Winter);
		return held - province.Grain;
	}

	/// <summary>A winter county with bread in the barn and a herd in the field, fed on the lord's
	/// chosen mix. Winter so that nothing is sown, reaped or dug and the only thing moving the stores
	/// is the meal.</summary>
	private static TurnSummary Fed(GameBalance b, ProvinceDefinition def, int beef)
	{
		ProvinceEconomy province = Province();
		province.Grain = 4000;
		province.Cattle = 80;
		province.BeefShare = beef;
		province.StandingCrop = 0;
		Labour.FarmsFirst(province, def, b, Season.Winter);
		return EconomySimulation.RunTurn(province, def, b, Season.Winter);
	}

	// --- what an army costs to keep ---------------------------------------------------------------

	/// <summary>Men under arms eat and are paid. The first of those is the one that needs pinning
	/// down: a soldier is taken OUT of the population when he is raised, so for as long as he did
	/// not eat, every company a lord trained quietly made his winter cheaper — an army was a saving.
	/// That is the exact inversion this case exists to keep shut.</summary>
	private void TheArmy(GameBalance b, ProvinceDefinition def)
	{
		// Armies eat, at the county's own ration, as the original's option has it: a hundred men in
		// the field are a hundred more at the table.
		ProvinceEconomy war = Fed();
		war.Population = 700;
		war.Muster("spear", 100);
		TurnSummary raised = EconomySimulation.RunTurn(war, def, b, Season.Winter);
		Is("the men in the field eat at the county's table", raised.Needed, 800);

		// Wages, out of what the reeve just brought in.
		Is("the men are paid", raised.Wages, Mathf.CeilToInt(100 * b.WagePerSoldier));
		Is("and nobody left over it", raised.Deserted, 0);

		// And an army the county cannot carry thins itself rather than putting the treasury into a
		// negative number nothing in the game knows how to answer.
		ProvinceEconomy broke = Fed();
		broke.Population = 700;
		broke.Gold = 0;
		broke.Tax = 0; // nothing comes in, so nothing can be paid out
		broke.Muster("spear", 100);
		TurnSummary unpaid = EconomySimulation.RunTurn(broke, def, b, Season.Winter);

		Is("unpaid men walk away", unpaid.Deserted, Mathf.CeilToInt(100 * b.DesertionRate));
		Is("and are gone from the roster", broke.Soldiers, 100 - unpaid.Deserted);
		Is("the treasury is emptied, not overdrawn", broke.Gold >= 0, true);

		// A company cut in two, which is how a lord leaves a ford held and goes on with the rest. He
		// says which men walk off, kind by kind; nobody may be lost or conjured in the cut, and both
		// halves stand where the whole one did with the same season left in their legs.
		ProvinceEconomy cut = Fed();
		cut.Muster("spear", 25, 40f);
		cut.Muster("bow", 15);
		FieldArmy whole = cut.Armies[0];
		whole.County = "Elsewhere";
		FieldArmy half = cut.Split(whole, new Dictionary<string, int> { ["spear"] = 5, ["bow"] = 15 });
		Is("a company splits as the lord cut it", cut.Armies.Count, 2);
		Is("the men he sent are the ones that went", half.Men.GetValueOrDefault("bow"), 15);
		Is("  and the rest are still his", whole.Strength, 20);
		Is("  nobody is lost in the cut", half.Strength + whole.Strength, 40);
		Is("  a kind emptied is off the roster", whole.Men.ContainsKey("bow"), false);
		Is("  on the same ground", half.County, whole.County);
		Is("  with the same legs left", half.MarchLeft, whole.MarchLeft);

		// And the one rule, whatever the screen asks for: neither banner may be raised over nobody.
		ProvinceEconomy all = Fed();
		all.Muster("spear", 10);
		FieldArmy only = all.Armies[0];
		Is("a company cannot walk off entire", all.Split(only, new Dictionary<string, int> { ["spear"] = 10 }) == null, true);
		Is("  nor cut into nobody", all.Split(only, new Dictionary<string, int> { ["spear"] = 0 }) == null, true);
		Is("  and more than he has is only what he has",
			all.Split(only, new Dictionary<string, int> { ["spear"] = 99 }) == null, true);
		Is("  so his company is untouched", all.Armies.Count, 1);
	}

	/// <summary>A province with bread in the barn, no herd to milk and nobody at work: whatever
	/// moves its granary in a winter turn is what it ate.</summary>
	private static ProvinceEconomy Fed()
	{
		ProvinceEconomy province = Province();
		province.Grain = 5000;
		province.Cattle = 0;
		province.Gold = 2000;
		return province;
	}

	/// <summary>Turning a field to another use. The rule worth guarding is the one that costs the
	/// player something: a crop already sown goes under the plough with the field it stood on.</summary>
	private void Turning(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();
		p.StandingCrop = 20;

		EconomySimulation.SetField(p, 0, FieldUse.Pasture);
		Is("ploughing a sown field under costs its share", p.StandingCrop, 15);
		Is("  and the field is under the herd now", p.FieldsUnder(FieldUse.Grain), 3);
		Is("  which the herd has more room for", p.FieldsUnder(FieldUse.Pasture), 4);

		EconomySimulation.SetField(p, 9, FieldUse.Grain);
		Is("turning land to grain out of season sows nothing", p.StandingCrop, 15);
		Is("  though the field is grain from now on", p.FieldsUnder(FieldUse.Grain), 4);

		EconomySimulation.SetField(p, 9, FieldUse.Grain);
		Is("and turning a field to what it already is costs nothing", p.StandingCrop, 15);

		// Fewer fields under grain is fewer hands wanted at the sowing.
		Is("the land sets what the sowing asks for",
			EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Winter), 96);
		EconomySimulation.SetField(p, 1, FieldUse.Fallow);
		Is("  and one field less asks for twenty-four hands less",
			EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Winter), 72);
	}

	/// <summary>The number on the panel has to be the number the season delivers, and looking at it
	/// must not move anything. Both are easy to lose: a projection written as its own formula drifts
	/// the first time a yield changes, and one that forgets to work on a copy plays the turn twice.</summary>
	private void Projection(GameBalance b, ProvinceDefinition def)
	{
		foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
		{
			ProvinceEconomy p = Province();
			p.StandingCrop = 20;
			Labour.FarmsFirst(p, def, b, season);

			ProvinceEconomy untouched = p.Copy();
			TurnSummary shown = EconomySimulation.Preview(p, def, b, season);

			Is($"{season}: looking at the season does not spend it", p.Grain, untouched.Grain);
			Is("  nor its herd", p.Cattle, untouched.Cattle);
			Is("  nor its people", p.Population, untouched.Population);

			TurnSummary happened = EconomySimulation.RunTurn(p, def, b, season);
			Is("  and what it showed is what it did", shown.GrainChange, happened.GrainChange);
			Is("  down to the herd", shown.CattleChange, happened.CattleChange);
			Is("  and the purse", shown.GoldChange, happened.GoldChange);
		}

		// The one the player is really watching: a province eating its granary shows a minus, not a
		// blank, and three seasons of the year that is the honest reading.
		ProvinceEconomy winter = Province();
		Labour.FarmsFirst(winter, def, b, Season.Winter);
		Is("a province eating its stores reads as a loss",
			EconomySimulation.Preview(winter, def, b, Season.Winter).GrainChange < 0, true);
	}

	/// <summary>As in Lords of the Realm, a county has a quarry or a mine and never both: what it
	/// cannot dig it buys, or takes from the neighbour who can. Every campaign's every county.</summary>
	private void StoneOrIron()
	{
		const string Campaigns = "res://data/campaigns";
		foreach (string campaign in DirAccess.GetDirectoriesAt(Campaigns))
		{
			string folder = $"{Campaigns}/{campaign}/provinces";
			if (!DirAccess.DirExistsAbsolute(folder))
			{
				continue;
			}

			foreach (string file in DirAccess.GetFilesAt(folder))
			{
				if (!file.EndsWith(".tres"))
				{
					continue;
				}

				var county = GD.Load<ProvinceDefinition>($"{folder}/{file}");
				Is($"{county.ProvinceName} digs stone or iron, not both",
					county.StoneWorkerCapacity > 0 && county.IronWorkerCapacity > 0, false);
			}
		}
	}

	// --- all four seasons of it --------------------------------------------------------------------

	/// <summary>The thing the parts are for: Kingsreach as the campaign hands it over, worked for a
	/// year by a reeve who redeploys every season. It must come out of its first autumn with more
	/// bread than it went into its first spring with, and it must find that its harvest costs it
	/// every other trade it has.</summary>
	private void TheYear(GameBalance b)
	{
		var def = GD.Load<ProvinceDefinition>("res://data/campaigns/royal-crown/provinces/kingsreach.tres");
		ProvinceEconomy p = ProvinceEconomy.FromDefinition(def);
		int opened = p.Grain;

		foreach (Season season in new[] { Season.Winter, Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
		{
			Labour.FarmsFirst(p, def, b, season);
			if (season == Season.Autumn)
			{
				Is("the harvest has reapers for all of it", p.GrainWorkers * 3 / 2 >= p.StandingCrop, true);
			}

			TurnSummary s = EconomySimulation.RunTurn(p, def, b, season);
			Is($"  {season.ToString().ToLowerInvariant()} feeds the province", s.Achieved >= RationLevel.Normal, true);
		}

		Is("a worked year leaves more bread than it began with", p.Grain > opened, true);
		Is("and the province is bigger for it", p.Population > def.InitialPopulation, true);
	}

	// --- scaffolding -------------------------------------------------------------------------------

	private static ProvinceDefinition Definition() => new()
	{
		ProvinceName = "Testshire",
		InitialPopulation = 1000,
		Fields = 10,
		InitialGrainFields = 4,
		InitialPastureFields = 3,
		InitialGrain = 250,
		InitialCattle = 40,
	};

	private static ProvinceEconomy Province()
	{
		ProvinceEconomy province = ProvinceEconomy.FromDefinition(Definition());
		province.Loyalty = 70f;
		return province;
	}

	private static ProvinceEconomy Rested(ProvinceDefinition def, GameBalance b)
	{
		ProvinceEconomy province = Province();
		Labour.FarmsFirst(province, def, b, Season.Spring);
		return province;
	}

	private static int Collected(GameBalance b, ProvinceDefinition def, string fortification, int percent = -1)
	{
		ProvinceEconomy province = Province();
		province.Fortification = fortification;
		if (percent >= 0)
		{
			province.Tax = percent;
		}

		province.Grain = 4000; // nothing here is about hunger
		Labour.FarmsFirst(province, def, b, Season.Winter);
		return EconomySimulation.RunTurn(province, def, b, Season.Winter).GoldChange;
	}

	private void Is(string what, int got, int expected) =>
		Report(got == expected, what, got.ToString(), expected.ToString());

	private void Is(string what, bool got, bool expected) =>
		Report(got == expected, what, got.ToString(), expected.ToString());

	private void Is(string what, float got, float expected) =>
		Report(Mathf.Abs(got - expected) < 0.001f, what, $"{got:0.###}", $"{expected:0.###}");

	/// <summary>Anything else that compares by value — a ration level, a name. The numeric cases keep
	/// their own overloads above, because a float wants a tolerance and this one cannot give it
	/// one.</summary>
	private void Is<T>(string what, T got, T expected) =>
		Report(System.Collections.Generic.EqualityComparer<T>.Default.Equals(got, expected),
			what, got?.ToString() ?? "nothing", expected?.ToString() ?? "nothing");

	private void Report(bool passed, string what, string got, string expected)
	{
		if (passed)
		{
			GD.Print($"ok   {what}");
			return;
		}

		_failed++;
		GD.PrintErr($"FAIL {what}: got {got}, expected {expected}");
	}
}
