using System;
using System.Collections.Generic;
using Godot;

/// <summary>A room of a province, opened over the campaign map: the smithy, the training yard, the
/// market. They differ in what they are for — one takes an order and makes you wait for it, another
/// trades across a counter on the spot — but they are all the same room seen from the door: film
/// underneath, the province's stores along the top, a title, signs hung over what the room offers,
/// and a panel in the corner reading whatever is chosen.
///
/// That frame is all this class is. What the room does with a choice is the page's own business.
///
/// It opens over the map rather than replacing it, so the turn, the camera and the selection are
/// exactly where they were when it closes.</summary>
public abstract partial class RoomPage : Control
{
	// The game's handwriting is Chrome's, so the hall and the rooms are lettered and framed alike.
	// These are here only so a page can reach them without naming Chrome on every line.
	protected const string IconDirectory = Chrome.IconDirectory;

	protected static readonly Color Cream = Chrome.Cream;
	protected static readonly Color Dim = Chrome.Dim;
	protected static readonly Color Short = Chrome.Short;
	protected static readonly Color Bright = Chrome.Bright;
	protected static readonly Color Soft = Chrome.Soft;

	/// <summary>Raised when the player shuts the room, so the map can drop this page and refresh
	/// whatever happened in here.</summary>
	public event Action Closed;

	protected ProvinceEconomy Province { get; private set; }

	/// <summary>The row across the foot of the page: whatever a room lays its choices out in goes
	/// here, to the left of the panel that reads them.</summary>
	protected HBoxContainer Body { get; private set; }

	/// <summary>The column inside the panel in the corner. A page empties it and fills it again
	/// whenever what it reads has changed.</summary>
	protected VBoxContainer Detail { get; private set; }

	private readonly Dictionary<string, Button> _signs = new();
	/// <summary>True between a slider's grab and its release. A page rebuilds its panel from scratch
	/// whenever the number changes, which would free the slider under the hand still dragging it, so
	/// while that hand is down only the reading moves and the rebuild waits for the release.</summary>
	private bool _dragging;
	private ResourceBar _stores;
	private Label _provinceLabel;

	// --- what each room says for itself --------------------------------------------------------

	protected abstract string RoomName { get; }

	/// <summary>A line under the room's name, where it has one.</summary>
	protected virtual string Tagline => null;

	/// <summary>Where each sign hangs, in the video frame's own proportions — over the thing it
	/// names. Read off a still of the film rather than laid out by a container: the player is
	/// choosing off the room itself, not off a list beside it.</summary>
	protected abstract Dictionary<string, Vector2> SignSpots { get; }

	/// <summary>Reads whatever the room offers, before anything is built from it.</summary>
	protected abstract void Load();

	/// <summary>How the room offers its choices. A wall of signs, a row of cards — each room says.</summary>
	protected abstract void BuildChoosers();

	/// <summary>Told the room has just been opened, so it can settle on a first choice.</summary>
	protected abstract void Opened();

	/// <summary>Fills the panel in the corner with whatever is chosen now.</summary>
	protected abstract void ShowDetail();

	// --- the page ------------------------------------------------------------------------------

	public override void _Ready()
	{
		// A few seconds of the room, played back to back: it should never stop moving. Not every
		// room is filmed, though — a view over a valley is a painting, and holds still — so a scene
		// that puts something other than a player behind itself is left alone.
		var background = GetNodeOrNull<VideoStreamPlayer>("Background");
		if (background != null)
		{
			background.Finished += background.Play;
		}

		Load();
		BuildChrome();
	}

