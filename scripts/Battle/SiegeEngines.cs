using System.Collections.Generic;

/// <summary>What a besieging army builds before it storms a castle: rams for the gate and catapults
/// for the curtain, no more than GameBalance.SiegeEnginesMost of each, each costing seasons of the
/// siege before the assault can go in. What they do to the walls is Battle's (the frontage and the
/// stone); this is only what there is and how long it takes.</summary>
public static class SiegeEngines
{
	public const string Ram = "ram";
	public const string Catapult = "catapult";

	/// <summary>How many seasons of siege these engines take to build, one after the other.</summary>
	public static int Seasons(IReadOnlyDictionary<string, int> engines, GameBalance b) =>
		(engines.GetValueOrDefault(Ram) * b.RamSeasons) + (engines.GetValueOrDefault(Catapult) * b.CatapultSeasons);

	public static int Count(IReadOnlyDictionary<string, int> engines) =>
		engines.GetValueOrDefault(Ram) + engines.GetValueOrDefault(Catapult);

	/// <summary>The engines as the field of an assault knows them: slow, hard to break, mounted in the
	/// sense that matters (they go up no ladder), and striking no man. Not raised in any yard, so they
	/// are here and not in recruits.json. [I]</summary>
	public static readonly Dictionary<string, Units.Unit> Kinds = new()
	{
		[Catapult] = new Units.Unit("Catapult", 0, 0, 7, 4, true, "stone", IsEngine: true),
		[Ram] = new Units.Unit("Battering Ram", 0, 0, 9, 4, true, "castle", IsEngine: true),
	};

	/// <summary>How far a catapult throws, in metres, and how near the wall it can still throw from;
	/// how near the gate a ram has to be to beat at it; and how many men's health an engine has. [I]</summary>
	public const float ThrowsFrom = 18f;
	public const float ThrowsTo = 75f;
	public const float RamsWithin = 9f;
	public const float EngineHealth = 12f;
}
