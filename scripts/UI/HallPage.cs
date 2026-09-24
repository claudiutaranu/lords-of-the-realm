using System;
using System.Collections.Generic;
using Godot;

/// <summary>The hall of lords: the room the realm is run from, rather than a room you step into and
/// back out of. The smithy, the yard and the market are all somewhere you go; this is where you are.
///
/// That is why it is not a RoomPage. It carries no panel in the corner and no way out, because
/// there is nowhere behind it to go back to; what it carries instead is the realm's whole ledger
/// along the top, the season it stands in, and the door to every other screen hung on its walls.
///
/// The signs are the same plaques the rooms hang, and for the same reason: a player should not be
/// able to tell which screen a sign was drawn for.</summary>
public partial class HallPage : Control
{
	/// <summary>A door out of the hall: where it hangs, what it is called and the icon on it. The
	/// key is what the hall reports when it is pressed, so the campaign decides what opens.</summary>
	private record Door(string Key, string Name, string Icon, Vector2 Spot, string Blurb = null);

	// Read off the hall itself: each sign hangs over whoever answers for it — diplomacy over the
	// envoys at the door, the treasury over the clerk at his table.
	private static readonly Door[] Doors =
	{
		new("diplomacy", "Diplomacy", "scales", new Vector2(0.145f, 0.295f)),
		new("army", "Army", "sword", new Vector2(0.695f, 0.282f)),
		// ponytail: shield stands in for the spies — the icon set has no eye or hooded figure yet.
		new("spies", "Spy Network", "shield", new Vector2(0.913f, 0.286f)),
		new("treasury", "Treasury", "gold", new Vector2(0.900f, 0.515f)),
		new("provinces", "Province Affairs", "scroll", new Vector2(0.471f, 0.745f),
			"Manage your lands, develop provinces,\nand strengthen your realm."),
	};

	private const string LogoPath = "res://assets/ui/logo.png";

	private const string Motto = "\"A realm is more than land.\nIt is the sum of those who stand with you.\"";

	/// <summary>Raised with a door's key when the player presses a sign, so the campaign decides
	/// what that opens. The hall knows the doors are there; it does not know what is behind them.</summary>
	public event Action<string> DoorChosen;

	/// <summary>Raised when the player ends the turn from here.</summary>
	public event Action TurnEnded;

	/// <summary>Raised when the player leaves the hall.</summary>
	public event Action Closed;

	private TurnManager _turns;
	private ResourceBar _stores;
	private Label _season;
	private Label _turn;

	public override void _Ready()
	{
		// A few seconds of the hall, played back to back: it should never stop moving.
		var background = GetNode<VideoStreamPlayer>("Background");
		background.Finished += background.Play;

		BuildChrome();
	}

