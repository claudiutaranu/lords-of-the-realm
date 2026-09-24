using System.Collections.Generic;
using Godot;

/// <summary>How a county lives from one season to the next, by Lords of the Realm's own rules
/// (docs/lotr2-engine-checklist.md: "Hrană și rații", "Sănătate", "Fericire", "Populație",
/// "Taxe"): what it eats and off what, how healthy that leaves it, how content, how many are born
/// and how many die, and what the reeve collects.
///
/// Whole numbers throughout, rounded the original's way (<see cref="Pct"/>, <see cref="DivCeil"/>),
/// because the original's numbers are what the county is held to: Raw Sienna at six percent on a
/// wooden palisade paid 135, 152, 161, 167, 179 and 199 crowns over six seasons, and those are the
/// numbers this pays.</summary>
public static class Livelihood
{
	// --- rounding ------------------------------------------------------------------------------

	/// <summary>The original's percentage: whole people, rounded down.</summary>
	public static int Pct(int of, int percent) => (int)((long)of * percent / 100);

	public static int DivCeil(int of, int by) => by <= 0 ? 0 : (of + by - 1) / by;

	// --- the reeve -----------------------------------------------------------------------------

	/// <summary>What the county pays at its rate: its people times what its walls make a head worth
	/// (fortifications.json "taxBase", 320 on open ground), in two whole-number steps.</summary>
	public static int TaxDue(ProvinceEconomy p) =>
		Pct(Pct(p.Population, Fortifications.TaxBase(p.Fortification)), Mathf.Clamp(p.Tax, 0, MostTax));

	public const int MostTax = 50;

	/// <summary>A county's own rate against its happiness: five is even, every point over it costs
	/// one a season, every point under it pays one.</summary>
	public static int TaxTerm(int rate) => 5 - rate;

	/// <summary>What one county taxed at this rate costs every county of the same realm, the county
	/// itself included. Nothing below twenty; then a point for each few, and a point a percent from
	/// forty-four up.</summary>
	public static int EmpireTerm(int rate) => rate switch
	{
		< 20 => 0,
		<= 23 => -1,
		<= 27 => -2,
		<= 31 => -3,
		<= 34 => -4,
		<= 37 => -5,
		<= 39 => -6,
		<= 41 => -7,
		<= 43 => -8,
		_ => -8 - (Mathf.Min(rate, MostTax) - 43),
	};

	// --- the table -----------------------------------------------------------------------------

	/// <summary>The portions a ration asks for, a portion being one man fed for a season.</summary>
	public static int Portions(int people, RationLevel level) => level switch
	{
		RationLevel.Quarter => DivCeil(people, 4),
		RationLevel.Half => DivCeil(people, 2),
		RationLevel.Normal => people,
		RationLevel.Double => people * 2,
		RationLevel.Triple => people * 3,
		_ => 0,
	};

	/// <summary>How many a cow feeds without being eaten; how many one killed feeds; how many a sack of
	/// grain feeds.</summary>
	public const int FedByDairy = 5;
	public const int FedByBeef = 10;
	public const int FedBySack = 6;

	/// <summary>The meal. The dairy first and free — a cow is milked, not eaten — then what is left is
	/// split by the lord's own share (BeefShare) between beef and bread, both at once. Where either
	/// side cannot be met out of its store the whole table goes down a ration and is tried again,
	/// until it can be served or there is nothing on it. Returns the ration actually served.</summary>
	public static RationLevel Eat(ProvinceEconomy p, int eaters, TurnSummary summary)
	{
		int dairy = p.Cattle * FedByDairy;
		for (RationLevel level = p.Ration; level >= RationLevel.None; level--)
		{
			int portions = Portions(eaters, level);
			int rest = Mathf.Max(0, portions - dairy);
			int offTheHerd = Pct(rest, Mathf.Clamp(p.BeefShare, 0, 100));
			int killed = DivCeil(offTheHerd, FedByBeef);
			int sacks = DivCeil(rest - offTheHerd, FedBySack);

			// A side its store cannot meet is made up by the other before the table comes down a
			// step: a full barn is not a famine because the lord asked for beef off a small herd.
			if (killed > p.Cattle)
			{
				int unserved = (killed - p.Cattle) * FedByBeef;
				killed = p.Cattle;
				sacks += DivCeil(unserved, FedBySack);
			}
			else if (sacks > p.Grain)
			{
				int unserved = (sacks - p.Grain) * FedBySack;
				sacks = p.Grain;
				killed += DivCeil(unserved, FedByBeef);
			}

			if (killed > p.Cattle || sacks > p.Grain)
			{
				continue;
			}

			p.Cattle -= killed;
			p.Grain -= sacks;
			summary.Needed = portions;
			summary.Dairy = Mathf.Min(dairy, portions);
			summary.Slaughtered = killed;
			summary.Bread = sacks;
			summary.Achieved = level;
			return level;
		}

		summary.Achieved = RationLevel.None;
		return RationLevel.None;
	}

	/// <summary>The ration's own term on happiness: three a step, eight off at nothing.</summary>
	public static int RationTerm(RationLevel served) => (3 * Tier(served)) - 8;

