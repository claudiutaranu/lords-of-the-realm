using Godot;

/// <summary>The barracks' counter: one card a kind of man, dealt in a row, with the band for hire
/// standing beside them.</summary>
public partial class RecruitsPage
{
	/// <summary>How large a man is drawn on the roster. The width is a floor rather than a size —
	/// the row shares the page out between them — and it is set by the widest thing on the card,
	/// which is the order button rather than the picture: a card narrower than its own button wears
	/// it over both edges and across its neighbours. Shorter than it was, so that seven men and a
	/// company for hire all stand on the page together without anything being dragged into view.</summary>
	private const int CardWidth = 118;
	private const int CardHeight = 344;

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

		// The yard's own men in one run, and anything the yard did not raise kept out of it: a company
		// that costs gold and no sons should not read as one more rack on the same wall.
		_roster = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_roster.AddThemeConstantOverride("separation", 16);
		column.AddChild(_roster);

		// Every man the yard can raise, on the page at once. The roster is a thing to compare across,
		// and a row you have to drag sideways to see the cavalry is a row that hides the cavalry.
		_row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_row.SizeFlagsStretchRatio = Items.Count;
		_row.AddThemeConstantOverride("separation", 6);
		_roster.AddChild(_row);

		foreach (Item item in Items)
		{
			DealCard(item, item.Key, _row, OrderLine);
		}
	}

	/// <summary>Lays one man out on the roster. <paramref name="portrait"/> is whose picture stands
	/// on the card, which is the unit's rather than the item's wherever the two differ: a band of
	/// hired Scots is drawn as the swordsmen they are, because nobody has painted a Scot.</summary>
	private void DealCard(Item item, string portrait, Container into, string button)
	{
		Item chosen = item;
		(Control tile, VBoxContainer stack) = UnitCard.Build(
			portrait, item.Name, CardHeight, titled: true, pressed: () => Choose(chosen), pad: 18);

		// The row shares its width out; the card only insists on enough of it to hold its own button.
		tile.CustomMinimumSize = new Vector2(CardWidth, CardHeight);
		into.AddChild(tile);
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

		// Told to clip rather than to grow: if a longer word is ever put on it, the card stays the
		// size it is and the button gives, instead of the row shoving every man sideways.
		// Sized to its own word. "Add to the muster" wants the whole card under it; "Hire" does not,
		// and a short word on a card-wide plate reads as a mistake.
		var recruit = new Button
		{
			Text = button,
			CustomMinimumSize = new Vector2(button.Length > 8 ? 0 : 132, 40),
			SizeFlagsHorizontal = button.Length > 8 ? SizeFlags.Fill : SizeFlags.ShrinkCenter,
			ClipText = true,
		};
		recruit.AddThemeFontSizeOverride("font_size", 14);
		recruit.Pressed += () =>
		{
			Choose(chosen);
			PlaceOrder();
		};
		stack.AddChild(recruit);
	}
}
