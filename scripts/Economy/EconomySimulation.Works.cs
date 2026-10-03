using Godot;

/// <summary>The county's works in its season: the diggings dug, the smithy's weapons forged, and
/// the masons' wall raised.</summary>
public static partial class EconomySimulation
{
	// --- what comes out of the ground -------------------------------------------------------------

	/// <summary>Wood, stone and iron, which unlike grain are worked every season there are hands for
	/// it. The winter multipliers slow them — frozen ground, short days — and so does a site still
	/// learning its work (Labour.Practise).</summary>
	private static void Dig(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		p.Wood += Dug(Labour.Wood, p.WoodWorkers, def.WoodWorkerCapacity, b.WoodYieldPerWorker * def.WoodModifier, b.WoodSeasonMultiplier[(int)season], p);
		p.Stone += Dug(Labour.Stone, p.StoneWorkers, def.StoneWorkerCapacity, b.StoneYieldPerWorker * def.StoneModifier, b.StoneSeasonMultiplier[(int)season], p);
		p.Iron += Dug(Labour.Iron, p.IronWorkers, def.IronWorkerCapacity, b.IronYieldPerWorker * def.IronModifier, b.IronSeasonMultiplier[(int)season], p);
	}

	/// <summary>What one diggings brings up: every hand on it at the site's rate, as practised as
	/// the site is. Nothing where the county has none of it, however many are sent.</summary>
	private static int Dug(string site, int hands, int capacity, float perHand, float seasonal, ProvinceEconomy p) =>
		capacity <= 0 ? 0 : Mathf.RoundToInt(hands * perHand * seasonal * p.EfficiencyOf(site) / 100f);

	// --- work already paid for --------------------------------------------------------------------

	/// <summary>How many weapons the smiths at the anvil can hammer in a season, as practised as the
	/// smithy is, whatever the stores say.</summary>
	public static int Forgeable(ProvinceEconomy p, GameBalance b) =>
		p.SmithWorkers * p.EfficiencyOf(Labour.Smith) / 100 / Mathf.Max(1, b.SmithsPerWeapon);

	/// <summary>A season at the anvil: as many of the weapon in hand as the smiths can make and the
	/// stores pay for, each paid for as it is made. Whatever an older save still had on order lands
	/// first — it was paid for long ago.</summary>
	private static void ForgeWeapons(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		if (p.Forging.Length == 0)
		{
			return;
		}

		p.Add(p.Forging, p.ForgeBatch);
		p.ForgeBatch = 0;

		int made = Mathf.Min(Forgeable(p, b), Smithy.Affordable(p, p.Forging));
		if (made <= 0)
		{
			return;
		}

		foreach ((string store, int each) in Smithy.Recipe(p.Forging))
		{
			p.Add(store, -each * made);
		}

		p.Add(p.Forging, made);
		summary.Forged = made;
	}

	/// <summary>The masons work another season on whatever was ordered, and the new wall replaces
	/// the old one on the season it is finished. It was paid for when the order was placed, so
	/// nothing is spent here — a province that falls on hard times still gets the castle it already
	/// bought.</summary>
	private static void RaiseFortification(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		if (p.Building.Length == 0)
		{
			return;
		}

		// A wall is work, not a wait: the masons on it this season take their share out of what is
		// left of it. The seasons the fortifications room quotes are what it takes at the nominal
		// gang, so a lord who puts his idle county on the walls finishes in half the time, and one
		// who puts nobody on them watches the scaffolding stand there.
		p.BuildLeft = Mathf.Max(0, p.BuildLeft - p.BuildWorkers);
		p.BuildSeasonsLeft = Mathf.CeilToInt((float)p.BuildLeft / Mathf.Max(1, b.MasonsPerBuildSeason));
		if (p.BuildLeft > 0)
		{
			return;
		}

		p.Fortification = p.Building;
		summary.WallRaised = p.Building;
		p.Building = "";
		p.BuildWorkers = 0;
	}
}
