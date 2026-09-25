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
public partial class ArmyPanel : PaintedPanel
{
	private const string CaptainPath = "res://assets/ui/army-captain.png";
	private const string IconDirectory = "res://assets/ui/icons";

	/// <summary>How big the panel is drawn. Wide because a company is read in two columns and a lord
	/// should not have to lean in: the whole thing is lettered at the size the map's own furniture
	/// is, over ground that is moving.</summary>
	private static readonly Vector2 Drawn = new(1200, 1010);

	/// <summary>The drop from the painted ribbon to the field underneath it.</summary>
	private const int RibbonDrop = 40;

	private static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);

	/// <summary>The tile each man on the roster stands on.</summary>
	private static readonly Vector2 PortraitTile = new(110, 64);

	/// <summary>How the roster is laid out. Shared with the drawing of the panel itself, which is cut
	/// to the number of rows this company actually fills.</summary>
	private const int RosterColumns = 3;
	private const int RosterGap = 8;

	/// <summary>What the lord can order this company to do — that company, not its county's men in
	/// general. The panel decides none of it: what splitting or sending men home does to the ledger
	/// is the ledger's business.</summary>
	public event System.Action<FieldArmy> MarchPressed;
	public event System.Action<FieldArmy> SplitPressed;
	public event System.Action<FieldArmy> GarrisonPressed;
	public event System.Action<FieldArmy> DisbandPressed;

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
	private Button _garrison;
	private Button _disband;

	/// <summary>Whether Disband has been pressed once already. Sending a company home cannot be
	/// undone and nothing else on this panel asks twice, so the button itself does the asking.</summary>
	private bool _disbandAsked;

	/// <summary>The word on the Disband plate, which is how it asks a second time.</summary>
	private Label _disbandWord;

	protected override Vector2 PanelSize => Drawn;

	protected override void Furnish()
	{
		// Under the painted ribbon, not on its fringe.
		Column.AddChild(new Control { CustomMinimumSize = new Vector2(0, RibbonDrop) });
		_subtitle = Chrome.Line("", 20, Chrome.Soft);
		_subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		Column.AddChild(_subtitle);

		Column.AddChild(Head());
		Column.AddChild(Readings());
		Column.AddChild(Heading("Unit roster"));
		Column.AddChild(Roster());
		Column.AddChild(Tally());

		// Whatever the frame has left over sits here, so the orders stay on its bottom rail.
		Column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		Column.AddChild(Orders());
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
		cell.AddChild(Chrome.Icon(icon, 40));

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
		var grid = new GridContainer { Columns = RosterColumns, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		grid.AddThemeConstantOverride("h_separation", 16);
		grid.AddThemeConstantOverride("v_separation", RosterGap);
		foreach (string kind in Units.All())
		{
			var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			row.AddThemeConstantOverride("separation", 10);
			row.CustomMinimumSize = new Vector2(0, PortraitTile.Y + 8);
			row.AddChild(UnitArt.Tile(kind, PortraitTile));
			Label name = Chrome.Line(Units.Of(kind).Name, 20, Chrome.Bright);
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			name.VerticalAlignment = VerticalAlignment.Center;
			if (Units.IsHired(kind))
			{
				// Said on the row itself, under the band's name: which of these men are hired is
				// the first thing a lord asks of a company he is paying a captain for.
				var named = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
				named.AddThemeConstantOverride("separation", 0);
				named.AddChild(name);
				var tag = new HBoxContainer();
				tag.AddThemeConstantOverride("separation", 6);
				tag.AddChild(Chrome.Icon("mercenaries", 18));
				tag.AddChild(Chrome.Line("Mercenaries", 14, Chrome.Cream));
				named.AddChild(tag);
				row.AddChild(named);
			}
			else
			{
				row.AddChild(name);
			}

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

	private Control Tally()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);

		var counted = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		counted.AddThemeConstantOverride("separation", 8);
		counted.AddChild(Chrome.Icon("men", 38));
		_total = Chrome.Line("", 22, Chrome.Bright);
		counted.AddChild(_total);
		row.AddChild(counted);

		var hired = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
		hired.AddThemeConstantOverride("separation", 8);
		hired.AddChild(Chrome.Icon("mercenaries", 36));
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

		_march = Chrome.Order("Move", "footsteps", () =>
		{
			FieldArmy marching = _army;
			Close();
			MarchPressed?.Invoke(marching);
		}, out Label _);
		row.AddChild(_march);

		_split = Chrome.Order("Split Army", "company", () =>
		{
			FieldArmy splitting = _army;
			Close();
			SplitPressed?.Invoke(splitting);
		}, out Label _);
		row.AddChild(_split);

		_garrison = Chrome.Order("Garrison", "castle", () =>
		{
			FieldArmy manning = _army;
			Close();
			GarrisonPressed?.Invoke(manning);
		}, out Label _);
		row.AddChild(_garrison);

		_disband = Chrome.Order("Disband", "morale", () =>
		{
			if (!_disbandAsked)
			{
				_disbandAsked = true;
				_disbandWord.Text = "Sure?";
				return;
			}

			FieldArmy sent = _army;
			Close();
			DisbandPressed?.Invoke(sent);
		}, out _disbandWord);
		row.AddChild(_disband);
		return row;
	}

	/// <summary>Offers the walls of the county the company is standing in, when the map says they
	/// will take it. Asked after Show, which does not know whose walls are whose.</summary>
	public void OfferWalls(bool open) => _garrison.Visible = open;

	/// <summary>Shows one company: these men, their own legs, and the county that pays them.
	/// <paramref name="yours"/> decides whether the lord may order them about from here — a rival's
	/// company is something to look at.</summary>
	public void Show(FieldArmy army, ProvinceEconomy county, GameBalance balance, string realm,
		string realmKey, bool yours)
	{
		_army = army;
		Title.Text = army.County.Length > 0 && army.County != army.Home
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

		int kinds = 0;
		float rowHigh = 0f;
		foreach ((string kind, (Control row, Label count)) in _roster)
		{
			int men = army.Men.GetValueOrDefault(kind);
			count.Text = men.ToString("N0");
			// A kind nobody in this company carries is not in it, and a roster is what is standing
			// there rather than a list of everything the realm knows how to raise.
			row.Visible = men > 0;
			kinds += men > 0 ? 1 : 0;
			rowHigh = row.GetCombinedMinimumSize().Y;
		}

		_men.Text = army.Strength.ToString("N0");
		_wages.Text = Mathf.CeilToInt(army.Strength * balance.WagePerSoldier).ToString("N0");
		_paces.Text = Mathf.RoundToInt(army.MarchLeft / balance.MarchCostByRoad).ToString("N0");
		_total.Text = $"{army.Strength:N0} men under arms";
		int hired = 0;
		foreach ((string kind, int men) in army.Men)
		{
			hired += Units.IsHired(kind) ? men : 0;
		}

		_mercenaries.Text = hired == 0 ? "No mercenaries in this company"
			: hired == army.Strength ? "A hired company, under its own banner"
			: $"{hired:N0} of them hired";

		_march.Visible = yours && army.Strength > 0 && army.MarchLeft > 0f;
		_split.Visible = yours && army.Strength > 1 && !army.IsHired;
		_garrison.Visible = false;
		_disband.Visible = yours;
		_disbandAsked = false;

		// Drawn to fit: the frame closes under however many rows this company fills, rather than
		// leaving a hand's depth of empty blue under a roster of three. Counted off the rows rather
		// than asked of the container, which is not told until the next frame that some are gone.
		// The rows that go take their share of the frame with them: what is cut is the rows themselves
		// plus the ribbon and rail that would have been drawn around them.
		// The art is laid for the county's own kinds; a hired band is a row past them.
		int laid = 0;
		foreach (string kind in _roster.Keys)
		{
			laid += Units.IsHired(kind) ? 0 : 1;
		}

		float gone = (Rows(laid) - Rows(kinds)) * (rowHigh + RosterGap) / Writable;
		Draw(PanelSize.Y - gone);

		_disbandWord.Text = "Disband";
		Reveal();
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

	/// <summary>How many rows that many kinds of man fill.</summary>
	private static int Rows(int kinds) => Mathf.CeilToInt(kinds / (float)RosterColumns);
}
