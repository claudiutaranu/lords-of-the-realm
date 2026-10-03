using System.Collections.Generic;

/// <summary>What a besieging army builds before it storms a castle: rams for the gate and catapults
/// for the curtain, no more than GameBalance.SiegeEnginesMost of them, each costing seasons of the
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
}
