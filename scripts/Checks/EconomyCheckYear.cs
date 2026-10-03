using Godot;

/// <summary>The county's year turning over: a season run, the projection the screens show, and a
/// whole year of them.</summary>
public partial class EconomyCheck
{
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
}
