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

	/// <summary>Hands at the anvil for one weapon a season. The original's rate is not known;
	/// ponytail: a flat rate with no efficiency ramp — Lords of the Realm's sites start near 15% and
	/// warm up season on season, and that curve belongs here once the original's table is read.</summary>
	[Export] public int SmithsPerWeapon = 2;

	/// <summary>The most hand-seasons of reclaiming torn ground takes in one season: the original's
	/// two hundred to a field.</summary>
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
	[Export] public float BasePopulationGrowthRate = 0.01f;

	/// <summary>Seasons of bread a county wants in the barn before it has children at the full rate;
	/// below it, births fall in step with the barn.</summary>
	[Export] public float BirthsWantBarnSeasons = 1f;
	/// <summary>What one head pays the reeve for each point of tax, a season. A county of a thousand
	/// on the rate it calls fair brings in a hundred and fifty crowns, and every point above that is
	/// thirty more — so the tax is the lord's main lever on his income, and a heavy one really is
	/// worth the goodwill it burns.</summary>
	[Export] public float GoldPerHeadPerTaxPoint = 0.03f;

	[Export] public float StarvationPopulationLossPerDeficit = 0.075f;
	[Export] public float StarvationLoyaltyLossPerDeficit = 25f;

	// [Spring, Summer, Autumn, Winter]
	[Export] public float[] WoodSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };
	[Export] public float[] StoneSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.70f };
	[Export] public float[] IronSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };

	/// <summary>Who moves, and how fast. A content province is somewhere people walk towards; a
	/// resented one empties from the edges long before it revolts.</summary>
	[Export] public float ImmigrationAbove = 72f;
	[Export] public float EmigrationBelow = 35f;
	[Export] public float ImmigrationRate = 0.006f;
	[Export] public float EmigrationRate = 0.012f;

	/// <summary>How much faster a county at no goodwill at all empties than one that has only just
	/// crossed the line. A flat rate makes losing a county entirely look exactly like being mildly
	/// disliked, and the lord cannot tell from the number which one he has.</summary>
	[Export] public float EmigrationDepthMultiple = 2f;

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

	/// <summary>Goodwill a season for each point the rate sits away from fair. Signed by which side
	/// of fair it falls on: a light hand is worth something, a heavy one costs.
	///
	/// One heart a point, which is the old game's own slope: +5 at nothing, -12 at seventeen, and
	/// every point in between exactly one apart. A gentler line is not a kinder game, it is a game
	/// where the tax is not a decision — at a fifth of this a lord could sit on a punitive rate for
	/// twenty years and shrug off what it cost him.</summary>
	[Export] public float LoyaltyPerTaxPoint = 1f;

	/// <summary>The most goodwill a light hand can be worth in one season. With the fair rate at five
	/// and a heart a point, taking nothing at all is already the best a lord can do and comes to
	/// exactly this — so the cap is not shaping the curve, it is standing behind it in case somebody
	/// moves the fair rate later and turns a light hand into a way of buying a county outright.</summary>
	[Export] public float TaxGoodwillCap = 5f;

	/// <summary>How much of a county's resentment at a heavy tax is felt in the lord's OTHER
	/// counties. Word travels: a realm that squeezes one shire is a realm the next shire expects to
	/// be squeezed by. It is what stops a lord parking one county on a punitive rate, letting it rot,
	/// and running the rest of his realm as though nothing were happening.</summary>
	[Export] public float OtherCountiesTaxShare = 0.2f;

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

	// --- what the world throws at a province -----------------------------------------------------

	/// <summary>How often the world stirs at all: the chance, per province per season, that
	/// ANYTHING out of the lord's hands happens to it. One number for the whole pace of a reign,
	/// because the alternative — a separate roll per disaster — looks rare on every line and adds up
	/// to something happening nearly every turn.
	///
	/// The opening year is spared entirely. A lord who loses his herd to murrain in his first spring
	/// has learnt nothing about murrain, only that the game is unfair.</summary>
	[Export] public float WorldEventChance = 0.2f;
	[Export] public int QuietOpeningTurns = 4;

	/// <summary>Turns a realm is left alone after the world has done something to it. The roll above
	/// is made once per realm per season, not once per county, and this is the breath after.</summary>
	[Export] public int WorldEventGap = 2;

	/// <summary>The size of the world's attention when it stirs. The weights below are shares of
	/// THIS, not of each other, so a province exposed to only one thing draws mostly nothing and a
	/// province doing everything wrong draws something nearly every time. Drop it to zero and the
	/// weights become shares of each other again — whatever the county is open to, it gets.</summary>
	[Export] public float WorldEventFloor = 12f;

	/// <summary>What each event is worth when the world does stir, against the others it could pick
	/// instead and against the attention above. What an event is even eligible for is decided by the
	/// state of the province — the rats need an overfull granary, murrain needs a crowded pasture —
	/// and these only settle which of the open ones the season brings. Zero switches one off.</summary>
	[Export] public float PlagueWeight = 1f;
	[Export] public float PlagueWeightHungry = 4f;
	[Export] public float FloodWeight = 2f;
	[Export] public float DroughtWeight = 2f;
	[Export] public float RatsWeight = 3f;
	[Export] public float MurrainWeight = 3f;
	[Export] public float BanditWeight = 3f;
	[Export] public float BumperWeight = 3f;

	/// <summary>How often a company walks into the county looking for work, and how long it waits
	/// before walking on. Its own roll rather than a share of the world's attention: a band for hire
	/// is not something that happens TO a county the way a flood does, and it should not be able to
	/// crowd a flood off the table. Three seasons is long enough for a lord to sell something and
	/// short enough that he has to decide. One chance in eight left a lord with a single county eight
	/// turns without a band a third of the time; one in three sees one most years.</summary>
	[Export] public float MercenaryChance = 0.33f;
	[Export] public int MercenarySeasons = 3;

	/// <summary>The Black Death runs for this many seasons once it arrives, killing this share of
	/// the county every one of them, before it burns itself out.</summary>
	[Export] public int PlagueSeasons = 3;
	[Export] public float PlagueDeathRate = 0.09f;
	[Export] public float PlagueLoyaltyLoss = 6f;

	/// <summary>What one flooded field takes to put right, counted in hand-seasons. The reclaimers
	/// take this down every turn, so how long a field lies waste is the lord's own answer to how many
	/// men he spares for it rather than a timer he waits out. Four hundred is two seasons at the
	/// most a season will take, and forever for a county that has moved everybody into the mine.
	/// The flood is a spring event and the drought a summer one, because both are only worth
	/// anything while there is a crop in the ground to take.</summary>
	[Export] public int FieldRepairWork = 400;
	[Export] public float DroughtCropLoss = 0.5f;

	/// <summary>Soil below this has been cropped past its rest, which is what makes a flood the
	/// lord's fault rather than the rain's; above <see cref="GoodHeart"/> the land is in condition
	/// to repay a good year.</summary>
	[Export] public int TiredSoil = -30;
	[Export] public int GoodHeart = 60;

	/// <summary>The rats want a granary so full it is spilling: a hoard past this many sacks is what
	/// brings them, and they take this share of it.</summary>
	[Export] public int RatsGranary = 900;
	[Export] public float RatsGrainLoss = 0.25f;

	/// <summary>Murrain wants a herd with nowhere to stand — past what the province's pastures
	/// carry, not past a flat number, so the answer is fewer beasts or more pasture.</summary>
	[Export] public float MurrainHerdLoss = 0.3f;

	/// <summary>Brigands come for a county with no soldiers in it, or one whose own people have been
	/// driven into the woods.</summary>
	[Export] public float BanditGoldLoss = 0.2f;
	[Export] public float BanditGrainLoss = 0.15f;

	/// <summary>The one piece of good news, and it has to be earned: a harvest off land in heart.</summary>
	[Export] public float BumperCropBonus = 0.25f;
	[Export] public float BumperCropLoyalty = 4f;

	/// <summary>Loyalty below this is a county worth warning a lord about. Below zero it is a county
	/// in revolt, which the clamp in the simulation already draws the line at.</summary>
	[Export] public float UnrestBelow = 35f;

	/// <summary>How many seasons one kind of news stays quiet after it has been told. Without it a
	/// hard winter is the same famine warning four turns running, and the player stops listening —
	/// which costs more than the warning was worth.</summary>
	[Export] public int EventQuietTurns = 4;

	// --- what an army costs to keep ----------------------------------------------------------------

	/// <summary>What a soldier eats, against what one of the people eats. He is not on the ration the
	/// lord sets for his peasants — an army is fed or it is not an army — so cutting the county to
	/// half rations does not feed the garrison any cheaper. That is the point of it: men under arms
	/// are a standing cost on the granary and not a way to have fewer mouths.</summary>
	[Export] public float SoldierAppetite = 1.6f;

	/// <summary>How much ground an army covers in a season, counted in map pixels of road. About a
	/// county and a half of good road, or half that across country — enough to answer trouble next
	/// door and not enough to be everywhere, which is what makes holding ground a decision rather
	/// than a formality.</summary>
	[Export] public float MarchReach = 520f;

	/// <summary>What a pace of ground costs an army: one along a road, more than twice that over open
	/// country. An army is not held to the roads — it is only slowed by leaving them, which is the
	/// whole reason a road is worth the stone it is laid with.</summary>
	[Export] public float MarchCostByRoad = 1f;
	[Export] public float MarchCostOffRoad = 2.2f;

	/// <summary>Gold a soldier is owed each season — a third of a crown a year, as Lords of the Realm
	/// pays him — and how many of the unpaid walk away. Wages come out of what the reeve brought in,
	/// so an army bigger than the county can carry empties the treasury first and then thins itself.
	/// At seven times this, a lord who had not yet raised his tax off nothing was broke by his
	/// second company.</summary>
	[Export] public float WagePerSoldier = 1f / 12f;
	[Export] public float DesertionRate = 0.35f;

	/// <summary>How many men under arms a county takes for granted, as a share of its own people: a
	/// watch on the gate is not an occupation. Past that share they are men who eat at the village's
	/// hearths and answer to the lord rather than to it, and it says so — this much goodwill a
	/// season for every tenth of the county standing under arms above the allowance.
	///
	/// The allowance is not softness. Without it the game would contradict itself: a county with no
	/// soldiers in it is the one the brigands come for, so a lord would be punished for the garrison
	/// he keeps and robbed for the one he does not. The cost has to begin where the watch ends.</summary>
	/// <summary>How many of a county's own people stand up for it while nobody holds it, as a share
	/// of them. An unclaimed county is nobody's army and nobody's wage bill — it is the place itself,
	/// defending itself with whatever hangs in the barn, which is what the old game this one is
	/// copied from put in front of every lord who went looking for easy land.
	///
	/// Mostly farmhands with whatever hangs in the barn — but not only: a town keeps its own watch
	/// (MilitiaArmed), bowmen on the gate and spears in the street, and a lord who marched on one
	/// expecting pitchforks met arrows first.
	///
	/// By difficulty, and it binds the rival lords as much as the player: the harder the game, the
	/// more of a county turns out for it, and the dearer every piece of empty country is to anybody.</summary>
	[Export] public float[] MilitiaShare = { 0.14f, 0.17f, 0.21f };

	/// <summary>The share of that militia that is the town's trained watch, half archers and half
	/// spearmen, by difficulty; the rest are peasants.</summary>
	[Export] public float[] MilitiaArmed = { 0.15f, 0.25f, 0.3f };

	[Export] public float GarrisonTolerated = 0.05f;
	[Export] public float GarrisonLoyaltyPerTenth = 4f;

	/// <summary>What an intake of men costs in goodwill, per hundred taken, and how much of that the
	/// county forgets each season. Sons taken for war are a real grievance in this game's ancestor
	/// and the advisor has a line for it, so it has to be a real number here or the line would be
	/// an accusation with nothing behind it.</summary>
	[Export] public float ConscriptionLoyaltyPerHundred = 7f;
	[Export] public float ConscriptionForgetRate = 0.34f;

	// --- what a battle does ------------------------------------------------------------------------

	/// <summary>How much of a side one round of fighting takes off, at most — the share that falls
	/// when the two sides are exactly matched is half this. A battle is a handful of rounds and not
	/// a grind: the men who break, break early, and a field that took twenty exchanges to decide
	/// would be a field nobody could have read beforehand.</summary>
	[Export] public float BattleBite = 0.12f;

	/// <summary>How much of what it brought a side loses before it breaks. Armies do not fight to
	/// the last man and never have — they come apart, and the ones who can run, run. It is also what
	/// makes a won battle affordable: the winner buries a fraction of what the loser does.</summary>
	[Export] public float BattleBreakPoint = 0.35f;

	/// <summary>How long the day is. An attack that has not carried by the end of it has not carried
	/// — the ground stays with whoever was standing on it, which is what withdrawing from a wall
	/// looks like from the outside.</summary>
	[Export] public int BattleMostRounds = 12;

	/// <summary>How far THE DAY swings either side of what the numbers say — rolled once for each
	/// army when it forms up, and not again.
	///
	/// Once and not per round on purpose. A die thrown every exchange averages itself out over the
	/// handful of exchanges a battle lasts, and what comes out the far end is arithmetic with a
	/// rattle on it: the stronger side wins every single time and the noise shows up in nothing but
	/// the casualty list. Rolled once, it is the ground, the weather and whether the captain has
	/// slept — a real uncertainty a lord has to leave room for, and the reason to bring more men
	/// than he strictly needs.</summary>
	[Export] public float BattleLuck = 0.15f;

	/// <summary>How much of a starving garrison is lost each season once the larder is out, and how
	/// many of those seasons they hold before the gate opens. Men do not sit behind a wall until the
	/// last of them is dead: they hold out for a while on nothing and then somebody draws the bolt,
	/// which is how nearly every castle in the period actually fell.</summary>
	[Export] public float StarvedGarrisonRate = 0.2f;
	[Export] public int SurrenderAfterHungrySeasons = 3;

	/// <summary>What a county thinks of the lord who has just taken it. Nobody is glad to be
	/// conquered, and a county held down is a county that has to be fed, garrisoned and watched
	/// before it is worth anything — which is what stops a lord from simply taking everything he can
	/// reach and is the whole reason holding ground is a decision.</summary>
	[Export] public float ConquestResentment = 20f;

	/// <summary>What the town itself is worth to whoever is holding it: lanes they know, a wall of a
	/// house at their back, and nobody having to be told where anything is.</summary>
	[Export] public float TownDefence = 1.15f;

	/// <summary>How much a county's goodwill is worth to the men defending it, either way. A people
	/// who think well of their lord hold longer for him; a people who do not, do not — which is the
	/// one place in this game where the happiness bar and the sword meet.</summary>
	[Export] public float LoyaltyDefence = 0.25f;

	/// <summary>What a man coming up a wall is worth defending himself. He has a ladder in one hand
	/// and nowhere to give ground, and everything above him is dropping things on his head.
	///
	/// The third of the three things a wall does, and the one without which the other two are not
	/// enough: a multiplier only makes the garrison harder to kill, and a small force attacking a
	/// castle is not crowded by its frontage either — so without this a keep would be worth nothing
	/// at all against exactly the army it exists to stop.</summary>
	[Export] public float AssaultExposure = 0.6f;

	/// <summary>What an archer standing in the open is worth shooting at men behind stone. Half a
	/// bow: he is loosing at heads and helmets over a parapet while they are loosing at all of him.</summary>
	[Export] public float AssaultVolley = 0.5f;

	/// <summary>What men who have spent the whole season walking are worth on the day they arrive.
	/// This is the reason to set out early rather than to arrive at all costs — a lord who has run
	/// his army to the edge of its legs is attacking with a tired one.</summary>
	[Export] public float MarchedOutOffence = 0.9f;

	// --- the lords who are not the player -----------------------------------------------------------

	// Difficulty, as four kinds of competence. [Easy, Medium, Hard] throughout, and not one of them
	// is a bonus: every number here describes a lord playing the same economy as the player, better
	// or worse. A rival who cheated would be unreadable — the player could no longer judge what is
	// possible on this map by what is possible in his own county.

	/// <summary>Hands the lord never gets to the right work. Not people he does not have — people
	/// standing in the wrong field, which is the honest way for a poor lord to be poor.</summary>
	[Export] public float[] LordIdleHands = { 0.15f, 0.06f, 0f };

	/// <summary>Seasons of bread he keeps in the barn before he will sell any, and buys back up to
	/// when he is under it. How far ahead a lord counts is most of what makes him hard to starve.</summary>
	[Export] public float[] LordGrainSeasons = { 1f, 2f, 3f };

	/// <summary>How tired the county's soil (−100..100) has to be before a lord rests a field, and
	/// how rich before he sows one back. Without them he only ever ploughed fallow up and never let
	/// it rest, and a county that fed itself at the start was starving in forty years.</summary>
	[Export] public int LordRestsBelow = 0;

	/// <summary>The goodwill under which a lord raises nobody: the sons are the last thing a
	/// resentful county gives. The same for every lord, and just above where people start to leave
	/// (EmigrationBelow) — any higher and an easy lord, who runs his counties close to that line,
	/// went decades without raising a man.</summary>
	[Export] public float LordLevyAbove = 36f;

	/// <summary>The share of a county a lord counts on being in the fields at harvest, which is what
	/// decides how many fields he sows: a full field wants the reapers for a hundred and twenty sacks.</summary>
	[Export] public float LordReapShare = 0.9f;

	/// <summary>Iron, stone and timber a lord keeps in the yard; everything above it he sells each
	/// season.</summary>
	[Export] public int LordKeepsWares = 150;
	[Export] public int LordSowsAbove = 40;

	/// <summary>The share of an ordinary season's selling price a sack has to fetch him before he will
	/// let it go. Prices are whole crowns and a sack's are the original's small ones — two in an
	/// ordinary season, one in the autumn glut — so 0.45 is a lord who sells whenever the barn is
	/// full, and 0.9 and 0.95 hold the grain back through the glut and sell it when it is dear again,
	/// only as much as the market takes without pushing it back down.</summary>
	[Export] public float[] LordSellsAbove = { 0.45f, 0.9f, 0.95f };

	/// <summary>The goodwill he eases the tax at. A poor lord reads his treasury and squeezes until
	/// the county is on the edge of leaving; a good one reads the county long before. Never under
	/// EmigrationBelow: a floor of 25 had the easy lord sitting his county in the band where people
	/// walk out, and it emptied under him in twenty years without a blow struck.</summary>
	[Export] public float[] LordTaxFloor = { 37f, 42f, 55f };

	// --- the lords at war (LordArms, LordsCampaign), by difficulty -------------------------------

	/// <summary>The share of his people a lord keeps in the field, the watch on his gates aside.
	/// About one in eight is what it takes to beat a neutral county's militia once the town keeps a
	/// watch of bows and spears (MilitiaArmed); a hard lord keeps half as many again.</summary>
	[Export] public float[] LordArmyShare = { 0.15f, 0.25f, 0.35f };

	/// <summary>How sure a lord wants to be before he attacks, as the share of days he would carry.
	/// A hard lord takes a real risk. The easy lord is as careful as the middling one: what makes
	/// him easy is that he starts late, never comes for the player and never sits down before a
	/// gate.</summary>
	[Export] public float[] LordAttackOdds = { 0.75f, 0.65f, 0.6f };

	/// <summary>The first turn a lord marches on anybody. He takes the empty country first
	/// (LordsCampaign.Wanted), so this is how soon the race for it begins: a hard lord is on the road
	/// in his first summer.</summary>
	[Export] public int[] LordFirstMarch = { 12, 8, 2 };

	/// <summary>Seasons a lord spends settling a county he has just taken — its wall, its watch, its
	/// people — before his host marches on the next. Without it the Northern Watch took the whole
	/// empty country in five seasons on Medium and was at the player's gate by the fourth year's
	/// spring, before the player had raised a second company: a race nobody could run.</summary>
	[Export] public int[] LordSettles = { 6, 3, 1 };

	/// <summary>Whether a lord comes for the player's counties at all (1) or only for the empty
	/// country (0), and whether he will sit down before a gate he cannot storm.</summary>
	[Export] public int[] LordWillAttackPlayer = { 0, 1, 1 };
	[Export] public int[] LordBesieges = { 0, 1, 1 };

	/// <summary>The share of his taxes a lord will spend on wages, which caps his army whatever his
	/// difficulty; how many batches of ten his smithy takes on at once; how much of a county he
	/// raises in one season, from how big a county at the least; and the smallest company he will
	/// raise, and the smallest he will march.</summary>
	[Export] public float LordWagesShare = 0.6f;
	[Export] public int LordSmithyBatches = 3;
	[Export] public float LordLevyShare = 0.06f;
	[Export] public int LordLeastPeopleToLevy = 300;
	[Export] public int LordLeastCompany = 10;
	[Export] public int LordLeastHost = 40;

	/// <summary>The chance, each season he has the stores for it, that a lord orders the next rung of
	/// his walls — so one county has its palisade in the second year and the next in the fifth — and
	/// how many men he puts on a wall once it stands.</summary>
	[Export] public float[] LordBuildChance = { 0.05f, 0.10f, 0.15f };
	[Export] public int LordWatch = 30;

	/// <summary>The share of what a wall holds that a lord keeps on it, the watch above being the
	/// least he puts there: a stone keep with thirty men on it was a keep for the taking.</summary>
	[Export] public float LordWatchShare = 0.4f;

	/// <summary>What a lord's treasury is worth to his army each season, as a share of it spent on
	/// wages on top of his taxes; the gold he keeps back whatever the war; and the share of what is
	/// above that he will put into arms bought at market or a band of hired men in one season — by
	/// difficulty. A lord who paid his men from his taxes alone sat on fifty thousand crowns with
	/// forty men in the field; one who spent half his chest a season took the player's seat in five
	/// years on middling.</summary>
	[Export] public float[] LordTreasuryShare = { 0f, 0.01f, 0.03f };
	[Export] public int LordGoldReserve = 500;
	[Export] public float[] LordWarChest = { 0.1f, 0.25f, 0.5f };

	/// <summary>How many battles a lord fights in his head before deciding one in the field.</summary>
	[Export] public int LordOddsTrials = 40;

	// [None, Half, Normal, Double, Triple] — the multiples themselves, so the food a county eats is
	// literally the ration its lord set. A county fed double eats twice the bread; there is no
	// separate fudge factor between the word on the panel and the hole in the granary.
	[Export] public float[] RationFoodMultiplier = { 0f, 0.5f, 1.0f, 2.0f, 3.0f };

	/// <summary>Births, against the ordinary ration. A well-fed county grows and a hungry one does
	/// not, which is the slow half of what the ration buys.</summary>
	[Export] public float[] RationGrowthMultiplier = { 0f, 0.5f, 1.0f, 1.5f, 2.0f };

	/// <summary>And the fast half: what the table does to their goodwill each season. Feeding a
	/// county well is the lord's answer to a tax he cannot afford to cut — it costs grain instead of
	/// crowns, which is exactly the trade the old game was built on. Worth less than the tax can take
	/// away, on purpose: bread makes a hard rate bearable, it does not make it free.</summary>
	[Export] public float[] RationLoyaltyDelta = { -15f, -6f, 0f, 3f, 5f };
}
