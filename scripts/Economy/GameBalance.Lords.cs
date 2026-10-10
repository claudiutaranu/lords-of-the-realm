using Godot;

/// <summary>The figures for the lords who are not the player, by difficulty where the difficulty is
/// theirs: how they keep their counties, how they make war, and the letters they write.</summary>
public partial class GameBalance
{
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
	[Export] public float[] LordArmyShare = { 0.18f, 0.35f, 0.45f };

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
	/// country (0).</summary>
	[Export] public int[] LordWillAttackPlayer = { 0, 1, 1 };

	/// <summary>The original's raid (its AI's step 10): about fifty peasants a lord sends over his
	/// nearest enemy's land to tread his fields, for LordRaidSeasons, one raid out at a time.
	/// ponytail: how long a raid stays out is not known from the original.</summary>
	[Export] public int LordRaidMen = 50;
	[Export] public int LordRaidSeasons = 4;

	/// <summary>Whether a lord will sit down before manned walls at all; one who will not takes a castle
	/// only once nobody is left on it, since nobody storms walls off the march.</summary>
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
	[Export] public float[] LordTreasuryShare = { 0.01f, 0.03f, 0.05f };
	[Export] public int LordGoldReserve = 500;
	[Export] public float[] LordWarChest = { 0.1f, 0.25f, 0.5f };

	/// <summary>How many battles a lord fights in his head before deciding one in the field.</summary>
	[Export] public int LordOddsTrials = 40;

	// --- the lords' letters (Diplomacy) ---------------------------------------------------------

	/// <summary>How far two lords can think well or ill of each other, either way — the original's
	/// −30..+30. Between two of the other lords it mends by DiplomacyMends a season on its own;
	/// towards the player it never does.</summary>
	[Export] public int DiplomacyStandingMost = 30;
	[Export] public int DiplomacyMends = 1;

	/// <summary>What a gift smaller than the biggest one ever sent to that lord costs: the original's
	/// −8. What a big enough one earns is not known from it — a point a hundred crowns, no more than
	/// ten points a letter, is ours [I].</summary>
	[Export] public int GiftSlight = -8;
	[Export] public int GoldPerGoodwill = 100;
	[Export] public int GiftMostGoodwill = 10;

	/// <summary>The first, second and third compliment to a lord, and every one after that: the
	/// original's +15, +8, then −4 a letter once a lord has had three. What the third earns is not
	/// clear from it; nothing is ours [I].</summary>
	[Export] public int[] ComplimentGoodwill = { 15, 8, 0 };
	[Export] public int ComplimentWeary = -4;

	/// <summary>What an insult, and an alliance broken by the player, cost [I].</summary>
	[Export] public int InsultGoodwill = -10;
	[Export] public int BreakGoodwill = -10;

	/// <summary>The standing a lord wants before he takes an offered alliance, before he offers one of
	/// his own, and before he marches at an ally's asking. None of the three is known from the
	/// original [I].</summary>
	[Export] public int AllianceAt = 10;
	[Export] public int LordOffersAt = 0;
	[Export] public int ErrandAt = 0;

	/// <summary>Warnings a lord writes, a season apiece, while he thinks as ill of the player as he
	/// can before he declares a war that is never made up; and the letters an inbox holds.</summary>
	[Export] public int WarWarnings = 2;
	[Export] public int InboxSize = 5;
}
