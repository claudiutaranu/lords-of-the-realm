using System.Collections.Generic;
using Godot;

/// <summary>War at a county's gate: the battles in the open and at the walls, the sieges sat down
/// in front of it, the fallen buried, and the end of a reign that has lost them all.</summary>
public partial class TurnManager
{
	/// <summary>True once the reign is over: the player holds no county at all, or every county he
	/// holds has risen against him (happiness at nothing) and he has not one man under arms left to
	/// hold any of them down.</summary>
	public bool PlayerFallen
	{
		get
		{
			bool holds = false;
			bool loyal = false;
			int men = 0;
			foreach (ProvinceEconomy province in _provincesByName.Values)
			{
				if (province.Realm == _playerRealm)
				{
					holds = true;
					loyal |= province.Loyalty > 0f;
					men += province.Soldiers;
				}
			}

			return !holds || (!loyal && men == 0);
		}
	}

	/// <summary>True once no lord but the player holds a county: every rival on the map has been
	/// driven from the last of his land, and the map is won.</summary>
	public bool RivalsFallen => !PlayerFallen && Rivals().Count == 0;

	/// <summary>Fights for a county, and writes what the day cost into the ledger.
	///
	/// <paramref name="walls"/> picks which of the two fights this is. What is standing in the open
	/// is beaten in the open; whatever is behind the stone is beaten afterwards and on far worse
	/// terms. The caller asks for them in that order — see <see cref="DefendersOf"/>, whose two
	/// rosters are the two halves — because there is nothing to storm until the field is cleared.
	///
	/// The county changes hands the moment there is nobody left to stop it and not before, which is
	/// what a castle is for: a lord can lose every man he had outside his walls and still hold his
	/// county, as long as somebody is standing on them.
	///
	/// <paramref name="fought"/> is a day the lord fought in the field himself (<see cref="FieldBattle"/>):
	/// it is written into the ledger exactly as the captain's reckoning would have been.</summary>
	public Battle.Result Attack(FieldArmy army, string county, Vector2 at, bool walls, Battle.Result? fought = null)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		if (here == null || army.Strength == 0)
		{
			return new Battle.Result(false, new Dictionary<string, int>(),
				new Dictionary<string, int>(), 0);
		}

		// Whoever is holding the county holds it together: his companies are put under one banner
		// before a blow is struck, so what the day kills comes off men who are really standing there.
		Rally(county);

		// Men who have nothing left in their legs are attacking on the last of them. The map charged
		// them for the road on the way here, so this is simply read off what is left of the season.
		bool spent = army.MarchLeft <= 0f;
		ProvinceEconomy town = _provincesByName.GetValueOrDefault(county);
		bool turnedOut = !walls && town != null && StandingIn(county).Count == 0;
		Defenders against = DefendersOf(county);
		Battle.Result day = fought ?? (walls
			? Battle.OnTheWalls(army.Men, against, spent, _balance, _rng)
			: Battle.InTheField(army.Men, against, spent, _balance, _rng));

		Bury(army.Men, day.AttackerLosses);
		here.Bury();
		if (!walls && StandingIn(county) is { Count: > 1 } companies)
		{
			// A hired band keeps its own banner even at the gate it holds with the rest (Merge),
			// so the field it fought in is several companies: the dead come off each of them.
			BuryAcross(companies, day.DefenderLosses);
		}
		else
		{
			Bury(walls ? against.Castle : against.Field, day.DefenderLosses);
		}

		// A held town that turned out for itself lost its own people, and does not turn out again this
		// season if it broke.
		if (turnedOut && ProvinceEconomy.Men(day.DefenderLosses) > 0)
		{
			town.Population = Mathf.Max(0, town.Population - ProvinceEconomy.Men(day.DefenderLosses));
			EconomySimulation.FitWorkforce(town);
			if (day.AttackerWon)
			{
				town.MilitiaRoutedTurn = Turn;
			}
		}

