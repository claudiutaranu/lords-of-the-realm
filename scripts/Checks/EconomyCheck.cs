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
		Carts(b);
		Saves(b);

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

	private void Mending(GameBalance b, ProvinceDefinition def)
	{
		// Waste lies as it is until the lord orders it reclaimed: no reclaimers are wanted for it.
		ProvinceEconomy torn = Province();
		torn.Fields[0] = FieldUse.Waste;
		Is("waste nobody has ordered reclaimed wants no reclaimers", Husbandry.ReclaimWork(torn, b), 0);
		EconomySimulation.SetField(torn, 0, FieldUse.Grain);
		Is("  and no order sows it", torn.Fields[0], FieldUse.Waste);
		EconomySimulation.SetField(torn, 0, FieldUse.Reclaiming);
		Is("  but it can be set to reclaiming", torn.Fields[0], FieldUse.Reclaiming);
		EconomySimulation.SetField(torn, 0, FieldUse.Waste);
		Is("  and abandoned again", torn.Fields[0], FieldUse.Waste);

		torn.Weathered = 0;
		EconomySimulation.SetField(torn, 0, FieldUse.Reclaiming);
		Is("a field struck this season takes no order", torn.Fields[0], FieldUse.Waste);
		torn.Weathered = -1;
		EconomySimulation.SetField(torn, 0, FieldUse.Reclaiming);

		// Eight hundred hand-seasons, no more than two hundred of them a season.
		torn.GrainWorkers = 520;
		torn.ReclaimWorkers = 0;
		Is("reapers reclaim nothing", Husbandry.SeasonsToReclaim(torn, b), -1);
		int atMost = b.FieldReclaimWork / b.ReclaimPerSeason;
		torn.ReclaimWorkers = b.ReclaimPerSeason / 2;
		Is("half a season's most of reclaimers takes twice the seasons", Husbandry.SeasonsToReclaim(torn, b), 2 * atMost);
		torn.ReclaimWorkers = b.ReclaimPerSeason;
		Is("  a season's most, the fewest", Husbandry.SeasonsToReclaim(torn, b), atMost);
		torn.ReclaimWorkers = 2 * b.ReclaimPerSeason;
		Is("  and no faster than a field's most", Husbandry.SeasonsToReclaim(torn, b), atMost);

		// Two fields at it: the furthest on first, and what it cannot take goes on to the next.
		ProvinceEconomy two = Province();
		two.Fields[0] = FieldUse.Reclaiming;
		two.Fields[1] = FieldUse.Reclaiming;
		two.Reclaimed[1] = 100;
		two.ReclaimWorkers = b.ReclaimPerSeason + 100;
		Husbandry.Reclaim(two, b);
		Is("the furthest-on field takes its season's most", two.Reclaimed[1], 100 + b.ReclaimPerSeason);
		Is("  and the rest goes on to the next", two.Reclaimed[0], 100);

		// And the field is land again the season the work is done, resting.
		ProvinceEconomy mended = Province();
		mended.Fields[0] = FieldUse.Reclaiming;
		mended.Reclaimed[0] = b.FieldReclaimWork - b.ReclaimPerSeason;
		mended.ReclaimWorkers = b.ReclaimPerSeason;
		Husbandry.Reclaim(mended, b);
		Is("the last season closes the work", mended.Fields[0], FieldUse.Fallow);
		Is("  and forgets it", mended.Reclaimed.ContainsKey(0), false);
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
