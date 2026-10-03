using System.Collections.Generic;
using System.Linq;

/// <summary>The other lords' turn, taken in front of the player before the season turns over, and
/// the letters he sends them.</summary>
public partial class TurnManager
{
	/// <summary>The turn the other lords last took theirs, so they take it once a season whoever
	/// asks: the map, to watch their men walk, or AdvanceTurn itself when nobody is watching.</summary>
	private int _rivalsTookTurn = -1;

	/// <summary>What the other lords' war did to the player this season, held until the season is
	/// reckoned and its news told.</summary>
	private readonly List<FiredEvent> _rivalNews = new();

	/// <summary>What the rivals' marches this turn have to tell the lord, before the season turns and
	/// hands it to the advisor — for the one screen that comes up before then: the end of his reign.</summary>
	public IReadOnlyList<FiredEvent> RivalNews => _rivalNews;

	/// <summary>The other lords' half of the season, the way Lords of the Realm plays it: the player
	/// ends his turn, and then they give their orders, raise their men and march them, while he
	/// watches. Returns every march they made, so the map can walk the banners along the roads they
	/// took before the season is reckoned. Once a turn; a second call is nothing.</summary>
	public List<LordsCampaign.RivalMarch> RivalsTurn()
	{
		var walked = new List<LordsCampaign.RivalMarch>();
		if (_rivalsTookTurn == Turn)
		{
			return walked;
		}

		_rivalsTookTurn = Turn;
		Season season = CurrentSeason;

		// Letters first, as the original's lords read their inbox before they set a tax: a lord
		// the player has just sworn to does not march on him the same season.
		Diplomacy.Season(Turn, _playerRealm, Rivals(), SeatedLords(), _balance);

		// Every rival gives his orders before ANY county is run. Two passes rather than one, because
		// a lord's tax is felt in his neighbours' counties as well as his own: run them one at a
		// time and whether a rate counted this season or next would depend on which province happens
		// to be authored first.
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			if (province.Realm != _playerRealm)
			{
				LordAI.TakeTurn(province, definition, _balance, Market, season, Difficulty, _rng, RivalWallsUpTo);

				// And his muster, from the first season: the men he raises stand at home and defend it
				// until LordsCampaign decides the season has come to march them.
				LordArms.Arm(province, _balance, Difficulty,
					county => _provincesByName.GetValueOrDefault(county)?.Realm ?? "", Market);
				LordWalls.ManTheWalls(province, _balance);
			}
		}

		// Every army gets its season's ground back. Done before the turn rather than after, so a
		// county taken this turn can be marched out of next turn and not the turn after that.
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			foreach (FieldArmy standing in province.Armies)
			{
				standing.MarchLeft = _balance.MarchReach;
			}
		}

		// Then they march: after their orders and before the season is reckoned, so a county they
		// take this turn is run by them this turn. What it costs the player is news he hears first.
		if (_campaign != null)
		{
			_rivalNews.AddRange(_campaign.March(this, _balance, walked));
		}

		return walked;
	}

	/// <summary>The realms the other lords hold, in the campaign's authored order.</summary>
	public List<string> Rivals()
	{
		var rivals = new List<string>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			string realm = _provincesByName[definition.ProvinceName].Realm;
			if (realm.Length > 0 && realm != _playerRealm && !rivals.Contains(realm))
			{
				rivals.Add(realm);
			}
		}

		return rivals;
	}

	private Dictionary<string, Lord> SeatedLords()
	{
		var seated = new Dictionary<string, Lord>();
		foreach ((string realm, string key) in LordOf)
		{
			Lord lord = Lords.Find(key);
			if (lord != null)
			{
				seated[realm] = lord;
			}
		}

		return seated;
	}

	/// <summary>The player writes to a lord. A gift is paid out of his purse as it is sent, and is not
	/// sent at all if the purse cannot cover it.</summary>
	public bool Write(Letter letter)
	{
		Treasury purse = _provincesByName.Values.FirstOrDefault(p => p.Realm == _playerRealm)?.Purse;
		if (letter.From != _playerRealm || purse == null || purse.Gold < letter.Gold
			|| !Diplomacy.Send(letter, Rivals().Count))
		{
			return false;
		}

		purse.Gold -= letter.Kind == Diplomacy.Gift ? letter.Gold : 0;
		return true;
	}
}
