using System.Collections.Generic;
using Godot;

/// <summary>The county's people between seasons: their numbers written down, moving house to the
/// happiest neighbour, and what the realm's rates make them resent.</summary>
public partial class TurnManager
{
	/// <summary>Adds this season's goodwill into the county's record of the year. A running mean, so
	/// a year part-way through reads as the year so far rather than as a hole in the chart.
	///
	/// A save written before counties kept a record has none, and its early years are drawn at what
	/// the county stands at today — there is nothing else to draw them from, and a chart that begins
	/// at the year the save was loaded looks like the campaign did.</summary>
	private void Remember(ProvinceEconomy province)
	{
		int year = (Turn - 1) / SeasonsPerYear;
		int seasonsIn = ((Turn - 1) % SeasonsPerYear) + 1;
		while (province.HappinessByYear.Count <= year)
		{
			province.HappinessByYear.Add(province.Loyalty);
		}

		province.HappinessByYear[year] += (province.Loyalty - province.HappinessByYear[year]) / seasonsIn;
	}

	/// <summary>How many seasons of a county's people are kept. ponytail: a flat cap of twenty years;
	/// a reign that wants its whole length charted wants the old seasons thinned, not dropped.</summary>
	private const int MostSeasonsKept = 80;

	/// <summary>Writes down the season just ended for the people table: the count, the health, and
	/// the births and deaths the season's arithmetic made.</summary>
	private void RememberPeople(ProvinceEconomy province, TurnSummary summary)
	{
		province.PeopleBySeason.Add(new PeopleSeason
		{
			Turn = Turn,
			Population = province.Population,
			Health = province.Health,
			Born = summary.Born,
			Died = summary.Died,
		});

		if (province.PeopleBySeason.Count > MostSeasonsKept)
		{
			province.PeopleBySeason.RemoveAt(0);
		}
	}

	/// <summary>Which counties border which, by name, as the campaign's map has them
	/// (provinces.json "neighbours"). Empty — the checks — and nobody moves house.</summary>
	public Dictionary<string, List<string>> Neighbours { get; set; } = new();

	/// <summary>The season's moving house, the original's rule: from every county, some of its people
	/// leave for its happiest neighbour if that neighbour is happier (Livelihood.Movers). Worked out
	/// for every county before anybody moves, so one county's arrivals do not change whether they
	/// themselves would have left.</summary>
	private void Migrate()
	{
		var moves = new List<(ProvinceEconomy From, ProvinceEconomy To, int People)>();
		foreach (ProvinceEconomy county in _provincesByName.Values)
		{
			ProvinceEconomy best = null;
			foreach (string name in Neighbours.GetValueOrDefault(county.ProvinceName, new List<string>()))
			{
				ProvinceEconomy next = _provincesByName.GetValueOrDefault(name);
				if (next != null && (best == null || next.Loyalty > best.Loyalty))
				{
					best = next;
				}
			}

			int movers = best == null ? 0 : Livelihood.Movers(county, best.Loyalty, neutral: false);
			if (movers > 0)
			{
				moves.Add((county, best, movers));
			}
		}

		foreach ((ProvinceEconomy from, ProvinceEconomy to, int people) in moves)
		{
			int going = Mathf.Min(people, from.Population);
			from.Population -= going;
			to.Population += going;
			if (_lastSeason.TryGetValue(from.ProvinceName, out TurnSummary left))
			{
				left.Moved -= going;
				left.Restate(from);
			}

			if (_lastSeason.TryGetValue(to.ProvinceName, out TurnSummary came))
			{
				came.Moved += going;
				came.Restate(to);
			}
		}
	}

	/// <summary>What the realm's rates cost this county's happiness a season: the original's table
	/// (Livelihood.EmpireTerm) summed over every county its lord holds, this one among them — so a
	/// lord who squeezes one shire past twenty percent is resented in all of them.</summary>
	public float Resented(ProvinceEconomy province)
	{
		int term = 0;
		foreach (ProvinceEconomy other in _provincesByName.Values)
		{
			if (other.Realm == province.Realm)
			{
				term += Livelihood.EmpireTerm(other.Tax);
			}
		}

		return term;
	}

	/// <summary>How many other counties the same lord holds — what the tax table needs to know
	/// whether "other counties" means anything at all yet.</summary>
	public int OtherCounties(ProvinceEconomy province)
	{
		int held = 0;
		foreach (ProvinceEconomy other in _provincesByName.Values)
		{
			if (other != province && other.Realm == province.Realm)
			{
				held++;
			}
		}

		return held;
	}
}
