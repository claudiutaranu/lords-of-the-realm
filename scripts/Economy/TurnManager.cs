using System.Collections.Generic;
using Godot;

/// <summary>Owns the runtime economy of every province somebody holds — the player's and every
/// rival lord's alike — and advances them one season per End Turn. Each province keeps its own
/// stockpiles; there is no shared empire-wide treasury. UI reads state through this and calls
/// AdvanceTurn(); it never calls EconomySimulation directly (design doc section 33).
///
/// A province nobody holds is not in here at all, and that is how an unclaimed county stays exactly
/// as the campaign authored it until somebody takes it: not a flag checked every turn, but a county
/// the turn never reaches. Conquest, when it lands, is one province moving into this list with a
/// realm on it — which is also why who holds what lives on the ProvinceEconomy and is saved.
///
/// The player's counties and the lords' run through the same EconomySimulation, in the same loop,
/// on the same season. The only difference is who gives the orders in the moment before it runs:
/// the player spent his turn doing it, and <see cref="LordAI"/> does it for everybody else.</summary>
public class TurnManager
{
	/// <summary>What a county with no lord puts in the way of one who wants it, by the keys
	/// recruits.json uses: its farmhands, and the town's own watch of bowmen and spears.</summary>
	private const string MilitiaUnit = "peasant";
	private const string WatchBows = "bow";
	private const string WatchSpears = "spear";

	/// <summary>How much news one turn may carry, however many provinces a lord holds. Lords of the
	/// Realm told you one thing at a time and let you get on with it; a realm of eight counties
	/// reporting everything would bury the turn it belongs to.</summary>
	private const int MostNewsPerTurn = 3;

	private readonly RandomNumberGenerator _rng = new();
	private readonly GameBalance _balance;
	private readonly List<ProvinceDefinition> _definitions;
	private readonly Dictionary<string, ProvinceEconomy> _provincesByName = new();

	/// <summary>Which realm is the player's. Everything the screens ask for is asked on behalf of
	/// this one — what is in the hall, whose rooms can be walked into, whose news is heard.</summary>
	private readonly string _playerRealm;

	/// <summary>The land nobody holds, by name. Not run, not fed, not counted — it sits exactly as the
	/// campaign authored it until an army walks onto it, and this is where its description waits in
	/// the meantime.</summary>
	private readonly Dictionary<string, ProvinceDefinition> _unheld = new();

	/// <summary>Whether a county can change hands at all: one somebody holds, or one with land
	/// described that nobody holds yet. A county the map draws but no economy describes is scenery.</summary>
	public bool CanBeTaken(string county) =>
		_provincesByName.ContainsKey(county) || _unheld.ContainsKey(county);

	/// <summary>How well the other lords play. Held here rather than read from a global each turn so
	/// that a loaded save is played at the difficulty it was started on, not at whatever the menu
	/// last had.</summary>
	public Difficulty Difficulty { get; private set; }

	/// <summary>The highest rung of the ladder the other lords may build, by key; empty for all of it.
	/// The campaign's to say (provinces.json "rivalWallsUpTo").</summary>
	public string RivalWallsUpTo { get; set; } = "";

	/// <summary>The realm the player is, for whoever has to tell his men from everybody else's.</summary>
	public string PlayerRealm => _playerRealm;

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

	/// <summary>The rival lords' war. Null until the map has handed over its ground (see
	/// <see cref="Survey"/>): without a road to walk, nobody marches — which is also how the checks
	/// run a turn without a map.</summary>
	private LordsCampaign _campaign;

	/// <summary>Hands the turn the ground the map lays out, so the other lords can march over it by
	/// the player's own rules.</summary>
	public void Survey(LordsCampaign.Way way, System.Func<Vector2, string> countyAt,
		Dictionary<string, Vector2> towns, float reach) =>
		_campaign = new LordsCampaign(way, countyAt, towns, reach);

	/// <summary>Calendar year the campaign opens on. Four seasons make a year, so the
	/// displayed year advances every fourth turn.</summary>
	public const int StartYear = 1268;
	private const int SeasonsPerYear = 4;

	public int Turn { get; private set; } = 1;
	public Season CurrentSeason => (Season)((Turn - 1) % SeasonsPerYear);
	public int CurrentYear => StartYear + ((Turn - 1) / SeasonsPerYear);

	/// <summary>The realm's one market. It belongs here and not to the screen that trades on it,
	/// because what the player did to a price last autumn has to still be true when he walks back
	/// into the stall in spring — and has to survive being saved.</summary>
	public Market Market { get; }

