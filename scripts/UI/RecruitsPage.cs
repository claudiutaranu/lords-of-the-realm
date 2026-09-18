using System.Collections.Generic;
using Godot;

/// <summary>The barracks: men, raised out of the province's own people and armed out of its
/// armoury. A recruit costs both — one of the population he is taken from, and the weapon the
/// smithy forged for him — which is what ties this room to the other one, and why the armoury is
/// read across the middle of the page: it is what limits who can be raised.
///
/// Where the smithy hangs signs on its wall, the yard deals its men out on cards, the same cards
/// the sidebar musters them on. An intake is sized by the player rather than fixed.
///
/// One intake at a time: a yard drilling two companies drills neither.</summary>
public partial class RecruitsPage : ProductionPage
{
	/// <summary>The arms an intake can be equipped from, as the band reads them: the key, and what a
	/// rack of them is called.</summary>
	private static readonly (string Key, string Name)[] Armoury =
	{
		("sword", "Swords"),
		("bow", "Bows"),
		("crossbow", "Crossbows"),
		("spear", "Pikes"),
		("mace", "Maces"),
		("horse", "Horses"),
	};

	private readonly Dictionary<string, Control> _cards = new();
	private HBoxContainer _armouryRow;
	private Label _availableLabel;

	protected override string DataPath => "res://data/recruits.json";

	protected override string RoomName => "Barracks";

	protected override string Tagline => "Recruit and organize your forces.";

	// The captain is asked how many men, in fives.
	protected override bool SizedOrder => true;

	protected override int OrderStep => 5;

	// The yard chooses off cards along the foot of the page, not off signs on a wall.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override (string Making, int TurnsLeft) InHand => (Province.Training, Province.TrainTurnsLeft);

	protected override string BusyLine => "The yard is drilling";

	protected override string OrderLine => "Recruit";

	protected override string DeliveryLine(Item item) =>
		$"in {item.Turns} turn{(item.Turns == 1 ? "" : "s")}";

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

	protected override void Begin(Item item, int count)
	{
		Province.Training = item.Key;
		Province.TrainTurnsLeft = item.Turns;
		Province.TrainBatch = count;
	}

	/// <summary>The two things that limit an intake, read across the middle of the page: the people
	/// there are to take, and the arms there are to hand them.</summary>
	protected override Control BuildBand()
	{
		var band = new HBoxContainer();
		band.AddThemeConstantOverride("separation", 14);

		var people = new VBoxContainer();
		people.AddThemeConstantOverride("separation", 2);
		Label heading = Line("POPULATION", 13, Dim);
		people.AddChild(heading);

		var line = new HBoxContainer();
		line.AddThemeConstantOverride("separation", 8);
		line.AddChild(Icon("population", 26));
		_availableLabel = Line("", 19, Cream);
		_availableLabel.VerticalAlignment = VerticalAlignment.Center;
		line.AddChild(_availableLabel);
		people.AddChild(line);
		people.AddChild(Line("Population limits how many men you can raise.", 12, Dim));
		band.AddChild(Framed(people, 10));

		var arms = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		arms.AddThemeConstantOverride("separation", 4);
		Label armsHeading = Line("AVAILABLE WEAPONS", 13, Dim);
		armsHeading.HorizontalAlignment = HorizontalAlignment.Center;
		arms.AddChild(armsHeading);

		_armouryRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_armouryRow.AddThemeConstantOverride("separation", 26);
		arms.AddChild(_armouryRow);
		band.AddChild(Framed(arms, 10));
		return band;
	}

	/// <summary>A card for each kind of man along the foot of the page: who he is, what one of him
	/// needs, and the order itself. Picking one also sets the panel beside them.</summary>
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
				item.Key, item.Name, 360, titled: true, pressed: () => Choose(chosen), pad: 12);
			row.AddChild(tile);
			_cards[item.Key] = tile;

			// What ONE of him needs, which is how the panel prices the whole intake.
			var price = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			price.AddThemeConstantOverride("separation", 14);
			stack.AddChild(price);

			foreach ((string key, int amount) in item.Cost)
			{
				var group = new HBoxContainer();
				group.AddThemeConstantOverride("separation", 4);
				group.AddChild(Icon(IconFor(key), 22));
				group.AddChild(Line(amount.ToString(), 16, Cream));
				price.AddChild(group);
			}

			var recruit = new Button { Text = "Recruit", CustomMinimumSize = new Vector2(0, 40) };
			recruit.AddThemeFontSizeOverride("font_size", 17);
			recruit.Pressed += () =>
			{
				Choose(chosen);
				PlaceOrder();
			};
			stack.AddChild(recruit);
		}
	}

	/// <summary>The chosen card is the lit one, the way the smithy's chosen sign is.</summary>
	protected override void Chosen(Item item)
	{
		foreach ((string key, Control card) in _cards)
		{
			bool lit = item != null && key == item.Key;
			foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
			{
				var style = (StyleBoxFlat)card.GetThemeStylebox(state).Duplicate();
				style.BorderColor = lit
					? new Color(1f, 0.84f, 0.45f, 1f)
					: new Color(0.549f, 0.447f, 0.271f, 0.8f);
				style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom =
					lit ? 3 : 2;
				card.AddThemeStyleboxOverride(state, style);
			}
		}
	}

	/// <summary>The band's two readings, over what the page already refreshes.</summary>
	public override void Refresh()
	{
		base.Refresh();
		if (_armouryRow == null)
		{
			return;
		}

		_availableLabel.Text = $"{Held("people"):N0} to be raised";

		foreach (Node cell in _armouryRow.GetChildren())
		{
			cell.QueueFree();
		}

		foreach ((string key, string name) in Armoury)
		{
			int held = Held(key);
			var stock = new VBoxContainer();
			stock.AddThemeConstantOverride("separation", 0);

			var line = new HBoxContainer();
			line.AddThemeConstantOverride("separation", 6);
			line.Alignment = BoxContainer.AlignmentMode.Center;
			line.AddChild(Icon(key, 24));
			line.AddChild(Line(held.ToString("N0"), 16, held > 0 ? Cream : Dim));
			stock.AddChild(line);

			Label label = Line(name, 12, Dim);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			stock.AddChild(label);
			_armouryRow.AddChild(stock);
		}
	}
}
