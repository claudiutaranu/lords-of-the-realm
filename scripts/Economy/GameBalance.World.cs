using Godot;

/// <summary>The figures for what the world throws at a province: how often it stirs, what each
/// event is worth, the weather's swing, and how long news stays quiet.</summary>
public partial class GameBalance
{
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

	/// <summary>What one waste field takes to put right, in hand-seasons, no more than ReclaimPerSeason
	/// of it in a season. The original's is eight hundred, four seasons at the least; halved at the
	/// user's word, because a flood that cost a field for a year read as a punishment rather than
	/// as weather.</summary>
	[Export] public int FieldReclaimWork = 400;

	/// <summary>What a field soldiers trod to waste takes to put right: ground churned, not washed
	/// away, so a season of a field's most (Husbandry.Reclaim) mends it. Counted as that much left of
	/// FieldReclaimWork.</summary>
	[Export] public int TrampledReclaimWork = 200;

	/// <summary>Seasons a quarry, mine or wood stays shut after an enemy army halted on it, as in the
	/// original.</summary>
	[Export] public int OccupiedSeasons = 3;

	/// <summary>The extra swing of the sky over one county a season and, half of it, its neighbours
	/// (Climate). ponytail: the original's size for it is not known.</summary>
	[Export] public int WeatherSwing = 16;

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
}