		// Nobody falls back behind the walls any more: a field army that broke is not an army. What
		// still turns one battle into two is the watch on the gate — men who were never in the first
		// fight — and that is what a lord who keeps a garrison is paying for.
		// Carrying the walls IS taking the county: whoever is left on them when they are carried is
		// taken with them, and Claim clears them off. Carrying the FIELD only takes it where there
		// was nowhere left to fall back to — a lord can lose every man he had outside his walls and
		// still hold the place, which is the entire argument for quarrying stone.
		if (day.AttackerWon && army.Strength > 0 && (walls || !DefendersOf(county).Held))
		{
			Claim(army, county, at);
		}

		return day;
	}

	/// <summary>One company falls on another in open country, away from any gate: the same day in the
	/// field a county's defence is, with the other company standing in for the county's. Nothing
	/// changes hands; whichever side breaks is gone. <paramref name="fought"/> is a day the lord
	/// fought himself, as for <see cref="Attack"/>.</summary>
	public Battle.Result Engage(FieldArmy army, FieldArmy enemy, Battle.Result? fought = null)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		ProvinceEconomy there = enemy == null ? null : _provincesByName.GetValueOrDefault(enemy.Home);
		if (here == null || there == null || army.Strength == 0 || enemy.Strength == 0)
		{
			return new Battle.Result(false, new Dictionary<string, int>(), new Dictionary<string, int>(), 0);
		}

		var against = new Defenders(enemy.Men, new Dictionary<string, int>(), "", there.Loyalty, InOpenCountry: true);
		Battle.Result day = fought ?? Battle.InTheField(army.Men, against, army.MarchLeft <= 0f, _balance, _rng);
		Bury(army.Men, day.AttackerLosses);
		Bury(enemy.Men, day.DefenderLosses);
		here.Bury();
		there.Bury();
		return day;
	}

	/// <summary>Sits an army down in front of a gate it has decided not to climb.
	///
	/// The other way to take a castle, and the one the stone rungs are actually taken by: a garrison
	/// eats what was carried up before the siege, and then it eats nothing. It costs the besieger
	/// his army's whole season, every season — the men are standing there rather than anywhere
	/// else — and it costs the besieged his county's income for as long as it lasts.
	///
	/// Sat down before the walls even with his companies still in the open (the user's call: a castle
	/// is gone for at the castle, its engines chosen the day the army halts there); they are fought in
	/// the open first, the day the walls are stormed.</summary>
	public bool Besiege(FieldArmy army, string county, IReadOnlyDictionary<string, int> engines = null)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
		Defenders against = DefendersOf(county);
		// Nor where somebody already sits: a second company coming up beside the first set the siege
		// back to its first day, and the engines were never finished.
		if (here == null || there == null || army.Strength == 0 || here.Realm == there.Realm
			|| !against.Held || ArmyOf(there.BesiegedFrom) is { Strength: > 0 })
		{
			return false;
		}

		there.BesiegedFrom = army.Key;
		there.SiegeSeasons = 0;
		there.HungrySeasons = 0;
		there.SiegeEngines = new Dictionary<string, int>();
		foreach ((string kind, int count) in engines ?? new Dictionary<string, int>())
		{
			there.SiegeEngines[kind] = Mathf.Clamp(count, 0, _balance.SiegeEnginesMost);
		}

		return true;
	}

	/// <summary>The county a company is sitting outside, or empty.</summary>
	public string Besieging(FieldArmy army)
	{
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			if (army != null && province.BesiegedFrom == army.Key)
			{
				return province.ProvinceName;
			}
		}

		return "";
	}

	/// <summary>How many more seasons the siege of a county must go on before its engines are built
	/// and the walls can be stormed; nought when they can be, and -1 when nobody is besieging it.</summary>
	public int SiegeSeasonsLeft(string county)
	{
		ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
		if (there == null || there.BesiegedFrom.Length == 0)
		{
			return -1;
		}

		return Mathf.Max(0, SiegeEngines.Seasons(there.SiegeEngines, _balance) - there.SiegeSeasons);
	}

	/// <summary>Whether the engines before a county were finished this very season: the one season the
	/// assault is put to the lord without his asking for it. A siege with no engines is a starving
	/// out, and nobody is called to storm anything.</summary>
	public bool IsSiegeJustReady(string county)
	{
		ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
		int seasons = there == null ? 0 : SiegeEngines.Seasons(there.SiegeEngines, _balance);
		return there != null && there.BesiegedFrom.Length > 0 && seasons > 0 && there.SiegeSeasons == seasons;
	}

	/// <summary>Takes one company out of every siege it was keeping.</summary>
	private void Lift(FieldArmy besieger)
	{
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			if (besieger != null && province.BesiegedFrom == besieger.Key)
			{
				province.EndSiege();
			}
		}
	}

	/// <summary>A season of sitting outside somebody's gate.
	///
	/// Run after every county has had its own season, because a surrender moves a county between
	/// realms and doing that in the middle of the loop that is running them would be running a
	/// county for the lord who no longer holds it.</summary>
	private void Sieges(List<FiredEvent> news)
	{
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			FieldArmy besieger = province.BesiegedFrom.Length == 0
				? null
				: ArmyOf(province.BesiegedFrom);

			if (province.BesiegedFrom.Length == 0)
			{
				continue;
			}

			// Nobody out there any more — the besiegers starved, deserted or were beaten off.
			if (besieger == null || besieger.Strength == 0)
			{
				province.EndSiege();
				continue;
			}

			province.SiegeSeasons++;
			int eaten = Mathf.CeilToInt(province.CastleMen / _balance.PeoplePerGrain * _balance.SoldierAppetite);
			if (province.CastleStores >= eaten)
			{
				province.CastleStores -= eaten;
				province.HungrySeasons = 0;
				continue;
			}

			// The larder is out. They hold for a while on nothing, thinning as they go, and then
			// somebody draws the bolt — which is how nearly every castle of the period fell.
			province.CastleStores = 0;
			province.HungrySeasons++;
			foreach (string unit in new List<string>(province.Castle.Keys))
			{
				int lost = Mathf.CeilToInt(province.Castle[unit] * _balance.StarvedGarrisonRate);
				province.Castle[unit] -= lost;
				if (province.Castle[unit] <= 0)
				{
					province.Castle.Remove(unit);
				}
			}

			if (province.HungrySeasons < _balance.SurrenderAfterHungrySeasons && province.CastleMen > 0)
			{
				continue;
			}

			bool ours = RealmOf(besieger) == _playerRealm;
			string county = province.ProvinceName;
			Claim(besieger, county, new Vector2(besieger.X, besieger.Y));

			// Written here rather than authored into events.json with the rest: this line has to
			// name a county and a number, and a static line cannot.
			if (ours)
			{
				news.Add(new FiredEvent(county,
					new GameEvent("siege-fallen", "The gate is opened",
						$"{county} has given up its castle. They were starved out of it.", ""),
					false));
			}
		}
	}

	/// <summary>Takes a battle's dead off a roster. A company wiped out is gone from it rather than
	/// left standing at nought men, so everything that counts companies counts the ones there
	/// are.</summary>
	/// <summary>The dead of one field shared out over the companies that stood in it, kind by kind,
	/// each giving what it has until the roll is met; the empty ones are struck off.</summary>
	private void BuryAcross(List<FieldArmy> companies, Dictionary<string, int> fallen)
	{
		var owed = new Dictionary<string, int>(fallen);
		foreach (FieldArmy company in companies)
		{
			var lost = new Dictionary<string, int>();
			foreach ((string unit, int men) in owed)
			{
				lost[unit] = Mathf.Min(men, company.Men.GetValueOrDefault(unit));
			}

			foreach ((string unit, int men) in lost)
			{
				owed[unit] -= men;
			}

			Bury(company.Men, lost);
			_provincesByName.GetValueOrDefault(company.Home)?.Bury();
		}
	}

	private static void Bury(Dictionary<string, int> roster, Dictionary<string, int> fallen)
	{
		foreach ((string unit, int men) in fallen)
		{
			int left = roster.GetValueOrDefault(unit) - men;
			if (left > 0)
			{
				roster[unit] = left;
			}
			else
			{
				roster.Remove(unit);
			}
		}
	}
}
