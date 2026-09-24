using Godot;

/// <summary>The county's year by Lords of the Realm's own rules (Livelihood), held to the original's
/// numbers — Raw Sienna, the first county of its Quaintville campaign, read off the game season by
/// season: 435 people at 72 happiness on a wooden palisade, taxed at six percent and fed Normal.</summary>
public partial class EconomyCheck
{
	private void AsTheOriginal(GameBalance b, ProvinceDefinition def)
	{
		// The reeve: six percent of a palisaded county, in the original's two whole-number steps.
		// These are the crowns Raw Sienna's treasury rose by, season after season.
		ProvinceEconomy sienna = Sienna(0, 0f);
		foreach ((int people, int crowns) in new[] { (471, 135), (528, 152), (561, 161), (580, 167), (623, 179), (693, 199) })
		{
			sienna.Population = people;
			Is($"{people} people at six percent behind a palisade pay {crowns}", Livelihood.TaxDue(sienna), crowns);
		}

		Is("open ground pays two thirds of a palisade", Fortifications.TaxBase(""), Fortifications.OpenGroundTaxBase);

		// The roll, man for man: the three seasons Raw Sienna neither gained nor lost anybody to its
		// neighbours. The turn played in a season is reckoned against the deaths of the one it opens.
		foreach ((Season season, int people, float happy, int after) in new[]
		{
			(Season.Winter, 435, 72f, 471), (Season.Spring, 471, 73f, 528), (Season.Summer, 528, 74f, 561),
		})
		{
			ProvinceEconomy county = Sienna(people, happy);
			EconomySimulation.RunTurn(county, def, b, season);
			Is($"{people} people at {happy} happiness, a {season.ToString().ToLowerInvariant()} later, are {after}",
				county.Population, after);
			Is("  and one point happier", county.Loyalty, happy + 1f);
		}

		// Happiness: the rate against five, the health band, the ration served. The original's own
		// balance points: perfect health on a Normal table holds at eight, good health at seven.
		Is("perfect health and a Normal table hold at eight percent",
			Livelihood.TaxTerm(8) + Livelihood.HealthTerm(95) + Livelihood.RationTerm(RationLevel.Normal), 0);
		Is("good health and a Normal table hold at seven",
			Livelihood.TaxTerm(7) + Livelihood.HealthTerm(75) + Livelihood.RationTerm(RationLevel.Normal), 0);
		Is("an empty table costs eight", Livelihood.RationTerm(RationLevel.None), -8);
		Is("a triple one earns seven", Livelihood.RationTerm(RationLevel.Triple), 7);
		Is("a realm taxed under twenty is resented nowhere", Livelihood.EmpireTerm(19), 0);
		Is("  at twenty, a point in every county", Livelihood.EmpireTerm(20), -1);
		Is("  at fifty, fifteen", Livelihood.EmpireTerm(50), -15);

		// The table: the dairy first and free, then beef and bread by the lord's share of the rest.
		ProvinceEconomy herd = Province();
		herd.Population = 600;
		herd.Cattle = 100; // five hundred fed off the milk
		herd.Grain = 1000;
		herd.BeefShare = 0;
		var served = new TurnSummary();
		Is("a Normal table is served", Livelihood.Eat(herd, 600, served), RationLevel.Normal);
		Is("  a hundred cows milked for five hundred", served.Dairy, 500);
		Is("  and the other hundred on bread, six to a sack", served.Bread, 17);
		Is("  and not a cow eaten", herd.Cattle, 100);

		// Beef asked for off a herd too small for it: the barn makes it up, and nobody goes short.
		ProvinceEconomy beefless = Province();
		beefless.Population = 600;
		beefless.Cattle = 2;
		beefless.Grain = 1000;
		beefless.BeefShare = 100;
		Is("a full barn feeds a county its lord wanted fed on beef", Livelihood.Eat(beefless, 600, new TurnSummary()),
			RationLevel.Normal);

		// And a table the stores cannot lay goes down a step until they can.
		ProvinceEconomy bare = Province();
		bare.Population = 600;
		bare.Cattle = 0;
		bare.Grain = 60; // 360 fed: short of Normal, enough for Half
		bare.Ration = RationLevel.Double;
		Is("double ordered into a bare barn comes down to what it holds",
			Livelihood.Eat(bare, 600, new TurnSummary()), RationLevel.Half);

		// Health, by the ration served and the band it is read from.
		ProvinceEconomy well = Province();
		well.Health = 75;
		Livelihood.Heal(well, RationLevel.Normal);
		Is("a Normal table keeps a good county a point better", well.Health, 76);
		Livelihood.Heal(well, RationLevel.None);
		Is("  and an empty one costs it sixteen", well.Health, 60);

		// What sons cost, taken all at once.
		Is("a tenth of the county taken costs five", Livelihood.RecruitingCost(50, 500), 5);
		Is("  half of it, ninety", Livelihood.RecruitingCost(250, 500), 90);

		// Moving house: from a county at fifty to a neighbour at seventy-six, four in the hundred.
		ProvinceEconomy unhappy = Province();
		unhappy.Population = 450;
		unhappy.Loyalty = 50f;
		Is("a county twenty-six points less happy sends four in the hundred", Livelihood.Movers(unhappy, 76f, false), 18);
		Is("  and none to a neighbour no happier", Livelihood.Movers(unhappy, 50f, false), 0);
	}

	/// <summary>Raw Sienna as the original has it: a palisade, six percent, a Normal table off a barn
	/// that will not run short, and good health.</summary>
	private static ProvinceEconomy Sienna(int people, float happy)
	{
		ProvinceEconomy county = Fed();
		county.Population = people;
		county.Loyalty = happy;
		county.Health = 75;
		county.Tax = 6;
		county.Fortification = "small-palisade";
		county.Ration = RationLevel.Normal;
		return county;
	}
}
