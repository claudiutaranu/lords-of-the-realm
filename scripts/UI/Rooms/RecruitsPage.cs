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

	protected override int DetailWidth => 360;

	/// <summary>The most that can be raised: as many as every purse the card draws on can pay for —
	/// the people there are to take and the weapons in the armoury alike, with whatever the muster on
	/// the table has already claimed — so the slider stops at what the yard can actually turn out.</summary>
	protected override int OrderCeiling(Item item)
	{
		int most = int.MaxValue;
		foreach ((string purse, int each) in item.Cost)
		{
			if (each > 0)
			{
				most = Mathf.Min(most, Mustered(purse) / each);
			}
		}

		return most == int.MaxValue ? 0 : most;
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
				new Dictionary<string, int> { ["gold"] = band.Gold },
				Items.Find(item => item.Key == band.Unit)?.Strengths ?? "");
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
