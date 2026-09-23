using System.Collections.Generic;
using Godot;

/// <summary>The rival lords' war, once a season: their companies gather, pick the nearest county that
/// is not theirs, march on it when they like their chances, and fight for it at its gate or sit down
/// before it. The same rules the player's men are held to — TurnManager.March, Attack, Besiege — and
/// the same ground: the march grid the map lays out, handed over through <see cref="Way"/>.
///
/// Difficulty decides how sure a lord has to be (LordAttackOdds), how soon he starts
/// (LordFirstMarch), and whether the player's counties are on his list at all (LordWillAttackPlayer).
/// An easy lord takes the empty country; a middling one takes what the player leaves weak; a hard
/// one takes risks, and besieges what he cannot storm.</summary>
public sealed class LordsCampaign
{
	/// <summary>The cheapest road between two map pixels, every step with what has been spent by then,
	/// or empty where there is none — MarchGrid.Way, as the map has it.</summary>
	public delegate List<(Vector2 At, float Spent)> Way(Vector2 from, Vector2 to);

	/// <summary>One company's march this season: where it set out from and every step it took, for
	/// the map to walk its banner along.</summary>
	public record RivalMarch(string Army, Vector2 From, List<Vector2> Road);

	private readonly Way _way;
	private readonly System.Func<Vector2, string> _countyAt;
	private readonly Dictionary<string, Vector2> _towns;
	private readonly float _reach;
	private readonly RandomNumberGenerator _dice = new();

	/// <param name="towns">The village of every county that can be taken, by name.</param>
	/// <param name="reach">How close to a village counts as standing at its gate
	/// (MapDecoration.TownRing).</param>
	public LordsCampaign(Way way, System.Func<Vector2, string> countyAt, Dictionary<string, Vector2> towns, float reach)
	{
		_way = way;
		_countyAt = countyAt;
		_towns = towns;
		_reach = reach;
		_dice.Randomize();
	}

	/// <summary>Moves every rival company for the season. Returns what the player has to hear: his
	/// counties attacked, besieged or lost.</summary>
	/// <param name="walked">Every march made, in the order made, for the map to show.</param>
	public List<FiredEvent> March(TurnManager turns, GameBalance b, List<RivalMarch> walked)
	{
		var news = new List<FiredEvent>();
		int skill = (int)turns.Difficulty;
		if (turns.Turn < b.LordFirstMarch[skill])
		{
			return news;
		}

		Gather(turns);
		var marched = new HashSet<FieldArmy>();
		foreach (FieldArmy army in turns.Armies())
		{
			string realm = turns.RealmOf(army);
			if (army.Strength == 0 || realm == turns.PlayerRealm || realm.Length == 0 || marched.Contains(army))
			{
				continue;
			}

			string besieged = Besieging(turns, army);
			if (besieged.Length > 0)
			{
				// Still at the gate. He storms it the season he likes his chances on the walls; until
				// then the larder behind them does the work.
				if (Odds(turns, army, besieged, walls: true, b) >= b.LordAttackOdds[skill])
				{
					Strike(turns, army, besieged, Pixel(army), walls: true, news);
				}

				continue;
			}

			string target = Prize(turns, army, realm, b, skill);
			if (target == null)
			{
				continue;
			}

			if (army.County == target && Pixel(army).DistanceTo(_towns[target]) <= _reach)
			{
				Engage(turns, army, target, b, skill, news);
				continue;
			}

			// Every company of his standing in the same county goes as one: the day is reckoned on all
			// of them, they all set out, and they fall in together at the gate. Weighed one company at
			// a time, none of them ever liked its chances and nobody marched at all.
			List<FieldArmy> host = Host(turns, army);
			var together = new Dictionary<string, int>();
			foreach (FieldArmy company in host)
			{
				marched.Add(company);
				foreach ((string unit, int men) in company.Men)
				{
					together[unit] = together.GetValueOrDefault(unit) + men;
				}
			}

			if (ProvinceEconomy.Men(together) < b.LordLeastHost
				|| Odds(turns, together, target, walls: false, b, marched: true) < b.LordAttackOdds[skill])
			{
				continue;
			}

			foreach (FieldArmy company in host)
			{
				Walk(turns, company, target, walked);
			}

			if (army.County == target && Pixel(army).DistanceTo(_towns[target]) <= _reach)
			{
				Engage(turns, army, target, b, skill, news);
			}
		}

		return news;
	}

