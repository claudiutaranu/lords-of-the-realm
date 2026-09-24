using System.Collections.Generic;
using Godot;

/// <summary>The province block of the campaign sidebar: whose province it is and how it stands,
/// what it holds and what next turn will bring in, then what it has built and what it musters.
///
/// Built in code rather than in the scene because nearly all of it is one cell repeated — five
/// resources and seven kinds of soldier — and a loop stays even where hand-placed panels drift
/// apart.
///
/// The yields are the same projection the worker panel previews, so moving a worker changes both
/// at once; the page refreshes this when that panel is touched. The muster cards read "—" until
/// the smithy has finished something, rather than showing a number nothing produced.</summary>
public partial class ProvinceSidebar : VBoxContainer
{
	private const string IconDirectory = "res://assets/ui/icons";
	// The crest art carries wide empty margins, and this is the crop the top bar's shield uses too.
	// A realm whose shield is drawn to different margins needs its own crop, not this one.
	private static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);

	private static readonly Color Cream = new("d9cdb4");
	private static readonly Color Gain = new("6fbf5f");
	private static readonly Color Lack = new("c65a45");
	private static readonly Color Waiting = new("6f6a60");

	private static readonly (ResourceType Type, string Icon)[] Resources =
	{
		(ResourceType.Grain, "food"),
		(ResourceType.Cattle, "livestock"),
		(ResourceType.Wood, "wood"),
		(ResourceType.Stone, "stone"),
		(ResourceType.Iron, "iron"),
	};

	// Keyed by the weapon each one carries, which names both its portrait in assets/units and its
	// glyph in assets/ui/icons. Those portraits belong to no campaign: every realm musters spearmen.
	//
	// A card appears once its portrait exists, so the peasant — who needs no weapon, only people
	// willing to be led — joins the grid the moment assets/units/peasant.png is dropped in.
	/// <summary>What the smithy makes and the yard arms its men out of, in the order the armoury
	/// counts them.</summary>
	private static readonly (string Weapon, string Icon)[] Arms =
	{
		("sword", "sword"),
		("bow", "bow"),
		("crossbow", "crossbow"),
		("spear", "spear"),
		("mace", "mace"),
		("horse", "helmet"),
	};

	private const string UnclaimedArtPath = "res://assets/ui/unclaimed.png";

	private Control _held;
	private Control _foreign;
	private Label _word;
	private ColorRect _accent;
	private TextureRect _crest;
	private Label _name;
	private Label _realm;
	private Label _population;
	private Label _loyalty;
	private Label _tax;
	private Label _ration;
	private Button _march;
	private readonly Dictionary<ResourceType, Label> _stock = new();
	private readonly Dictionary<ResourceType, Label> _yield = new();
	private readonly List<(Control Divider, Control Cell, Label Count, Label Yield, string Weapon)> _armoury = new();
	private Control _armouryFrame;
	private LabourBar _labour;
	private Control _labourFrame;

	/// <summary>The lord wants a word with the reeve about the tax. Raised rather than handled here:
	/// the sidebar is a readout, and the page above it owns what opens over the map.</summary>
	public event System.Action TaxPressed;

	/// <summary>And with whoever keeps the county's mood. Same arrangement: the sidebar reads, the
	/// page above it opens things.</summary>
	public event System.Action LoyaltyPressed;

	/// <summary>And with whoever feeds them.</summary>
	public event System.Action RationPressed;

	/// <summary>The lord wants his men to march. The sidebar knows they exist and that they have a
	/// move left; where they are going is the map's question, not this panel's.</summary>
	public event System.Action MarchPressed;

	private ProvinceEconomy _economy;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 8);
		BuildHeader();

		// Everything below the header is somebody's books, and you only get to read your own. The
		// two halves are built once and swapped by Refresh, rather than torn down and rebuilt every
		// time the selection crosses a border.
		_held = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_held.AddThemeConstantOverride("separation", 8);
		AddChild(_held);
		BuildStats();
		BuildResources();
		BuildLabour();
		BuildArmoury();
		BuildMarch();

		BuildForeign();
	}

	/// <summary>The one order that is given from here rather than from a room: an army marches off
	/// the map it is standing on. Hidden — not greyed — when there is nobody to march or no march
	/// left in them, because an order a county cannot give is not an order it should be offered.</summary>
	private void BuildMarch()
	{
		_march = new Button { CustomMinimumSize = new Vector2(0, 40) };
		_march.AddThemeFontSizeOverride("font_size", 15);
		_march.Pressed += () => MarchPressed?.Invoke();
		_held.AddChild(_march);
	}

	/// <summary>Who this province is. Independent of the economy, so the rival's provinces and the
	/// unclaimed ones still get a header of their own.</summary>
	public void ShowHeader(string provinceName, string realmName, string realmKey, Color accent)
	{
		_name.Text = provinceName;
		_realm.Text = realmName;
		_accent.Color = accent;

		// A realm has a crest when someone has drawn one; the accent stripe carries the rest. The slot
		// keeps its place either way — hiding it would shorten the header, and the whole sidebar
		// would shift every time the selection moved between a realm's province and an unclaimed one.
		string crestPath = $"{IconDirectory}/shield-{realmKey}.png";
		_crest.Texture = ResourceLoader.Exists(crestPath)
			? new AtlasTexture { Atlas = GD.Load<Texture2D>(crestPath), Region = CrestRegion }
			: null;
	}

	/// <summary>The province's own numbers, or nothing at all for one no realm is running yet.</summary>
	public void ShowEconomy(ProvinceEconomy economy, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_economy = economy;
		_definition = definition;
		_balance = balance;
		_season = season;
		Refresh();
	}

	/// <summary>Re-reads the economy — after a turn, or after a worker is moved somewhere else.</summary>
	public void Refresh()
	{
		bool held = _economy != null;
		_held.Visible = held;
		_foreign.Visible = !held;
		_population.Text = held ? _economy.Population.ToString("N0") : "—";
		_loyalty.Text = held ? Mathf.RoundToInt(_economy.Loyalty).ToString() : "—";
		_tax.Text = held ? $"{_economy.Tax}%" : "—";

		// A county's button means the company it would send: the biggest one with a season left in
		// its legs. Everything else is ordered about by its own banner on the map.
		FieldArmy ready = held ? _economy.Readiest() : null;
		_march.Visible = ready != null;
		if (ready != null)
		{
			// In paces of good road, which is the only unit a lord can hold in his head: the map
			// charges more than a pace for a pace of hillside, and it says so as he points at it.
			_march.Text = $"March  ({Mathf.RoundToInt(ready.MarchLeft / _balance.MarchCostByRoad):N0} paces)";
		}

		_ration.Text = held ? _economy.Ration.ToString() : "—";

		foreach ((ResourceType type, string _) in Resources)
		{
			if (!held)
			{
				_stock[type].Text = "—";
				_yield[type].Text = "";
				continue;
			}

			_stock[type].Text = StockOf(type).ToString("N0");
		}

		// What next season will leave the province with, net: the harvest less what the people eat,
		// the seed that goes back into the ground, the herd that goes under the knife in a bad
		// winter. A row that only ever counted up would show a granary gaining every season of a
		// year it is quietly being emptied. A county nobody here runs has no season to play out: the
		// loop above has already blanked its row. Previewing it anyway threw, and since the turn
		// reselects whatever county is in hand, a lord who had last looked at a neighbour's land lost
		// the rest of every turn after it — the fields, the walls and the season on the map.
		TurnSummary next = held ? EconomySimulation.Preview(_economy, _definition, _balance, _season) : null;
		if (held)
		{
			foreach ((ResourceType type, string _) in Resources)
			{
				int change = ChangeIn(next, type);
				Label reading = _yield[type];
				// Nothing at all rather than a zero: a column of zeroes is noise under the numbers.
				reading.Text = change == 0 ? "" : change > 0 ? $"+{change:N0}" : $"−{-change:N0}";
				reading.AddThemeColorOverride("font_color", change < 0 ? Lack : Gain);
			}
		}

		_labour.Show(held ? _economy : null, _definition, _balance, _season);
		_labourFrame.Visible = held;

		// The weapons waiting in the armoury, not the men holding them: an army raised in the yard
		// marches out of the county, so a list of it here is a list of who has already gone. What
		// the smithy has made stays until somebody is handed it. Only what there is, and what the
		// forge is making, with what next season adds under it the way the stores carry theirs — a
		// lit forge that will turn out nothing says so in red.
		bool shown = false;
		foreach ((Control divider, Control cell, Label count, Label yield, string weapon) in _armoury)
		{
			int stocked = held ? _economy.Armoury.GetValueOrDefault(weapon) : 0;
			bool forging = held && _economy.Forging == weapon;
			count.Text = stocked.ToString("N0");
			yield.Text = forging ? $"+{next.Forged:N0}" : "";
			yield.AddThemeColorOverride("font_color", forging && next.Forged == 0 ? Lack : Gain);
			cell.Visible = stocked > 0 || forging;
			if (divider != null)
			{
				divider.Visible = cell.Visible && shown;
			}

			shown |= cell.Visible;
		}

		_armouryFrame.Visible = shown;
	}

	// --- building the thing ------------------------------------------------------------------

	private void BuildHeader()
	{
		var content = new HBoxContainer();
		content.AddThemeConstantOverride("separation", 10);
		AddChild(Framed(content, 10));

		_accent = new ColorRect { CustomMinimumSize = new Vector2(4, 0) };
		content.AddChild(_accent);

		_crest = new TextureRect
		{
			CustomMinimumSize = new Vector2(42, 50),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		content.AddChild(_crest);

		// Expanding, or the name gets the narrowest box the text will fit in.
		var titles = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		titles.AddThemeConstantOverride("separation", 2);
		content.AddChild(titles);

		// One line, always: a name long enough to wrap would make the header taller and push
		// everything under it down the moment the selection changed.
		_name = new Label
		{
			Text = "Select a stronghold",
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			ThemeTypeVariation = "GildedTitle", // the title font and its outline; the gradient is below
		};
		_name.AddThemeFontSizeOverride("font_size", 23);
		GoldTitle.Apply(_name);
		titles.AddChild(_name);

		_realm = Small("", Waiting);
		titles.AddChild(_realm);
	}

	// Population, loyalty, tax, ration: the four numbers a lord is judged on, in one strip.
	private void BuildStats()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);
		_held.AddChild(Framed(row, 8));

		// 20 and 14 rather than 22 and 16: tax and ration read as words, not numbers, and at the
		// larger size the four cells together are wider than the sidebar is — which pushed the whole
		// sidebar out every time the selection landed on a province you hold.
		_population = StatCell(row, Icon("population", 20));
		row.AddChild(Divider());
		_loyalty = StatCell(row, Icon("heart", 20), () => LoyaltyPressed?.Invoke(),
			"See where this county's goodwill went");
		row.AddChild(Divider());
		_tax = StatCell(row, Icon("gold", 20), () => TaxPressed?.Invoke(), "Set what this county pays");
		row.AddChild(Divider());
		_ration = StatCell(row, Icon("food", 20), () => RationPressed?.Invoke(),
			"Set what this county is given to eat");
	}

	private Label StatCell(HBoxContainer row, Control icon, System.Action pressed = null, string hint = "")
	{
		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cell.AddThemeConstantOverride("separation", 5);
		cell.AddChild(icon);

		var value = new Label { VerticalAlignment = VerticalAlignment.Center };
		value.AddThemeFontSizeOverride("font_size", 14);
		value.AddThemeColorOverride("font_color", Cream);
		cell.AddChild(value);

		row.AddChild(cell);
		if (pressed == null)
		{
			return value;
		}

		// A cell that can be pressed has to say so before it is pressed, and the cursor cannot say
		// it — the game draws the same sword over everything. So the number itself answers the
		// mouse: it brightens under the pointer, the way the plaques in the rooms do.
		cell.MouseFilter = MouseFilterEnum.Stop;
		cell.TooltipText = hint;
		cell.MouseEntered += () => value.AddThemeColorOverride("font_color", Chrome.Bright);
		cell.MouseExited += () => value.AddThemeColorOverride("font_color", Cream);
		cell.GuiInput += pointer =>
		{
			if (pointer is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				pressed();
			}
		};

		return value;
	}

	// What the province holds, and under it what next turn adds to it.
	private void BuildResources()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);
		_held.AddChild(Framed(row, 8));

		bool first = true;
		foreach ((ResourceType type, string icon) in Resources)
		{
			if (!first)
			{
				row.AddChild(Divider());
			}

			first = false;

			var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cell.AddThemeConstantOverride("separation", 2);
			cell.AddChild(Centered(Icon(icon, 34)));

			var stock = new Label { HorizontalAlignment = HorizontalAlignment.Center };
			stock.AddThemeFontSizeOverride("font_size", 24);
			stock.AddThemeColorOverride("font_color", Cream);
			cell.AddChild(stock);

			Label yield = Small("", Gain);
			yield.HorizontalAlignment = HorizontalAlignment.Center;
			cell.AddChild(yield);

			_stock[type] = stock;
			_yield[type] = yield;
			row.AddChild(cell);
		}
	}

	/// <summary>What the county has in its armoury, and what the smithy adds to it a season. This is
	/// where the muster used to stand, and it earns the room better: men raised in the yard leave
	/// the county, weapons stay in it until somebody is given them.</summary>
	private void BuildArmoury()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);

		foreach ((string weapon, string icon) in Arms)
		{
			Control divider = null;
			if (_armoury.Count > 0)
			{
				divider = Divider();
				row.AddChild(divider);
			}

			var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cell.AddThemeConstantOverride("separation", 2);
			cell.AddChild(Centered(Icon(icon, 30)));

			var count = new Label { HorizontalAlignment = HorizontalAlignment.Center };
			count.AddThemeFontSizeOverride("font_size", 20);
			count.AddThemeColorOverride("font_color", Cream);
			cell.AddChild(count);

			Label yield = Small("", Gain);
			yield.HorizontalAlignment = HorizontalAlignment.Center;
			cell.AddChild(yield);

			_armoury.Add((divider, cell, count, yield, weapon));
			row.AddChild(cell);
		}

		_armouryFrame = Framed(row, 8);
		_held.AddChild(_armouryFrame);
	}

	/// <summary>The labour bar, on the panel the lord is looking at his county from — which is where
	/// Lords of the Realm kept it. The province's own screen carries one too; neither remembers
	/// anything, both read the allocation, so they cannot drift apart.</summary>
	private void BuildLabour()
	{
		_labour = new LabourBar();
		_labourFrame = Framed(_labour, 8);
		_held.AddChild(_labourFrame);
		// Nothing on this panel follows the grip while it moves — the bar keeps its own two counts —
		// so only the letting go is worth a redraw, and that is when the forecasts change.
		_labour.Settled += Refresh;
	}

	/// <summary>What the scouts say of a county you do not hold, under its keep: not its books, but
	/// enough to know which gate is worth the march.</summary>
	public void ShowWord(string word) => _word.Text = word;

	/// <summary>What stands in place of the books for a province you do not hold: the keep on its
	/// hill, unlit, and a line saying why there are no numbers under it. A column of dashes where
	/// the stores should be reads as a province with nothing in it rather than as one you cannot
	/// see into.</summary>
	private void BuildForeign()
	{
		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 8);

		// Covered and expanding: the panel runs the whole height the books would have taken, and the
		// keep is cropped to fill it rather than leaving a dark gap under a small picture.
		stack.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(UnclaimedArtPath),
			CustomMinimumSize = new Vector2(0, 190),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		});

		_word = Small("No word reaches you of what it holds.", Waiting);
		_word.AutowrapMode = TextServer.AutowrapMode.Word;
		_word.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(_word);

		_foreign = Framed(stack, 8);
		_foreign.SizeFlagsVertical = SizeFlags.ExpandFill;
		_foreign.Visible = false;
		AddChild(_foreign);
	}

	// --- small parts -------------------------------------------------------------------------

	/// <summary>Wraps a row in a gilded frame with an even margin around it — the sidebar reads as a
	/// stack of framed panels, the way the rest of the campaign's chrome does.</summary>
	private static PanelContainer Framed(Control content, int margin)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", GroupStyle());
		var inset = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", margin);
		}

		panel.AddChild(inset);
		inset.AddChild(content);
		return panel;
	}

	/// <summary>The gilded frame a whole group sits in, and the darker cell one tile sits in: the
	/// same border, so a row of tiles reads as belonging inside its panel.</summary>
	private static StyleBoxFlat GroupStyle()
	{
		StyleBoxFlat style = TileStyle();
		style.BgColor = new Color(0.098f, 0.094f, 0.090f, 0.94f);
		return style;
	}

	private static StyleBoxFlat TileStyle() => new()
	{
		BgColor = new Color(0.078f, 0.075f, 0.078f, 0.9f),
		BorderWidthLeft = 2,
		BorderWidthTop = 2,
		BorderWidthRight = 2,
		BorderWidthBottom = 2,
		BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.8f),
		CornerRadiusTopLeft = 3,
		CornerRadiusTopRight = 3,
		CornerRadiusBottomRight = 3,
		CornerRadiusBottomLeft = 3,
	};

	/// <summary>The hairline between two cells of a row, so a strip of numbers reads as separate
	/// readings rather than one run of digits.</summary>
	private static VSeparator Divider()
	{
		var line = new VSeparator();
		line.AddThemeStyleboxOverride("separator", new StyleBoxLine
		{
			Color = new Color(0.549f, 0.447f, 0.271f, 0.35f),
			Vertical = true,
			Thickness = 1,
		});
		return line;
	}

	private static TextureRect Icon(string name, int side) => new()
	{
		Texture = GD.Load<Texture2D>($"{IconDirectory}/{name}.png"),
		CustomMinimumSize = new Vector2(side, side),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
	};

	private static Label Small(string text, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 14);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static Control Centered(Control control)
	{
		var center = new CenterContainer();
		center.AddChild(control);
		return center;
	}

	private static int ChangeIn(TurnSummary summary, ResourceType type) => type switch
	{
		ResourceType.Grain => summary.GrainChange,
		ResourceType.Cattle => summary.CattleChange,
		ResourceType.Wood => summary.WoodChange,
		ResourceType.Stone => summary.StoneChange,
		_ => summary.IronChange,
	};

	private int StockOf(ResourceType type) => type switch
	{
		ResourceType.Grain => _economy.Grain,
		ResourceType.Cattle => _economy.Cattle,
		ResourceType.Wood => _economy.Wood,
		ResourceType.Stone => _economy.Stone,
		_ => _economy.Iron,
	};

}
