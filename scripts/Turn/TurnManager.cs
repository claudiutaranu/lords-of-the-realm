using System.Collections.Generic;
using System.Linq;
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
public partial class TurnManager
{
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

	/// <summary>Which of the lords (Lords) sits in each realm, by key — the campaign's to say
	/// (provinces.json "lord"). A realm with nobody seated writes no letters.</summary>
	public Dictionary<string, string> LordOf { get; set; } = new();

	/// <summary>The letters between the realms: who thinks what of whom, who is sworn to whom.</summary>
	public Diplomacy Diplomacy { get; private set; } = new();

	/// <summary>The realm the player is, for whoever has to tell his men from everybody else's.</summary>
	public string PlayerRealm => _playerRealm;

	public GameBalance Balance => _balance;

	/// <summary>What the player's realm has in its one purse, or nothing once he holds no county.</summary>
	public int PlayerGold => _provincesByName.Values.FirstOrDefault(p => p.Realm == _playerRealm)?.Purse.Gold ?? 0;

	/// <summary>The rival lords' war. Null until the map has handed over its ground (see
	/// <see cref="Survey"/>): without a road to walk, nobody marches — which is also how the checks
	/// run a turn without a map.</summary>
	private LordsCampaign _campaign;

	/// <summary>Adds a line to what the player will hear this turn, the steward's numbers written in.</summary>
	private void Told(string county, string id, Dictionary<string, string> fill)
	{
		GameEvent said = EventEngine.Find(id);
		if (said == null)
		{
			return;
		}

		string text = said.Text;
		foreach ((string field, string value) in fill)
		{
			text = text.Replace($"{{{field}}}", value);
		}

		_rivalNews.Add(new FiredEvent(county, said with { Text = text }, FromThePeople: true));
	}

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
	/// of six counties heard of rats, murrain or plague nearly every season, one on top of another.</summary>
	private readonly Dictionary<string, int> _worldLastStirred = new();

	public IReadOnlyDictionary<string, int> WorldLastStirred => _worldLastStirred;

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

	public ProvinceEconomy GetProvince(string name)
	{
		ProvinceEconomy province = _provincesByName.GetValueOrDefault(name);
		return province != null && province.Realm == _playerRealm ? province : null;
	}

	/// <summary>Any province being run, whoever holds it — for the few things a lord can see from
	/// the road: his neighbour's fields under the plough, and the walls going up on his seat.</summary>
	public ProvinceEconomy AnyProvince(string name) =>
		_provincesByName.GetValueOrDefault(name);

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

	public List<TurnSummary> AdvanceTurn()
	{
		// Nobody watched them take their turn — the checks, a harness — so they take it now.
		RivalsTurn();

		// The carts roll after the rivals have marched, so a company that halted on the road is
		// standing there when they come by; and before the counties' season, so what they bring in
		// is in the granary the county eats from.
		Haul();

		Season season = CurrentSeason;
		var summaries = new List<TurnSummary>(_definitions.Count);
		var news = new List<FiredEvent>(_rivalNews);
		_rivalNews.Clear();
		Flooded.Clear();

		// The sky first (the original's step 3), over every county anybody holds: the season's
		// weather is what its sowing, growing, reaping and herd are reckoned under.
		news.AddRange(Skies((Season)(((int)season + 1) % SeasonsPerYear)));

		Dictionary<string, string> stirs = WhereTheWorldStirs();
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			bool players = province.Realm == _playerRealm;

			TurnSummary summary = EconomySimulation.RunTurn(province, definition, _balance, season);

			// A site an enemy army stood on has been shut a season more.
			foreach (string site in new List<string>(province.Occupied.Keys))
			{
				if (--province.Occupied[site] <= 0)
				{
					province.Occupied.Remove(site);
				}
			}

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
			RememberPeople(province, summary);

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
