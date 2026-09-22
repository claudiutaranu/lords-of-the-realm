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
/// Men are raised the day they are paid for. There is no drill queue: what a lord gives up for a
/// company he gives up at once — the people off the roll, the hands off the fields and the arms
/// out of the armoury — and the company falls in the same afternoon.</summary>
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
	private HBoxContainer _row;
	private HBoxContainer _roster;
	private MercenaryBand _band;
	private Item _lit;
	private HBoxContainer _armouryRow;
	private Label _availableLabel;

	protected override string DataPath => "res://data/recruits.json";

	protected override string RoomName => "Barracks";

	protected override string Tagline => "Recruit and organize your forces.";

	// The captain is asked how many men, in fives.
	/// <summary>The captain is asked how many men — except of a hired company, which came as a
	/// company: a hundred Scots for eighteen hundred crowns, all of them or none.</summary>
	protected override bool Sized(Item item) => item != null && item.Key != _band?.Key;

	protected override string TermsLine(Item item) =>
		item.Key == _band?.Key ? "The company asks" : "Each needs";

	protected override int OrderStep => 5;

	/// <summary>How large a man is drawn on the roster. The width is a floor rather than a size —
	/// the row shares the page out between them — and it is set by the widest thing on the card,
	/// which is the order button rather than the picture: a card narrower than its own button wears
	/// it over both edges and across its neighbours. Shorter than it was, so that seven men and a
	/// company for hire all stand on the page together without anything being dragged into view.</summary>
	private const int CardWidth = 118;
	private const int CardHeight = 344;

	/// <summary>Every man is taken out of the province's own people, so the most that can be raised
	/// is the people there are to take. Arms cap an intake too, but not the slider: running out of
	/// swords is worth showing in red on a price the player can still reach for, where a slider that
	/// stops short only looks broken.</summary>
	protected override int OrderCeiling(Item item) =>
		item.Cost.TryGetValue("people", out int each) && each > 0 ? Mustered("people") / each : 0;

	/// <summary>What is left of a store after the muster already standing on the table has claimed
	/// its share. A slider that offers a lord men he has already promised to another company is a
	/// slider that lets him raise the same peasant twice.</summary>
	private int Mustered(string purse)
	{
		int left = Held(purse);
		foreach ((string key, int men) in _muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item != null && item.Cost.TryGetValue(purse, out int each))
			{
				left -= each * men;
			}
		}

		return Mathf.Max(0, left);
	}

	// The yard chooses off cards along the foot of the page, not off signs on a wall.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	/// <summary>The yard is never busy: men are raised the day they are paid for, so there is no
	/// bench to be waited on and no order to be blocked by one already in hand.</summary>
	protected override (string Making, int TurnsLeft) InHand => ("", 0);

	protected override string BusyLine => "The yard is drilling";

	/// <summary>A company is hired, not recruited: the word on the button follows what is being
	/// looked at, so it never offers to raise men who are already soldiers.</summary>
	/// <summary>"Muster" is a word for gathering an army and nothing else, which makes it a word the
	/// button can be read wrong through. To enlist a man is to put him on the roll, which is exactly
	/// what pressing this does — the company itself is made by the plate at the foot of the panel.
	///
	/// One word, because seven of these stand side by side across the page and the plate they sit on
	/// spends half its width on the gilt at either end.</summary>
	protected override string OrderLine => _lit != null && _lit.Key == _band?.Key ? "Hire" : "Enlist";

	protected override string DeliveryLine(Item item) => "at once";

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

	/// <summary>Men are the one thing this game builds that goes on costing. They eat off the same
	/// granary the county does — more than a ploughman, and not on the ration the lord sets his
	/// people — and they are owed wages every season out of the same purse the walls are paid for.
	/// Said here, where the order is placed, because it is the only place the decision is being made
	/// and the cost itself does not appear until the season after.</summary>
	protected override string UpkeepLine()
	{
		GameBalance balance = GameBalance.Engine;
		int grain = Mathf.CeilToInt(10 / balance.PeoplePerGrain * balance.SoldierAppetite);
		int gold = Mathf.CeilToInt(10 * balance.WagePerSoldier);
		int quartered = Mathf.FloorToInt((Province?.Population ?? 0) * balance.GarrisonTolerated);
		return $"Keeping them: every ten men eat {grain} grain a season and are owed {gold} gold. "
			+ "Men who cannot be paid go home. "
			+ $"The county quarters {quartered:N0} without complaint and resents every one above that.";
	}

	protected override void Pay(string purse, int amount)
	{
		switch (purse)
		{
			// Taken out of the fields, and remembered: the county resents an intake for a while
			// afterwards, which is what gives the advisor's "you took too many sons" something real
			// behind it.
			case "people":
				// Taken out of the fields as well as off the roll: a man handed a spear is not also
				// bringing the harvest in, and the county resents an intake for a while afterwards,
				// which is what gives the advisor's "you took too many sons" something behind it.
				EconomySimulation.Conscript(Province, amount);
				break;
			case "gold": Province.Gold -= amount; break;
			case "grain": Province.Grain -= amount; break;
			case "cattle": Province.Cattle -= amount; break;
			case "wood": Province.Wood -= amount; break;
			case "stone": Province.Stone -= amount; break;
			case "iron": Province.Iron -= amount; break;
			default: Province.Armoury[purse] = Province.Armoury.GetValueOrDefault(purse) - amount; break;
		}
	}

	/// <summary>The order in the yard is not "raise these men", it is "put them on the list". Nothing
	/// is paid for and nobody is raised until the muster is called — so a lord picks his forty
	/// spearmen and his twenty archers, looks at what the whole company will cost him, and only then
	/// commits to it. Raising them one card at a time gave him no moment to see the total.</summary>
	protected override void PlaceOrder()
	{
		if (_lit == null)
		{
			return;
		}

		int count = Sized(_lit) ? OrderSize : _lit.Batch;
		if (count <= 0 || !Affordable(_lit.Key, count))
		{
			return;
		}

		_muster[_lit.Key] = _muster.GetValueOrDefault(_lit.Key) + count;
		ShowDetail();
	}

	/// <summary>Whether the county could pay for the whole muster with this added to it. Asked
	/// against the total and not against one company, because the purse is one purse.</summary>
	private bool Affordable(string key, int more)
	{
		var wanted = new Dictionary<string, int>(_muster);
		wanted[key] = wanted.GetValueOrDefault(key) + more;
		foreach ((string purse, int owed) in Bill(wanted))
		{
			if (Held(purse) < owed)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>What a muster comes to, store by store.</summary>
	private Dictionary<string, int> Bill(Dictionary<string, int> muster)
	{
		var owed = new Dictionary<string, int>();
		foreach ((string key, int men) in muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item == null)
			{
				continue;
			}

			foreach ((string purse, int each) in item.Cost)
			{
				owed[purse] = owed.GetValueOrDefault(purse) + (each * (Sized(item) ? men : 1));
			}
		}

		return owed;
	}

	/// <summary>The muster on the table: who is on it, what it will cost, and the one button that
	/// turns it into an army. Empty until something is put on it, because a button that raises
	/// nobody is a button that teaches the player it does nothing.</summary>
	protected override Control Footer()
	{
		if (_muster.Count == 0)
		{
			return null;
		}

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 6);
		column.AddChild(Chrome.Rule(420));

		int men = 0;
		foreach ((string key, int count) in _muster)
		{
			Item item = Items.Find(card => card.Key == key);
			men += count;

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);
			row.AddChild(Icon(item?.Icon ?? key, 22));
			row.AddChild(Line($"{count:N0} {item?.Name ?? key}", 16, Bright));
			column.AddChild(row);
		}

		var price = new HBoxContainer();
		price.AddThemeConstantOverride("separation", 10);
		price.AddChild(Line("The army costs", 15, Soft));
		foreach ((string purse, int owed) in Bill(_muster))
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Line(owed.ToString("N0"), 15, Held(purse) >= owed ? Bright : Short));
			group.AddChild(Icon(IconFor(purse), 19));
			price.AddChild(group);
		}

		column.AddChild(price);

		var raise = new Button { Text = $"Create army  —  {men:N0} men", CustomMinimumSize = new Vector2(0, 44) };
		raise.AddThemeFontSizeOverride("font_size", 17);
		raise.Pressed += Raise;
		column.AddChild(raise);

		var scrap = new Button { Text = "Send them home", CustomMinimumSize = new Vector2(0, 32) };
		scrap.AddThemeFontSizeOverride("font_size", 14);
		scrap.Pressed += () =>
		{
			_muster.Clear();
			ShowDetail();
		};

		column.AddChild(scrap);
		return column;
	}

	/// <summary>Calls the muster: everything on the list is paid for and raised, in one go. This is
	/// the moment the army exists — before it, nothing has been spent and nobody has left the
	/// fields.</summary>
	private void Raise()
	{
		foreach ((string purse, int owed) in Bill(_muster))
		{
			if (Held(purse) < owed)
			{
				return; // the county cannot carry it after all; nothing is taken
			}
		}

		foreach ((string purse, int owed) in Bill(_muster))
		{
			Pay(purse, owed);
		}

		// One muster, one company: everything raised together falls in under one banner, and it is a
		// NEW banner. What the yard turns out does not walk into whatever army the county already
		// has standing — the lord decides whether the two become one (see the map's join).
		_raised = null;
		foreach ((string key, int men) in _muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item != null)
			{
				Begin(item, men);
			}
		}

		_raised = null;
		_muster.Clear();
		ShowDetail();
		Refresh();
	}

	private readonly Dictionary<string, int> _muster = new();

	/// <summary>The company this muster is falling in under, made by the first card that raises
	/// anybody and let go the moment the muster is over. Forty spears and twenty bows ordered
	/// together are one army, not two.</summary>
	private FieldArmy _raised;

	protected override void Begin(Item item, int count)
	{
		// Raised the day they are paid for. Hired men fall in as the company they already are: forty
		// Scots stand in the muster as forty swordsmen, because the roster is kept in units and not
		// in nationalities.
		bool hired = item.Key == _band?.Key;
		string unit = hired ? _band.Unit : item.Key;
		_raised ??= Province.Raise(GameBalance.Engine.MarchReach);
		_raised.Men[unit] = _raised.Men.GetValueOrDefault(unit) + count;
		if (hired)
		{
			Mercenaries.Hire(Province, count);
		}
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

	/// <summary>The roster is dealt before the page is told which province it is looking at, and a
	/// band of mercenaries belongs to the province rather than to the game — so the card for
	/// whoever is standing in the county this season is laid down here, once the county is known,
	/// beside the men the yard can raise itself.</summary>
	protected override void Opened()
	{
		MercenaryBand band = Mercenaries.Standing(Province);
		if (band != null && !_cards.ContainsKey(band.Key))
		{
			_band = band;

			// He is told about them here and nowhere else. The map only marks the county; the line
			// and the voice belong to the yard, where he is standing in front of the men.
			Narrator.Say(EventEngine.VoicePath(EventEngine.Find($"merc-{band.Key}")));
			var hired = new Item(band.Key, band.Name, band.Unit, band.Blurb, band.Attack, band.Range,
				band.Defence, band.Speed, Turns: 1, Batch: band.Men,
				new Dictionary<string, int> { ["gold"] = band.Gold });
			Items.Add(hired);

			// Its own counter beside the roster, under its own sign. What the yard raises and what
			// walks up to the gate asking for pay are two different transactions, and a lord should
			// not have to read the price to tell them apart.
			var column = new VBoxContainer();
			column.AddThemeConstantOverride("separation", 10);

			var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			heading.AddThemeConstantOverride("separation", 10);
			heading.AddChild(Icon("mercenaries", 28));
			Label sign = Line("FOR HIRE", 20, Cream);
			sign.VerticalAlignment = VerticalAlignment.Center;
			heading.AddChild(sign);
			column.AddChild(heading);

			var cards = new HBoxContainer();
			cards.AddThemeConstantOverride("separation", 8);
			column.AddChild(cards);
			DealCard(hired, band.Unit, cards, "Hire");

			PanelContainer frame = Framed(column, 12);
			frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			frame.SizeFlagsStretchRatio = 1.35f;
			_roster.AddChild(frame);
		}

		base.Opened();
	}

	/// <summary>The chosen card is the lit one, the way the smithy's chosen sign is.</summary>
	protected override void Chosen(Item item)
	{
		_lit = item;
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