	/// <summary>Escape leaves the hall, for as long as there is still somewhere behind it.</summary>
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Closed?.Invoke();
		}
	}

	/// <summary>Opens the hall on a campaign. Everything on it is read off the realm, not off one
	/// province: this is the crown's view.</summary>
	public void Open(TurnManager turns)
	{
		_turns = turns;
		Refresh();
	}

	/// <summary>Re-reads the realm. Call it again whenever a turn or a trade has moved anything.</summary>
	public void Refresh()
	{
		if (_turns == null)
		{
			return;
		}

		// The realm as one purse: there is no shared treasury, so the strip adds the provinces up.
		_stores.Show(_turns.RealmStore);
		_season.Text = _turns.CurrentSeason.ToString();
		_turn.Text = $"Turn {_turns.Turn}";
	}

	// --- the hall ------------------------------------------------------------------------------

	private void BuildChrome()
	{
		// A shadow down from the top edge and up from the bottom, so the ledger and the motto have
		// something to sit on whatever the film is doing behind them.
		AddChild(Scrim(LayoutPreset.TopWide, 0.22f, up: false));
		AddChild(Scrim(LayoutPreset.BottomWide, 0.22f, up: true));

		BuildLogo();
		BuildStores();
		HangCloseButton();
		BuildDoors();
		BuildMotto();
		BuildFoot();
	}

	private Control Scrim(LayoutPreset edge, float depth, bool up)
	{
		var shades = new Gradient();
		shades.SetOffset(0, 0f);
		shades.SetColor(0, new Color(0f, 0f, 0f, up ? 0f : 0.68f));
		shades.SetOffset(1, 1f);
		shades.SetColor(1, new Color(0f, 0f, 0f, up ? 0.68f : 0f));

		var scrim = new TextureRect
		{
			Texture = new GradientTexture2D
			{
				Gradient = shades,
				Width = 2,
				Height = 256,
				FillFrom = new Vector2(0, 0),
				FillTo = new Vector2(0, 1),
			},
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};

		scrim.SetAnchorsPreset(edge);
		if (up)
		{
			scrim.AnchorTop = 1f - depth;
		}
		else
		{
			scrim.AnchorBottom = depth;
		}

		return scrim;
	}

	/// <summary>The game's own logo in the corner, and under it the season the realm stands in.
	/// It is the mark the main menu opens on, not a title set in type: one wordmark, drawn once.</summary>
	private void BuildLogo()
	{
		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 6);

		stack.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(LogoPath),
			CustomMinimumSize = new Vector2(196, 172),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		});

		var calendar = new HBoxContainer();
		calendar.AddThemeConstantOverride("separation", 10);
		// ponytail: laurel stands in for the season — there is no weather icon set yet, so winter
		// and high summer wear the same wreath.
		calendar.AddChild(Chrome.Icon("laurel", 26));

		var when = new VBoxContainer();
		when.AddThemeConstantOverride("separation", 0);
		when.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		_season = Chrome.Line("", 16, Chrome.Bright);
		when.AddChild(_season);
		_turn = Chrome.Line("", 11, Chrome.Dim);
		when.AddChild(_turn);
		calendar.AddChild(when);

		var centred = new CenterContainer();
		centred.AddChild(Chrome.Framed(calendar, 10));
		stack.AddChild(centred);

		// All four offsets, not two: an anchor preset leaves the others where they were, and a box
		// given only a left edge keeps whatever width it happened to have.
		AddChild(stack);
		stack.SetAnchorsPreset(LayoutPreset.TopLeft);
		stack.OffsetLeft = 24;
		stack.OffsetRight = 220;
		stack.OffsetTop = 8;
		stack.OffsetBottom = 250;
	}

	/// <summary>The same stockpile strip every other screen wears, centred over the page — the hall
	/// reads the realm rather than one province, but a player should not have to learn a second
	/// way of reading the same six numbers.</summary>
	private void BuildStores()
	{
		_stores = new ResourceBar();

		var centre = new CenterContainer();
		centre.AddChild(_stores);
		AddChild(centre);

		centre.SetAnchorsPreset(LayoutPreset.TopWide);
		centre.OffsetLeft = 0;
		centre.OffsetRight = 0;
		centre.OffsetTop = 10;
		centre.OffsetBottom = 90;
	}

	/// <summary>The way out, in the corner every other screen keeps it in.</summary>
	private void HangCloseButton()
	{
		Button close = Chrome.Plate("✕", 52);
		close.AddThemeFontSizeOverride("font_size", 22);
		close.Pressed += () => Closed?.Invoke();
		AddChild(close);

		close.SetAnchorsPreset(LayoutPreset.TopRight);
		close.OffsetLeft = -80;
		close.OffsetTop = 12;
		close.OffsetRight = -28;
		close.OffsetBottom = 64;
	}

	/// <summary>The doors out, hung over whoever answers for each of them.</summary>
	private void BuildDoors()
	{
		foreach (Door door in Doors)
		{
			Door chosen = door;
			var sign = new Button { TooltipText = door.Blurb?.Replace("\n", " ") };
			sign.Pressed += () => DoorChosen?.Invoke(chosen.Key);
			AddChild(sign);

			var plate = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			plate.AddThemeConstantOverride("separation", 12);
			plate.Alignment = BoxContainer.AlignmentMode.Center;
			sign.AddChild(plate);
			Chrome.Fill(plate);

			TextureRect glyph = Chrome.Icon(door.Icon, door.Blurb == null ? 28 : 34);
			glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			plate.AddChild(glyph);

			var words = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			words.AddThemeConstantOverride("separation", 2);
			words.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			Label name = Chrome.Line(door.Name, door.Blurb == null ? 20 : 22, Chrome.Cream);
			name.VerticalAlignment = VerticalAlignment.Center;
			// Centred over the line under it rather than ranged left: a short title left-aligned
			// over a long one reads as a mistake.
			name.HorizontalAlignment = HorizontalAlignment.Center;
			words.AddChild(name);

			if (door.Blurb != null)
			{
				Label blurb = Chrome.Line(door.Blurb, 13, Chrome.Dim);
				blurb.HorizontalAlignment = HorizontalAlignment.Center;
				words.AddChild(blurb);
			}

			plate.AddChild(words);

			Chrome.DressPlaque(sign, lit: false);
			Chrome.Anchor(sign, door.Spot, plate.GetCombinedMinimumSize().X + 40,
				door.Blurb == null ? 56 : 84);
		}
	}

	/// <summary>A chronicler's line in the corner, because a hall of lords should say something
	/// about itself while you decide what to do in it.</summary>
	private void BuildMotto()
	{
		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 2);

		Label quote = Chrome.Line(Motto, 15, Chrome.Soft);
		stack.AddChild(quote);
		stack.AddChild(Chrome.Line("— Unknown Chronicler", 12, Chrome.Dim));

		AddChild(stack);
		stack.SetAnchorsPreset(LayoutPreset.BottomLeft);
		stack.OffsetLeft = 34;
		stack.OffsetTop = -96;
		stack.OffsetBottom = -28;
	}

	/// <summary>The foot of the hall: the two small keys, and the one button that moves the game on.</summary>
	private void BuildFoot()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		row.Alignment = BoxContainer.AlignmentMode.End;

		row.AddChild(Chrome.Plate("⚙", 44, () => DoorChosen?.Invoke("settings")));
		row.AddChild(Chrome.Plate("?", 44, () => DoorChosen?.Invoke("help")));

		var end = new Button { Text = "End Turn  ›", CustomMinimumSize = new Vector2(230, 54) };
		end.AddThemeFontSizeOverride("font_size", 20);
		end.Pressed += () => TurnEnded?.Invoke();
		row.AddChild(end);

		AddChild(row);
		row.SetAnchorsPreset(LayoutPreset.BottomRight);
		row.OffsetLeft = -330;
		row.OffsetTop = -82;
		row.OffsetRight = -34;
		row.OffsetBottom = -28;
	}
}
