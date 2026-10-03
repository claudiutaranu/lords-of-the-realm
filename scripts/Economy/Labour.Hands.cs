using Godot;

/// <summary>The hands themselves: who is on each job, the shares remembered from them, the
/// diggings' practice climbing, and the arithmetic every deal is done in.</summary>
public static partial class Labour
{
	/// <summary>Reads the proportions off the hands where they are now: how a county saved before it
	/// kept any is carried over without anybody moving.</summary>
	private static void Remember(ProvinceEconomy p)
	{
		int farm = 0;
		int industry = 0;
		foreach (string job in Farm)
		{
			farm += Hands(p, job);
		}

		foreach (string job in Industry)
		{
			industry += Hands(p, job);
		}

		p.IndustryShare = Mathf.RoundToInt(100f * industry / Mathf.Max(1, farm + industry));
		FromHands(p, Farm, farm);
		FromHands(p, Industry, industry);
	}

	private static void FromHands(ProvinceEconomy p, string[] jobs, int total)
	{
		foreach (string job in jobs)
		{
			p.Shares[job] = total > 0 ? (int)((long)Hands(p, job) * Whole / total) : Opening[job];
		}
	}

	// --- the sites ----------------------------------------------------------------------------

	/// <summary>A season at the sites: each one worked this season grows more practised at it, and
	/// one left with nobody on it goes back to where a new site starts.</summary>
	public static void Practise(ProvinceEconomy p, GameBalance b)
	{
		foreach (string site in Sites)
		{
			int now = p.EfficiencyOf(site);
			p.Efficiency[site] = Hands(p, site) > 0
				? Mathf.Min(100, now + Mathf.Max(1, Pct(now, b.SiteEfficiencyGrowth)))
				: b.SiteEfficiencyFloor;
		}
	}

	// --- the counts ---------------------------------------------------------------------------

	public static int Hands(ProvinceEconomy p, string job) => job switch
	{
		Grain => p.GrainWorkers,
		Cattle => p.CattleWorkers,
		Reclaim => p.ReclaimWorkers,
		Castle => p.BuildWorkers,
		Iron => p.IronWorkers,
		Stone => p.StoneWorkers,
		Wood => p.WoodWorkers,
		Smith => p.SmithWorkers,
		_ => 0,
	};

	public static void Put(ProvinceEconomy p, string job, int hands)
	{
		switch (job)
		{
			case Grain: p.GrainWorkers = hands; break;
			case Cattle: p.CattleWorkers = hands; break;
			case Reclaim: p.ReclaimWorkers = hands; break;
			case Castle: p.BuildWorkers = hands; break;
			case Iron: p.IronWorkers = hands; break;
			case Stone: p.StoneWorkers = hands; break;
			case Wood: p.WoodWorkers = hands; break;
			case Smith: p.SmithWorkers = hands; break;
		}
	}

	private static int Share(ProvinceEconomy p, string job) =>
		p.Shares.TryGetValue(job, out int share) ? share : Opening[job];

	/// <summary>The original's percentage: whole people, rounded down.</summary>
	public static int Pct(int of, int percent) => (int)((long)of * percent / 100);

	/// <summary>The industry's half of a county, off a share that may fall between whole percents: the
	/// bar's arrows move it a man at a time. The hair of slack keeps 100·k/n from flooring to k − 1.</summary>
	public static int Pct(int of, double percent) => (int)System.Math.Floor(of * percent / 100 + 1e-6);

	/// <summary>The share that gives the industry exactly this many of the county's hands.</summary>
	public static double ShareOf(int industry, int people) =>
		people <= 0 ? 0 : System.Math.Clamp(100.0 * industry / people, 0, 100);

	/// <summary>A job's part of its half, rounded down to whole people.</summary>
	private static int Part(int half, int share) => (int)((long)half * share / Whole);
}
