using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The march and the blow: a host walked along the road to its prize, the fight at the
/// gate or in the open, and the news of it told to the player.</summary>
public partial class LordsCampaign
{
	/// <summary>As far along the road as this season's legs carry them — or, <paramref name="gather"/>,
	/// no nearer the gate than the assembly point, to wait there for the rest of the host.</summary>
	private void Walk(TurnManager turns, FieldArmy army, string target, List<RivalMarch> walked, bool gather = false) =>
		Go(turns, army, _towns[target], walked, gather ? Gathering * 0.6f : 0f);

	/// <summary>Along the road towards a map pixel, as far as this season's legs carry them, and no
	/// nearer it than <paramref name="stopShort"/>; trampling whatever is another lord's on the way.</summary>
	private void Go(TurnManager turns, FieldArmy army, Vector2 to, List<RivalMarch> walked, float stopShort = 0f)
	{
		Vector2 from = Pixel(army);
		if (!_roads.TryGetValue((from, to), out List<(Vector2 At, float Spent)> road))
		{
			road = _way(from, to);
			_roads[(from, to)] = road;
		}

		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			bool tooNear = road[step].At.DistanceTo(to) < stopShort;
			if (road[step].Spent <= army.MarchLeft && !tooNear)
			{
				halt = step;
			}
		}

		if (halt < 0)
		{
			return;
		}

		// A field of another lord's on the way is where the march ends: trodden bare, and the season
		// spent doing it.
		int spoil = turns.FirstSpoil(army, road.ConvertAll(step => step.At).GetRange(0, halt + 1));
		halt = spoil > 0 ? spoil : halt;
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
			turns.Trample(army, steps);
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
}