	/// <summary>What the world did to the realm this turn, in the order it should be told. Read by
	/// the map after the season has turned over, and replaced every turn.</summary>
	public List<FiredEvent> News { get; private set; } = new();

	/// <summary>Every county the river rose in this turn, whoever holds it. Not news — a lord is not
	/// told a rival's harvest drowned — but weather, and weather over the next county is something
	/// anybody can see from his own walls.</summary>
	public List<string> Flooded { get; } = new();

	/// <summary>The season just played, county by county. Kept because the happiness table is read
	/// after the turn is over and the summary is otherwise thrown away with the turn that made it —
	/// and a breakdown of a season the player can no longer see is the only kind worth showing.</summary>
	private readonly Dictionary<string, TurnSummary> _lastSeason = new();

	/// <summary>The turn the world last did something to each realm. A realm is visited by at most one
	/// disaster a season and then left alone for WorldEventGap turns: rolled county by county, a lord
	/// of six counties heard of rats, murrain or plague nearly every season, one on top of another.
	///
	/// ponytail: not saved — a loaded campaign may hear from the world one season early. Put it on
	/// the save if that ever matters.</summary>
	private readonly Dictionary<string, int> _worldLastStirred = new();

	/// <summary>How the county's goodwill was arrived at last season, or null before its first turn.
	/// Reachable only through a province the caller already holds, which is the screens' own
	/// gate.</summary>
	public TurnSummary LastSeason(string province) => _lastSeason.GetValueOrDefault(province);

	/// <summary>Every province that somebody holds, with who holds it. Unclaimed counties are simply
	/// not passed in — see the note above.</summary>
	public TurnManager(GameBalance balance, List<ProvinceDefinition> definitions,
		Dictionary<string, string> realmByProvince, string playerRealm, Difficulty difficulty,
		List<ProvinceDefinition> unheld = null)
	{
		foreach (ProvinceDefinition waiting in unheld ?? new List<ProvinceDefinition>())
		{
			_unheld[waiting.ProvinceName] = waiting;
		}

		// A campaign's luck is its own: seeding this would give every playthrough the same plague in
		// the same spring, which is a puzzle rather than a reign.
		_rng.Randomize();
		_balance = balance;
		Market = new Market(balance);
		Market.Turned(CurrentSeason);
		// Copied, because taking a county adds to this list and the page that handed it over is still
		// using its own.
		_definitions = new List<ProvinceDefinition>(definitions);
		_playerRealm = playerRealm;
		Difficulty = difficulty;
		foreach (ProvinceDefinition definition in definitions)
		{
			ProvinceEconomy province = ProvinceEconomy.FromDefinition(definition);
			province.Realm = realmByProvince.GetValueOrDefault(definition.ProvinceName, "");
			foreach (FieldArmy standing in province.Armies)
			{
				standing.MarchLeft = balance.MarchReach;
			}

			// A province opens with its people already at work. Nobody would hand a lord a county
			// where every field is sown and not one man is in it. Off the quarter the bar opens at,
			// not off this season's need: fitted to spring's few tenders, the bar stayed there, and a
			// lord who never touched it had no reapers in autumn and starved by the second winter.
			Labour.Deal(province, definition, balance, CurrentSeason);
			_provincesByName[definition.ProvinceName] = province;
		}

		// A realm keeps one purse. What its counties were each given to open with is pooled into it,
		// so a lord holding three counties opens with the three of them together rather than with
		// three separate piles he can only spend where they lie.
		PoolPurses(pooling: true);
	}

