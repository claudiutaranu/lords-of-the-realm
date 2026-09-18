using System.Collections.Generic;
using Godot;

/// <summary>The training yard: men, raised out of the province's own people and armed out of its
/// armoury. A recruit costs both — the population he is taken from, and the weapon the smithy
/// forged for him — which is what ties this room to the other one.
///
/// Where the smithy hangs signs on its wall, the yard lays its men out on cards, the same cards the
/// sidebar musters them on. Above them a band says what there is to arm them with.
///
/// One intake at a time: a yard drilling two companies drills neither.</summary>
public partial class RecruitsPage : ProductionPage
{
	/// <summary>The arms an intake can be equipped from, in the order the band reads them.</summary>
	private static readonly string[] Armouries = { "sword", "bow", "crossbow", "spear", "mace", "horse" };

	private readonly Dictionary<string, Control> _cards = new();
	private HBoxContainer _armouryRow;

	protected override string DataPath => "res://data/recruits.json";

	protected override string RoomName => "Recruits";

	// The yard chooses off cards along the foot of the page, not off signs on a wall.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	// The same stockpiles the smithy prices in, so the two rooms read alike along the top.
	protected override (string Key, string Icon)[] Purses { get; } =
	{
		("gold", "gold"),
		("grain", "food"),
		("wood", "wood"),
		("stone", "stone"),
		("iron", "iron"),
		("people", "population"),
	};

	protected override (string Making, int TurnsLeft) InHand => (Province.Training, Province.TrainTurnsLeft);

	protected override string BusyLine => "The yard is drilling";

	protected override string OrderLine => "Begin training";

	protected override string DeliveryLine(Item item) =>
		$"{item.Batch} mustered in {item.Turns} turn{(item.Turns == 1 ? "" : "s")}";

	protected override string IconFor(string purse) => purse switch
	{
		"people" => "population",
		"grain" => "food",
		"cattle" => "livestock",
		_ => purse,
	};

	/// <summary>People and stores are the province's own; weapons are counted out of the armoury the
	/// smithy fills.</summary>
	protected override int Held(string purse) => purse switch
	{
		"people" => Province?.Population ?? 0,
		"gold" => Province?.Gold ?? 0,
		"grain" => Province?.Grain ?? 0,
		"cattle" => Province?.Cattle ?? 0,
		"wood" => Province?.Wood ?? 0,
		"stone" => Province?.Stone ?? 0,
		"iron" => Province?.Iron ?? 0,
		_ => Province?.Armoury.GetValueOrDefault(purse) ?? 0,
	};

	protected override void Pay(string purse, int amount)
	{
		switch (purse)
		{
			case "people": Province.Population -= amount; break;
			case "gold": Province.Gold -= amount; break;
			case "grain": Province.Grain -= amount; break;
			case "cattle": Province.Cattle -= amount; break;
			case "wood": Province.Wood -= amount; break;
			case "stone": Province.Stone -= amount; break;
			case "iron": Province.Iron -= amount; break;
			default: Province.Armoury[purse] = Province.Armoury.GetValueOrDefault(purse) - amount; break;
		}
	}

	protected override void Begin(Item item)
	{
		Province.Training = item.Key;
		Province.TrainTurnsLeft = item.Turns;
		Province.TrainBatch = item.Batch;
	}

	/// <summary>What the smithy has left in the armoury, which is what limits who can be raised.</summary>
	protected override Control BuildBand()
	{
		var band = new HBoxContainer();
		band.AddThemeConstantOverride("separation", 26);
		band.Alignment = BoxContainer.AlignmentMode.Center;

		Label heading = Line("ARMOURY", 14, Dim);
		heading.VerticalAlignment = VerticalAlignment.Center;
		band.AddChild(heading);

		_armouryRow = new HBoxContainer();
		_armouryRow.AddThemeConstantOverride("separation", 22);
		band.AddChild(_armouryRow);
		return Framed(band, 8);
	}

	/// <summary>A card for each kind of man, along the foot of the page. Picking one is picking what
	/// the yard drills next; the panel beside them takes the order.</summary>
	protected override void BuildChoosers()
	{
		var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 8);
		Body.AddChild(row);
		Body.MoveChild(row, 0);

		foreach (Item item in Items)
		{
			Item chosen = item;
			(Control tile, VBoxContainer stack) = UnitCard.Build(
				item.Key, item.Name, 200, titled: true, pressed: () => Choose(chosen));
			tile.CustomMinimumSize = new Vector2(124, 200);
			row.AddChild(tile);
			_cards[item.Key] = tile;

			// What one intake of him costs, in the same order the panel reads it.
			var price = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			price.AddThemeConstantOverride("separation", 10);
			stack.AddChild(price);

			foreach ((string key, int amount) in item.Cost)
			{
				var group = new HBoxContainer();
				group.AddThemeConstantOverride("separation", 4);
				group.AddChild(Icon(IconFor(key), 18));
				group.AddChild(Line(amount.ToString(), 13, Cream));
				price.AddChild(group);
			}
		}
	}

	/// <summary>The armoury band and the card the yard is set to, over what the page already
	/// refreshes.</summary>
	public override void Refresh()
	{
		base.Refresh();
		if (_armouryRow == null)
		{
			return;
		}

		foreach (Node cell in _armouryRow.GetChildren())
		{
			cell.QueueFree();
		}

		foreach (string weapon in Armouries)
		{
			int held = Province?.Armoury.GetValueOrDefault(weapon) ?? 0;
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Icon(weapon, 22));
			group.AddChild(Line(held.ToString("N0"), 15, held > 0 ? Cream : Dim));
			_armouryRow.AddChild(group);
		}
	}
}
