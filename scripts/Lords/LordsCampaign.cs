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
public sealed partial class LordsCampaign
{
	/// <summary>The cheapest road between two map pixels, every step with what has been spent by then,
	/// or empty where there is none — MarchGrid.Way, as the map has it.</summary>
	public delegate List<(Vector2 At, float Spent)> Way(Vector2 from, Vector2 to);

	/// <summary>One company's march this season: where it set out from and every step it took, for
	/// the map to walk its banner along.</summary>
	public record RivalMarch(string Army, Vector2 From, List<Vector2> Road);

	private readonly Way _way;
	private readonly System.Func<Vector2, Vector2, bool> _reaches;

	/// <summary>The roads worked out this season, by where they start and end. A host whose companies
	/// set out from the same spot, or a company sent the same way twice — the ground has not moved
	/// between them, so the road is walked out once.</summary>
	private readonly Dictionary<(Vector2 From, Vector2 To), List<(Vector2 At, float Spent)>> _roads = new();
	private readonly System.Func<Vector2, string> _countyAt;
	private readonly Dictionary<string, Vector2> _towns;
	private readonly float _reach;
	private readonly System.Func<string, List<Vector2>> _groundOf;
	private readonly RandomNumberGenerator _dice = new();

	/// <summary>How many counties each lord held last season, and the turn each may march again after
	/// taking one (LordSettles). ponytail: not saved, so a loaded game lets a settling lord march at
	/// once; save both with the campaign if that is ever noticed.</summary>
	private readonly Dictionary<string, int> _held = new();
	private readonly Dictionary<string, int> _settledBy = new();

	/// <param name="towns">The village of every county that can be taken, by name.</param>
	/// <param name="reach">How close to a village counts as standing at its gate
	/// (MapDecoration.TownRing).</param>
	/// <param name="groundOf">A county's fields and diggings, as map pixels: where a raid goes.</param>
	/// <param name="reaches">Whether there is any road at all between two map pixels, answered without
	/// walking it (MarchGrid.Reaches). Without it the road is walked to find out.</param>
	public LordsCampaign(Way way, System.Func<Vector2, string> countyAt, Dictionary<string, Vector2> towns, float reach,
		System.Func<string, List<Vector2>> groundOf = null, System.Func<Vector2, Vector2, bool> reaches = null)
	{
		_groundOf = groundOf;
		_way = way;
		_reaches = reaches ?? ((from, to) => way(from, to).Count > 0);
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
		_roads.Clear();

		// One banner a county from the first season, marching or not.
		Gather(turns);
		if (turns.Turn < b.LordFirstMarch[skill])
		{
			return news;
		}

		Settle(turns, b, skill);
		Raid(turns, b, skill, walked);
		var marched = new HashSet<FieldArmy>();
		Rally(turns, walked, marched);
		foreach (FieldArmy army in turns.Armies())
		{
			string realm = turns.RealmOf(army);
			if (army.Strength == 0 || realm == turns.PlayerRealm || realm.Length == 0 || marched.Contains(army)
				|| army.Raider)
			{
				continue;
			}

			string besieged = Besieging(turns, army);
			if (besieged.Length > 0)
			{
				// Still at the gate. He storms it once his engines are built and he likes his chances
				// on the walls; until then the larder behind them does the work.
				if (turns.SiegeSeasonsLeft(besieged) == 0 && Odds(turns, army, besieged, walls: true, b) >= b.LordAttackOdds[skill])
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

	/// <summary>Where the company stands: where it was last marched to, or its own village if it has
	/// never been sent anywhere.</summary>
	public Vector2 Pixel(FieldArmy army)
	{
		var at = new Vector2(army.X, army.Y);
		return at != Vector2.Zero ? at : _towns.GetValueOrDefault(army.Home);
	}
}