	/// <summary>Puts every county of a realm on the one purse.
	///
	/// <paramref name="pooling"/> says what to do with what each county is carrying. Opening a
	/// campaign, they each carry their own share and it is added up. Coming back from a save they
	/// are all carrying the same figure — it was one purse when it was written — so the first one
	/// read stands for the realm and the rest are simply pointed at it.</summary>
	private void PoolPurses(bool pooling)
	{
		var purses = new Dictionary<string, Treasury>();
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			if (!purses.TryGetValue(province.Realm, out Treasury purse))
			{
				purses[province.Realm] = province.Purse;
				continue;
			}

			if (pooling)
			{
				purse.Gold += province.Gold;
			}

			province.Purse = purse;
		}
	}

	/// <summary>The player's province of that name, or null — which is what every screen that asks
	/// this actually means: a room that is not his to walk into, a ledger that is not his to read. A
	/// rival's county is in here too, and is deliberately not what comes back.</summary>
	/// <summary>Sends a county's men across country to a point on the map.
	///
	/// The WAY is the map's business and the LEDGER is this one's. Which cells are passable, what a
	/// road saves and how far a budget stretches are questions about ground, and the ground is drawn
	/// in images this class has never seen; so the map works the road out and comes here with a
	/// destination and a price. What cannot be delegated is asked here: that there are men to send,
	/// and that they can afford the walk.
	///
	/// A march is a march and not a conquest. Crossing into another lord's county puts the men on his
	/// ground and nothing more — see <see cref="Claim"/> for the only thing that moves a border, and
	/// <see cref="DefendersOf"/> for what has to be beaten first.</summary>
	public bool March(FieldArmy army, string toCounty, Vector2 at, float cost)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		if (here == null || army.Strength == 0 || cost <= 0f || cost > army.MarchLeft)
		{
			return false;
		}

		// Men who are marching are not sitting in front of anybody's gate. A siege is the army being
		// THERE, so the moment it is somewhere else there is no siege — no order to cancel and no way
		// to forget to.
		Lift(army);

		// Wherever they were sent, they are standing there now and the season is that much shorter.
		// Everything below is about whether anything CHANGED HANDS by their standing there, which is
		// a different question and mostly answered no.
		army.X = at.X;
		army.Y = at.Y;
		army.County = toCounty;
		army.MarchLeft -= cost;

		ProvinceEconomy there = _provincesByName.GetValueOrDefault(toCounty);

		// Their own county, another of the same lord's, or a rival's they are only crossing. Walking
		// over a county has never taken it and does not take it now: that is settled at the seat,
		// against whoever is standing on it. Two of a lord's own companies standing in the same field
		// stay two companies — putting them under one banner is an order he gives (see
		// <see cref="Merge"/>), not something the ground does to them.
		// Ground the map draws and no economy describes is only ground: walked across, never taken.
		// Asking to claim it failed, and a march reported refused after the men had already moved was
		// a banner the map would not walk to a place the ledger had put it.
		if (there != null || toCounty == army.Home || !CanBeTaken(toCounty))
		{
			return true;
		}

		// Nobody holds it. If its own people have taken up what hangs in the barn, they have to be
		// beaten before anything changes hands, and the march simply ends on their ground. An empty
		// county is walked into, the way it always was — and the march stands either way: the men
		// are there.
		if (DefendersOf(toCounty).Men == 0)
		{
			Claim(army, toCounty, at);
		}

		return true;
	}

	/// <summary>Puts one company's men under another's banner, where the lord wants one army instead
	/// of two standing in the same field. What the season has left is the slower of the two: a
	/// company does not get its legs back by falling in with men who have walked less far.</summary>
	public bool Merge(FieldArmy into, FieldArmy from)
	{
		if (into == null || from == null || into == from || into.County != from.County
			|| RealmOf(into) != RealmOf(from))
		{
			return false;
		}

		foreach ((string unit, int men) in from.Men)
		{
			into.Men[unit] = into.Men.GetValueOrDefault(unit) + men;
		}

		into.MarchLeft = Mathf.Min(into.MarchLeft, from.MarchLeft);
		Lift(from);
		_provincesByName.GetValueOrDefault(from.Home)?.Disband(from);
		return true;
	}

	/// <summary>Sends men off a company up onto the walls of the county it is standing in — one of
	/// its own lord's, not besieged, and with room: whoever the walls will not hold stays in the field.
	/// A company that goes up entire is gone from the map. Any of the lord's castles will take them,
	/// not only the county that raised them, and from then on it is that county that feeds and pays
	/// them, the way it does the rest of its watch. Returns how many went up.</summary>
	public int Garrison(FieldArmy army, Dictionary<string, int> going)
	{
		ProvinceEconomy walls = army == null ? null : _provincesByName.GetValueOrDefault(army.County);
		if (walls == null || walls.Realm != RealmOf(army) || walls.BesiegedFrom.Length > 0)
		{
			return 0;
		}

		int room = walls.WallRoom;
		int up = 0;
		foreach (string unit in Units.All())
		{
			int men = Mathf.Min(room - up, Mathf.Min(going.GetValueOrDefault(unit), army.Men.GetValueOrDefault(unit)));
			if (men <= 0)
			{
				continue;
			}

			army.Men[unit] -= men;
			if (army.Men[unit] == 0)
			{
				army.Men.Remove(unit);
			}

			walls.Castle[unit] = walls.Castle.GetValueOrDefault(unit) + men;
			up += men;
		}

		if (army.Strength == 0)
		{
			Lift(army);
			_provincesByName.GetValueOrDefault(army.Home)?.Disband(army);
		}

		return up;
	}

	/// <summary>Whose men these are: the realm of the county that raised them, wherever they have
	/// marched to since.</summary>
	public string RealmOf(FieldArmy army) =>
		army == null ? "" : _provincesByName.GetValueOrDefault(army.Home)?.Realm ?? "";

	/// <summary>The company that key names, or null when it has been wiped out, disbanded or merged
	/// away. Screens hold armies by key across a turn, and a turn can take one off the board.</summary>
	public FieldArmy ArmyOf(string key)
	{
		int mark = key?.LastIndexOf('#') ?? -1;
		if (mark <= 0 || !int.TryParse(key[(mark + 1)..], out int id))
		{
			return null;
		}

		return _provincesByName.GetValueOrDefault(key[..mark])?.Army(id);
	}

	/// <summary>Every company standing on the board, whoever raised it. The map draws off this: one
	/// banner per army, wherever the season has left it.</summary>
	public List<FieldArmy> Armies()
	{
		var standing = new List<FieldArmy>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			standing.AddRange(_provincesByName[definition.ProvinceName].Armies);
		}

		return standing;
	}

	/// <summary>The companies of the realm that holds a county, standing in that county. Who would
	/// have to be beaten to take it, and who a lord's men find waiting when they get there.</summary>
	private List<FieldArmy> StandingIn(string county)
	{
		string realm = _provincesByName.GetValueOrDefault(county)?.Realm ?? "";
		var there = new List<FieldArmy>();
		foreach (FieldArmy army in Armies())
		{
			if (army.County == county && army.Strength > 0 && RealmOf(army) == realm)
			{
				there.Add(army);
			}
		}

		return there;
	}

	/// <summary>Puts every company the holder has in a county under one banner, because a battle for
	/// a county is one battle: men caught in the same field fight it together or they are beaten in
	/// detail by the same enemy on the same afternoon. Called before anything that can kill them, so
	/// what the fighting takes off comes off men who are really there.</summary>
	private FieldArmy Rally(string county)
	{
		List<FieldArmy> there = StandingIn(county);
		if (there.Count == 0)
		{
			return null;
		}

		for (int index = there.Count - 1; index > 0; index--)
		{
			Merge(there[0], there[index]);
		}

		return there[0];
	}

	/// <summary>Hands a county to the realm whose men are standing on its seat, with those men on it.
	/// Called when there is nobody left in the way — either because there never was anybody, or
	/// because the battle for it has just been settled.
	///
	/// The county keeps everything but its lord: its stores, its fields, its walls and its people are
	/// exactly what they were the moment before, because they are the reason anybody wanted it. What
	/// it loses is whoever was holding it, and whatever they still had standing.</summary>
	public bool Claim(FieldArmy army, string toCounty, Vector2 at)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		if (here == null || toCounty == army.Home)
		{
			return false;
		}

		ProvinceEconomy there = _provincesByName.GetValueOrDefault(toCounty);
		if (there == null)
		{
			// Until somebody's army stands on the ground, an unclaimed county is not in the turn at
			// all — this is where it joins it.
			ProvinceDefinition taken = _unheld.GetValueOrDefault(toCounty);
			if (taken == null)
			{
				return false; // no land described there: nothing to walk into
			}

			there = ProvinceEconomy.FromDefinition(taken);
			Labour.Deal(there, taken, _balance, CurrentSeason);
			_definitions.Add(taken);
			_provincesByName[toCounty] = there;
			_unheld.Remove(toCounty);

			// What was in its coffers falls with it, into the one purse the taking realm keeps.
			// Otherwise the ground taken would sit on money the crown could see and never spend.
			here.Purse.Gold += there.Gold;
		}

		// A county taken is a county nobody is besieging any more, whichever way it fell.
		there.BesiegedFrom = "";
		there.SiegeSeasons = 0;
		there.HungrySeasons = 0;

		// Whoever was holding it is not holding it any more, and neither are the men who were
		// standing in it: they are dead, scattered or walked off by the time anybody is claiming
		// anything.
		//
		// The companies it raised that are somewhere ELSE are a different matter. They are still an
		// army in the field, three counties away, and nothing has happened to them today — so they
		// pass to another county of their own lord, which pays and feeds them from now on. Only a
		// lord with nothing left at all loses them with his last seat.
		ProvinceEconomy refuge = Refuge(there.Realm, toCounty);
		for (int index = there.Armies.Count - 1; index >= 0; index--)
		{
			FieldArmy company = there.Armies[index];
			there.Armies.RemoveAt(index);
			if (refuge != null && company.County != toCounty && company.Strength > 0)
			{
				refuge.Adopt(company);
			}
		}

		there.Castle.Clear();
		there.Realm = here.Realm;
		there.Purse = here.Purse;

		// Nobody is glad to be conquered. A county taken has to be held before it is worth having,
		// which is what stops a lord taking everything he can walk to.
		there.Loyalty = Mathf.Max(0f, there.Loyalty - _balance.ConquestResentment);

		// The men who took it are standing on it, and they are still the company that walked in:
		// their own county pays and feeds them however far they have got. What they hold, they hold
		// by being there.
		army.County = toCounty;
		army.X = at.X;
		army.Y = at.Y;
		return true;
	}

	/// <summary>Another county of the same realm, to take in the companies of one that has just
	/// fallen. Null where the lord has none left — which is the end of him.</summary>
	private ProvinceEconomy Refuge(string realm, string lost)
	{
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy county = _provincesByName[definition.ProvinceName];
			if (county.Realm == realm && county.ProvinceName != lost)
			{
				return county;
			}
		}

		return null;
	}

	/// <summary>Fights for a county, and writes what the day cost into the ledger.
	///
	/// <paramref name="walls"/> picks which of the two fights this is. What is standing in the open
	/// is beaten in the open; whatever is behind the stone is beaten afterwards and on far worse
	/// terms. The caller asks for them in that order — see <see cref="DefendersOf"/>, whose two
	/// rosters are the two halves — because there is nothing to storm until the field is cleared.
	///
	/// The county changes hands the moment there is nobody left to stop it and not before, which is
	/// what a castle is for: a lord can lose every man he had outside his walls and still hold his
	/// county, as long as somebody is standing on them.</summary>
	public Battle.Result Attack(FieldArmy army, string county, Vector2 at, bool walls)
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
		Battle.Result day = walls
			? Battle.OnTheWalls(army.Men, against, spent, _balance, _rng)
			: Battle.InTheField(army.Men, against, spent, _balance, _rng);

		Bury(army.Men, day.AttackerLosses);
		here.Bury();
		Bury(walls ? against.Castle : against.Field, day.DefenderLosses);

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
	/// changes hands; whichever side breaks is gone.</summary>
	public Battle.Result Engage(FieldArmy army, FieldArmy enemy)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		ProvinceEconomy there = enemy == null ? null : _provincesByName.GetValueOrDefault(enemy.Home);
		if (here == null || there == null || army.Strength == 0 || enemy.Strength == 0)
		{
			return new Battle.Result(false, new Dictionary<string, int>(), new Dictionary<string, int>(), 0);
		}

		var against = new Defenders(enemy.Men, new Dictionary<string, int>(), "", there.Loyalty, InOpenCountry: true);
		Battle.Result day = Battle.InTheField(army.Men, against, army.MarchLeft <= 0f, _balance, _rng);
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
	/// Refused where any of its lord's companies still stand in the open: a castle cannot be shut in
	/// while his field army is at large behind the siege lines. The town's own militia is not such an
	/// army — a siege shuts it in with the rest of the town.</summary>
	public bool Besiege(FieldArmy army, string county)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
		Defenders against = DefendersOf(county);
		if (here == null || there == null || army.Strength == 0 || here.Realm == there.Realm
			|| !against.Held || StandingIn(county).Count > 0)
		{
			return false;
		}

		there.BesiegedFrom = army.Key;
		there.SiegeSeasons = 0;
		there.HungrySeasons = 0;
		return true;
	}

	/// <summary>Takes one company out of every siege it was keeping.</summary>
	private void Lift(FieldArmy besieger)
	{
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			if (besieger != null && province.BesiegedFrom == besieger.Key)
			{
				province.BesiegedFrom = "";
				province.SiegeSeasons = 0;
				province.HungrySeasons = 0;
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
				province.BesiegedFrom = "";
				province.SiegeSeasons = 0;
				province.HungrySeasons = 0;
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

	/// <summary>Who would have to be beaten to take a county: the men a lord has standing in it, or
	/// the militia an unclaimed one raises out of its own people.
	///
	/// One answer and not two, because the banner the map hangs over a county is read off the same
	/// call the battle is fought against. A county that shows forty men from the hilltop and fields
	/// ninety when the shooting starts is the game lying to the player about the one thing he planned
	/// his season around.
	///
	/// The rosters of a county somebody holds are that county's own and not copies: what a battle
	/// takes out of them, it takes out of the men themselves. An unclaimed county's militia is raised
	/// fresh on every call, so what a failed attack cost them is forgotten by the next one — they are
	/// not in the turn, and there is nowhere for it to be remembered.
	/// ponytail: militia damage is not kept, add when unheld counties take turns of their own.</summary>
	public Defenders DefendersOf(string county)
	{
		ProvinceEconomy held = _provincesByName.GetValueOrDefault(county);
		if (held != null)
		{
			// One company's own roster where there is one company, so a battle takes its dead off
			// the men who died. Where a lord has several standing there it is a reading and not a
			// roster — Attack and Besiege put them under one banner first, and then there is one.
			List<FieldArmy> there = StandingIn(county);
			Dictionary<string, int> field = there.Count == 1 ? there[0].Men : new Dictionary<string, int>();
			if (there.Count > 1)
			{
				foreach (FieldArmy army in there)
				{
					foreach ((string unit, int men) in army.Men)
					{
						field[unit] = field.GetValueOrDefault(unit) + men;
					}
				}
			}

			// Nobody of the lord's standing there: the town turns out for itself, the way an unclaimed
			// one does — unless it already turned out this season and was beaten, or a siege has it
			// shut in behind its gate.
			if (there.Count == 0 && held.MilitiaRoutedTurn != Turn && held.BesiegedFrom.Length == 0)
			{
				field = Militia(held.Population);
			}

			return new Defenders(field, held.Castle, held.Fortification, held.Loyalty);
		}

		ProvinceDefinition free = _unheld.GetValueOrDefault(county);
		if (free == null)
		{
			return new Defenders(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 0f);
		}

		return new Defenders(Militia(free.InitialPopulation), new Dictionary<string, int>(), free.InitialFortification,
			ProvinceEconomy.OpeningLoyalty);
	}

	/// <summary>What a town turns out when nobody else is standing in it: MilitiaShare of its people,
	/// the watch first — MilitiaArmed of them, bows and spears half and half — and every other man
	/// with what hangs in the barn.</summary>
	private Dictionary<string, int> Militia(int people)
	{
		var raised = new Dictionary<string, int>();
		int militia = Mathf.FloorToInt(people * _balance.MilitiaShare[(int)Difficulty]);
		int armed = Mathf.FloorToInt(militia * _balance.MilitiaArmed[(int)Difficulty]);
		int bows = armed / 2;
		foreach ((string unit, int men) in new[] { (WatchBows, bows), (WatchSpears, armed - bows), (MilitiaUnit, militia - armed) })
		{
			if (men > 0)
			{
				raised[unit] = men;
			}
		}

		return raised;
	}

	public ProvinceEconomy GetProvince(string name)
	{
		ProvinceEconomy province = _provincesByName.GetValueOrDefault(name);
		return province != null && province.Realm == _playerRealm ? province : null;
	}

	/// <summary>Any province being run, whoever holds it — for the few things a lord can see from
	/// the road: his neighbour's fields under the plough, and the walls going up on his seat.</summary>
	public ProvinceEconomy AnyProvince(string name) =>
		_provincesByName.GetValueOrDefault(name);

	/// <summary>Adds this season's goodwill into the county's record of the year. A running mean, so
	/// a year part-way through reads as the year so far rather than as a hole in the chart.
	///
	/// A save written before counties kept a record has none, and its early years are drawn at what
	/// the county stands at today — there is nothing else to draw them from, and a chart that begins
	/// at the year the save was loaded looks like the campaign did.</summary>
	private void Remember(ProvinceEconomy province)
	{
		int year = (Turn - 1) / SeasonsPerYear;
		int seasonsIn = ((Turn - 1) % SeasonsPerYear) + 1;
		while (province.HappinessByYear.Count <= year)
		{
			province.HappinessByYear.Add(province.Loyalty);
		}

		province.HappinessByYear[year] += (province.Loyalty - province.HappinessByYear[year]) / seasonsIn;
	}

	/// <summary>Which counties border which, by name, as the campaign's map has them
	/// (provinces.json "neighbours"). Empty — the checks — and nobody moves house.</summary>
	public Dictionary<string, List<string>> Neighbours { get; set; } = new();

	/// <summary>The season's moving house, the original's rule: from every county, some of its people
	/// leave for its happiest neighbour if that neighbour is happier (Livelihood.Movers). Worked out
	/// for every county before anybody moves, so one county's arrivals do not change whether they
	/// themselves would have left.</summary>
	private void Migrate()
	{
		var moves = new List<(ProvinceEconomy From, ProvinceEconomy To, int People)>();
		foreach (ProvinceEconomy county in _provincesByName.Values)
		{
			ProvinceEconomy best = null;
			foreach (string name in Neighbours.GetValueOrDefault(county.ProvinceName, new List<string>()))
			{
				ProvinceEconomy next = _provincesByName.GetValueOrDefault(name);
				if (next != null && (best == null || next.Loyalty > best.Loyalty))
				{
					best = next;
				}
			}

			int movers = best == null ? 0 : Livelihood.Movers(county, best.Loyalty, neutral: false);
			if (movers > 0)
			{
				moves.Add((county, best, movers));
			}
		}

		foreach ((ProvinceEconomy from, ProvinceEconomy to, int people) in moves)
		{
			int going = Mathf.Min(people, from.Population);
			from.Population -= going;
			to.Population += going;
			if (_lastSeason.TryGetValue(from.ProvinceName, out TurnSummary left))
			{
				left.Moved -= going;
				left.Restate(from);
			}

			if (_lastSeason.TryGetValue(to.ProvinceName, out TurnSummary came))
			{
				came.Moved += going;
				came.Restate(to);
			}
		}
	}

	/// <summary>What the realm's rates cost this county's happiness a season: the original's table
	/// (Livelihood.EmpireTerm) summed over every county its lord holds, this one among them — so a
	/// lord who squeezes one shire past twenty percent is resented in all of them.</summary>
	public float Resented(ProvinceEconomy province)
	{
		int term = 0;
		foreach (ProvinceEconomy other in _provincesByName.Values)
		{
			if (other.Realm == province.Realm)
			{
				term += Livelihood.EmpireTerm(other.Tax);
			}
		}

		return term;
	}

	/// <summary>How many other counties the same lord holds — what the tax table needs to know
	/// whether "other counties" means anything at all yet.</summary>
	public int OtherCounties(ProvinceEconomy province)
	{
		int held = 0;
		foreach (ProvinceEconomy other in _provincesByName.Values)
		{
			if (other != province && other.Realm == province.Realm)
			{
				held++;
			}
		}

		return held;
	}

	/// <summary>Every province in authored order — what a save writes out.</summary>
	public List<ProvinceEconomy> Provinces =>
		_definitions.ConvertAll(definition => _provincesByName[definition.ProvinceName]);

	/// <summary>What the realm as a whole holds of one store right now, across every province it
	/// keeps. There is no shared treasury — each province has its own pile — so the hall adds them
	/// up to say what the crown is worth.</summary>
	public int RealmStore(string store)
	{
		int total = 0;
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			// The player's own, and only his. A rival's granary counted into the crown's total would
			// be a lie the top bar tells every turn, and the one the player plans his winter on.
			if (province.Realm == _playerRealm)
			{
				total += province.Stored(store);
			}
		}

		return total;
	}

	/// <summary>Puts a save's state back in place. Provinces are matched by name, so a save
	/// written before a province was added or renamed still loads: the missing one simply keeps
	/// the starting values the definitions gave it.</summary>
	public void Restore(int turn, List<ProvinceEconomy> provinces, Dictionary<string, float> prices,
		Difficulty difficulty)
	{
		Turn = turn;
		Difficulty = difficulty;
		// A save written before the market moved carries no prices, and an empty book is exactly
		// right for it: every store simply sits at what it is worth.
		Market.Pressure = prices ?? new Dictionary<string, float>();
		Market.Turned(CurrentSeason);
		foreach (ProvinceEconomy province in provinces)
		{
			if (!_provincesByName.ContainsKey(province.ProvinceName))
			{
				continue;
			}

			// A save written before provinces carried a holder says nothing about who held them, and
			// the campaign's own layout is the right answer for that file: nobody had conquered
			// anything yet, so everyone still holds what they opened with.
			if (province.Realm.Length == 0)
			{
				province.Realm = _provincesByName[province.ProvinceName].Realm;
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

			province.Bury();
			_provincesByName[province.ProvinceName] = province;
		}

		PoolPurses(pooling: false);
	}

	/// <summary>The season, everywhere at once. Every held county is run — the player's and the
	/// lords' — and what comes back is what happened in the player's own, because that is all the
	/// screens have any business drawing.
	///
	/// Fixed iteration order (the authored definitions list), not dictionary enumeration order, so a
	/// turn's outcome is reproducible (design doc section 34).</summary>
	/// <summary>The one county of each realm the world may visit this season, if the realm is not
	/// still getting over the last visit. Picked at random among the realm's counties, walked in
	/// authored order so the same seed picks the same county.</summary>
	private Dictionary<string, string> WhereTheWorldStirs()
	{
		var counties = new Dictionary<string, List<string>>();
		var realms = new List<string>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			string realm = _provincesByName[definition.ProvinceName].Realm;
			if (!counties.TryGetValue(realm, out List<string> held))
			{
				held = new List<string>();
				counties[realm] = held;
				realms.Add(realm);
			}

			held.Add(definition.ProvinceName);
		}

		var stirs = new Dictionary<string, string>();
		foreach (string realm in realms)
		{
			if (_worldLastStirred.TryGetValue(realm, out int last) && Turn - last <= _balance.WorldEventGap)
			{
				continue;
			}

			List<string> held = counties[realm];
			stirs[realm] = held[_rng.RandiRange(0, held.Count - 1)];
		}

		return stirs;
	}

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

	public List<TurnSummary> AdvanceTurn()
	{
		// Nobody watched them take their turn — the checks, a harness — so they take it now.
		RivalsTurn();

		Season season = CurrentSeason;
		var summaries = new List<TurnSummary>(_definitions.Count);
		var news = new List<FiredEvent>(_rivalNews);
		_rivalNews.Clear();
		Flooded.Clear();

		Dictionary<string, string> stirs = WhereTheWorldStirs();
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			bool players = province.Realm == _playerRealm;

			TurnSummary summary = EconomySimulation.RunTurn(province, definition, _balance, season);

			// What the rest of the realm's rates cost this county. Applied here and not in the
			// simulation because it is the one thing about a county's year that is not about the
			// county: it takes every other province the same lord holds to work out.
			summary.LoyaltyFromNeighbours = Resented(province);
			province.Loyalty = Mathf.Clamp(province.Loyalty + summary.LoyaltyFromNeighbours, 0f, 100f);

			// The world takes its turn after the province has taken its own, so an event answers the
			// season that actually happened — the rats come for the granary as the harvest left it.
			// It comes for a rival's granary on the same terms: an AI lord the plague cannot touch is
			// a cheat, and it would be one the player never sees and could never account for.
			bool stirred = stirs.GetValueOrDefault(province.Realm) == province.ProvinceName;
			List<FiredEvent> happened =
				EventEngine.AfterTurn(province, _balance, season, Turn, summary, _rng, stirred);
			if (happened.Exists(item => item.Said.Id.StartsWith("flood")))
			{
				Flooded.Add(province.ProvinceName);
			}

			if (stirred && happened.Exists(item => !item.FromThePeople))
			{
				_worldLastStirred[province.Realm] = Turn;
			}

			// Soldiers for hire walk in on their own errand, into any lord's county: the rivals hire
			// them too (LordArms).
			Mercenaries.Season(province, _balance, _rng);

			// Last of all, and after the world has had its turn: a season that killed or drove out
			// people is dealt again, or the county goes on being paid for labour by men who are dead
			// or three counties away.
			Labour.Deal(province, definition, _balance, (Season)(((int)season + 1) % SeasonsPerYear));

			// Stamped again: a plague or a flood moved these numbers after the arithmetic finished,
			// and the summary is supposed to be what the season did, not what it had done by halfway.
			summary.Restate(province);
			_lastSeason[province.ProvinceName] = summary;
			Remember(province);

			// Only what happened in his own counties is news a lord could have heard. He is not told
			// the Northern Watch has murrain, and his turn report is not padded with a rival's year.
			if (players)
			{
				news.AddRange(happened);
				summaries.Add(summary);
			}
		}

		Migrate();

		// Last, and outside the loop above: a castle that gives up moves a county between realms, and
		// doing that while that loop is still walking the counties would be running one of them for
		// a lord who no longer holds it.
		Sieges(news);

		// A turn that carries more news than it can tell drops the world's half first: cutting by the
		// order the provinces happen to be authored in would let one county's rats bury another
		// county's revolt, and the lord would never learn he had lost it.
		//
		// Two passes rather than a sort, because .NET's sort is not stable and the provinces are
		// walked in authored order on purpose — a comparator would quietly shuffle which county is
		// heard first from one run of the same turn to the next.
		var told = new List<FiredEvent>(news.Count);
		foreach (FiredEvent item in news)
		{
			if (item.FromThePeople)
			{
				told.Add(item);
			}
		}

		foreach (FiredEvent item in news)
		{
			if (!item.FromThePeople)
			{
				told.Add(item);
			}
		}

		News = told.Count > MostNewsPerTurn ? told.GetRange(0, MostNewsPerTurn) : told;
		Turn++;

		// After the turn counter, so the market opens the new season on the new season's prices.
		Market.Turned(CurrentSeason);
		return summaries;
	}
}
