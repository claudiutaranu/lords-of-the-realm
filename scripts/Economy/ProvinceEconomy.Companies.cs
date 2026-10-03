using System.Collections.Generic;
using Godot;

/// <summary>The county's companies handled: raised, adopted from a fallen county, split, buried
/// after a fight, mustered into and drawn from, and the readiest of them found. Only methods live
/// here; every saved figure stays in ProvinceEconomy.cs, in the order a save writes it.</summary>
public partial class ProvinceEconomy
{
	/// <summary>Musters a new company at the county's seat, with the ground the caller says it has
	/// in its legs. Ids are never reused: an order given to a banner cannot land on a different
	/// company later.</summary>
	public FieldArmy Raise(float marchLeft = 0f)
	{
		var raised = new FieldArmy
		{
			Home = ProvinceName,
			Id = NextId(),
			County = ProvinceName,
			MarchLeft = marchLeft,
		};

		Armies.Add(raised);
		return raised;
	}

	/// <summary>Takes another county's company onto this county's books — what happens to a company
	/// in the field when the county that raised it falls: somebody else has to pay and feed it. It
	/// keeps its men and its legs and takes a place in its new county's line.</summary>
	public void Adopt(FieldArmy company)
	{
		company.Home = ProvinceName;
		company.Id = NextId();
		Armies.Add(company);
	}

	/// <summary>The next number this county has not used. Never reused, so an order given to a
	/// banner cannot land on a different company later.</summary>
	private int NextId()
	{
		// Counted, not read off who is standing: the highest company killed or disbanded would
		// otherwise hand its number to the next one raised, and an order still held for the dead
		// company would land on the new one. A save from before the count starts from its companies.
		int id = LastArmyId + 1;
		foreach (FieldArmy standing in Armies)
		{
			id = standing.Id >= id ? standing.Id + 1 : id;
		}

		LastArmyId = id;
		return id;
	}

	/// <summary>The army of that id, or null. Saves and screens hold on to armies by name and id
	/// rather than by reference, because the object can be gone by the time they ask again.</summary>
	public FieldArmy Army(int id) => Armies.Find(standing => standing.Id == id);

	/// <summary>Takes an army off the county — wiped out, disbanded, or merged into another.</summary>
	public void Disband(FieldArmy army) => Armies.Remove(army);

	/// <summary>Cuts a company in two: the men named in <paramref name="taken"/> fall in under a new
	/// banner on the same ground, with the same legs left under it, and the rest stay where they
	/// were. Null when that would leave a banner standing over nobody — neither side of a split may
	/// be empty, and a company that walks off entire has been renamed rather than cut.
	///
	/// What is asked for is clamped to what is there rather than trusted: a screen is a screen, and
	/// the roster is what actually decides how many of a kind there are to give away.
	///
	/// Where the two halves then go is the map's business. What is settled here is only who is
	/// whose, because that is the part a save has to carry.</summary>
	public FieldArmy Split(FieldArmy army, Dictionary<string, int> taken)
	{
		// A hired band is one company under one captain: it is not cut, as it is not joined.
		if (!Armies.Contains(army) || army.IsHired)
		{
			return null;
		}

		var goes = new Dictionary<string, int>();
		int marching = 0;
		foreach ((string kind, int asked) in taken)
		{
			int men = Mathf.Clamp(asked, 0, army.Men.GetValueOrDefault(kind));
			if (men > 0)
			{
				goes[kind] = men;
				marching += men;
			}
		}

		if (marching == 0 || marching == army.Strength)
		{
			return null;
		}

		FieldArmy half = Raise(army.MarchLeft);
		half.County = army.County;
		half.X = army.X;
		half.Y = army.Y;
		foreach ((string kind, int men) in goes)
		{
			half.Men[kind] = men;
			// A kind nobody is left carrying is gone from the roster rather than left at nought.
			if ((army.Men[kind] -= men) == 0)
			{
				army.Men.Remove(kind);
			}
		}

		return half;
	}

	/// <summary>Drops every company with nobody left in it. Called after anything that can kill men,
	/// so a banner is never left standing over an empty field.</summary>
	public void Bury()
	{
		for (int index = Armies.Count - 1; index >= 0; index--)
		{
			if (Armies[index].Strength == 0)
			{
				Armies.RemoveAt(index);
			}
		}
	}

	/// <summary>How many of a kind the county has standing in the field, whichever of its armies
	/// they are in.</summary>
	public int Mustered(string unit)
	{
		int men = 0;
		foreach (FieldArmy standing in Armies)
		{
			men += standing.Men.GetValueOrDefault(unit);
		}

		return men;
	}

	/// <summary>The company a county-wide order falls to: the biggest one it still has ground for,
	/// or null when every man it has is spent or on the walls. The sidebar's March is a county's
	/// button rather than an army's, and this is what it means by "the army".</summary>
	public FieldArmy Readiest()
	{
		FieldArmy best = null;
		foreach (FieldArmy standing in Armies)
		{
			if (standing.MarchLeft > 0f && standing.Strength > 0
				&& (best == null || standing.Strength > best.Strength))
			{
				best = standing;
			}
		}

		return best;
	}

	/// <summary>Every kind of soldier the county has anywhere — standing in the field with one of
	/// its companies, or on the gate. What the walls are manned off.</summary>
	public List<string> Companies()
	{
		var kinds = new List<string>();
		foreach (FieldArmy standing in Armies)
		{
			foreach (string unit in standing.Men.Keys)
			{
				if (!kinds.Contains(unit))
				{
					kinds.Add(unit);
				}
			}
		}

		foreach (string unit in Castle.Keys)
		{
			if (!kinds.Contains(unit))
			{
				kinds.Add(unit);
			}
		}

		return kinds;
	}

	/// <summary>Sets how many of a kind stand in the field — what the walls take and give back.
	/// Men coming down off the gate fall in with the company at the seat, and men going up are taken
	/// off the companies raised last, so a lord manning his walls empties his newest levy before he
	/// touches the army he has standing in the field.</summary>
	public void Muster(string unit, int men, float marchLeft = 0f)
	{
		for (int index = Armies.Count - 1; index >= 0 && Mustered(unit) > men; index--)
		{
			int has = Armies[index].Men.GetValueOrDefault(unit);
			int keeps = Mathf.Max(0, has - (Mustered(unit) - men));
			if (keeps > 0)
			{
				Armies[index].Men[unit] = keeps;
			}
			else
			{
				Armies[index].Men.Remove(unit);
			}
		}

		int short_ = men - Mustered(unit);
		if (short_ > 0)
		{
			FieldArmy seat = Armies.Count > 0 ? Armies[0] : Raise(marchLeft);
			seat.Men[unit] = seat.Men.GetValueOrDefault(unit) + short_;
		}

		Bury();
	}

	/// <summary>The company an old save is being read into, made on the first line that needs it.
	/// One with nobody in it is dropped when the save is restored (see TurnManager.Restore).</summary>
	private FieldArmy Older() => Armies.Count > 0 ? Armies[0] : Raise();

	/// <summary>How many men a roster comes to. Public because whoever is counting the defenders of
	/// a county that nobody holds is counting a roster that belongs to no province.</summary>
	public static int Men(Dictionary<string, int> roster)
	{
		int men = 0;
		foreach (int company in roster.Values)
		{
			men += company;
		}

		return men;
	}
}
