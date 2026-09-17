using System.Collections.Generic;
using Godot;

/// <summary>The province block of the campaign sidebar: whose province it is and how it stands,
/// what it holds and what next turn will bring in, then what it has built and what it musters.
///
/// Built in code rather than in the scene because nearly all of it is one cell repeated — five
/// resources, eight buildings, four kinds of soldier — and a loop stays even where seventeen
/// hand-placed panels drift apart.
///
/// The yields are the same projection the worker panel previews, so moving a worker changes both
/// at once; the page refreshes this when that panel is touched. Buildings and muster have no
/// systems behind them yet (Phase 2, Phase 3): their tiles are drawn dim and read "—" rather than
/// showing a number nothing produces.</summary>
public partial class ProvinceSidebar : VBoxContainer
{
	private const string IconDirectory = "res://assets/ui/icons";
	// The crest art carries wide empty margins, and this is the crop the top bar's shield uses too.
	// A realm whose shield is drawn to different margins needs its own crop, not this one.
	private static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);

	private static readonly Color Cream = new("d9cdb4");
	private static readonly Color Gain = new("6fbf5f");
	private static readonly Color Waiting = new("6f6a60");

	private static readonly (ResourceType Type, string Icon)[] Resources =
	{
		(ResourceType.Grain, "food"),
		(ResourceType.Cattle, "livestock"),
		(ResourceType.Wood, "wood"),
		(ResourceType.Stone, "stone"),
		(ResourceType.Iron, "iron"),
	};

	private static readonly string[] Buildings =
		{ "Houses", "Granary", "Mill", "Market", "Church", "Blacksmith", "Barracks", "Keep" };

	private static readonly (string Name, string Icon)[] Muster =
	{
		("Spearmen", "spear"),
		("Archers", "bow"),
		("Horse", "horse"),
		("Men-at-arms", "sword"),
	};

	private ColorRect _accent;
	private TextureRect _crest;
	private Label _name;
	private Label _realm;
	private Label _population;
	private Label _loyalty;
	private Label _tax;
	private Label _ration;
	private readonly Dictionary<ResourceType, Label> _stock = new();
	private readonly Dictionary<ResourceType, Label> _yield = new();

	private ProvinceEconomy _economy;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 8);
		BuildHeader();
		BuildStats();
		BuildResources();
		BuildBuildings();
		BuildMuster();
	}

	/// <summary>Who this province is. Independent of the economy, so the rival's provinces and the
	/// unclaimed ones still get a header of their own.</summary>
	public void ShowHeader(string provinceName, string realmName, string realmKey, Color accent)
	{
		_name.Text = provinceName;
		_realm.Text = realmName;
		_accent.Color = accent;

		// A realm has a crest when someone has drawn one; the accent stripe carries the rest.
		string crestPath = $"{IconDirectory}/shield-{realmKey}.png";
		bool hasCrest = ResourceLoader.Exists(crestPath);
		_crest.Visible = hasCrest;
		if (hasCrest)
		{
			_crest.Texture = new AtlasTexture
			{
				Atlas = GD.Load<Texture2D>(crestPath),
				Region = CrestRegion,
			};
		}
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
		_population.Text = held ? _economy.Population.ToString("N0") : "—";
		_loyalty.Text = held ? Mathf.RoundToInt(_economy.Loyalty).ToString() : "—";
		_tax.Text = held ? _economy.Tax.ToString() : "—";
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
			int projected = EconomySimulation.ProjectedYield(
				type, WorkersOn(type), type == ResourceType.Cattle ? _economy.Cattle : 0,
				_definition, _balance, _season);
			// Nothing at all rather than a dash: a column of dashes is noise under the numbers.
			_yield[type].Text = projected > 0 ? $"+{projected}" : "";
		}
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

		// Expanding, or the name gets the narrowest box the text will fit in and "Icemere Reach"
		// comes out broken across two lines mid-word.
		var titles = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		titles.AddThemeConstantOverride("separation", 2);
		content.AddChild(titles);

		_name = new Label
		{
			Text = "Select a stronghold",
			AutowrapMode = TextServer.AutowrapMode.Word,
			ThemeTypeVariation = "GildedTitle", // the title font and its outline; the gradient is below
		};
		_name.AddThemeFontSizeOverride("font_size", 20);
		GoldTitle.Apply(_name);
		titles.AddChild(_name);

		_realm = Small("", Waiting);
		titles.AddChild(_realm);
	}

	// Population, loyalty, tax, ration: the four numbers a lord is judged on, in one strip.
	private void BuildStats()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);
		AddChild(Framed(row, 8));

		_population = StatCell(row, Icon("population", 18));
		_loyalty = StatCell(row, new NavRailIcon { Kind = NavRailIcon.Glyph.Heart, CustomMinimumSize = new Vector2(18, 18) });
		_tax = StatCell(row, Icon("gold", 18));
		_ration = StatCell(row, Icon("food", 18));
	}

	private Label StatCell(HBoxContainer row, Control icon)
	{
		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cell.AddThemeConstantOverride("separation", 5);
		cell.AddChild(icon);

		var value = new Label { VerticalAlignment = VerticalAlignment.Center };
		value.AddThemeFontSizeOverride("font_size", 14);
		value.AddThemeColorOverride("font_color", Cream);
		cell.AddChild(value);

		row.AddChild(cell);
		return value;
	}

	// What the province holds, and under it what next turn adds to it.
	private void BuildResources()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);
		AddChild(Framed(row, 8));

		foreach ((ResourceType type, string icon) in Resources)
		{
			var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cell.AddThemeConstantOverride("separation", 2);
			cell.AddChild(Centered(Icon(icon, 26)));

			var stock = new Label { HorizontalAlignment = HorizontalAlignment.Center };
			stock.AddThemeFontSizeOverride("font_size", 18);
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

	private void BuildBuildings()
	{
		var grid = new GridContainer { Columns = 4 };
		grid.AddThemeConstantOverride("h_separation", 5);
		grid.AddThemeConstantOverride("v_separation", 5);
		AddChild(Framed(grid, 6));

		foreach (string building in Buildings)
		{
			var tile = new PanelContainer
			{
				CustomMinimumSize = new Vector2(0, 52),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			tile.AddThemeStyleboxOverride("panel", TileStyle());

			var stack = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
			stack.AddThemeConstantOverride("separation", 1);
			tile.AddChild(stack);

			// No wrapping: at this width "Blacksmith" would break across two lines mid-word.
			Label name = Small(building, Waiting);
			name.HorizontalAlignment = HorizontalAlignment.Center;
			name.AddThemeFontSizeOverride("font_size", 11);
			name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			stack.AddChild(name);

			Label state = Small("—", Waiting);
			state.HorizontalAlignment = HorizontalAlignment.Center;
			stack.AddChild(state);

			grid.AddChild(tile);
		}
	}

	private void BuildMuster()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 5);
		AddChild(Framed(row, 6));

		foreach ((string name, string icon) in Muster)
		{
			var tile = new PanelContainer
			{
				CustomMinimumSize = new Vector2(0, 68),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				TooltipText = name,
			};
			tile.AddThemeStyleboxOverride("panel", TileStyle());

			var stack = new VBoxContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
			stack.AddThemeConstantOverride("separation", 2);
			tile.AddChild(stack);

			TextureRect glyph = Icon(icon, 34);
			glyph.Modulate = new Color(1, 1, 1, 0.75f); // dimmed: nothing musters them yet
			stack.AddChild(Centered(glyph));

			Label count = Small("—", Waiting);
			count.HorizontalAlignment = HorizontalAlignment.Center;
			stack.AddChild(count);

			row.AddChild(tile);
		}
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
		label.AddThemeFontSizeOverride("font_size", 12);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static Control Centered(Control control)
	{
		var center = new CenterContainer();
		center.AddChild(control);
		return center;
	}

	private int StockOf(ResourceType type) => type switch
	{
		ResourceType.Grain => _economy.Grain,
		ResourceType.Cattle => _economy.Cattle,
		ResourceType.Wood => _economy.Wood,
		ResourceType.Stone => _economy.Stone,
		_ => _economy.Iron,
	};

	private int WorkersOn(ResourceType type) => type switch
	{
		ResourceType.Grain => _economy.GrainWorkers,
		ResourceType.Cattle => _economy.CattleWorkers,
		ResourceType.Wood => _economy.WoodWorkers,
		ResourceType.Stone => _economy.StoneWorkers,
		_ => _economy.IronWorkers,
	};
}
