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
	/// <summary>What a county with no lord puts in the way of one who wants it, by the key
	/// recruits.json uses. Nobody drilled them and nobody armed them.</summary>
	private const string MilitiaUnit = "peasant";

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

	/// <summary>How well the other lords play. Held here rather than read from a global each turn so
	/// that a loaded save is played at the difficulty it was started on, not at whatever the menu
	/// last had.</summary>
	public Difficulty Difficulty { get; private set; }

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

	/// <summary>The season just played, county by county. Kept because the happiness table is read
	/// after the turn is over and the summary is otherwise thrown away with the turn that made it —
	/// and a breakdown of a season the player can no longer see is the only kind worth showing.</summary>
	private readonly Dictionary<string, TurnSummary> _lastSeason = new();

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
			province.MarchLeft = balance.MarchReach;
			// A province opens with its people already at work. Nobody would hand a lord a county
			// where every field is sown and not one man is in it.
			EconomySimulation.Deploy(province, definition, balance, CurrentSeason);
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
	public bool March(string from, string toCounty, Vector2 at, float cost)
	{
		ProvinceEconomy here = _provincesByName.GetValueOrDefault(from);
		if (here == null || here.FieldMen == 0 || cost <= 0f || cost > here.MarchLeft)
		{
			return false;
		}

		// Men who are marching are not sitting in front of anybody's gate. A siege is the army being
		// THERE, so the moment it is somewhere else there is no siege — no order to cancel and no way
		// to forget to.
		Lift(from);

		// Wherever they were sent, they are standing there now and the season is that much shorter.
		// Everything below is about whether anything CHANGED HANDS by their standing there, which is
		// a different question and mostly answered no.
		here.ArmyX = at.X;
		here.ArmyY = at.Y;
		here.MarchLeft -= cost;

		ProvinceEconomy there = _provincesByName.GetValueOrDefault(toCounty);

		// Their own county, or another lord's they are only crossing. Walking over a county has never
		// taken it and does not take it now: that is settled at the seat, against whoever is standing
		// on it. The men stay on the roster of the county that raised them — the one still feeding
		// and paying them — however far from home they have got.
		if (toCounty == from || (there != null && there.Realm != here.Realm))
		{
			return true;
		}

		if (there == null)
		{
			// Nobody holds it. If its own people have taken up what hangs in the barn, they have to
			// be beaten before anything changes hands, and the march simply ends on their ground.
			// An empty county is walked into, the way it always was.
			return DefendersOf(toCounty).Men > 0 || Claim(from, toCounty, at);
		}

		// A county of his own realm: the men join whoever is already standing there, so two counties'
		// companies can be brought together into one army instead of standing in the same field under
		// two banners.
		Join(here, there, at);
		return true;
	}

	/// <summary>Hands a county to the realm whose men are standing on its seat, with those men on it.
	/// Called when there is nobody left in the way — either because there never was anybody, or
	/// because the battle for it has just been settled.
	///
	/// The county keeps everything but its lord: its stores, its fields, its walls and its people are
	/// exactly what they were the moment before, because they are the reason anybody wanted it. What
	/// it loses is whoever was holding it, and whatever they still had standing.</summary>
	public bool Claim(string from, string toCounty, Vector2 at)
	{
		ProvinceEconomy here = _provincesByName.GetValueOrDefault(from);
		if (here == null || toCounty == from)
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
			EconomySimulation.Deploy(there, taken, _balance, CurrentSeason);
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

		// Whoever was holding it is not holding it any more, and neither are his men: they are dead,
		// scattered or walked off by the time anybody is claiming anything.
		there.Garrison.Clear();
		there.Castle.Clear();
		there.Realm = here.Realm;
		there.Purse = here.Purse;

		// Nobody is glad to be conquered. A county taken has to be held before it is worth having,
		// which is what stops a lord taking everything he can walk to.
		there.Loyalty = Mathf.Max(0f, there.Loyalty - _balance.ConquestResentment);
		Join(here, there, at);
		return true;
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
	public Battle.Result Attack(string from, string county, Vector2 at, bool walls)
	{
		ProvinceEconomy here = _provincesByName.GetValueOrDefault(from);
		if (here == null || here.FieldMen == 0)
		{
			return new Battle.Result(false, new Dictionary<string, int>(),
				new Dictionary<string, int>(), 0);
		}

		// Men who have nothing left in their legs are attacking on the last of them. The map charged
		// them for the road on the way here, so this is simply read off what is left of the season.
		bool spent = here.MarchLeft <= 0f;
		Defenders against = DefendersOf(county);
		Battle.Result day = walls
			? Battle.OnTheWalls(here.Garrison, against, spent, _balance, _rng)
			: Battle.InTheField(here.Garrison, against, spent, _balance, _rng);

		Bury(here.Garrison, day.AttackerLosses);
		Bury(walls ? against.Castle : against.Field, day.DefenderLosses);

		// Beaten in the open with walls at their back, the survivors do not stand in the field to be
		// ridden down. This is what turns one battle into two, and what a lord who keeps a garrison
		// is paying for.
		if (!walls && day.AttackerWon)
		{
			FallBack(county);
		}

		// Carrying the walls IS taking the county: whoever is left on them when they are carried is
		// taken with them, and Claim clears them off. Carrying the FIELD only takes it where there
		// was nowhere left to fall back to — a lord can lose every man he had outside his walls and
		// still hold the place, which is the entire argument for quarrying stone.
		if (day.AttackerWon && (walls || !DefendersOf(county).Held))
		{
			Claim(from, county, at);
		}

		return day;
	}

	/// <summary>Sits an army down in front of a gate it has decided not to climb.
	///
	/// The other way to take a castle, and the one the stone rungs are actually taken by: a garrison
	/// eats what was carried up before the siege, and then it eats nothing. It costs the besieger
	/// his army's whole season, every season — the men are standing there rather than anywhere
	/// else — and it costs the besieged his county's income for as long as it lasts.
	///
	/// Refused where there is anybody still standing in the open: a castle cannot be shut in while
	/// its lord's field army is at large behind the siege lines.</summary>
	public bool Besiege(string from, string county)
	{
		ProvinceEconomy here = _provincesByName.GetValueOrDefault(from);
		ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
		Defenders against = DefendersOf(county);
		if (here == null || there == null || here.FieldMen == 0 || here.Realm == there.Realm
			|| !against.Held || ProvinceEconomy.Men(against.Field) > 0)
		{
			return false;
		}

		there.BesiegedFrom = from;
		there.SiegeSeasons = 0;
		there.HungrySeasons = 0;
		return true;
	}

	/// <summary>Takes a county's army out of every siege it was keeping.</summary>
	private void Lift(string besieger)
	{
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			if (province.BesiegedFrom == besieger)
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
			ProvinceEconomy besieger = province.BesiegedFrom.Length == 0
				? null
				: _provincesByName.GetValueOrDefault(province.BesiegedFrom);

			if (province.BesiegedFrom.Length == 0)
			{
				continue;
			}

			// Nobody out there any more — the besiegers starved, deserted or were beaten off.
			if (besieger == null || besieger.FieldMen == 0)
			{
				Lift(province.BesiegedFrom);
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

			bool ours = besieger.Realm == _playerRealm;
			string county = province.ProvinceName;
			Claim(province.BesiegedFrom, county, new Vector2(besieger.ArmyX, besieger.ArmyY));

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

	/// <summary>Puts what is left of a beaten field army behind its own walls, where it has any. A
	/// county with none has nowhere to fall back to and loses everything with the field.</summary>
	private void FallBack(string county)
	{
		ProvinceEconomy holding = _provincesByName.GetValueOrDefault(county);
		if (holding == null || holding.Fortification.Length == 0)
		{
			return;
		}

		foreach ((string unit, int men) in holding.Garrison)
		{
			holding.Castle[unit] = holding.Castle.GetValueOrDefault(unit) + men;
		}

		holding.Garrison.Clear();
	}

	/// <summary>Moves a county's field army onto another county of the same realm, with whatever the
	/// season has left in it. The men move, not a copy of them: the county they left has none and the
	/// county they entered has them all.
	///
	/// What the season has left goes with the men rather than with the county they walked out of —
	/// an army that crossed one border can cross another before the snow.</summary>
	private static void Join(ProvinceEconomy here, ProvinceEconomy there, Vector2 at)
	{
		foreach ((string unit, int men) in here.Garrison)
		{
			there.Garrison[unit] = there.Garrison.GetValueOrDefault(unit) + men;
		}

		float left = here.MarchLeft;
		here.Garrison.Clear();
		here.MarchLeft = 0f;

		there.ArmyX = at.X;
		there.ArmyY = at.Y;
		there.MarchLeft = Mathf.Max(0f, left);
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
			return new Defenders(held.Garrison, held.Castle, held.Fortification, held.Loyalty);
		}

		ProvinceDefinition free = _unheld.GetValueOrDefault(county);
		if (free == null)
		{
			return new Defenders(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 0f);
		}

		var raised = new Dictionary<string, int>();
		int militia = Mathf.FloorToInt(free.InitialPopulation * _balance.MilitiaShare);
		if (militia > 0)
		{
			raised[MilitiaUnit] = militia;
		}

		return new Defenders(raised, new Dictionary<string, int>(), free.InitialFortification,
			ProvinceEconomy.OpeningLoyalty);
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

	/// <summary>What the rest of this province's realm costs it in goodwill each season, by how hard
	/// the rest of it is being taxed. Public because the tax table has to show a lord what a rate
	/// will do to his other counties BEFORE he sets it — a cost you only discover after the turn is
	/// a cost the player cannot plan around, and he will read it as the game being unfair.
	///
	/// Walked in authored order rather than by dictionary, like everything else that adds up across
	/// provinces here: floating point does not add the same way twice if the order moves.</summary>
	public float Resented(ProvinceEconomy province)
	{
		float spill = 0f;
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy other = _provincesByName[definition.ProvinceName];
			if (other != province && other.Realm == province.Realm)
			{
				spill += EconomySimulation.TaxSpill(other.Tax, _balance);
			}
		}

		return spill;
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
	public List<TurnSummary> AdvanceTurn()
	{
		Season season = CurrentSeason;
		var summaries = new List<TurnSummary>(_definitions.Count);
		var news = new List<FiredEvent>();

		// Every rival gives his orders before ANY county is run. Two passes rather than one, because
		// a lord's tax is felt in his neighbours' counties as well as his own: run them one at a
		// time and whether a rate counted this season or next would depend on which province happens
		// to be authored first.
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			if (province.Realm != _playerRealm)
			{
				LordAI.TakeTurn(province, definition, _balance, Market, season, Difficulty);
			}
		}

		// Every army gets its season's ground back. Done before the turn rather than after, so a
		// county taken this turn can be marched out of next turn and not the turn after that.
		foreach (ProvinceEconomy province in _provincesByName.Values)
		{
			province.MarchLeft = _balance.MarchReach;
		}

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
			List<FiredEvent> happened =
				EventEngine.AfterTurn(province, _balance, season, Turn, summary, _rng);

			// Soldiers for hire walk in on their own errand, and only where the lord could actually
			// take them up on it: nobody is offering a company to a county he does not hold.
			if (players)
			{
				Mercenaries.Season(province, _balance, _rng);
			}

			// Last of all, and after the world has had its turn: a season that killed or drove out
			// people has to take their hands out of the work too, or the county goes on being paid
			// for labour by men who are dead or three counties away.
			EconomySimulation.FitWorkforce(province);

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