	/// <summary>A ration's step on the original's ladder, None 0 to Triple 5.</summary>
	public static int Tier(RationLevel level) => (int)level;

	// --- health --------------------------------------------------------------------------------

	/// <summary>The five bands the health counter is read in, worst first.</summary>
	public enum Band
	{
		Diseased,
		Sick,
		Average,
		Good,
		Perfect,
	}

	public static Band BandOf(int health) => health switch
	{
		<= 10 => Band.Diseased,
		<= 35 => Band.Sick,
		<= 65 => Band.Average,
		<= 90 => Band.Good,
		_ => Band.Perfect,
	};

	/// <summary>How a season's ration moves the counter from each band: [ration][band].</summary>
	private static readonly int[,] HealthMove =
	{
		{ -8, -10, -13, -16, -20 },
		{ -4, -6, -9, -12, -15 },
		{ -2, -4, -6, -8, -12 },
		{ 8, 4, 2, 1, -1 },
		{ 12, 8, 4, 2, 0 },
		{ 20, 12, 6, 3, 1 },
	};

	public static void Heal(ProvinceEconomy p, RationLevel served) =>
		p.Health = Mathf.Clamp(p.Health + HealthMove[Tier(served), (int)BandOf(p.Health)], 0, 100);

	/// <summary>The health band's own term on happiness.</summary>
	public static int HealthTerm(int health) => BandOf(health) switch
	{
		Band.Diseased => -10,
		Band.Sick => -5,
		Band.Average => 0,
		Band.Good => 1,
		_ => 2,
	};

	// --- the people ----------------------------------------------------------------------------

	/// <summary>Births in percent by the size of the county: a hamlet doubles, a city barely grows.</summary>
	private static readonly (int Upto, int Percent)[] BirthLadder =
	{
		(40, 100), (80, 70), (100, 50), (250, 30), (500, 20), (700, 15), (800, 14), (900, 13), (1000, 12),
		(1100, 11), (1200, 10), (1300, 9), (1400, 8), (1500, 7), (1600, 6), (1700, 5), (1800, 4),
		(1900, 3), (2000, 2), (3000, 1),
	};

	private static int BirthRate(int people)
	{
		foreach ((int upto, int percent) in BirthLadder)
		{
			if (people <= upto)
			{
				return percent;
			}
		}

		return 0;
	}

	/// <summary>How much of the ladder a county of this happiness gets.</summary>
	private static int Willing(float happiness) => happiness switch
	{
		< 26 => 25,
		< 51 => 50,
		< 76 => 75,
		< 100 => 100,
		_ => 120,
	};

	private static readonly int[] DeathByBand = { 35, 20, 8, 3, 0 };

	/// <summary>Deaths by the season being entered: the turn played in winter is reckoned against
	/// spring's four, and so round — which is what Raw Sienna's roll comes out to, man for man.</summary>
	private static readonly int[] DeathBySeason = { 4, 0, 2, 8 };

	/// <summary>A season's births and deaths, in the original's order, off the county's happiness and
	/// health. Returns them for the summary.</summary>
	public static (int Born, int Died) Generations(ProvinceEconomy p, Season entering)
	{
		int people = p.Population;
		int rate = Pct(BirthRate(people), Willing(p.Loyalty));
		int born = Pct(people, rate);
		if (born == 0 && rate != 0)
		{
			born = 1;
		}

		Band band = BandOf(p.Health);
		int deathRate = DeathByBand[(int)band] + DeathBySeason[(int)entering];
		int died = Pct(people, deathRate);
		if (died == 0 && deathRate != 0)
		{
			died = 1;
		}

		if (band == Band.Diseased)
		{
			died += 2;
		}

		if (rate < deathRate)
		{
			died++;
		}
		else
		{
			born++;
		}

		p.Population = Mathf.Max(0, people + born - died);
		return (born, died);
	}

	/// <summary>What sons taken for the army cost a county's happiness, by the share of it taken in
	/// one muster.</summary>
	public static int RecruitingCost(int taken, int people)
	{
		if (taken <= 0)
		{
			return 0;
		}

		int percent = people <= 0 ? 100 : taken * 100 / people;
		return percent switch
		{
			< 5 => 0,
			< 10 => 2,
			< 20 => 5,
			< 25 => 10,
			< 33 => 19,
			< 50 => 37,
			< 60 => 90,
			_ => 101,
		};
	}

	// --- moving house --------------------------------------------------------------------------

	/// <summary>How many leave for the happiest neighbour, if he is happier: a share of the county
	/// by the gap between them, never more than a hundred in a season. Half as many out of a county
	/// nobody holds.</summary>
	public static int Movers(ProvinceEconomy p, float bestNeighbour, bool neutral)
	{
		int happiness = Mathf.RoundToInt(p.Loyalty);
		int best = Mathf.RoundToInt(bestNeighbour);
		if (best <= happiness || happiness >= 100)
		{
			return 0;
		}

		int percent = Pct(best - happiness, (100 - happiness) / 3);
		int movers = Mathf.Min(Pct(p.Population, percent), 100);
		return neutral ? movers / 2 : movers;
	}
}
