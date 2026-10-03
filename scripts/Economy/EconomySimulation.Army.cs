using System.Collections.Generic;
using Godot;

/// <summary>The county's men under arms in its season: the garrison paid or thinned by the unpaid
/// walking off, and men conscripted out of the people.</summary>
public static partial class EconomySimulation
{
	/// <summary>The season's wages, out of what the reeve just brought in. A treasury that cannot
	/// cover them pays what it can and loses the difference in men: the unpaid do not stand about
	/// being unpaid, they go home. An army bigger than its county can carry therefore empties the
	/// purse first and then thins itself, which is the honest end of overreaching — rather than a
	/// negative number in the treasury that nothing in the game knows how to answer.</summary>
	private static void PayTheGarrison(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		int owed = Mathf.CeilToInt(p.Soldiers * b.WagePerSoldier);
		if (owed <= 0)
		{
			return;
		}

		summary.Wages = Mathf.Min(owed, p.Gold);
		p.Gold -= summary.Wages;

		float unpaid = (float)(owed - summary.Wages) / owed;
		if (unpaid > 0f)
		{
			summary.Deserted = Disband(p, unpaid * b.DesertionRate);
		}
	}

	/// <summary>Thins every company by the same share and gives back how many left. Rounded up, so
	/// a company that is owed anything at all loses somebody — a desertion of nought men is a
	/// consequence the player cannot see.</summary>
	private static int Disband(ProvinceEconomy p, float share)
	{
		int gone = Thin(p.Castle, share);
		foreach (FieldArmy standing in p.Armies)
		{
			gone += Thin(standing.Men, share);
		}

		// A company nobody is left in is a company that is not there any more, rather than a banner
		// standing over an empty field.
		p.Bury();
		return gone;
	}

	/// <summary>Thins one roster. The walls go on the same terms as the field: a man on the gate who
	/// is not paid walks home like anybody else, and a castle that quietly kept its garrison for
	/// nothing would be the one place in the realm where soldiering was free.</summary>
	private static int Thin(Dictionary<string, int> roster, float share)
	{
		int gone = 0;
		foreach (string unit in new List<string>(roster.Keys))
		{
			int leaving = Mathf.Min(roster[unit], Mathf.CeilToInt(roster[unit] * share));
			if (leaving <= 0)
			{
				continue;
			}

			gone += leaving;
			roster[unit] -= leaving;
			if (roster[unit] <= 0)
			{
				roster.Remove(unit);
			}
		}

		return gone;
	}

	/// <summary>Takes men out of the county and puts them under arms, which costs it twice: the
	/// people are gone from the roll, and the hands they were are gone from the work.
	///
	/// The idle are spent first — they are standing about for exactly this — and whatever is still
	/// owed comes off the industries in proportion to what each of them holds, so a levy does not
	/// gut the harvest and leave the mine untouched. A county cannot have more men at work than it
	/// has men, and the screens would otherwise go on reading the old allocation as if it were
	/// still being worked.</summary>
	public static void Conscript(ProvinceEconomy p, int men)
	{
		// The sons are resented the day they are taken, by the share of the county that went — as in
		// the original, where a county at nothing can give no more.
		p.Loyalty = Mathf.Max(0f, p.Loyalty - Livelihood.RecruitingCost(men, p.Population));
		p.Population = Mathf.Max(0, p.Population - men);
		FitWorkforce(p);
	}
}