	/// <summary>Escape shuts the room, the same as the button in the corner. The room is the last
	/// thing added over the map, so it is offered the key first and swallows it — the map behind
	/// never sees the press that closed what was on top of it.</summary>
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Closed?.Invoke();
		}
	}

	/// <summary>Opens the room for one province. Everything on the page is read against it.</summary>
	public void Open(ProvinceEconomy province)
	{
		Province = province;
		_provinceLabel.Text = province.ProvinceName;
		Opened();
		Refresh();
	}

	/// <summary>Re-reads the province: its stores along the top, and the panel in the corner.</summary>
	public virtual void Refresh()
	{
		_stores.Show(Province);
		ShowDetail();
	}

	/// <summary>Shuts the room from inside it, for a page that has its own way out.</summary>
	protected void Close() => Closed?.Invoke();

	private void BuildChrome()
	{
		// The video underneath is a room, not a backdrop: the wash is only enough to stop the title
		// dissolving into it. The panels carry their own dark, so this stays light.
		var wash = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.14f) };
		wash.SetAnchorsPreset(LayoutPreset.FullRect);
		wash.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(wash);

		// A shadow down from the top edge, under the stores and the title. Some rooms open onto a
		// dark wall and never needed it; the market opens onto a sunlit archway, and gilt lettering
		// on white sky is lettering nobody can read. It fades out before the room does.
		var scrim = new TextureRect
		{
			Texture = TopShadow(),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		scrim.SetAnchorsPreset(LayoutPreset.TopWide);
		scrim.AnchorBottom = 0.36f;
		AddChild(scrim);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "left", "right", "bottom" })
		{
			margin.AddThemeConstantOverride($"margin_{side}", 28);
		}

		margin.AddThemeConstantOverride("margin_top", 10);

		AddChild(margin);

		var page = new VBoxContainer();
		page.AddThemeConstantOverride("separation", 4);
		margin.AddChild(page);

		page.AddChild(BuildTopRow());
		page.AddChild(BuildTitle());

		var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 20);
		body.Alignment = BoxContainer.AlignmentMode.End;
		page.AddChild(body);

		Body = body;
		body.AddChild(BuildDetailPanel());
		HangCloseButton();
		BuildChoosers();
	}

	/// <summary>The shared stockpile strip, centred over the page: the same component every scene
	/// that spends a province's goods puts at the top, so every room reads alike along the top.</summary>
	private Control BuildTopRow()
	{
		var centre = new CenterContainer();
		_stores = new ResourceBar();
		centre.AddChild(_stores);
		return centre;
	}

	/// <summary>The way out, in the corner it is always in — anchored to the page rather than laid
	/// out with the stores, so centring them cannot push it around.</summary>
	private void HangCloseButton()
	{
		var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(52, 52) };
		close.AddThemeFontSizeOverride("font_size", 22);
		close.Pressed += () => Closed?.Invoke();
		AddChild(close);

		close.SetAnchorsPreset(LayoutPreset.TopRight);
		close.OffsetLeft = -80;
		close.OffsetTop = 12;
		close.OffsetRight = -28;
		close.OffsetBottom = 80;
	}

	private Control BuildTitle()
	{
		var titles = new VBoxContainer();
		titles.AddThemeConstantOverride("separation", 2);

		var name = new Label
		{
			Text = RoomName.ToUpperInvariant(),
			HorizontalAlignment = HorizontalAlignment.Center,
			ThemeTypeVariation = "GildedTitle",
		};
		name.AddThemeFontSizeOverride("font_size", 58);
		GoldTitle.Apply(name);
		titles.AddChild(name);

		// A rule with a diamond on it, the way the rest of the game separates a heading from what
		// follows it.
		var rule = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		rule.AddThemeConstantOverride("separation", 10);
		rule.AddChild(Chrome.Rule(150));
		rule.AddChild(Line("◆", 12, new Color(0.72f, 0.60f, 0.36f)));
		rule.AddChild(Chrome.Rule(150));
		titles.AddChild(rule);

		// White, both of them. These sit on the film itself with no panel under them, and the rooms
		// open onto anything from a dark forge to a noon sky over a valley — cream reads on the
		// first and vanishes on the second.
		if (Tagline != null)
		{
			Label tagline = Line(Tagline, 19, Bright);
			tagline.HorizontalAlignment = HorizontalAlignment.Center;
			titles.AddChild(tagline);
		}

		_provinceLabel = Line("", 15, Soft);
		_provinceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titles.AddChild(_provinceLabel);
		return titles;
	}

	/// <summary>The shadow that sits under the title: opaque enough at the very top to hold the
	/// stockpile strip, gone by the bottom so the room is not curtained off.</summary>
	private static GradientTexture2D TopShadow()
	{
		var shades = new Gradient();
		shades.SetOffset(0, 0f);
		shades.SetColor(0, new Color(0f, 0f, 0f, 0.62f));
		shades.SetOffset(1, 1f);
		shades.SetColor(1, new Color(0f, 0f, 0f, 0f));

		return new GradientTexture2D
		{
			Gradient = shades,
			Width = 2,
			Height = 256,
			FillFrom = new Vector2(0, 0),
			FillTo = new Vector2(0, 1),
		};
	}

	/// <summary>Hangs one sign in the room, the way a smith labels his own wall. Signs are children
	/// of the page rather than of a container, anchored by the fractions in SignSpots, so each stays
	/// over its own corner however the window is shaped. A key with no spot picked for it yet hangs
	/// nowhere rather than landing in the middle of the room.</summary>
	protected Button HangSign(string key, string name, string icon, string blurb, Action pressed)
	{
		if (!SignSpots.TryGetValue(key, out Vector2 spot))
		{
			return null;
		}

		// The button's own icon and label, not a container laid inside it: a Button centres those
		// itself, where a child container has to be given a size and quietly sat in the corner when
		// it was not.
		var sign = new Button
		{
			TooltipText = blurb,
			Text = name.ToUpperInvariant(),
			Icon = GD.Load<Texture2D>($"{IconDirectory}/{icon}.png"),
			Alignment = HorizontalAlignment.Center,
			IconAlignment = HorizontalAlignment.Left,
			ExpandIcon = false,
		};
		sign.AddThemeFontSizeOverride("font_size", 20);
		sign.AddThemeColorOverride("font_color", Cream);
		sign.AddThemeColorOverride("font_hover_color", new Color(1f, 0.92f, 0.72f));
		sign.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.92f, 0.72f));
		sign.AddThemeConstantOverride("h_separation", 12);
		sign.AddThemeConstantOverride("icon_max_width", 34);
		sign.Pressed += pressed;
		AddChild(sign);

		_signs[key] = sign;
		DressSign(key, lit: false);

		// Measured after it is dressed, so the frame and its padding are counted into the width.
		Chrome.Anchor(sign, spot, sign.GetCombinedMinimumSize().X, 64);
		return sign;
	}

	/// <summary>Lights one sign and puts every other one back on the wall. A null key lights none.</summary>
	protected void LightSign(string key)
	{
		foreach (string hanging in _signs.Keys)
		{
			DressSign(hanging, hanging == key);
		}
	}

	private void DressSign(string key, bool lit) => Chrome.DressPlaque(_signs[key], lit);

	private Control BuildDetailPanel()
	{
		Detail = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
		Detail.AddThemeConstantOverride("separation", 10);

		var holder = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd };
		holder.AddThemeStyleboxOverride("panel", CardStyle(new Color(0.05f, 0.045f, 0.04f, 0.92f)));

		var inset = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", 16);
		}

		holder.AddChild(inset);
		inset.AddChild(Detail);
		return holder;
	}

	/// <summary>Empties the panel in the corner, for a page about to fill it again.</summary>
	protected void ClearDetail()
	{
		foreach (Node child in Detail.GetChildren())
		{
			child.QueueFree();
		}
	}

	/// <summary>A number the player sets: the plates either side, the reading between them, and a
	/// slider running to what the room will allow — men the province can raise, sacks the purse can
	/// pay for. <paramref name="settled"/> is called with the new number once it has stopped moving,
	/// and a page rebuilds its panel from there.</summary>
	protected Control Stepper(string label, int value, int step, int ceiling, Action<int> settled)
	{
		ceiling = Mathf.Max(step, ceiling);
		value = Mathf.Clamp(value, step, ceiling);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		if (label != null)
		{
			Label name = Line(label, 16, Soft);
			name.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(name);
		}

		int current = value;
		int bound = ceiling;
		row.AddChild(Plate("−", 44, () => settled(Mathf.Clamp(current - step, step, bound))));

		Label reading = Line(value.ToString("N0"), 23, Bright);
		reading.HorizontalAlignment = HorizontalAlignment.Center;
		reading.CustomMinimumSize = new Vector2(96, 0);
		reading.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(reading);

		row.AddChild(Plate("+", 44, () => settled(Mathf.Clamp(current + step, step, bound))));

		var slider = new HSlider
		{
			MinValue = step,
			MaxValue = ceiling,
			Step = step,
			Value = value,
			Editable = ceiling > step,
			TooltipText = $"Up to {ceiling:N0}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(130, 0),
		};

		slider.DragStarted += () => _dragging = true;
		slider.DragEnded += _ =>
		{
			_dragging = false;
			settled((int)slider.Value);
		};

		slider.ValueChanged += moved =>
		{
			reading.Text = ((int)moved).ToString("N0");

			// A wheel or an arrow key moves it without ever grabbing it, and those have to settle
			// the panel themselves.
			if (!_dragging)
			{
				settled((int)moved);
			}
		};

		row.AddChild(slider);
		return row;
	}

	// --- small parts ---------------------------------------------------------------------------

	protected static PanelContainer Framed(Control content, int margin) => Chrome.Framed(content, margin);

	protected static StyleBoxFlat CardStyle(Color background) => Chrome.CardStyle(background);

	protected static Label Line(string text, int size, Color color) => Chrome.Line(text, size, color);

	protected static TextureRect Icon(string name, int side) => Chrome.Icon(name, side);

	protected static Button Plate(string text, int side, Action pressed) => Chrome.Plate(text, side, pressed);
}
