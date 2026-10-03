using System.Collections.Generic;
using Godot;

/// <summary>The lord's hand on the labour bar: a figure moved, the bar divided between the halves,
/// a site shut or opened, and the rivals' farms-first order.</summary>
public static partial class Labour
{
	// --- the lord's hand ----------------------------------------------------------------------

	/// <summary>The lord asks for a number of hands on one job, and the proportions are rewritten so
	/// the deal gives them — the way dragging a figure rewrites them in the original.
	///
	/// A figure taken off a job stands idle: its share is simply not given to anybody else. A figure
	/// put on one comes from the idle and only from the idle, as in Lords of the Realm — nobody is
	/// pulled off another job behind the lord's back; he takes them off it himself first. When the
	/// idle it draws on stand across the bar, the bar moves to fetch them. Asking for hands at a site
	/// he had shut opens it.</summary>
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
			// The idle alone, wherever they stand — a figure fetched from the other half moves the bar.
			need -= Draw(ref ownIdle, need);
			int fetched = Draw(ref otherIdle, need);
			need -= fetched;

			ownHalf += fetched;
			otherHalf -= fetched;
			want[job] -= need; // as in the original, only the idle can be put to work
		}

		// The bar moves only when a figure crossed it, and to the man: a whole percent of five hundred
		// men rounded away took five of them off a job nobody had touched.
		if (ownHalf != (farming ? people - industry : industry))
		{
			p.IndustryShare = ShareOf(farming ? otherHalf : ownHalf, people);
		}
		int industryNow = Pct(people, p.IndustryShare);
		Proportion(p, Farm, people - industryNow, want);
		Proportion(p, Industry, industryNow, want);
		Deal(p, def, b, season);
	}

	/// <summary>The most one job could be given now: what it has and every idle hand, never past what
	/// it can use.</summary>
	public static int Most(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, string job) =>
		Mathf.Min(Ceiling(p, def, b, season, job),
			Mathf.Min(Mathf.Max(0, p.Workers), Hands(p, job) + EconomySimulation.Idle(p, def, b, season)));

	/// <summary>The lord moves the bar between the farm and the industry, and the county is dealt again.</summary>
	public static void Divide(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, double toIndustry)
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
}
