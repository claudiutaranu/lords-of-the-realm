using System.Collections.Generic;
using Godot;

/// <summary>Who works where, dealt the way Lords of the Realm deals it (docs/lotr2-engine-checklist.md,
/// "Muncă: 9 joburi și alocatorul").
///
/// Nobody is placed by hand. The lord keeps a set of proportions — how much of the county goes to
/// industry, and within the farm and within the industry how the half is divided — and the county is
/// dealt from scratch against them, twice a season and whenever he changes anything. Each job takes
/// its share of its half or as much as it can use, whichever is less; what is left over walks on to
/// the jobs in the same half that still have room, and whatever finds no room stands idle. Moving a
/// figure on the screen does not put a man somewhere: it rewrites the proportions and deals again.
///
/// What a job can use is its useful ceiling. The fields and the herd are finite and change with the
/// season; the diggings are all but bottomless; a site the county does not have, or one the lord has
/// shut, can use nobody.</summary>
public static class Labour
{
	public const string Grain = "grain";
	public const string Cattle = "cattle";
	public const string Reclaim = "reclaim";
	public const string Castle = "castle";
	public const string Iron = "iron";
	public const string Stone = "stone";
	public const string Wood = "wood";
	public const string Smith = "smith";

	/// <summary>The farm half and the industry half, in the order the original numbers its jobs.</summary>
	public static readonly string[] Farm = { Grain, Cattle, Reclaim };
	public static readonly string[] Industry = { Castle, Iron, Stone, Wood, Smith };

	/// <summary>The four sites a lord can shut with a click on the building.</summary>
	public static readonly string[] Sites = { Iron, Stone, Wood, Smith };

	/// <summary>A diggings' ceiling: more than any county will ever send, which is the original's way
	/// of saying there is none.</summary>
	public const int Bottomless = 100_000;

	/// <summary>How many rounds the leftovers of the farm half spend on the grain and the herd for
	/// every one they spend on the ground being reclaimed.</summary>
	private const int FarmRoundsPerReclaim = 5;

	/// <summary>The proportions a new county opens on.</summary>
	public static Dictionary<string, int> OpeningShares() => new(Opening);

	/// <summary>A whole half, in the units a job's share is kept in: hundredths of a percent. The
	/// original keeps whole percents; a figure moved on this screen is rewritten into them every
	/// press, and whole percents of a few hundred men rounded every press walk the other jobs off
	/// by a man or two each time. The bar itself stays in whole percents.</summary>
	public const int Whole = 10_000;

	/// <summary>How the halves are divided in a county nobody has divided yet. Not the original's —
	/// its opening proportions are not known — and they matter less than they look: a share a job
	/// cannot use walks on to the jobs that can.</summary>
	private static readonly Dictionary<string, int> Opening = new()
	{
		[Grain] = 6_000, [Cattle] = 2_000, [Reclaim] = 2_000,
		[Castle] = 2_000, [Iron] = 2_000, [Stone] = 2_000, [Wood] = 2_000, [Smith] = 2_000,
	};

	// --- the deal ------------------------------------------------------------------------------

	/// <summary>Deals the whole county again from its proportions, for the season it is about to
	/// work. Every hand lands on exactly one of the nine jobs, idle included.</summary>
	public static void Deal(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		if (p.Shares.Count == 0 && p.AllocatedWorkers > 0)
		{
			Remember(p);
		}

		// A save made the day the shares were whole percents: every one of them at a hundred or less
		// and together a whole half's worth. Read in hundredths it would stand the county idle.
		int largest = 0;
		int sum = 0;
		foreach (int share in p.Shares.Values)
		{
			largest = Mathf.Max(largest, share);
			sum += share;
		}

		if (largest <= 100 && sum >= 100)
		{
			foreach (string job in new List<string>(p.Shares.Keys))
			{
				p.Shares[job] *= 100;
			}
		}

		int people = Mathf.Max(0, p.Workers);
		int industry = Pct(people, Mathf.Clamp(p.IndustryShare, 0, 100));
		var given = new Dictionary<string, int>();
		DealHalf(p, def, b, season, Farm, people - industry, given);
		DealHalf(p, def, b, season, Industry, industry, given);
		foreach ((string job, int hands) in given)
		{
			Put(p, job, hands);
		}
	}

