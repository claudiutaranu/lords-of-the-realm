using System.Collections.Generic;
using Godot;

/// <summary>A lord's masons and the watch on his walls. Each season a county with the stores for the
/// next rung of the ladder may order it — may, because a lord does not build the season he can, but
/// when he gets round to it: LordBuildChance a season, by difficulty, so one county has its palisade
/// in the second year and its neighbour in the fifth. Once a wall stands he puts men on it; walls with
/// nobody on them are a gate anybody walks through.
///
/// How high he climbs is the campaign's to say (provinces.json "rivalWallsUpTo"): on the first map the
/// rivals raise timber and nothing more, so the player meets stone for the first time on his own
/// masons' account.</summary>
public static class LordWalls
{
	/// <summary>The rung this county would build next, or empty if it is at the top, at the campaign's
	/// ceiling, or already building. <paramref name="upTo"/> is the highest rung allowed, by key;
	/// empty allows the whole ladder.</summary>
	public static string Next(ProvinceEconomy p, string upTo)
	{
		if (p.Building.Length > 0)
		{
			return "";
		}

		string next = Fortifications.Next(p.Fortification);
		bool allowed = upTo.Length == 0 || Fortifications.Of(next).Rung <= Fortifications.Of(upTo).Rung;
		return next.Length > 0 && allowed ? next : "";
	}

	/// <summary>Orders the next rung if the stores will pay for it and the season is the one he gets
	/// round to it. Paid in full on the order, the way the player's fortifications room pays.</summary>
	public static void Fortify(ProvinceEconomy p, GameBalance b, Difficulty skill, RandomNumberGenerator dice,
		string upTo)
	{
		string next = Next(p, upTo);
		if (next.Length == 0 || p.BesiegedFrom.Length > 0)
		{
			return;
		}

		Fortifications.Wall wall = Fortifications.Of(next);
		foreach ((string store, int amount) in wall.Cost)
		{
			if (p.Stored(store) < amount)
			{
				return;
			}
		}

		if (dice.Randf() >= b.LordBuildChance[(int)skill])
		{
			return;
		}

		foreach ((string store, int amount) in wall.Cost)
		{
			p.Add(store, -amount);
		}

		p.Building = next;
		p.BuildLeft = wall.Seasons * b.MasonsPerBuildSeason;
		p.BuildSeasonsLeft = wall.Seasons;
	}

	/// <summary>How much of a store he holds back from market for the rung he means to build next.</summary>
	public static int Saving(ProvinceEconomy p, string store, string upTo)
	{
		string next = Next(p, upTo);
		return next.Length == 0 ? 0 : Fortifications.Of(next).Cost.GetValueOrDefault(store);
	}

	/// <summary>Puts men on walls that have too few: up to LordWatch of them, out of his own companies
	/// standing at home, largest first. A wall is not a garrison, and a lord who built one and left it
	/// empty had built a gate for the first army past to walk through.</summary>
	public static void ManTheWalls(ProvinceEconomy p, GameBalance b)
	{
		int wanted = b.LordWatch - p.CastleMen;
		if (p.Fortification.Length == 0 || wanted <= 0)
		{
			return;
		}

		var home = p.Armies.FindAll(army => army.County == p.ProvinceName);
		home.Sort((x, y) => y.Strength != x.Strength ? y.Strength.CompareTo(x.Strength) : x.Id.CompareTo(y.Id));
		foreach (FieldArmy army in home)
		{
			var units = new List<string>(army.Men.Keys);
			units.Sort(System.StringComparer.Ordinal);
			foreach (string unit in units)
			{
				int up = Mathf.Min(wanted, army.Men[unit]);
				army.Men[unit] -= up;
				if (army.Men[unit] <= 0)
				{
					army.Men.Remove(unit);
				}

				p.Castle[unit] = p.Castle.GetValueOrDefault(unit) + up;
				wanted -= up;
				if (wanted <= 0)
				{
					break;
				}
			}

			if (army.Strength == 0)
			{
				p.Disband(army);
			}

			if (wanted <= 0)
			{
				return;
			}
		}
	}
}
