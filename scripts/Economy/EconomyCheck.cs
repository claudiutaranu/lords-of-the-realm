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
		Sowing(b, def);
		Weeding(b, def);
		Reaping(b, def);
		Mending(b, def);
		Levy(b, def);
		LabourBar(b, def);
		Masons(b, def);
		OnePurse(b, def);
		Soil(b, def);
		Herd(b, def);
		Eating(b, def);
		TheTable(b, def);
		TheArmy(b, def);
		Taxes(b, def);
		TheAccount(b, def);
		Migration(b, def);
		Turning(b, def);
		Projection(b, def);
		TheYear(b);

		GD.Print(_failed == 0
			? "\nprovince economy: all checks passed"
			: $"\nprovince economy: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	// --- labour ----------------------------------------------------------------------------------

	private void Demands(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();

		// Four fields under grain, and what they ask for through the year. Autumn is the whole game.
		Is("spring ploughs and sows", EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Spring), 440);
		Is("summer weeds", EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Summer), 320);
		Is("autumn asks two hundred and forty", EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Autumn), 960);
		Is("winter hedges and ditches", EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Winter), 180);
		Is("a herdsman to every few head", EconomySimulation.Demand(ResourceType.Cattle, p, def, b, Season.Spring), 16);
		Is("the quarry takes what it has faces for", EconomySimulation.Demand(ResourceType.Stone, p, def, b, Season.Spring), def.StoneWorkerCapacity);

		// The pool and the figures drawn against it were rescaled together when the population
		// became the pool; the diggings are what catches it if only one of the two ever moves again.
		ProvinceEconomy digging = Province();
		EconomySimulation.Deploy(digging, def, b, Season.Spring);
		int wood = digging.Wood;
		EconomySimulation.RunTurn(digging, def, b, Season.Spring);
		Is("a full woodpile is worth seventy-five a season", digging.Wood - wood, 75);

		Is("half the hands do half the work", EconomySimulation.Covered(30, 60), 0.5f);
		Is("more hands than work is still one job", EconomySimulation.Covered(600, 60), 1f);
		Is("no work is not short-handed", EconomySimulation.Covered(30, 0), 0f);

		// Hands on a job that cannot use them are wasted, and the waste is counted rather than
		// silently folded into the yield.
		p.GrainWorkers = 200;
		p.CattleWorkers = p.WoodWorkers = p.StoneWorkers = p.IronWorkers = 0;
		Is("hands past the work stand idle", EconomySimulation.Idle(p, def, b, Season.Summer), 1000 - 200);
	}

	// --- the farming year --------------------------------------------------------------------------

	private void Sowing(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();
		EconomySimulation.Deploy(p, def, b, Season.Spring);
		TurnSummary s = EconomySimulation.RunTurn(p, def, b, Season.Spring);

		Is("four fields take twenty sacks of seed", s.Sown, 20);
		Is("  and the seed comes out of the granary", s.GrainBefore - 20 >= p.Grain, true);

		// Seed the province does not have is seed it cannot sow.
		ProvinceEconomy bare = Province();
		bare.Grain = 6;
		EconomySimulation.Deploy(bare, def, b, Season.Spring);
		Is("a bare granary sows what little it has", EconomySimulation.RunTurn(bare, def, b, Season.Spring).Sown, 6);

		// And hands it does not send are fields it does not sow.
		ProvinceEconomy short_ = Province();
		EconomySimulation.Deploy(short_, def, b, Season.Spring);
		short_.GrainWorkers = 220; // half of what four fields ask for
		Is("half the sowers sow half the seed", EconomySimulation.RunTurn(short_, def, b, Season.Spring).Sown, 10);
	}

	private void Weeding(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy tended = Province();
		tended.StandingCrop = 20;
		EconomySimulation.Deploy(tended, def, b, Season.Summer);
		EconomySimulation.RunTurn(tended, def, b, Season.Summer);
		Is("a weeded crop keeps all of itself", tended.StandingCrop, 20);

		ProvinceEconomy neglected = Province();
		neglected.StandingCrop = 20;
		neglected.GrainWorkers = 0;
		EconomySimulation.RunTurn(neglected, def, b, Season.Summer);
		Is("an unweeded one comes in light", neglected.StandingCrop, 9);
	}

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

	private void LabourBar(GameBalance b, ProvinceDefinition def)
	{
		// Everything to the fields: the woods and the workings stand empty however much they ask for.
		ProvinceEconomy fields = Province();
		EconomySimulation.Split(fields, def, b, Season.Autumn, toTrades: 0f);
		Is("the bar hard over to the fields empties the trades",
			fields.WoodWorkers + fields.StoneWorkers + fields.IronWorkers, 0);
		Is("  and puts what the harvest asks for into it", fields.GrainWorkers > 0, true);
		Is("  and the bar reads where it was put", EconomySimulation.TradesShare(fields), 0f);

		// Everything to the trades: the harvest is left standing in the field.
		ProvinceEconomy trades = Province();
		EconomySimulation.Split(trades, def, b, Season.Autumn, toTrades: 1f);
		Is("the bar hard over to the trades empties the fields",
			trades.GrainWorkers + trades.CattleWorkers, 0);
		Is("  and the bar reads where it was put", EconomySimulation.TradesShare(trades), 1f);

		// A half that has more hands than work leaves the rest standing rather than lending them.
		ProvinceEconomy even = Province();
		EconomySimulation.Split(even, def, b, Season.Winter, toTrades: 0.5f);
		Is("nobody is at work who has no work", even.AllocatedWorkers <= even.Workers, true);

		// And the bar never disagrees with the plaques: move a man by hand and it has already moved.
		ProvinceEconomy argued = Province();
		EconomySimulation.Split(argued, def, b, Season.Spring, toTrades: 0.5f);
		float before = EconomySimulation.TradesShare(argued);
		argued.IronWorkers += 40;
		Is("a man moved by hand moves the bar", EconomySimulation.TradesShare(argued) > before, true);
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
		// Summer asks four fields for 320 hands. Everything past that goes on the torn ground.
		ProvinceEconomy torn = Province();
		torn.FieldRepair = 400;
		torn.GrainWorkers = 320;
		Is("hands the year itself wants mend nothing", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), -1);

		torn.GrainWorkers = 420;
		Is("a hundred spare hands is four seasons", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), 4);

		torn.GrainWorkers = 520;
		Is("twice the hands, half the seasons", EconomySimulation.SeasonsToMend(torn, b, Season.Summer), 2);

		EconomySimulation.RunTurn(torn, def, b, Season.Summer);
		Is("  and a season of them takes its share off", torn.FieldRepair, 200);

		// The heart the water took comes back on the turn the work is finished, not before it.
		ProvinceEconomy last = Province();
		last.FieldRepair = 100;
		last.GrainWorkers = 420;
		for (int field = 0; field < last.Fertility.Length; field++)
		{
			last.Fertility[field] = 0.5f;
		}

		EconomySimulation.RunTurn(last, def, b, Season.Summer);
		Is("the last season closes the work", last.FieldRepair, 0);
		Is("  and gives the land back what the flood took", last.Fertility[0], 0.65f);
	}

	private void Reaping(GameBalance b, ProvinceDefinition def)
	{
		// Twenty sacks in the ground, twenty-four back for each of them, all the hands it wants.
		ProvinceEconomy full = Province();
		full.StandingCrop = 20;
		EconomySimulation.Deploy(full, def, b, Season.Autumn);
		Is("a full harvest is the year's whole yield", EconomySimulation.RunTurn(full, def, b, Season.Autumn).Harvest, 480);
		Is("  and nothing is left standing", full.StandingCrop, 0);

		ProvinceEconomy half = Province();
		half.StandingCrop = 20;
		EconomySimulation.Deploy(half, def, b, Season.Autumn);
		half.GrainWorkers = 480; // half of the 960 four fields want
		Is("half the reapers reap half the year", EconomySimulation.RunTurn(half, def, b, Season.Autumn).Harvest, 240);

		// Tired land gives less of the same crop.
		ProvinceEconomy tired = Province();
		tired.StandingCrop = 20;
		for (int field = 0; field < tired.Fertility.Length; field++)
		{
			tired.Fertility[field] = 0.5f;
		}

		EconomySimulation.Deploy(tired, def, b, Season.Autumn);
		Is("tired land gives half as much", EconomySimulation.RunTurn(tired, def, b, Season.Autumn).Harvest, 240);
	}

	private void Soil(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();
		for (int field = 0; field < p.Fertility.Length; field++)
		{
			p.Fertility[field] = 0.5f;
		}

		EconomySimulation.Deploy(p, def, b, Season.Autumn);
		EconomySimulation.RunTurn(p, def, b, Season.Autumn);

		Is("a cropped field gives up some heart", p.Fertility[0], 0.38f);          // field 0 is grain
		Is("a herd gives half of it back", p.Fertility[4], 0.625f);                // 4..6 are pasture
		Is("rest gives all of it back", p.Fertility[7], 0.75f);                    // 7.. are fallow
		// And only after the harvest: a spring turn leaves the soil exactly where it found it.
		ProvinceEconomy spring = Rested(def, b);
		EconomySimulation.RunTurn(spring, def, b, Season.Spring);
		Is("and the soil only moves after the harvest", spring.Fertility[0], 0.5f);
	}

	// --- the herd ----------------------------------------------------------------------------------

	private void Herd(GameBalance b, ProvinceDefinition def)
	{
		ProvinceEconomy p = Province();
		EconomySimulation.Deploy(p, def, b, Season.Spring);
		TurnSummary s = EconomySimulation.RunTurn(p, def, b, Season.Spring);
		Is("forty head give twenty-four of food", s.Dairy, 24);
		Is("  and the herd adds to itself", p.Cattle, 42);

		// Nobody milking, nothing milked, and nothing born.
		ProvinceEconomy loose = Province();
		loose.CattleWorkers = 0;
		Is("an untended herd gives nothing", EconomySimulation.RunTurn(loose, def, b, Season.Spring).Dairy, 0);
		Is("  and grows by nothing", loose.Cattle, 40);

		// Three pasture fields carry sixty head. At a hundred and twenty they are standing on
		// each other and the herd stops growing.
		ProvinceEconomy packed = Province();
		packed.Cattle = 120;
		packed.Grain = 4000; // so nothing is slaughtered for want of bread
		EconomySimulation.Deploy(packed, def, b, Season.Spring);
		Is("a herd at twice its room does not grow", packed.Cattle, 120);
		EconomySimulation.RunTurn(packed, def, b, Season.Spring);
		Is("  even fully tended", packed.Cattle, 120);
	}

	// --- the table ---------------------------------------------------------------------------------

	private void Eating(GameBalance b, ProvinceDefinition def)
	{
		// A thousand people want a hundred sacks. Forty head give twenty-four, and the granary
		// covers the rest.
		ProvinceEconomy p = Province();
		p.StandingCrop = 0;
		EconomySimulation.Deploy(p, def, b, Season.Winter);
		int before = p.Grain;
		TurnSummary s = EconomySimulation.RunTurn(p, def, b, Season.Winter);
		Is("dairy is eaten before the granary is opened", s.Dairy, 19);   // winter milk is thinner
		Is("  and the granary covers what is left", before - p.Grain, 81);
		Is("  nobody goes short", s.FoodShort, 0);

		// No grain and a small herd: the herd goes under the knife, and it is still not enough.
		ProvinceEconomy hungry = Province();
		hungry.Grain = 0;
		hungry.Cattle = 10;
		EconomySimulation.Deploy(hungry, def, b, Season.Spring);
		TurnSummary famine = EconomySimulation.RunTurn(hungry, def, b, Season.Spring);
		Is("an empty granary puts the herd under the knife", famine.Slaughtered, 10);
		Is("  and what beef cannot cover is hunger", famine.FoodShort, 54);
		Is("  which costs the province people", hungry.Population < 1000, true);
		Is("  and its lord their goodwill", hungry.Loyalty < 70f, true);

		// The ration is a multiple of a man's bread and nothing else, so the same county on each
		// setting eats exactly that multiple. A winter province: nothing sown, reaped or dug, no herd
		// to milk, so what leaves the granary is the meal and only the meal.
		int ordinary = Eaten(b, def, RationLevel.Normal);
		Is("the ordinary ration feeds a thousand on a hundred sacks", ordinary, 100);
		Is("half of it takes half as much", Eaten(b, def, RationLevel.Half), ordinary / 2);
		Is("double takes twice", Eaten(b, def, RationLevel.Double), ordinary * 2);
		Is("triple takes three times", Eaten(b, def, RationLevel.Triple), ordinary * 3);
		Is("and feeding them nothing takes nothing", Eaten(b, def, RationLevel.None), 0);

		// And what the lord gets back for it. This is the whole reason to ever feed a county more
		// than it needs: bread is the answer to a tax he cannot afford to cut.
		Is("a county fed double thinks better of its lord",
			b.RationLoyaltyDelta[(int)RationLevel.Double] > 0f, true);
		Is("  and one fed triple better still",
			b.RationLoyaltyDelta[(int)RationLevel.Triple] > b.RationLoyaltyDelta[(int)RationLevel.Double], true);
		Is("  while a half ration is resented",
			b.RationLoyaltyDelta[(int)RationLevel.Half] < 0f, true);
	}

	/// <summary>What a thousand people eat out of the granary in one winter season on this ration.
	/// Read off the turn rather than the formula, so it is the meal the game actually serves.</summary>
	private static int Eaten(GameBalance b, ProvinceDefinition def, RationLevel ration)
	{
		ProvinceEconomy province = Province();
		province.Ration = ration;
		province.Cattle = 0;      // no dairy, so the granary answers for all of it
		province.Grain = 4000;    // and enough in it that nothing goes short
		province.StandingCrop = 0;
		EconomySimulation.Deploy(province, def, b, Season.Winter);
		int held = province.Grain;
		EconomySimulation.RunTurn(province, def, b, Season.Winter);
		return held - province.Grain;
	}

	/// <summary>The lord's own mix, and the ration he actually served. Two rules worth pinning down,
	/// both of them the kind that look fine on screen while being quietly wrong: asking for a meal
	/// off a herd that does not exist must still feed the county, and a ration nobody could serve
	/// must not be the ration the county is grateful for.</summary>
	private void TheTable(GameBalance b, ProvinceDefinition def)
	{
		// All bread. The granary answers for the whole meal and the herd is never touched — which is
		// what makes the herd worth keeping, because next season it gives milk again.
		TurnSummary loaves = Fed(b, def, beef: 0);
		Is("a county fed on bread leaves its herd standing", loaves.Slaughtered, 0);
		Is("  and the granary answers for the rest of the meal", loaves.Bread, loaves.Needed - loaves.Dairy);

		// All beef. Nothing is opened in the barn and the herd pays for dinner.
		TurnSummary beefy = Fed(b, def, beef: 100);
		Is("a county fed on beef opens no sacks", beefy.Bread, 0);
		Is("  and eats into the herd instead", beefy.Slaughtered > 0, true);

		// And asking for meat where there is no herd. The order is not a spell: the bread covers it.
		ProvinceEconomy landless = Province();
		landless.Cattle = 0;
		landless.Grain = 4000;
		landless.BeefShare = 100;
		landless.StandingCrop = 0;
		EconomySimulation.Deploy(landless, def, b, Season.Winter);
		TurnSummary asked = EconomySimulation.RunTurn(landless, def, b, Season.Winter);
		Is("meat from a county with no herd comes as bread", asked.Bread > 0, true);
		Is("  and nobody goes short for the asking", asked.FoodShort, 0);

		// A lord who orders double into a barn with one ordinary meal in it. The county eats what
		// there was, and thinks of him exactly what one ordinary meal is worth — not what he meant.
		ProvinceEconomy promised = Province();
		promised.Ration = RationLevel.Double;
		promised.Cattle = 0;
		promised.Grain = 100; // one ordinary meal for a thousand people, and not a sack more
		promised.StandingCrop = 0;
		EconomySimulation.Deploy(promised, def, b, Season.Winter);
		TurnSummary got = EconomySimulation.RunTurn(promised, def, b, Season.Winter);
		Is("a ration nobody could serve is not the ration they got", got.Achieved, RationLevel.Normal);
		Is("  and their goodwill answers to what was served",
			got.LoyaltyFromRations, b.RationLoyaltyDelta[(int)RationLevel.Normal]);
		Is("  while a full meal is not a famine, whatever was promised", got.FoodShort, 0);

		// Double, served in full, to a county whose size does not divide by ten. It was judged against
		// its mouths rounded up to a whole sack — 233 portions for 117 mouths is 1.99 of a ration — so
		// about half the counties on the map were fed double, thanked their lord for an ordinary meal,
		// and grew at the ordinary rate.
		ProvinceEconomy uneven = Province();
		uneven.Population = 1165;
		uneven.Ration = RationLevel.Double;
		uneven.Cattle = 0;
		uneven.Grain = 4000;
		uneven.StandingCrop = 0;
		EconomySimulation.Deploy(uneven, def, b, Season.Winter);
		Is("double served in full is double, whatever the county's size",
			EconomySimulation.RunTurn(uneven, def, b, Season.Winter).Achieved, RationLevel.Double);

		// The other way round: promised nothing and served nothing.
		ProvinceEconomy none = Province();
		none.Ration = RationLevel.None;
		none.Cattle = 0;
		none.Grain = 4000;
		none.StandingCrop = 0;
		EconomySimulation.Deploy(none, def, b, Season.Winter);
		TurnSummary starved = EconomySimulation.RunTurn(none, def, b, Season.Winter);
		Is("a county given nothing is served nothing", starved.Achieved, RationLevel.None);
		Is("  and that IS a famine", starved.FoodShort > 0, true);
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
		EconomySimulation.Deploy(province, def, b, Season.Winter);
		return EconomySimulation.RunTurn(province, def, b, Season.Winter);
	}

	// --- what an army costs to keep ---------------------------------------------------------------

	/// <summary>Men under arms eat and are paid. The first of those is the one that needs pinning
	/// down: a soldier is taken OUT of the population when he is raised, so for as long as he did
	/// not eat, every company a lord trained quietly made his winter cheaper — an army was a saving.
	/// That is the exact inversion this case exists to keep shut.</summary>
	private void TheArmy(GameBalance b, ProvinceDefinition def)
	{
		// Two counties holding the same thousand souls: one with all of them at the plough, one that
		// has put a hundred of them under arms. Winter, so nothing is sown, reaped or dug, and no
		// herd, so nothing is milked — what moves the granary is eating and only eating.
		ProvinceEconomy peace = Fed();
		peace.Population = 800;
		int atPlough = -EconomySimulation.RunTurn(peace, def, b, Season.Winter).GrainChange;

		ProvinceEconomy war = Fed();
		war.Population = 700;
		war.Muster("spear", 100);
		TurnSummary raised = EconomySimulation.RunTurn(war, def, b, Season.Winter);

		Is("the garrison eats", raised.SoldierFood, Mathf.CeilToInt(100 / b.PeoplePerGrain * b.SoldierAppetite));
		Is("a soldier eats more than a ploughman", -raised.GrainChange > atPlough, true);

		// Short rations are for the people. An army is fed or it is not an army, so cutting the
		// county's bread must not quietly cut the garrison's too.
		ProvinceEconomy starved = Fed();
		starved.Population = 700;
		starved.Ration = RationLevel.Half;
		starved.Muster("spear", 100);
		Is("and does not go on short rations with them",
			EconomySimulation.RunTurn(starved, def, b, Season.Winter).SoldierFood, raised.SoldierFood);

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

		// And what they cost in goodwill, which is measured against the village they stand in. The
		// allowance matters as much as the charge: a county with no soldiers at all is the one the
		// brigands come for, so the resentment has to start where the watch ends and not at the
		// first man.
		ProvinceEconomy watched = Fed();
		watched.Population = 1000;
		watched.Muster("spear", 40); // under the twentieth the county takes for granted
		Is("a watch on the gate costs no goodwill",
			EconomySimulation.RunTurn(watched, def, b, Season.Winter).LoyaltyFromGarrison, 0f);

		ProvinceEconomy occupied = Fed();
		occupied.Population = 1000;
		occupied.Muster("spear", 150); // a tenth of the county past it
		Is("a garrison it cannot ignore is an occupation",
			EconomySimulation.RunTurn(occupied, def, b, Season.Winter).LoyaltyFromGarrison,
			-b.GarrisonLoyaltyPerTenth);

		// The same hundred and fifty men in a county five times the size are nobody's business.
		ProvinceEconomy wide = Fed();
		wide.Population = 5000;
		wide.Muster("spear", 150);
		Is("  and the same men in a larger county are not",
			EconomySimulation.RunTurn(wide, def, b, Season.Winter).LoyaltyFromGarrison, 0f);

		// A company cut in two, which is how a lord leaves men holding a ford and goes on with the
		// rest. Nobody may be lost or conjured in the halving, and both halves stand where the whole
		// one did with the same season left in their legs.
		ProvinceEconomy cut = Fed();
		cut.Muster("spear", 25, 40f);
		cut.Muster("bow", 15);
		FieldArmy whole = cut.Armies[0];
		whole.County = "Elsewhere";
		FieldArmy half = cut.Split(whole);
		Is("a company splits in two", cut.Armies.Count, 2);
		Is("half the men go", half.Strength, 20);
		Is("  and half stay", whole.Strength, 20);
		Is("  on the same ground", half.County, whole.County);
		Is("  with the same legs left", half.MarchLeft, whole.MarchLeft);

		// The rounding, where it is tightest: a pair splits into one and one rather than into two and
		// nobody, and the man left over has nothing to split.
		ProvinceEconomy pair = Fed();
		pair.Muster("spear", 1);
		pair.Muster("bow", 1);
		Is("a pair splits one and one", pair.Split(pair.Armies[0]).Strength, 1);
		Is("  and a lone man does not split at all", pair.Split(pair.Armies[0]) == null, true);
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

	private void Taxes(GameBalance b, ProvinceDefinition def)
	{
		// The two numbers that have to agree and live in different files: what a province opens on,
		// and what its people call a fair rate. A comment asking them to stay in step is worth
		// nothing the day somebody retunes one of them.
		Is("a county opens on the rate it calls fair", Province().Tax, b.FairTaxPercent);

		Is("an open village pays plainly", Collected(b, def, ""), 150);
		Is("a keep is worth a tenth more", Collected(b, def, "medium-castle"), 165);
		Is("a royal castle near a quarter", Collected(b, def, "grand-castle"), 183);
		Is("a wall nobody has priced adds nothing", Collected(b, def, "turnip-fort"), 150);

		// The rate is a number the lord chooses, so what it is worth has to scale with it.
		Is("and a harder rate brings in more", Collected(b, def, "", 20), 600);
		Is("taking nothing brings in nothing", Collected(b, def, "", 0), 0);

		// What it costs him. Thanks are capped and resentment is not: a lord can always make things
		// worse faster than he can make them better, and a county cannot be bought outright by
		// simply never taxing it.
		Is("a fair rate is neither thanked nor resented", EconomySimulation.TaxGoodwill(b.FairTaxPercent, b), 0f);
		Is("a heavy one is resented by the point",
			EconomySimulation.TaxGoodwill(b.FairTaxPercent + 10, b), -10f * b.LoyaltyPerTaxPoint);
		Is("and a light hand is thanked, but only so far",
			EconomySimulation.TaxGoodwill(0, b), b.TaxGoodwillCap);

		// And what it costs the counties it was not levied on.
		Is("a fair rate is nobody else's business", EconomySimulation.TaxSpill(b.FairTaxPercent, b), 0f);
		Is("a heavy one is talked about next door",
			EconomySimulation.TaxSpill(b.FairTaxPercent + 10, b),
			-10f * b.LoyaltyPerTaxPoint * b.OtherCountiesTaxShare);
	}

	/// <summary>The happiness table adds its own lines up against the two numbers either side of
	/// them, so they have to be the WHOLE of what moved the county. The failure this guards is not a
	/// wrong number on a page — it is the day something starts moving goodwill without writing down
	/// that it did, and the page goes on looking perfectly reasonable while quietly not adding up.
	/// The player then has six honest lines and no way to tell which of them is short.</summary>
	private void TheAccount(GameBalance b, ProvinceDefinition def)
	{
		// A county with something wrong with it on every count, and starting high enough that the
		// whole bill fits: goodwill stops at nought, and a county driven through the floor is the one
		// case where the lines legitimately come to more than the difference between the seasons.
		ProvinceEconomy p = Province();
		p.Loyalty = 90f;
		p.Tax = 25;
		p.Ration = RationLevel.Half;
		p.Grain = 4000;
		p.ConscriptedRecently = 200;
		p.Muster("spear", 150);
		EconomySimulation.Deploy(p, def, b, Season.Winter);
		TurnSummary season = EconomySimulation.RunTurn(p, def, b, Season.Winter);

		float lines = season.LoyaltyFromTax + season.LoyaltyFromRations + season.LoyaltyFromStarvation
			+ season.LoyaltyFromConscription + season.LoyaltyFromGarrison + season.LoyaltyFromNeighbours
			+ season.LoyaltyFromEvents;

		Is("the account adds up to the season", lines, season.LoyaltyChange);
		Is("  the tax is on it", season.LoyaltyFromTax < 0f, true);
		Is("  the short ration is on it", season.LoyaltyFromRations < 0f, true);
		Is("  the sons taken are on it", season.LoyaltyFromConscription < 0f, true);
		Is("  and the men quartered on them are on it", season.LoyaltyFromGarrison < 0f, true);
	}

	private void Migration(GameBalance b, ProvinceDefinition def)
	{
		// Content, well fed, and growing: births, then people arriving from elsewhere.
		ProvinceEconomy liked = Province();
		liked.Loyalty = 80f;
		liked.Ration = RationLevel.Double;
		liked.Grain = 4000;
		EconomySimulation.Deploy(liked, def, b, Season.Winter);
		EconomySimulation.RunTurn(liked, def, b, Season.Winter);
		Is("a content province draws people in", liked.Population, 1021);

		// Resented and squeezed: the edges empty, and plainly. A county losing two people out of a
		// thousand is a county the player reads as steady — births at one percent a season used to
		// very nearly cancel the leavers, so a lord could tax his people to the brink and see no
		// movement at all in the one number he watches.
		ProvinceEconomy resented = Province();
		resented.Loyalty = 20f;
		resented.Tax = 30;
		resented.Grain = 4000;
		EconomySimulation.Deploy(resented, def, b, Season.Winter);
		EconomySimulation.RunTurn(resented, def, b, Season.Winter);
		Is("a resented one empties from the edges", resented.Population, 964);
		Is("  and visibly, not two souls at a time", 1000 - resented.Population > 10, true);

		// And a county that has nothing left to say to its lord goes faster than one that has only
		// just crossed the line.
		ProvinceEconomy lost = Province();
		lost.Loyalty = 0f;
		lost.Grain = 4000;
		EconomySimulation.Deploy(lost, def, b, Season.Winter);
		EconomySimulation.RunTurn(lost, def, b, Season.Winter);

		ProvinceEconomy sullen = Province();
		sullen.Loyalty = b.EmigrationBelow;
		sullen.Grain = 4000;
		EconomySimulation.Deploy(sullen, def, b, Season.Winter);
		EconomySimulation.RunTurn(sullen, def, b, Season.Winter);
		Is("a county in revolt empties faster than a sullen one",
			lost.Population < sullen.Population, true);
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

		// Fewer fields under grain is fewer hands wanted at harvest.
		Is("the land sets what the harvest asks for",
			EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Autumn), 960);
		EconomySimulation.SetField(p, 1, FieldUse.Fallow);
		Is("  and one field less asks for two hundred and forty hands less",
			EconomySimulation.Demand(ResourceType.Grain, p, def, b, Season.Autumn), 720);
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
			EconomySimulation.Deploy(p, def, b, season);

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
		EconomySimulation.Deploy(winter, def, b, Season.Winter);
		Is("a province eating its stores reads as a loss",
			EconomySimulation.Preview(winter, def, b, Season.Winter).GrainChange < 0, true);
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

		foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
		{
			EconomySimulation.Deploy(p, def, b, season);
			if (season == Season.Autumn)
			{
				Is("the harvest takes every hand Kingsreach has", p.GrainWorkers, p.Workers);
				Is("  leaving nobody in the quarry", p.StoneWorkers, 0);
				Is("  nor at the woodpile", p.WoodWorkers, 0);
			}

			TurnSummary s = EconomySimulation.RunTurn(p, def, b, season);
			Is($"  {season.ToString().ToLowerInvariant()} feeds the province", s.FoodShort, 0);
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
		for (int field = 0; field < province.Fertility.Length; field++)
		{
			province.Fertility[field] = 0.5f;
		}

		EconomySimulation.Deploy(province, def, b, Season.Spring);
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
		EconomySimulation.Deploy(province, def, b, Season.Winter);
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