	private static void DealHalf(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season,
		string[] jobs, int half, Dictionary<string, int> given)
	{
		// The shares of a half need not come to a hundred: what the lord has taken off every job and
		// left unshared stands idle by his choice, and does not walk. Only a share a job could not use
		// walks on.
		var ceiling = new Dictionary<string, int>();
		int shared = 0;
		foreach (string job in jobs)
		{
			shared += Share(p, job);
		}

		int left = Mathf.Min(half, Part(half, Mathf.Min(Whole, shared)));

		// The smallest shares are served first, so a whole-percent share rounded up spills its odd men
		// onto the biggest job of the half and never leaves the herd a man short of what it asked.
		var order = new List<string>(jobs);
		order.Sort((x, y) => Share(p, x).CompareTo(Share(p, y)));
		foreach (string job in order)
		{
			ceiling[job] = Ceiling(p, def, b, season, job);
			given[job] = Mathf.Min(Mathf.Min(Part(half, Share(p, job)), ceiling[job]), Mathf.Max(0, left));
			left -= given[job];
		}

		// The leftovers walk. The farm's go to the grain and the herd five rounds for every one they
		// spend on reclaiming, the industry's to every site with room alike, each round in proportion
		// to the shares.
		for (int round = 0; left > 0; round++)
		{
			bool reclaimRound = round % (FarmRoundsPerReclaim + 1) == FarmRoundsPerReclaim;
			var open = new List<string>();
			foreach (string job in jobs)
			{
				// A job the lord has set at nothing is left at nothing: what nobody with a share can
				// take stands idle.
				bool resting = job == Reclaim && !reclaimRound;
				if (!resting && given[job] < ceiling[job] && Share(p, job) > 0)
				{
					open.Add(job);
				}
			}

			if (open.Count == 0)
			{
				if (!HasRoom(p, jobs, given, ceiling))
				{
					break; // nowhere left to stand but idle
				}

				continue; // only the reclaiming has room, and its round is coming
			}

			int weight = 0;
			foreach (string job in open)
			{
				weight += Share(p, job);
			}

			int handed = 0;
			foreach (string job in open)
			{
				int part = left * Share(p, job) / weight;
				int take = Mathf.Min(Mathf.Max(1, part), Mathf.Min(ceiling[job] - given[job], left - handed));
				given[job] += take;
				handed += take;
				if (handed >= left)
				{
					break;
				}
			}

			left -= handed;
		}
	}

	private static bool HasRoom(ProvinceEconomy p, string[] jobs, Dictionary<string, int> given, Dictionary<string, int> ceiling)
	{
		foreach (string job in jobs)
		{
			if (given[job] < ceiling[job] && Share(p, job) > 0)
			{
				return true;
			}
		}

		return false;
	}

	// --- what a job can use --------------------------------------------------------------------

	/// <summary>The most hands a job can put to use this season. Past it a man only stands about.</summary>
	public static int Ceiling(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string job) => job switch
	{
		Grain => EconomySimulation.Demand(ResourceType.Grain, p, def, b, season),
		Cattle => EconomySimulation.Demand(ResourceType.Cattle, p, def, b, season),
		Reclaim => Mathf.Min(p.FieldRepair, b.ReclaimPerSeason),
		// ponytail: the original gives the masons nobody until every load of stone and timber is
		// on site; here the wall is paid for when it is ordered, so they can start at once.
		Castle => EconomySimulation.Masons(p),
		Smith => p.IsShut(Smith) ? 0 : EconomySimulation.Smiths(p, b),
		Iron => Open(p, Iron, def.IronWorkerCapacity),
		Stone => Open(p, Stone, def.StoneWorkerCapacity),
		Wood => Open(p, Wood, def.WoodWorkerCapacity),
		_ => 0,
	};

	/// <summary>A diggings is either there and open, and takes all comers, or it takes nobody. The
	/// county's capacity for it says only whether the ore or the stone or the wood is there at all.</summary>
	private static int Open(ProvinceEconomy p, string site, int capacity) =>
		capacity > 0 && !p.IsShut(site) ? Bottomless : 0;

