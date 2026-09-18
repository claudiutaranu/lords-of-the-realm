using System.Collections.Generic;
using Godot;

/// <summary>The smithy: weapons, bought out of the province's own stores. One order at a time — a
/// smith with two commissions finishes neither.</summary>
public partial class BlacksmithPage : ProductionPage
{
	protected override string DataPath => "res://data/weapons.json";

	protected override string RoomName => "Blacksmith";

	// Read off a still of the forge: each sign hangs over its own rack.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new()
	{
		["bow"] = new Vector2(0.205f, 0.115f),
		["crossbow"] = new Vector2(0.255f, 0.445f),
		["sword"] = new Vector2(0.445f, 0.215f),
		["spear"] = new Vector2(0.585f, 0.135f),
		["horse"] = new Vector2(0.720f, 0.300f),
		["mace"] = new Vector2(0.845f, 0.285f),
	};

	protected override (string Key, string Icon)[] Purses { get; } =
	{
		("gold", "gold"),
		("grain", "food"),
		("cattle", "livestock"),
		("wood", "wood"),
		("stone", "stone"),
		("iron", "iron"),
	};

	// The smith labels his own wall: each sign hangs over its rack.
	protected override void BuildChoosers() => HangSigns();

	protected override (string Making, int TurnsLeft) InHand => (Province.Forging, Province.ForgeTurnsLeft);

	protected override string BusyLine => "The forge is busy";

	protected override string OrderLine => "Start production";

	protected override string DeliveryLine(Item item) =>
		$"{item.Batch} forged in {item.Turns} turn{(item.Turns == 1 ? "" : "s")}";

	protected override string IconFor(string purse) => purse switch
	{
		"grain" => "food",
		"cattle" => "livestock",
		_ => purse,
	};

	protected override int Held(string purse) => purse switch
	{
		"gold" => Province?.Gold ?? 0,
		"grain" => Province?.Grain ?? 0,
		"cattle" => Province?.Cattle ?? 0,
		"wood" => Province?.Wood ?? 0,
		"stone" => Province?.Stone ?? 0,
		_ => Province?.Iron ?? 0,
	};

	protected override void Pay(string purse, int amount)
	{
		switch (purse)
		{
			case "gold": Province.Gold -= amount; break;
			case "grain": Province.Grain -= amount; break;
			case "cattle": Province.Cattle -= amount; break;
			case "wood": Province.Wood -= amount; break;
			case "stone": Province.Stone -= amount; break;
			default: Province.Iron -= amount; break;
		}
	}

	protected override void Begin(Item item, int count)
	{
		Province.Forging = item.Key;
		Province.ForgeTurnsLeft = item.Turns;
		Province.ForgeBatch = count;
	}
}
