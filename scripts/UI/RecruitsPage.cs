using System.Collections.Generic;
using Godot;

/// <summary>The training yard: men, raised out of the province's own people and armed out of its
/// armoury. A recruit costs both — the population he is taken from, and the weapon the smithy
/// forged for him — which is what ties this room to the other one.
///
/// One intake at a time: a yard drilling two companies drills neither.</summary>
public partial class RecruitsPage : ProductionPage
{
	protected override string DataPath => "res://data/recruits.json";

	protected override string RoomName => "Recruits";

	// Read off a still of the yard: each sign hangs over the men already drilling at that.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new()
	{
		["bow"] = new Vector2(0.130f, 0.380f),
		["crossbow"] = new Vector2(0.130f, 0.620f),
		["spear"] = new Vector2(0.335f, 0.400f),
		["peasant"] = new Vector2(0.500f, 0.330f),
		["mace"] = new Vector2(0.640f, 0.560f),
		["sword"] = new Vector2(0.700f, 0.400f),
		["horse"] = new Vector2(0.880f, 0.360f),
	};

	// People first, then the arms they are handed. The armoury is what the smithy has finished.
	protected override (string Key, string Icon)[] Purses { get; } =
	{
		("people", "population"),
		("spear", "spear"),
		("bow", "bow"),
		("crossbow", "crossbow"),
		("sword", "sword"),
		("mace", "mace"),
		("horse", "horse"),
	};

	protected override (string Making, int TurnsLeft) InHand => (Province.Training, Province.TrainTurnsLeft);

	protected override string BusyLine => "The yard is drilling";

	protected override string OrderLine => "Begin training";

	protected override string DeliveryLine(Item item) =>
		$"{item.Batch} mustered in {item.Turns} turn{(item.Turns == 1 ? "" : "s")}";

	protected override string IconFor(string purse) => purse == "people" ? "population" : purse;

	/// <summary>People are the province's own; everything else is counted out of the armoury.</summary>
	protected override int Held(string purse) =>
		purse == "people" ? Province?.Population ?? 0 : Province?.Armoury.GetValueOrDefault(purse) ?? 0;

	protected override void Pay(string purse, int amount)
	{
		if (purse == "people")
		{
			Province.Population -= amount;
			return;
		}

		Province.Armoury[purse] = Province.Armoury.GetValueOrDefault(purse) - amount;
	}

	protected override void Begin(Item item)
	{
		Province.Training = item.Key;
		Province.TrainTurnsLeft = item.Turns;
		Province.TrainBatch = item.Batch;
	}
}