	/// <summary>The fewest a job needs before it is short-handed, for the screen alone — the deal
	/// never reads it. The fields, the herd, the reclaiming, the scaffolding and the anvil need what
	/// they can use; a diggings is never short, only more or less busy.</summary>
	public static int Wanted(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string job) =>
		job is Iron or Stone or Wood ? 0
		: job == Cattle ? Husbandry.Herdsmen(p.Cattle) // the ceiling is twice this: extra hands only help it breed
		: Ceiling(p, def, b, season, job);

	// --- the lord's hand ----------------------------------------------------------------------

	/// <summary>The lord asks for a number of hands on one job, and the proportions are rewritten so
	/// the deal gives them — the way dragging a figure rewrites them in the original.
	///
	/// A figure taken off a job stands idle: its share is simply not given to anybody else. A figure
	/// put on one comes from whoever can best spare it — the idle of either half, then a job with
	/// more than it needs, and only last from a job that needs every man it has, this side of the
	/// bar before the other. Whenever it comes from across the bar, the bar moves to fetch it. Asking
	/// for hands at a site he had shut opens it.</summary>
	public static void Ask(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season,
		string job, int hands)
	{
		p.Shut.Remove(job);
		Deal(p, def, b, season); // from the county as it stands, not as it was last dealt
		int people = p.Workers;
		if (people <= 0)
		{
			return;
		}

		bool farming = System.Array.IndexOf(Farm, job) >= 0;
		string[] own = farming ? Farm : Industry;
		string[] other = farming ? Industry : Farm;
		var want = new Dictionary<string, int>();
		foreach (string each in Farm)
		{
			want[each] = Hands(p, each);
		}

		foreach (string each in Industry)
		{
			want[each] = Hands(p, each);
		}

		int industry = Pct(people, p.IndustryShare);
		int ownHalf = farming ? people - industry : industry;
		int otherHalf = people - ownHalf;
		int ownIdle = ownHalf - Sum(own, want);
		int otherIdle = otherHalf - Sum(other, want);

		int target = Mathf.Clamp(hands, 0, Ceiling(p, def, b, season, job));
		int need = target - want[job];
		want[job] = target;
		if (need > 0)
		{
			// The idle first, wherever they stand — a figure fetched from the other half moves the bar.
			need -= Draw(ref ownIdle, need);
			int fetched = Draw(ref otherIdle, need);
			need -= fetched;

			// Then whoever has more than his job needs, this side of the bar before the other.
			need -= Spare(p, def, b, season, own, job, want, need, beyondNeedOnly: true);
			int spared = Spare(p, def, b, season, other, job, want, need, beyondNeedOnly: true);
			need -= spared;
			fetched += spared;
			ownHalf += fetched;
			otherHalf -= fetched;

			// Last, from whoever has anybody at all: this side of the bar first, then the other — a
			// figure dragged from the reapers to the woodpile goes, harvest or no harvest.
			need -= Spare(p, def, b, season, own, job, want, need, beyondNeedOnly: false);
			int taken = Spare(p, def, b, season, other, job, want, need, beyondNeedOnly: false);
			need -= taken;
			ownHalf += taken;
			otherHalf -= taken;
			want[job] -= need; // nobody left anywhere to give
		}

		// The bar moves only when a figure crossed it — and then in whole percents of the county,
		// rounded so the half being given to is at least as big as it was asked to be. Recomputed on
		// every press, a percent of five hundred men rounded away took five of them off a job nobody
		// had touched.
		if (ownHalf != (farming ? people - industry : industry))
		{
			float industryShare = 100f * (farming ? otherHalf : ownHalf) / people;
			p.IndustryShare = Mathf.Clamp(farming ? Mathf.FloorToInt(industryShare) : Mathf.CeilToInt(industryShare), 0, 100);
		}
		int industryNow = Pct(people, p.IndustryShare);
		Proportion(p, Farm, people - industryNow, want);
		Proportion(p, Industry, industryNow, want);
		Deal(p, def, b, season);
	}

	private static int Sum(string[] jobs, Dictionary<string, int> want)
	{
		int total = 0;
		foreach (string job in jobs)
		{
			total += want[job];
		}

		return total;
	}