	/// <summary>Companies raised by the same county and standing at home fall in under one banner
	/// before anybody marches: a lord who sends his army out one company at a time loses it that way.
	/// Only the same county's: a merged company is paid and fed by the county it merged into, and
	/// merging every county's men into one host put the whole war on one county's granary.
	/// Companies of different counties join at the gate they are all marching on (<see cref="Muster"/>).</summary>
	private static void Gather(TurnManager turns)
	{
		var hosts = new Dictionary<string, FieldArmy>();
		foreach (FieldArmy army in turns.Armies())
		{
			if (army.Strength == 0 || turns.RealmOf(army) == turns.PlayerRealm || army.County != army.Home
				|| Besieging(turns, army).Length > 0)
			{
				continue;
			}

			if (hosts.TryGetValue(army.Home, out FieldArmy host))
			{
				turns.Merge(host, army);
			}
			else
			{
				hosts[army.Home] = army;
			}
		}
	}

	/// <summary>This company and every other of the same lord standing in the same county, not
	/// sitting before anybody's gate.</summary>
	private static List<FieldArmy> Host(TurnManager turns, FieldArmy army)
	{
		string realm = turns.RealmOf(army);
		return turns.Armies().FindAll(other => other.Strength > 0 && other.County == army.County
			&& turns.RealmOf(other) == realm && Besieging(turns, other).Length == 0);
	}

	/// <summary>Every company of the lord's standing at this gate falls in with the one about to fight
	/// for it, so the day is fought with all of them rather than one at a time.</summary>
	private void Muster(TurnManager turns, FieldArmy army, string county)
	{
		string realm = turns.RealmOf(army);
		foreach (FieldArmy other in turns.Armies())
		{
			if (other != army && other.Strength > 0 && other.County == county && turns.RealmOf(other) == realm
				&& Pixel(other).DistanceTo(_towns[county]) <= _reach && Besieging(turns, other).Length == 0)
			{
				turns.Merge(army, other);
			}
		}
	}

	/// <summary>The nearest county he can walk to that is not his and that his difficulty lets him
	/// want. Straight-line nearest first; the road is only asked of the first few.</summary>
	private string Prize(TurnManager turns, FieldArmy army, string realm, GameBalance b, int skill)
	{
		Vector2 here = Pixel(army);
		var wanted = new List<string>();
		foreach ((string county, Vector2 _) in _towns)
		{
			string holder = turns.AnyProvince(county)?.Realm ?? "";
			if (holder == realm || (holder == turns.PlayerRealm && b.LordWillAttackPlayer[skill] == 0))
			{
				continue;
			}

			wanted.Add(county);
		}

		wanted.Sort((x, y) =>
		{
			int nearer = here.DistanceSquaredTo(_towns[x]).CompareTo(here.DistanceSquaredTo(_towns[y]));
			return nearer != 0 ? nearer : string.CompareOrdinal(x, y);
		});

		for (int tried = 0; tried < wanted.Count && tried < 3; tried++)
		{
			if (army.County == wanted[tried] || _way(here, _towns[wanted[tried]]).Count > 0)
			{
				return wanted[tried];
			}
		}

		return null;
	}

	/// <summary>As far along the road as this season's legs carry them.</summary>
	private void Walk(TurnManager turns, FieldArmy army, string target, List<RivalMarch> walked)
	{
		Vector2 from = Pixel(army);
		List<(Vector2 At, float Spent)> road = _way(from, _towns[target]);
		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			if (road[step].Spent <= army.MarchLeft)
			{
				halt = step;
			}
		}

		if (halt < 0)
		{
			return;
		}

