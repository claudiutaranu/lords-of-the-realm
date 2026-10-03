using Godot;

/// <summary>Every tunable economy constant, in one place, editable from a .tres without
/// touching code. Season/tax/ration arrays are indexed by the matching enum's ordinal —
/// keep their lengths and order in sync with <see cref="Season"/>
/// and <see cref="RationLevel"/>.</summary>
[GlobalClass]
public partial class GameBalance : Resource
{
	private const string EnginePath = "res://data/game-balance.tres";

	/// <summary>The engine's own numbers. Balance is not a campaign's: every realm is simulated by
	/// the same rules, so anything that needs a figure and was not handed one reads it from here
	/// rather than writing the path out again.</summary>
	public static GameBalance Engine => GD.Load<GameBalance>(EnginePath);

	[Export] public float WoodYieldPerWorker = 0.375f;
	[Export] public float StoneYieldPerWorker = 0.3f;
	[Export] public float IronYieldPerWorker = 0.25f;

	/// <summary>Hands at the anvil for one weapon a season — one: at two, a county's smithy armed so
	/// few men a season that an army was years in the making (the user's call). The original's rate
	/// is not known;
	/// ponytail: a flat rate with no efficiency ramp — Lords of the Realm's sites start near 15% and
	/// warm up season on season, and that curve belongs here once the original's table is read.</summary>
	[Export] public int SmithsPerWeapon = 1;

	/// <summary>The most hand-seasons one waste field takes in a season: the original's two hundred.</summary>
	[Export] public int ReclaimPerSeason = 200;

	/// <summary>How practised a site is the first season it is worked, in percent, and how much of
	/// that it gains each season it keeps at least one man, compounded, up to a hundred. The
	/// original's floor is about fifteen; its rate of climb is not known and this one is a guess
	/// that takes a site from floor to full in about five seasons.</summary>
	[Export] public int SiteEfficiencyFloor = 15;
	[Export] public int SiteEfficiencyGrowth = 50;

	// --- the fields (Husbandry keeps the original's own figures) --------------------------------

	/// <summary>How many hands a rung of wall expects for each of the seasons it is priced at. At
	/// exactly this many it takes the seasons the fortifications room quotes; at twice, half as
	/// long; with nobody on it, it does not rise at all. It is the county's other great work, and
	/// the place a lord puts the men the fields and the trades have no use for — which is what
	/// Lords of the Realm's own labour bar is really for.</summary>
	[Export] public int MasonsPerBuildSeason = 200;

	[Export] public float PeoplePerGrain = 10f;

	// [Spring, Summer, Autumn, Winter]
	[Export] public float[] WoodSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };
	[Export] public float[] StoneSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.70f };
	[Export] public float[] IronSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };

	/// <summary>The goodwill above which a county draws people in and below which it starts to lose
	/// them: a content province is somewhere people walk towards; a resented one empties from the
	/// edges long before it revolts.</summary>
	[Export] public float ImmigrationAbove = 72f;
	[Export] public float EmigrationBelow = 35f;

	// [None, Low, Normal, High, Severe]
	/// <summary>The rate a county thinks is fair, and the most a lord is allowed to ask. Below the
	/// fair rate the people are grateful and above it they are not, so this one number is where the
	/// whole tax decision turns.
	///
	/// Five, because that is where Lords of the Realm II put it. Three of its own figures fall on one
	/// straight line: nothing taken is worth +5 a season, seventeen percent costs -12, and the line
	/// through them crosses zero at five points with a slope of exactly one. A county is grateful for
	/// a very light hand and resents almost everything else — which is why that game was played at
	/// ten percent and not at thirty.</summary>
	[Export] public int FairTaxPercent = 5;
	[Export] public int MostTaxPercent = 40;

	// What one unit of each store is worth at market, between what the merchant asks and what he
	// offers: Lords of the Realm II's own prices (sell/buy — a cow 12/24, a sack 2/4, stone 2/4, iron
	// and timber 1/2) come out of these at the spread below.
	[Export] public int GrainPrice = 3;
	[Export] public int CattlePrice = 18;
	[Export] public int WoodPrice = 2;
	[Export] public int StonePrice = 3;
	[Export] public int IronPrice = 2;

	/// <summary>How the counter works. The spread is the merchant's cut on both sides, which is what
	/// stops a moving price from being a money printer — sell until it drops, buy it back cheaper,
	/// repeat. The depth is how much gold of one-way trade it takes to move a price by its whole
	/// base value, so it is measured in turnover rather than in sacks and one figure covers every
	/// store. The drift is how much of that fades each season, and the floor and ceiling are how far
	/// a price can be driven in either direction before the market stops listening.</summary>
	[Export] public float MarketSpread = 0.333f; // the original buys at twice what it sells for
	[Export] public float MarketDepth = 1200f;
	[Export] public float PriceDrift = 0.25f;
	[Export] public float PriceFloor = 0.45f;
	[Export] public float PriceCeiling = 2.4f;

	/// <summary>What the time of year does to what the field's own produce fetches. Grain is cheap
	/// the week it is reaped and dear the week before the next harvest — which is the one thing in
	/// this game that asks a lord to look further ahead than next turn. [Spring, Summer, Autumn,
	/// Winter]</summary>
	[Export] public float[] GrainSeasonPrice = { 1.30f, 1.10f, 0.75f, 1.05f };
	[Export] public float[] CattleSeasonPrice = { 1.10f, 1.00f, 0.90f, 1.05f };

	// What finished arms fetch: Lords of the Realm II's own prices (sell/buy — spear 13/26, bow 16/32,
	// sword 23/46, crossbow 24/48, mace 10/20, a knight's armour 44/88) come out of these at the spread. Three times these, a spear cost a hundred once a rival had been
	// buying, and a lord with one county could not arm a company.
	[Export] public int SwordPrice = 35;
	[Export] public int BowPrice = 24;
	[Export] public int CrossbowPrice = 36;
	[Export] public int SpearPrice = 20;
	[Export] public int MacePrice = 15;
	[Export] public int HorsePrice = 66;
}