	private static int Draw(ref int pool, int need)
	{
		int taken = Mathf.Clamp(need, 0, Mathf.Max(0, pool));
		pool -= taken;
		return taken;
	}

	/// <summary>Takes up to <paramref name="need"/> hands off the jobs of a half, in proportion to
	/// what each can give: everything past what it needs, or with <paramref name="beyondNeedOnly"/>
	/// off, everything it has.</summary>
	private static int Spare(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season,
		string[] jobs, string asking, Dictionary<string, int> want, int need, bool beyondNeedOnly)
	{
		if (need <= 0)
		{
			return 0;
		}

		var spare = new Dictionary<string, int>();
		int total = 0;
		foreach (string job in jobs)
		{
			if (job == asking)
			{
				continue;
			}

			spare[job] = Mathf.Max(0, want[job] - (beyondNeedOnly ? Wanted(p, def, b, season, job) : 0));
			total += spare[job];
		}

		int taking = Mathf.Min(need, total);
		int taken = 0;
		foreach ((string job, int can) in spare)
		{
			int off = total == 0 ? 0 : Mathf.Min(can, (int)((long)taking * can / total));
			want[job] -= off;
			taken += off;
		}

		// Whole men only: the odd ones come off whoever still has the most to give.
		foreach ((string job, int _) in spare)
		{
			while (taken < taking && want[job] - (beyondNeedOnly ? Wanted(p, def, b, season, job) : 0) > 0)
			{
				want[job]--;
				taken++;
			}
		}

		return taken;
	}

	/// <summary>Writes a half's shares from the hands wanted on each job, rounded up so the deal
	/// never comes out a man short of what was asked; a job's own ceiling takes off any excess.</summary>
	private static void Proportion(ProvinceEconomy p, string[] jobs, int half, Dictionary<string, int> want)
	{
		foreach (string job in jobs)
		{
			p.Shares[job] = half <= 0 ? 0 : Mathf.Clamp((int)(((long)want[job] * Whole + half - 1) / half), 0, Whole);
		}
	}

	/// <summary>The most one job could be given: what it can use, and never more than the county.</summary>
	public static int Most(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string job) =>
		Mathf.Min(Ceiling(p, def, b, season, job), Mathf.Max(0, p.Workers));

	public static string JobOf(ResourceType type) => type switch
	{
		ResourceType.Grain => Grain,
		ResourceType.Cattle => Cattle,
		ResourceType.Wood => Wood,
		ResourceType.Stone => Stone,
		_ => Iron,
	};

	/// <summary>The lord moves the bar between the farm and the industry, and the county is dealt again.</summary>
	public static void Divide(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, int toIndustry)
	{
		p.IndustryShare = Mathf.Clamp(toIndustry, 0, 100);
		Deal(p, def, b, season);
	}

	/// <summary>Shuts a site or opens it again, and deals the county over it.</summary>
	public static void Toggle(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string site)
	{
		if (!p.Shut.Remove(site))
		{
			p.Shut.Add(site);
		}

		Deal(p, def, b, season);
	}

	/// <summary>A reeve's division rather than the lord's: enough of the county on the farm for every
	/// field and every beast this season (three herdsmen a head), and the rest to the industry. What a rival lord's county
	/// runs on, since nobody is moving his bar for him.</summary>
	public static void FarmsFirst(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		int people = Mathf.Max(1, p.Workers);
		int farm = 0;
		// What the farm needs, not all it could use: a herd takes twice its herdsmen, but only half
		// of those are what keeps it alive.
		foreach (string job in Farm)
		{
			farm += Wanted(p, def, b, season, job);
		}

		p.IndustryShare = Mathf.Clamp(100 - Mathf.CeilToInt(100f * farm / people), 0, 100);
		p.Shares = OpeningShares(); // and each half divided as a county nobody has touched
		Deal(p, def, b, season);
	}

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

	private static void Put(ProvinceEconomy p, string job, int hands)
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

	/// <summary>A job's part of its half, rounded down to whole people.</summary>
	private static int Part(int half, int share) => (int)((long)half * share / Whole);
}