		(Vector2 at, float spent) = road[halt];
		string county = _countyAt(at);
		if (county.Length > 0 && turns.March(army, county, at, spent))
		{
			var steps = new List<Vector2>(halt + 1);
			for (int step = 0; step <= halt; step++)
			{
				steps.Add(road[step].At);
			}

			walked.Add(new RivalMarch(army.Key, from, steps));
		}
	}

	/// <summary>At the gate: the field first, then the walls, the way the player's battle table
	/// runs it. What he will not storm he sits down before, if he is the kind of lord who does.</summary>
	private void Engage(TurnManager turns, FieldArmy army, string county, GameBalance b, int skill,
		List<FiredEvent> news)
	{
		Muster(turns, army, county);
		Vector2 at = Pixel(army);
		Defenders against = turns.DefendersOf(county);
		if (ProvinceEconomy.Men(against.Field) > 0 || !against.Held)
		{
			// Somebody in the open to beat first — or nobody at all, and the gate is simply walked
			// through; a county held by nobody standing in it is still only taken at its gate.
			if (Odds(turns, army, county, walls: false, b) < b.LordAttackOdds[skill])
			{
				return;
			}

			Strike(turns, army, county, at, walls: false, news);
		}

		ProvinceEconomy there = turns.AnyProvince(county);
		if (army.Strength == 0 || there == null || there.Realm == turns.RealmOf(army) || !turns.DefendersOf(county).Held)
		{
			return;
		}

		if (Odds(turns, army, county, walls: true, b) >= b.LordAttackOdds[skill])
		{
			Strike(turns, army, county, at, walls: true, news);
		}
		else if (b.LordBesieges[skill] == 1 && turns.Besiege(army, county))
		{
			Tell(turns, county, "county-besieged", news);
		}
	}

	private static void Strike(TurnManager turns, FieldArmy army, string county, Vector2 at, bool walls,
		List<FiredEvent> news)
	{
		bool players = turns.AnyProvince(county)?.Realm == turns.PlayerRealm;
		Battle.Result day = turns.Attack(army, county, at, walls);
		if (!players)
		{
			return;
		}

		bool lost = turns.AnyProvince(county)?.Realm != turns.PlayerRealm;
		Tell(turns, county, lost ? "county-lost" : day.AttackerWon ? "county-besieged" : "invaders-repelled", news);
	}

	private static void Tell(TurnManager turns, string county, string id, List<FiredEvent> news)
	{
		GameEvent said = EventEngine.Find(id);
		if (said != null && (turns.AnyProvince(county)?.Realm == turns.PlayerRealm || id == "county-lost"))
		{
			news.Add(new FiredEvent(county, said, FromThePeople: true));
		}
	}

	/// <summary>How often he would carry the day, fought out on copies so nothing real is touched.</summary>
	private float Odds(TurnManager turns, FieldArmy army, string county, bool walls, GameBalance b,
		bool marched = false) =>
		Odds(turns, army.Men, county, walls, b, marched || army.MarchLeft <= 0f);

	private float Odds(TurnManager turns, Dictionary<string, int> roster, string county, bool walls, GameBalance b,
		bool marched)
	{
		Defenders live = turns.DefendersOf(county);
		if (ProvinceEconomy.Men(walls ? live.Castle : live.Field) == 0)
		{
			return 1f;
		}

		int won = 0;
		for (int trial = 0; trial < b.LordOddsTrials; trial++)
		{
			Defenders copy = live with
			{
				Field = new Dictionary<string, int>(live.Field),
				Castle = new Dictionary<string, int>(live.Castle),
			};
			var men = new Dictionary<string, int>(roster);
			Battle.Result day = walls
				? Battle.OnTheWalls(men, copy, marched, b, _dice)
				: Battle.InTheField(men, copy, marched, b, _dice);
			won += day.AttackerWon ? 1 : 0;
		}

		return (float)won / Mathf.Max(1, b.LordOddsTrials);
	}

	private static string Besieging(TurnManager turns, FieldArmy army)
	{
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			if (county.BesiegedFrom == army.Key)
			{
				return county.ProvinceName;
			}
		}

		return "";
	}

	/// <summary>Where the company stands: where it was last marched to, or its own village if it has
	/// never been sent anywhere.</summary>
	private Vector2 Pixel(FieldArmy army)
	{
		var at = new Vector2(army.X, army.Y);
		return at != Vector2.Zero ? at : _towns.GetValueOrDefault(army.Home);
	}
}
