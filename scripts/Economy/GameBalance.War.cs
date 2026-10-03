using Godot;

/// <summary>The figures for an army: what it costs to keep and how far it walks, and what a battle
/// in the open or at the walls does to it.</summary>
public partial class GameBalance
{
	// --- what an army costs to keep ----------------------------------------------------------------

	/// <summary>What a soldier eats, against what one of the people eats. He is not on the ration the
	/// lord sets for his peasants — an army is fed or it is not an army — so cutting the county to
	/// half rations does not feed the garrison any cheaper. That is the point of it: men under arms
	/// are a standing cost on the granary and not a way to have fewer mouths.</summary>
	[Export] public float SoldierAppetite = 1.6f;

	/// <summary>How much ground an army covers in a season, counted in map pixels of road. About a
	/// county of good road, or half that across country — enough to answer trouble next door and
	/// not enough to be everywhere, which is what makes holding ground a decision rather than a
	/// formality. (520 until the user asked for a third less.)</summary>
	[Export] public float MarchReach = 364f;

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

	// --- what a battle does ------------------------------------------------------------------------

	/// <summary>How much of a side one round of fighting takes off, at most — the share that falls
	/// when the two sides are exactly matched is half this. A battle is a handful of rounds and not
	/// a grind: the men who break, break early, and a field that took twenty exchanges to decide
	/// would be a field nobody could have read beforehand.</summary>
	[Export] public float BattleBite = 0.12f;

	/// <summary>How long the day is, in exchanges. Battles are fought to the last man (the user's
	/// call), so this is only the end of a day nobody could finish — an assault on a wall that lets
	/// too few at it to carry it — and the ground stays with whoever was standing on it.</summary>
	[Export] public int BattleMostRounds = 150;

	/// <summary>How long a day at the walls is, in exchanges. Short: the men who could get at the
	/// wall have had their chance by nightfall, and an assault that has not cleared it has failed.
	/// This, and the frontage, is what a castle is for.</summary>
	[Export] public int AssaultMostRounds = 12;

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

	/// <summary>A battle the lord fights himself (<see cref="FieldBattle"/>), man against man. How much
	/// health a man has, what one blow that lands takes off it, how long a swing takes and how long
	/// a bow or a crossbow takes to draw again — and what an arrow is worth against a blow. Whether a
	/// blow lands is the two cards' attack against defence, so these are the same for every kind. [I]</summary>
	[Export] public float FieldManHealth = 100f;
	[Export] public float FieldHitDamage = 25f;
	[Export] public float FieldSwingSeconds = 1.6f;
	[Export] public float FieldReloadSeconds = 4f;
	[Export] public float FieldShotRate = 0.8f;

	/// <summary>How long the day lasts on the field, in seconds, and how many of them the captain's
	/// rounds are reckoned as. A field where both sides still stand at sundown is a day neither
	/// side won. [I]</summary>
	[Export] public float FieldDaySeconds = 600f;
	[Export] public float FieldRoundSeconds = 20f;

	/// <summary>How much of a starving garrison is lost each season once the larder is out, and how
	/// many of those seasons they hold before the gate opens. Men do not sit behind a wall until the
	/// last of them is dead: they hold out for a while on nothing and then somebody draws the bolt,
	/// which is how nearly every castle in the period actually fell.</summary>
	[Export] public float StarvedGarrisonRate = 0.2f;
	[Export] public int SurrenderAfterHungrySeasons = 3;

	/// <summary>The engines a besieging army can build before it storms (SiegeEngines): how many in
	/// all, how many seasons of the siege each one takes, and what each does to the walls. A ram
	/// opens the gate, so more men can come at it at once; a catapult knocks a breach in the curtain,
	/// which does the same and takes some of the stone from in front of the defenders. They widen the
	/// frontage rather than adding a multiplier, as fortifications.json has always meant them to. [I]</summary>
	[Export] public int SiegeEnginesMost = 3;
	[Export] public int RamSeasons = 1;
	[Export] public int CatapultSeasons = 2;
	[Export] public int RamFrontage = 15;
	[Export] public int CatapultFrontage = 10;
	[Export] public float CatapultDefenceCut = 0.15f;

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
}
