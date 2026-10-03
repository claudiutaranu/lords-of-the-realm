using System.Collections.Generic;

/// <summary>A lord's companies drawn into one host: those standing together merged into the
/// largest, the smaller marched to join it, and new men mustered at its side.</summary>
public partial class LordsCampaign
{
	/// <summary>Every company of the same lord standing in the same county falls in under one banner
	/// — the largest — before anybody marches: a lord who sends his army out one company at a time
	/// loses it that way, and a map with a banner for every levy is a map nobody can read. A merged
	/// company is fed by the county that raised the largest of them.</summary>
	private static void Gather(TurnManager turns)
	{
		var companies = turns.Armies().FindAll(army => army.Strength > 0 && turns.RealmOf(army) != turns.PlayerRealm
			&& !army.KeepsItsBanner && Besieging(turns, army).Length == 0);
		companies.Sort((x, y) => y.Strength != x.Strength ? y.Strength.CompareTo(x.Strength) : string.CompareOrdinal(x.Key, y.Key));
		var hosts = new Dictionary<(string Realm, string County), FieldArmy>();
		foreach (FieldArmy army in companies)
		{
			var where = (turns.RealmOf(army), army.County);
			if (hosts.TryGetValue(where, out FieldArmy host))
			{
				turns.Merge(host, army);
			}
			else
			{
				hosts[where] = army;
			}
		}
	}

	/// <summary>A lord's smaller companies march to join his main host, wherever it stands, so his men
	/// are one army and not a banner in every county. They stop short of a gate the host is waiting
	/// before, and fall in with it the season they share its county (<see cref="Gather"/>).</summary>
	private void Rally(TurnManager turns, List<RivalMarch> walked, HashSet<FieldArmy> marched)
	{
		var mains = new Dictionary<string, FieldArmy>();
		foreach (FieldArmy army in turns.Armies())
		{
			string realm = turns.RealmOf(army);
			if (army.Strength == 0 || realm == turns.PlayerRealm || realm.Length == 0 || army.Raider
				|| Besieging(turns, army).Length > 0)
			{
				continue;
			}

			if (!mains.TryGetValue(realm, out FieldArmy main) || army.Strength > main.Strength)
			{
				mains[realm] = army;
			}
		}

		foreach (FieldArmy army in new List<FieldArmy>(turns.Armies()))
		{
			string realm = turns.RealmOf(army);
			if (!mains.TryGetValue(realm, out FieldArmy main) || army == main || army.Strength == 0 || army.Raider
				|| army.County == main.County || Besieging(turns, army).Length > 0 || !_towns.ContainsKey(main.County))
			{
				continue;
			}

			bool ours = turns.AnyProvince(main.County)?.Realm == realm;
			Walk(turns, army, main.County, walked, gather: !ours);
			marched.Add(army);
		}
	}

	/// <summary>How far from a gate a lord's companies gather before they go in together: a morning's
	/// march, far enough that the town does not turn out on them.</summary>
	private float Gathering => _reach * 4f;

	/// <summary>Every company of a lord's that can be sent: in the field, with men in it, and not
	/// keeping a siege.</summary>
	private static List<FieldArmy> Free(TurnManager turns, string realm) =>
		turns.Armies().FindAll(army => army.Strength > 0 && turns.RealmOf(army) == realm && !army.Raider
			&& Besieging(turns, army).Length == 0);

	private static Dictionary<string, int> Roster(List<FieldArmy> companies)
	{
		var all = new Dictionary<string, int>();
		foreach (FieldArmy company in companies)
		{
			foreach ((string unit, int men) in company.Men)
			{
				all[unit] = all.GetValueOrDefault(unit) + men;
			}
		}

		return all;
	}

	/// <summary>This company and every other of the same lord standing in the same county, not
	/// sitting before anybody's gate.</summary>
	private static List<FieldArmy> Host(TurnManager turns, FieldArmy army)
	{
		string realm = turns.RealmOf(army);
		return turns.Armies().FindAll(other => other.Strength > 0 && other.County == army.County && !other.Raider
			&& turns.RealmOf(other) == realm && Besieging(turns, other).Length == 0);
	}

	/// <summary>Every company of the lord's gathered before this gate falls in with the one about to
	/// fight for it, so the day is fought with all of them rather than one at a time.</summary>
	private void Muster(TurnManager turns, FieldArmy army, string county)
	{
		string realm = turns.RealmOf(army);
		foreach (FieldArmy other in turns.Armies())
		{
			if (other != army && other.Strength > 0 && other.County == county && turns.RealmOf(other) == realm
				&& Pixel(other).DistanceTo(_towns[county]) <= Gathering && Besieging(turns, other).Length == 0)
			{
				turns.Merge(army, other);
			}
		}
	}
}
