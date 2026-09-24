using System.Collections.Generic;
using System.Linq;
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

	/// <summary>How many counties each lord held last season, and the turn each may march again after
	/// taking one (LordSettles). ponytail: not saved, so a loaded game lets a settling lord march at
	/// once; save both with the campaign if that is ever noticed.</summary>
	private readonly Dictionary<string, int> _held = new();
	private readonly Dictionary<string, int> _settledBy = new();

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

		// One banner a county from the first season, marching or not.
		Gather(turns);
		if (turns.Turn < b.LordFirstMarch[skill])
		{
			return news;
		}

		Settle(turns, b, skill);
		var marched = new HashSet<FieldArmy>();
		Rally(turns, walked, marched);
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

			// A county he has only just taken is settled before he goes looking for the next.
			if (turns.Turn < _settledBy.GetValueOrDefault(realm))
			{
				continue;
			}

			// Already at a gate he wants: that is the fight, whatever else is on the map.
			string atGate = AtGate(turns, army, realm, b, skill);
			if (atGate != null)
			{
				Engage(turns, army, atGate, b, skill, news);
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

			(string target, float odds) = ProvinceEconomy.Men(together) < b.LordLeastHost
				? (null, 0f)
				: Prize(turns, army, together, realm, b, skill);
			bool stopShort = false;
			if (target == null || odds < b.LordAttackOdds[skill])
			{
				// Not on its own. The war host, then: every company of his that is free to march, which
				// fall in together at an assembly point before the gate and go in as one. Weighed one
				// county's company at a time, a lord with five hundred men spread over six counties
				// never liked his chances against a town of eighty-five and never came.
				Dictionary<string, int> everyone = Roster(Free(turns, realm));
				(target, odds) = ProvinceEconomy.Men(everyone) < b.LordLeastHost
					? (null, 0f)
					: Prize(turns, army, everyone, realm, b, skill);
				if (target == null || odds < b.LordAttackOdds[skill])
				{
					continue;
				}

				// Those already gathered before the gate, with this one: enough to go in, or wait.
				var gathered = new Dictionary<string, int>(together);
				foreach (FieldArmy other in Free(turns, realm))
				{
					if (!host.Contains(other) && Pixel(other).DistanceTo(_towns[target]) <= Gathering)
					{
						foreach ((string unit, int men) in other.Men)
						{
							gathered[unit] = gathered.GetValueOrDefault(unit) + men;
						}
					}
				}

				stopShort = Odds(turns, gathered, target, walls: false, b, marched: true) < b.LordAttackOdds[skill];
			}

			foreach (FieldArmy company in host)
			{
				Walk(turns, company, target, walked, stopShort);
			}

			if (army.County == target && Pixel(army).DistanceTo(_towns[target]) <= _reach)
			{
				Engage(turns, army, target, b, skill, news);
			}
		}

		return news;
	}

	/// <summary>A lord who holds more counties than he did last season took one, and sits down to
	/// settle it for LordSettles seasons.</summary>
	private void Settle(TurnManager turns, GameBalance b, int skill)
	{
		var now = new Dictionary<string, int>();
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			now[county.Realm] = now.GetValueOrDefault(county.Realm) + 1;
		}

		foreach ((string realm, int held) in now)
		{
			if (_held.TryGetValue(realm, out int before) && held > before)
			{
				_settledBy[realm] = turns.Turn + b.LordSettles[skill];
			}
		}

		_held.Clear();
		foreach ((string realm, int held) in now)
		{
			_held[realm] = held;
		}
	}

	/// <summary>Every company of the same lord standing in the same county falls in under one banner
	/// — the largest — before anybody marches: a lord who sends his army out one company at a time
	/// loses it that way, and a map with a banner for every levy is a map nobody can read. A merged
	/// company is fed by the county that raised the largest of them.</summary>
	private static void Gather(TurnManager turns)
	{
		var companies = turns.Armies().FindAll(army => army.Strength > 0 && turns.RealmOf(army) != turns.PlayerRealm
			&& Besieging(turns, army).Length == 0);
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
			if (army.Strength == 0 || realm == turns.PlayerRealm || realm.Length == 0 || Besieging(turns, army).Length > 0)
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
			if (!mains.TryGetValue(realm, out FieldArmy main) || army == main || army.Strength == 0
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
		turns.Armies().FindAll(army => army.Strength > 0 && turns.RealmOf(army) == realm
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
		return turns.Armies().FindAll(other => other.Strength > 0 && other.County == army.County
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

	/// <summary>How many of the nearest counties he wants he weighs before choosing one.</summary>
	private const int PrizesWeighed = 3;

	/// <summary>The counties he could want: not his, somebody's to take — a county the map draws and
	/// no economy describes cannot change hands, and a company once sat in one for twenty years — and
	/// the player's only if his difficulty lets him. Nearest first.</summary>
	private List<string> Wanted(TurnManager turns, FieldArmy army, string realm, GameBalance b, int skill)
	{
		Vector2 here = Pixel(army);
		var wanted = new List<string>();
		foreach ((string county, Vector2 _) in _towns)
		{
			string holder = turns.AnyProvince(county)?.Realm ?? "";
			if (holder == realm || !turns.CanBeTaken(county)
				|| (holder == turns.PlayerRealm && b.LordWillAttackPlayer[skill] == 0))
			{
				continue;
			}

			wanted.Add(county);
		}

		// The empty country first: while any county is still nobody's, the player's are not on his
		// list. He grows on what is free for the taking, and comes for the player once there is
		// nothing else left — which gives a lord who moves quickly the same country to race him for.
		bool free = wanted.Exists(county => turns.AnyProvince(county) == null);
		if (free)
		{
			wanted.RemoveAll(county => turns.AnyProvince(county)?.Realm == turns.PlayerRealm);
		}

		wanted.Sort((x, y) =>
		{
			int nearer = here.DistanceSquaredTo(_towns[x]).CompareTo(here.DistanceSquaredTo(_towns[y]));
			return nearer != 0 ? nearer : string.CompareOrdinal(x, y);
		});
		return wanted;
	}

	/// <summary>The gate of a county he wants, if the company is standing at one.</summary>
	private string AtGate(TurnManager turns, FieldArmy army, string realm, GameBalance b, int skill)
	{
		foreach (string county in Wanted(turns, army, realm, b, skill))
		{
			if (army.County == county && Pixel(army).DistanceTo(_towns[county]) <= _reach)
			{
				return county;
			}
		}

		return null;
	}

	/// <summary>Of the few nearest counties he can walk to, the one his host would most surely take —
	/// so a strong neighbour does not stop his war dead while a weak one sits two valleys over.
	/// Nearer wins a tie.</summary>
	private (string County, float Odds) Prize(TurnManager turns, FieldArmy army, Dictionary<string, int> host,
		string realm, GameBalance b, int skill)
	{
		Vector2 here = Pixel(army);
		string best = null;
		float bestOdds = -1f;
		int weighed = 0;
		foreach (string county in Wanted(turns, army, realm, b, skill))
		{
			if (weighed == PrizesWeighed)
			{
				break;
			}

			if (army.County != county && _way(here, _towns[county]).Count == 0)
			{
				continue;
			}

			weighed++;
			float odds = Odds(turns, host, county, walls: false, b, marched: true);
			if (odds > bestOdds)
			{
				best = county;
				bestOdds = odds;
			}
		}

		return (best, bestOdds);
	}

	/// <summary>As far along the road as this season's legs carry them — or, <paramref name="gather"/>,
	/// no nearer the gate than the assembly point, to wait there for the rest of the host.</summary>
	private void Walk(TurnManager turns, FieldArmy army, string target, List<RivalMarch> walked, bool gather = false)
	{
		Vector2 from = Pixel(army);
		List<(Vector2 At, float Spent)> road = _way(from, _towns[target]);
		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			bool tooNear = gather && road[step].At.DistanceTo(_towns[target]) < Gathering * 0.6f;
			if (road[step].Spent <= army.MarchLeft && !tooNear)
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
			Tell(turns, county, "county-besieged", news, new Dictionary<string, string>
			{
				["came"] = $"{army.Strength}",
				["walls"] = $"{ProvinceEconomy.Men(turns.DefendersOf(county).Castle)}",
			});
		}
	}

	/// <summary>One day's fighting at somebody's gate. Where it is the player's gate he is told what
	/// it cost him, in men and by kind: how many came, how many of his stood, how many of his fell and
	/// how many are still standing — or, if it went the other way, how many of theirs now hold it.</summary>
	private static void Strike(TurnManager turns, FieldArmy army, string county, Vector2 at, bool walls,
		List<FiredEvent> news)
	{
		string realm = turns.RealmOf(army);
		bool players = turns.AnyProvince(county)?.Realm == turns.PlayerRealm;
		Defenders before = turns.DefendersOf(county);
		int came = army.Strength;
		int stood = ProvinceEconomy.Men(walls ? before.Castle : before.Field);
		Battle.Result day = turns.Attack(army, county, at, walls);
		if (!players)
		{
			// Somebody else's county, or nobody's: not a fight in his hall, but the race for the
			// country is, and a player who cannot see his rival growing does not know he is in one.
			if (turns.AnyProvince(county)?.Realm == realm)
			{
				Tell(turns, county, "rival-took", news, new Dictionary<string, string>
				{
					["county"] = county,
					["came"] = $"{came}",
					["theirs"] = $"{Holding(turns, realm)}",
					["ours"] = $"{Holding(turns, turns.PlayerRealm)}",
				});
			}

			return;
		}

		bool lost = turns.AnyProvince(county)?.Realm != turns.PlayerRealm;
		Defenders after = turns.DefendersOf(county);
		int left = lost ? 0 : ProvinceEconomy.Men(walls ? after.Castle : after.Field);
		string id = lost ? "county-lost" : day.AttackerWon ? "field-lost" : "invaders-repelled";
		Tell(turns, county, id, news, new Dictionary<string, string>
		{
			["came"] = $"{came}",
			["stood"] = stood > 0 ? $"{stood}" : "gate",
			["where"] = stood == 0 ? "with nobody on it" : walls ? "on the walls" : "in the field before the gate",
			["ourlost"] = Fallen(day.DefenderLosses),
			["ourleft"] = $"{left}",
			["theirleft"] = $"{army.Strength}",
			["theirfate"] = army.Strength == 0
				? $"all {came} are dead or scattered, and not one of them went home"
				: $"{day.AttackerFell} fell and {army.Strength} went back the way they came",
			["walls"] = $"{ProvinceEconomy.Men(after.Castle)}",
		});
	}

	private static int Holding(TurnManager turns, string realm) =>
		turns.Provinces.Count(county => county.Realm == realm);

	/// <summary>A roll of the dead, the way a steward reads it: how many, then how many of each.</summary>
	private static string Fallen(Dictionary<string, int> losses)
	{
		int all = ProvinceEconomy.Men(losses);
		if (all == 0)
		{
			return "nobody";
		}

		var kinds = new List<string>();
		foreach (string unit in Units.All())
		{
			int men = losses.GetValueOrDefault(unit);
			if (men > 0)
			{
				kinds.Add($"{men} {Plural(Units.Of(unit).Name, men).ToLowerInvariant()}");
			}
		}

		string roll = kinds.Count <= 1 ? string.Join("", kinds)
			: string.Join(", ", kinds.GetRange(0, kinds.Count - 1)) + " and " + kinds[^1];
		return kinds.Count == 1 ? roll : $"{all} men — {roll}";
	}

	/// <summary>"Spearmen", "Archers" and "Cavalry" are already plural in the yard's own book;
	/// "Peasant" is not.</summary>
	private static string Plural(string name, int men) =>
		men == 1 || name.EndsWith("men") || name.EndsWith("s") || name.EndsWith("ry") ? name : name + "s";

	/// <summary>Tells the player about his county, the steward's numbers written into the line.
	/// Only his, and a rival's conquests: who fights whom elsewhere is not news in his hall.</summary>
	private static void Tell(TurnManager turns, string county, string id, List<FiredEvent> news,
		Dictionary<string, string> fill)
	{
		GameEvent said = EventEngine.Find(id);
		if (said == null || (turns.AnyProvince(county)?.Realm != turns.PlayerRealm && id is not ("county-lost" or "rival-took")))
		{
			return;
		}

		string text = said.Text;
		foreach ((string field, string value) in fill)
		{
			text = text.Replace($"{{{field}}}", value);
		}

		news.Add(new FiredEvent(county, said with { Text = text }, FromThePeople: true));
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
