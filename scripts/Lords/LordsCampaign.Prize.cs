using System.Collections.Generic;
using Godot;

/// <summary>Which county a lord's host marches on: the nearest few weighed by how likely he is to
/// take them, and the odds of the fight at their gates.</summary>
public partial class LordsCampaign
{
	/// <summary>How many of the nearest counties he wants he weighs before choosing one.</summary>
	private const int PrizesWeighed = 3;

	/// <summary>The counties he could want: not his, somebody's to take — a county the map draws and
	/// no economy describes cannot change hands, and a company once sat in one for twenty years — not
	/// his ally's, and the player's only if his difficulty lets him. Nearest first, but the realm his
	/// ally sent him against before anyone.</summary>
	private List<string> Wanted(TurnManager turns, FieldArmy army, string realm, GameBalance b, int skill)
	{
		Vector2 here = Pixel(army);
		Diplomacy book = turns.Diplomacy;
		string ally = book.AllyOf(realm);
		bool isAtWarWithPlayer = book.AtWar(realm, turns.PlayerRealm);
		var wanted = new List<string>();
		foreach ((string county, Vector2 _) in _towns)
		{
			string holder = turns.AnyProvince(county)?.Realm ?? "";
			bool isPlayers = holder == turns.PlayerRealm;
			if (holder == realm || !turns.CanBeTaken(county) || (ally.Length > 0 && holder == ally)
				|| (isPlayers && b.LordWillAttackPlayer[skill] == 0 && !isAtWarWithPlayer))
			{
				continue;
			}

			wanted.Add(county);
		}

		// The empty country first: while any county is still nobody's, the player's are not on his
		// list. He grows on what is free for the taking, and comes for the player once there is
		// nothing else left — which gives a lord who moves quickly the same country to race him for.
		// Unless he has declared war on the player: then the player is what he is for.
		bool free = wanted.Exists(county => turns.AnyProvince(county) == null);
		if (free && !isAtWarWithPlayer)
		{
			wanted.RemoveAll(county => turns.AnyProvince(county)?.Realm == turns.PlayerRealm);
		}

		string errand = book.Errands.GetValueOrDefault(realm, "");
		wanted.Sort((x, y) =>
		{
			int sent = IsErrand(y).CompareTo(IsErrand(x));
			if (sent != 0)
			{
				return sent;
			}

			int nearer = here.DistanceSquaredTo(_towns[x]).CompareTo(here.DistanceSquaredTo(_towns[y]));
			return nearer != 0 ? nearer : string.CompareOrdinal(x, y);
		});
		return wanted;

		bool IsErrand(string county) => errand.Length > 0 && turns.AnyProvince(county)?.Realm == errand;
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

			if (army.County != county && !_reaches(here, _towns[county]))
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
}
