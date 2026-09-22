using System.Collections.Generic;
using Godot;

/// <summary>What is standing on a piece of ground, told in full: a county's men, kind by kind.
///
/// The map used to carry the number on a little shield under every banner. That answered "how
/// many" and nothing else, and it answered it over and over again in the corner of the lord's eye.
/// The figures on the ground say how big a body of men it is — one man, two, three — and this
/// panel, on a press of the right button, says exactly who they are: what they are armed with, what
/// they cost him a season, and how far they can still walk this one.
///
/// Everyone's men, not only the player's: what a lord can count from a hilltop is how many they are
/// and what they carry, and that is the one honest thing about another man's army.</summary>
public partial class ArmyPanel : Control
{
	private const string FramePath = "res://assets/ui/army-panel.png";
	private const string CaptainPath = "res://assets/ui/army-captain.png";
	private const string IconDirectory = "res://assets/ui/icons";
	private const float FadeSeconds = 0.16f;

	/// <summary>How big the panel is drawn, and the margins inside its painted frame: the art is a
	/// wide gold border with a ribbon across the top. Wide because a company is read in two columns
	/// and a lord should not have to lean in: the whole thing is lettered at the size the map's own
	/// furniture is, over ground that is moving.
	///
	/// The title is lettered ON the painted ribbon, so the top margin is the drop to the field
	/// underneath it rather than the frame's own edge, and the foot is deep enough that the orders
	/// stand inside the border instead of on it.</summary>
	private static readonly Vector2 PanelSize = new(1160, 960);
	private const int FrameInset = 50;
	private const int RibbonTop = 125;
	private const int RibbonDrop = 14;
	private const int FrameFoot = 129;
	private static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);

	/// <summary>The one yard every man on the roster stands in, and the tile he stands in it on. One
	/// ground for all of them on purpose — the roster is a list of who is here, and seven different
	/// skies behind seven rows makes it a gallery instead.
	///
	/// The tile's shape is also the crop: the portraits are full-length, and as much of one as a tile
	/// this wide can hold is the man from the waist up. A row three fingers high has no business
	/// showing a soldier's boots.</summary>
	private const string RosterGround = "res://assets/units/backgrounds/courtyard.jpg";
	private static readonly Vector2 PortraitTile = new(110, 64);

	/// <summary>How much sky is left over a man's head when the tile is cut.</summary>
	private const int HeadRoom = 24;

	/// <summary>What the lord can order this company to do — that company, not its county's men in
	/// general. The panel decides none of it: what splitting or sending men home does to the ledger
	/// is the ledger's business.</summary>
	public event System.Action<FieldArmy> MarchPressed;
	public event System.Action<FieldArmy> SplitPressed;
	public event System.Action<FieldArmy> DisbandPressed;

	private Label _title;
	private Label _subtitle;
	private Label _standing;
	private TextureRect _crest;
	private readonly Dictionary<string, (Control Row, Label Count)> _roster = new();
	private Label _total;
	private Label _mercenaries;
	private Label _men;
	private Label _wages;
	private Label _paces;
	private Label _walls;

	/// <summary>The company on the table. Held so the March button orders THESE men about and not
	/// whoever else their county has standing somewhere else.</summary>
	private FieldArmy _army;
	private Button _march;
	private Button _split;
	private Button _disband;

	/// <summary>Whether Disband has been pressed once already. Sending a company home cannot be
	/// undone and nothing else on this panel asks twice, so the button itself does the asking.</summary>
	private bool _disbandAsked;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.5f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer();
		Chrome.Fill(centred);
		centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centred);

		var frame = new TextureRect
		{
			Texture = GD.Load<Texture2D>(FramePath),
			CustomMinimumSize = PanelSize,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Stop,
		};
		centred.AddChild(frame);

		var inside = new MarginContainer();
		Chrome.Fill(inside);
		inside.AddThemeConstantOverride("margin_left", FrameInset);
		inside.AddThemeConstantOverride("margin_right", FrameInset);
		inside.AddThemeConstantOverride("margin_top", RibbonTop);
		inside.AddThemeConstantOverride("margin_bottom", FrameFoot);
		frame.AddChild(inside);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 3);
		inside.AddChild(column);

		// On the ribbon the frame is painted with.
		_title = Chrome.Line("", 36, Chrome.Cream);
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_title);
		column.AddChild(_title);

		// Under the painted ribbon, not on its fringe.
		column.AddChild(new Control { CustomMinimumSize = new Vector2(0, RibbonDrop) });
		_subtitle = Chrome.Line("", 20, Chrome.Soft);
		_subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_subtitle);

		column.AddChild(Head());
		column.AddChild(Readings());
		column.AddChild(Heading("Unit roster"));
		column.AddChild(Roster());
		column.AddChild(Tally());

		// Whatever the frame has left over sits here, so the orders stay on its bottom rail.
		column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		column.AddChild(Orders());

		// The way out, in the corner of the frame, where a painted panel keeps it.
		var close = new Button
		{
			Text = "✕",
			CustomMinimumSize = new Vector2(38, 38),
			Flat = true,
			Position = new Vector2(PanelSize.X - 120f, 62f),
		};
		close.AddThemeFontSizeOverride("font_size", 24);
		close.AddThemeColorOverride("font_color", Chrome.Cream);
		close.Pressed += Close;
		frame.AddChild(close);
	}

	/// <summary>The captain's portrait, whose men these are, and the realm's crest.</summary>
	private Control Head()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);

		var captain = new TextureRect
		{
			Texture = GD.Load<Texture2D>(CaptainPath),
			CustomMinimumSize = new Vector2(88, 88),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		};
		row.AddChild(Chrome.Framed(captain, 4));

		var said = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
		said.AddThemeConstantOverride("separation", 6);
		row.AddChild(said);

		_standing = Chrome.Line("", 24, Chrome.Bright);
		_standing.AutowrapMode = TextServer.AutowrapMode.Word;
		said.AddChild(_standing);

		_walls = Chrome.Line("", 18, Chrome.Soft);
		_walls.AutowrapMode = TextServer.AutowrapMode.Word;
		said.AddChild(_walls);

		_crest = new TextureRect
		{
			CustomMinimumSize = new Vector2(64, 76),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		row.AddChild(_crest);
		return row;
	}

	/// <summary>The strip of figures a lord reads first: how many men, how they are bearing it, what
	/// they cost him a season, and how much of this season's marching they have left.</summary>
	private Control Readings()
	{
		var strip = new HBoxContainer();
		strip.AddThemeConstantOverride("separation", 0);
		strip.AddChild(Reading("men", "Men", out _men));
		strip.AddChild(Reading("coins", "Wages", out _wages));
		strip.AddChild(Reading("boot", "Paces left", out _paces));
		return Chrome.Framed(strip, 8);
	}

	private static Control Reading(string icon, string what, out Label value)
	{
		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		cell.AddThemeConstantOverride("separation", 8);
		cell.AddChild(Chrome.Icon(icon, 26));

		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 0);
		cell.AddChild(stack);
		stack.AddChild(Chrome.Line(what, 15, Chrome.Soft));
		value = Chrome.Line("", 22, Chrome.Bright);
		stack.AddChild(value);
		return cell;
	}

	private static Control Heading(string what)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		Control left = Chrome.Rule(0);
		left.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		left.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(left);
		row.AddChild(Chrome.Line(what, 19, Chrome.Cream));
		Control right = Chrome.Rule(0);
		right.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		right.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(right);
		return row;
	}

	/// <summary>Every kind of man the yard trains, under his own portrait — the same picture the
	/// training yard deals him under (assets/units) — and how many of him are in this company. The
	/// kinds come off the same recruits.json the yard deals from, so a muster here and a muster there
	/// cannot drift apart.</summary>
	private Control Roster()
	{
		var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", 8);
		foreach (string kind in Units.All())
		{
			var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddThemeConstantOverride("separation", 10);
			row.CustomMinimumSize = new Vector2(0, PortraitTile.Y + 8);
			row.AddChild(Portrait(kind));
			Label name = Chrome.Line(Units.Of(kind).Name, 20, Chrome.Bright);
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			name.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(name);
			Label count = Chrome.Line("0", 24, Chrome.Bright);
			count.HorizontalAlignment = HorizontalAlignment.Right;
			count.CustomMinimumSize = new Vector2(56, 0);
			count.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(count);
			Control framed = Chrome.Framed(row, 8);
			framed.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			_roster[kind] = (framed, count);
			grid.AddChild(framed);
		}

		return grid;
	}

	/// <summary>A soldier, standing on ground rather than on nothing: the yard behind him and the
	/// man himself over it, cut off at the belt so the row shows a face and not a full-length figure
	/// three fingers high. The portraits are cut-outs, so without the yard the row would be a man
	/// hanging in the dark.</summary>
	private static Control Portrait(string kind)
	{
		var tile = new PanelContainer { ClipContents = true, CustomMinimumSize = PortraitTile };
		tile.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		tile.AddChild(Fill(GD.Load<Texture2D>(RosterGround)));

		string painted = UnitArt.Portrait(kind);
		if (ResourceLoader.Exists(painted))
		{
			var man = GD.Load<Texture2D>(painted);
			float tall = man.GetWidth() * PortraitTile.Y / PortraitTile.X;
			tile.AddChild(Fill(new AtlasTexture
			{
				Atlas = man,
				Region = new Rect2(0, Mathf.Min(HeadTop(man), man.GetHeight() - tall), man.GetWidth(), tall),
			}));
		}

		return Chrome.Framed(tile, 2);
	}

	/// <summary>How far down the man's head begins, so the tile is cut just above it instead of at
	/// the top of the picture. He stands at a different height in every portrait — a rider's head is
	/// near the top edge, a peasant's a fifth of the way down — so this is read off the picture
	/// rather than written down beside it, and art that is redrawn cannot drift from a number nobody
	/// remembers.
	///
	/// Only the middle of the frame is looked at, and coarsely: a spear held upright is not a head,
	/// and every fiftieth pixel says where a man starts as well as all of them would.</summary>
	private static int HeadTop(Texture2D portrait)
	{
		Image drawn = portrait.GetImage();
		int from = drawn.GetWidth() * 35 / 100;
		int to = drawn.GetWidth() * 68 / 100;
		for (int y = 0; y < drawn.GetHeight(); y += 2)
		{
			int solid = 0;
			for (int x = from; x < to; x += 3)
			{
				solid += drawn.GetPixel(x, y).A > 0.25f ? 1 : 0;
			}

			// A sixth of the band covered is a head and not the fringe of a banner behind him.
			if (solid * 18 > to - from)
			{
				return Mathf.Max(0, y - HeadRoom);
			}
		}

		return 0;
	}

	/// <summary>One layer of the tile. A PanelContainer lays every child over the same rect, so the
	/// ground goes in first and the man stands on it.</summary>
	private static TextureRect Fill(Texture2D texture) => new()
	{
		Texture = texture,
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		MouseFilter = MouseFilterEnum.Ignore,
	};

	private Control Tally()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);

		var counted = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		counted.AddThemeConstantOverride("separation", 8);
		counted.AddChild(Chrome.Icon("men", 28));
		_total = Chrome.Line("", 22, Chrome.Bright);
		counted.AddChild(_total);
		row.AddChild(counted);

		var hired = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		hired.AddThemeConstantOverride("separation", 8);
		hired.AddChild(Chrome.Icon("mercenaries", 26));
		_mercenaries = Chrome.Line("", 18, Chrome.Soft);
		hired.AddChild(_mercenaries);
		row.AddChild(hired);
		return row;
	}

	/// <summary>The three things a lord can do with a company that is standing somewhere: move it,
	/// cut it in two, or send it home.</summary>
	private Control Orders()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);

		_march = Order("Move", "footsteps", () =>
		{
			FieldArmy marching = _army;
			Close();
			MarchPressed?.Invoke(marching);
		});
		row.AddChild(_march);

		_split = Order("Split Army", "company", () =>
		{
			FieldArmy splitting = _army;
			Close();
			SplitPressed?.Invoke(splitting);
		});
		row.AddChild(_split);

		_disband = Order("Disband", "morale", () =>
		{
			if (!_disbandAsked)
			{
				_disbandAsked = true;
				_disband.Text = "Sure?";
				return;
			}

			FieldArmy sent = _army;
			Close();
			DisbandPressed?.Invoke(sent);
		});
		row.AddChild(_disband);
		return row;
	}

	private static Button Order(string text, string icon, System.Action pressed)
	{
		var order = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 50),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		order.AddThemeFontSizeOverride("font_size", 22);
		if (icon.Length > 0)
		{
			order.Icon = GD.Load<Texture2D>($"{IconDirectory}/{icon}.png");
			order.ExpandIcon = true;
			order.AddThemeConstantOverride("icon_max_width", 36);
			order.AddThemeConstantOverride("h_separation", 8);
		}

		order.Pressed += pressed;
		return order;
	}

	/// <summary>Shows one company: these men, their own legs, and the county that pays them.
	/// <paramref name="yours"/> decides whether the lord may order them about from here — a rival's
	/// company is something to look at.</summary>
	public void Show(FieldArmy army, ProvinceEconomy county, GameBalance balance, string realm,
		string realmKey, bool yours)
	{
		_army = army;
		_title.Text = army.County.Length > 0 && army.County != army.Home
			? $"{army.Home} in {army.County}"
			: army.Home;

		// Which of the county's companies this is, where it has more than one. A lord with three
		// banners on the board has to be able to tell which of them he has just opened.
		int banners = county?.Armies.Count ?? 1;
		_subtitle.Text = banners > 1
			? $"{Ordinal(army.Id)} company of {realm}"
			: $"Field army of {realm}";

		_standing.Text = county is { BesiegedFrom.Length: > 0 }
			? $"Their county is held under siege."
			: yours ? "These are your men." : "Another lord's men.";
		_walls.Text = $"{army.Strength:N0} in the field, {county?.CastleMen ?? 0:N0} behind the walls of "
			+ $"{army.Home}.";

		string crest = $"{IconDirectory}/shield-{realmKey}.png";
		_crest.Texture = ResourceLoader.Exists(crest)
			? new AtlasTexture { Atlas = GD.Load<Texture2D>(crest), Region = CrestRegion }
			: null;

		foreach ((string kind, (Control row, Label count)) in _roster)
		{
			int men = army.Men.GetValueOrDefault(kind);
			count.Text = men.ToString("N0");
			// A kind nobody in this company carries is still listed, and greyed: what a lord has not
			// raised is as much a fact about his army as what he has.
			row.Modulate = new Color(1f, 1f, 1f, men > 0 ? 1f : 0.45f);
		}

		_men.Text = army.Strength.ToString("N0");
		_wages.Text = Mathf.CeilToInt(army.Strength * balance.WagePerSoldier).ToString("N0");
		_paces.Text = Mathf.RoundToInt(army.MarchLeft / balance.MarchCostByRoad).ToString("N0");
		_total.Text = $"{army.Strength:N0} men under arms";
		_mercenaries.Text = county is { MercenaryMen: > 0 }
			? $"{county.MercenaryMen:N0} hired, {county.MercenarySeasonsLeft} season"
				+ (county.MercenarySeasonsLeft == 1 ? " left" : "s left")
			: "No mercenaries in this company";

		_march.Visible = yours && army.Strength > 0 && army.MarchLeft > 0f;
		_split.Visible = yours && army.Strength > 1;
		_disband.Visible = yours;
		_disbandAsked = false;
		_disband.Text = "Disband";
		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	/// <summary>Which company this is, in words. Past a handful it is simply numbered: a lord with
	/// six banners in one county is counting them, not naming them.</summary>
	private static string Ordinal(int id) => id switch
	{
		1 => "First",
		2 => "Second",
		3 => "Third",
		4 => "Fourth",
		5 => "Fifth",
		_ => $"No. {id}",
	};

	private void Close()
	{
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>A press on the dimmed map behind closes it, and so does Escape: nothing here is
	/// confirmed or cancelled.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Visible && @event is InputEventMouseButton { Pressed: true })
		{
			Close();
			AcceptEvent();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
