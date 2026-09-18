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
	protected const string IconDirectory = "res://assets/ui/icons";
	private const string SquarePlatePath = "res://assets/ui/button-square.png";

	protected static readonly Color Cream = new("d9cdb4");
	protected static readonly Color Dim = new("8d8577");
	protected static readonly Color Short = new("c9604e"); // a price the province cannot meet

	// The panel is read over moving film, not over a still page. White carries where the cream the
	// rest of the chrome is lettered in goes soft against a bright frame.
	protected static readonly Color Bright = Colors.White;
	protected static readonly Color Soft = new(0.90f, 0.90f, 0.88f);

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
		// A few seconds of the room, played back to back: it should never stop moving.
		var background = GetNode<VideoStreamPlayer>("Background");
		background.Finished += background.Play;

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
		rule.AddChild(Rule(150));
		rule.AddChild(Line("◆", 12, new Color(0.72f, 0.60f, 0.36f)));
		rule.AddChild(Rule(150));
		titles.AddChild(rule);

		if (Tagline != null)
		{
			Label tagline = Line(Tagline, 19, Cream);
			tagline.HorizontalAlignment = HorizontalAlignment.Center;
			titles.AddChild(tagline);
		}

		_provinceLabel = Line("", 15, Dim);
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

	/// <summary>A hairline of the same gold the frames use, for the rule under the title.</summary>
	private static Control Rule(int width) => new ColorRect
	{
		Color = new Color(0.549f, 0.447f, 0.271f, 0.55f),
		CustomMinimumSize = new Vector2(width, 1),
		SizeFlagsVertical = SizeFlags.ShrinkCenter,
		MouseFilter = MouseFilterEnum.Ignore,
	};

	/// <summary>Hangs one sign in the room, the way a smith labels his own wall. Signs are children
	/// of the page rather than of a container, anchored by the fractions in SignSpots, so each stays
	/// over its own corner however the window is shaped. A key with no spot picked for it yet hangs
	/// nowhere rather than landing in the middle of the room.</summary>
	protected void HangSign(string key, string name, string icon, string blurb, Action pressed)
	{
		if (!SignSpots.TryGetValue(key, out Vector2 spot))
		{
			return;
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

		sign.AnchorLeft = sign.AnchorRight = spot.X;
		sign.AnchorTop = sign.AnchorBottom = spot.Y;
		sign.OffsetTop = -32;
		sign.OffsetBottom = 32;

		_signs[key] = sign;
		DressSign(key, lit: false);

		// The plaque is cut to its own words rather than to one width for all of them: on a fixed
		// box a short name leaves the icon stranded at the far edge, a plaque away from what it
		// names. Measured after it is dressed, so the frame and its padding are counted in.
		float half = sign.GetCombinedMinimumSize().X / 2f;
		sign.OffsetLeft = -half;
		sign.OffsetRight = half;
	}

	/// <summary>Lights one sign and puts every other one back on the wall. A null key lights none.</summary>
	protected void LightSign(string key)
	{
		foreach (string hanging in _signs.Keys)
		{
			DressSign(hanging, hanging == key);
		}
	}

	/// <summary>A sign is either waiting on the wall or lit as the one in hand. The lit one carries a
	/// gold edge and a warm glow off the room; the rest sit back in the dark, and lift a little under
	/// the cursor so the wall answers the mouse.</summary>
	private void DressSign(string key, bool lit)
	{
		Button sign = _signs[key];

		StyleBoxFlat resting = CardStyle(lit
			? new Color(0.19f, 0.135f, 0.06f, 0.96f)   // lit by the room
			: new Color(0.085f, 0.065f, 0.048f, 0.92f)); // dark oak
		resting.BorderColor = lit ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.51f, 0.41f, 0.25f, 0.9f);
		resting.BorderWidthLeft = resting.BorderWidthTop = resting.BorderWidthRight = resting.BorderWidthBottom = lit ? 3 : 2;
		resting.CornerRadiusTopLeft = resting.CornerRadiusTopRight = 3;
		resting.CornerRadiusBottomLeft = resting.CornerRadiusBottomRight = 3;
		resting.ContentMarginLeft = resting.ContentMarginRight = 14;
		resting.ContentMarginTop = resting.ContentMarginBottom = 8;
		// Every plaque throws a shadow on the wall; the chosen one throws firelight instead.
		resting.ShadowColor = lit ? new Color(1f, 0.74f, 0.33f, 0.45f) : new Color(0f, 0f, 0f, 0.55f);
		resting.ShadowSize = lit ? 14 : 6;
		resting.ShadowOffset = lit ? Vector2.Zero : new Vector2(2, 3);

		StyleBoxFlat hovered = (StyleBoxFlat)resting.Duplicate();
		hovered.BgColor = lit ? new Color(0.27f, 0.2f, 0.09f, 0.96f) : new Color(0.10f, 0.088f, 0.075f, 0.92f);
		hovered.BorderColor = new Color(1f, 0.84f, 0.45f, lit ? 1f : 0.85f);

		sign.AddThemeStyleboxOverride("normal", resting);
		sign.AddThemeStyleboxOverride("focus", resting);
		sign.AddThemeStyleboxOverride("hover", hovered);
		sign.AddThemeStyleboxOverride("pressed", hovered);
	}

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

	// --- small parts ---------------------------------------------------------------------------

	protected static PanelContainer Framed(Control content, int margin)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", CardStyle(new Color(0.05f, 0.045f, 0.04f, 0.88f)));

		var inset = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", margin);
		}

		panel.AddChild(inset);
		inset.AddChild(content);
		return panel;
	}

	protected static StyleBoxFlat CardStyle(Color background) => new()
	{
		BgColor = background,
		BorderWidthLeft = 2,
		BorderWidthTop = 2,
		BorderWidthRight = 2,
		BorderWidthBottom = 2,
		BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.85f),
		CornerRadiusTopLeft = 4,
		CornerRadiusTopRight = 4,
		CornerRadiusBottomRight = 4,
		CornerRadiusBottomLeft = 4,
	};

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

	/// <summary>A small button wearing the square plate the rest of the game's icon buttons wear:
	/// the theme's own button is a gilded lozenge, which at this size reads as an ornament rather
	/// than as something to press.</summary>
	protected static Button Plate(string text, int side, Action pressed)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(side, side) };
		button.AddThemeFontSizeOverride("font_size", 22);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxTexture
			{
				Texture = GD.Load<Texture2D>(SquarePlatePath),
				ModulateColor = state == "normal" ? Colors.White : new Color(1.3f, 1.2f, 1.05f),
			});
		}

		button.Pressed += pressed;
		return button;
	}

	protected static Label Line(string text, int size, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	protected static TextureRect Icon(string name, int side) => new()
	{
		Texture = GD.Load<Texture2D>($"{IconDirectory}/{name}.png"),
		CustomMinimumSize = new Vector2(side, side),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
	};
}
