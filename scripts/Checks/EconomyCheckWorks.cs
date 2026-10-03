using System.Collections.Generic;
using Godot;

/// <summary>The masons and the smithy: walls built at the masons' pace and weapons forged as the
/// stores pay for them.</summary>
public partial class EconomyCheck
{
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
}
