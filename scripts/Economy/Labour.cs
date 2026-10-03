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
public static partial class Labour
{
	public const string Grain = "grain";
	public const string Cattle = "cattle";
	public const string Reclaim = "reclaim";
	public const string Castle = "castle";
	public const string Iron = "iron";
	public const string Stone = "stone";
	public const string Wood = "wood";

	/// <summary>A diggings as the steward names it.</summary>
	public static string SiteName(string site) => site switch
	{
		Iron => "mine",
		Stone => "quarry",
		_ => "woodcutters' camp",
	};
	public const string Smith = "smith";

	/// <summary>The farm half and the industry half, in the order the original numbers its jobs.</summary>
	public static readonly string[] Farm = { Grain, Cattle, Reclaim };
	public static readonly string[] Industry = { Castle, Iron, Stone, Wood, Smith };

	/// <summary>Every job a hand can be on, the farm's first; idle is whoever is on none of them.</summary>
	public static readonly string[] Jobs = { Grain, Cattle, Reclaim, Castle, Iron, Stone, Wood, Smith };

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

	/// <summary>Reads a save made the days the shares were whole percents: every one of them at a
	/// hundred or less and together a whole half's worth. Read in hundredths it would stand the county
	/// idle. Asked once, of an old file as it loads — files of that version were written on both sides
	/// of the change, which is what the look at the figures is for.</summary>
	public static void InHundredths(ProvinceEconomy p)
	{
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
		Grain => Husbandry.FieldWork(p, season),
		Cattle => 2 * Husbandry.Herdsmen(p.Cattle), // six a head: past three they only help it breed
		Reclaim => Husbandry.ReclaimWork(p, b),
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
	/// county's capacity for it says only whether the ore or the stone or the wood is there at all;
	/// one an enemy army has stood on is shut for its seasons (ProvinceEconomy.Occupied).</summary>
	private static int Open(ProvinceEconomy p, string site, int capacity) =>
		capacity > 0 && !p.IsShut(site) && !p.Occupied.ContainsKey(site) ? Bottomless : 0;

	/// <summary>The fewest a job needs before it is short-handed, for the screen alone — the deal
	/// never reads it. The fields, the herd, the reclaiming, the scaffolding and the anvil need what
	/// they can use; a diggings is never short, only more or less busy.</summary>
	public static int Wanted(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string job) =>
		job is Iron or Stone or Wood ? 0
		: job == Cattle ? Husbandry.Herdsmen(p.Cattle) // the ceiling is twice this: extra hands only help it breed
		: Ceiling(p, def, b, season, job);

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

	/// <summary>Writes a half's shares from the hands wanted on each job, rounded up so the deal
	/// never comes out a man short of what was asked; a job's own ceiling takes off any excess.</summary>
	private static void Proportion(ProvinceEconomy p, string[] jobs, int half, Dictionary<string, int> want)
	{
		foreach (string job in jobs)
		{
			p.Shares[job] = half <= 0 ? 0 : Mathf.Clamp((int)(((long)want[job] * Whole + half - 1) / half), 0, Whole);
		}
	}

	public static string JobOf(ResourceType type) => type switch
	{
		ResourceType.Grain => Grain,
		ResourceType.Cattle => Cattle,
		ResourceType.Wood => Wood,
		ResourceType.Stone => Stone,
		_ => Iron,
	};
}
