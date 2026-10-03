using System.Collections.Generic;

/// <summary>A saved campaign laid back over the turn: every county, company, cart and letter as the
/// file left them.</summary>
public partial class TurnManager
{
	/// <summary>Puts a save's state back in place. Provinces are matched by name, so a save
	/// written before a province was added or renamed still loads: the missing one simply keeps
	/// the starting values the definitions gave it.</summary>
	public void Restore(SaveGame save)
	{
		Turn = save.Turn;
		Difficulty = save.Difficulty;
		// A save written before there were letters: nobody has written anybody anything yet.
		Diplomacy = save.Diplomacy ?? new Diplomacy();
		// Nor carts: a save from before there were any has none on the road.
		Shipments = save.Shipments ?? new List<Shipment>();
		// A save written before the market moved carries no prices, and an empty book is exactly
		// right for it: every store simply sits at what it is worth.
		Market.Pressure = save.Prices ?? new Dictionary<string, float>();
		Market.Turned(CurrentSeason);
		_worldLastStirred.Clear();
		foreach ((string realm, int turn) in save.WorldLastStirred ?? new Dictionary<string, int>())
		{
			_worldLastStirred[realm] = turn;
		}

		foreach (ProvinceEconomy province in save.Provinces)
		{
			// A county taken from the empty country since the campaign opened is still waiting among
			// the unheld in a turn built fresh from the definitions. It joins the turn here the way it
			// joined it the day it was taken — without this, every county a lord ever claimed from
			// nobody vanished on load, stores, fields and all.
			if (_unheld.Remove(province.ProvinceName, out ProvinceDefinition taken))
			{
				_definitions.Add(taken);
			}
			else if (!_provincesByName.ContainsKey(province.ProvinceName))
			{
				continue;
			}

			// A save written before provinces carried a holder says nothing about who held them, and
			// the campaign's own layout is the right answer for that file: nobody had conquered
			// anything yet, so everyone still holds what they opened with.
			if (province.Realm.Length == 0 && _provincesByName.TryGetValue(province.ProvinceName, out ProvinceEconomy opened))
			{
				province.Realm = opened.Realm;
			}

			// A save written before a county could have more than one army carries one roster, which
			// ProvinceEconomy read into one company. It has no name for the county it stands in and
			// no home, and a county that had no men at all is carrying an empty banner.
			foreach (FieldArmy army in province.Armies)
			{
				army.Home = army.Home.Length > 0 ? army.Home : province.ProvinceName;
				army.County = army.County.Length > 0 ? army.County : province.ProvinceName;
				army.Id = army.Id > 0 ? army.Id : 1;
			}

			if (save.Version < SaveGame.SharesInHundredthsFrom)
			{
				Labour.InHundredths(province);
			}

			province.Bury();
			_provincesByName[province.ProvinceName] = province;
		}

		PoolPurses(pooling: false);
	}
}
