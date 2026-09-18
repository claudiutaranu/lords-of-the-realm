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

	/// <summary>Every man is taken out of the province's own people, so the most that can be raised
	/// is the people there are to take. Arms cap an intake too, but not the slider: running out of
	/// swords is worth showing in red on a price the player can still reach for, where a slider that
	/// stops short only looks broken.</summary>
	protected override int OrderCeiling(Item item) =>
		item.Cost.TryGetValue("people", out int each) && each > 0 ? Held("people") / each : 0;

	// The yard chooses off cards along the foot of the page, not off signs on a wall.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override (string Making, int TurnsLeft) InHand => (Province.Training, Province.TrainTurnsLeft);

	protected override string BusyLine => "The yard is drilling";

	protected override string OrderLine => "Recruit";

	protected override string DeliveryLine(Item item) =>
		$"in {item.Turns} turn{(item.Turns == 1 ? "" : "s")}";

	/// <summary>Stores are read by their own icon; a weapon is read by the icon the thing itself is
	/// read by, wherever it is counted — cavalry by its helm on the card, in the price and in the
	/// armoury alike. Asking the data file rather than repeating the mapping here is what keeps the
	/// three of them from drifting apart.</summary>
	protected override string IconFor(string purse) => purse switch
	{
		"people" => "population",
		"grain" => "food",
		"cattle" => "livestock",
		_ => Items.Find(item => item.Key == purse)?.Icon ?? purse,
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

	/// <summary>The two things that limit an intake, read directly over the roster: the people there
	/// are to take, and the arms there are to hand them. It sits on the cards rather than up under
	/// the title because it is what the cards are read against.</summary>
	private Control BuildBand()
	{
		var band = new HBoxContainer();
		band.AddThemeConstantOverride("separation", 14);

		var people = new VBoxContainer();
		people.AddThemeConstantOverride("separation", 3);
		people.AddChild(Line("POPULATION", 15, Soft));

		var line = new HBoxContainer();
		line.AddThemeConstantOverride("separation", 10);
		line.AddChild(Icon("population", 30));
		_availableLabel = Line("", 23, Bright);
		_availableLabel.VerticalAlignment = VerticalAlignment.Center;
		line.AddChild(_availableLabel);
		people.AddChild(line);
		people.AddChild(Line("Population limits how many men you can raise.", 14, Soft));
		band.AddChild(Framed(people, 16));

		var arms = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		arms.AddThemeConstantOverride("separation", 6);
		Label armsHeading = Line("AVAILABLE WEAPONS", 15, Soft);
		armsHeading.HorizontalAlignment = HorizontalAlignment.Center;
		arms.AddChild(armsHeading);

		_armouryRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_armouryRow.AddThemeConstantOverride("separation", 38);
		arms.AddChild(_armouryRow);

		// The armoury takes whatever the people's box leaves, so the band runs the full width of the
		// roster it is read against rather than stopping short of it.
		PanelContainer frame = Framed(arms, 16);
		frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		band.AddChild(frame);
		return band;
	}

	/// <summary>A card for each kind of man along the foot of the page: who he is, what one of him
	/// needs, and the order itself. Picking one also sets the panel beside them.</summary>
	protected override void BuildChoosers()
	{
		var column = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkEnd,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		column.AddThemeConstantOverride("separation", 10);
		Body.AddChild(column);
		Body.MoveChild(column, 0);
		column.AddChild(BuildBand());

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		column.AddChild(row);

		foreach (Item item in Items)
		{
			Item chosen = item;
			(Control tile, VBoxContainer stack) = UnitCard.Build(
				item.Key, item.Name, 360, titled: true, pressed: () => Choose(chosen), pad: 12);
			row.AddChild(tile);
			_cards[item.Key] = tile;

			// What ONE of him needs, which is how the panel prices the whole intake.
			var price = new HBoxContainer
			{
				Alignment = BoxContainer.AlignmentMode.Center,
				MouseFilter = MouseFilterEnum.Ignore,
			};
			price.AddThemeConstantOverride("separation", 18);
			stack.AddChild(price);

			foreach ((string key, int amount) in item.Cost)
			{
				var group = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
				group.AddThemeConstantOverride("separation", 6);
				group.AddChild(Icon(IconFor(key), 30));
				group.AddChild(Line(amount.ToString(), 21, Cream));
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
			line.AddThemeConstantOverride("separation", 8);
			line.Alignment = BoxContainer.AlignmentMode.Center;
			line.AddChild(Icon(IconFor(key), 30));
			line.AddChild(Line(held.ToString("N0"), 20, held > 0 ? Bright : Soft));
			stock.AddChild(line);

			Label label = Line(name, 14, Soft);
			label.HorizontalAlignment = HorizontalAlignment.Center;
			stock.AddChild(label);
			_armouryRow.AddChild(stock);
		}
	}
}
