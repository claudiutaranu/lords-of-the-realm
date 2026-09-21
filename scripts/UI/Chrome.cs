using System;
using Godot;

/// <summary>The game's own handwriting: the colours it letters in, the frames it draws, and the
/// gold-edged plaque it hangs over anything you can press.
///
/// It is a static class and not a base one on purpose. A room you open over the map and the hall you
/// play from are different screens with different furniture, but a sign over a rack of swords and a
/// sign over the treasurer's table have to be the same object or the game looks assembled from two
/// kits. This is where that sameness lives, so neither screen has to inherit from the other to get
/// it.</summary>
public static class Chrome
{
	public const string IconDirectory = "res://assets/ui/icons";

	public static readonly Color Cream = new("d9cdb4");
	public static readonly Color Dim = new("8d8577");
	public static readonly Color Short = new("c9604e"); // a price the province cannot meet
	public static readonly Color Gain = new("7fa86a");  // what a turn will add

	// Panels are read over moving film, not over a still page. White carries where cream goes soft
	// against a bright frame.
	public static readonly Color Bright = Colors.White;
	public static readonly Color Soft = new(0.90f, 0.90f, 0.88f);

	private const string SquarePlatePath = "res://assets/ui/button-square.png";

	/// <summary>How long a held plate waits before it starts running, and how fast it runs after
	/// that. The pause is what keeps a click a click; without it, a press that lingers a frame too
	/// long moves two.</summary>
	private const double RepeatDelaySeconds = 0.35;
	private const double RepeatEverySeconds = 0.05;

	public static Label Line(string text, int size, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	public static TextureRect Icon(string name, int side) => new()
	{
		Texture = GD.Load<Texture2D>($"{IconDirectory}/{name}.png"),
		CustomMinimumSize = new Vector2(side, side),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	public static StyleBoxFlat CardStyle(Color background) => new()
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

	public static PanelContainer Framed(Control content, int margin)
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

	/// <summary>A hairline of the same gold the frames use, for a rule under a heading.</summary>
	public static Control Rule(int width) => new ColorRect
	{
		Color = new Color(0.549f, 0.447f, 0.271f, 0.55f),
		CustomMinimumSize = new Vector2(width, 1),
		SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	/// <summary>A small button wearing the square plate the rest of the game's icon buttons wear:
	/// the theme's own button is a gilded lozenge, which at this size reads as an ornament rather
	/// than as something to press.</summary>
	public static Button Plate(string text, int side, Action pressed = null)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(side, side),
			// Square, and square whatever it is standing next to. A button in a row fills the row's
			// height by default, so a plate beside a tall label or a taller button comes out as an
			// oblong — and two plates side by side only look square while nothing else shares their
			// line.
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
		button.AddThemeFontSizeOverride("font_size", 22);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxTexture
			{
				Texture = GD.Load<Texture2D>(SquarePlatePath),
				ModulateColor = state == "normal" ? Colors.White : new Color(1.3f, 1.2f, 1.05f),
			});
		}

		if (pressed != null)
		{
			button.Pressed += pressed;
		}

		return button;
	}

	/// <summary>A plate that keeps stepping while it is held down: once on the press, a pause so a
	/// single click is still a single step, then quickly for as long as the finger is on it. A
	/// stepper you have to click forty times to cross its range is one the player stops using —
	/// he settles for whatever number is near enough rather than the one he wanted.
	///
	/// Driven from the press and not from <c>Pressed</c>, which fires on release: wired the other
	/// way, every hold would end with one extra step the player did not ask for.</summary>
	public static Button Repeating(string text, int side, Action pressed)
	{
		Button button = Plate(text, side);
		var repeat = new Timer { WaitTime = RepeatDelaySeconds };
		button.AddChild(repeat);

		repeat.Timeout += () =>
		{
			// The pause is only ever before the FIRST repeat; from then on it runs at the fast rate.
			// Kept on the timer rather than in a flag beside it, because the timer is the only thing
			// that knows how long the button has been held.
			repeat.WaitTime = RepeatEverySeconds;
			pressed();
		};

		button.ButtonDown += () =>
		{
			pressed();
			repeat.Start(RepeatDelaySeconds);
		};

		button.ButtonUp += repeat.Stop;
		return button;
	}

	/// <summary>The plaque a sign is painted on: dark oak on the wall, or lit by the room when it is
	/// the one in hand. The lit one carries a gold edge and a warm glow; the rest sit back in the
	/// dark and lift a little under the cursor, so the wall answers the mouse.</summary>
	public static void DressPlaque(Button sign, bool lit)
	{
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

	/// <summary>Pins a sign over its own corner of the frame, by a fraction of it rather than by a
	/// container: the player is choosing off the room itself, not off a list beside it, so a sign
	/// stays where it was hung however the window is shaped. The plaque is cut to its own contents,
	/// because one width for all of them strands a short name's icon at the far edge.</summary>
	public static void Anchor(Control sign, Vector2 spot, float width, float height)
	{
		sign.AnchorLeft = sign.AnchorRight = spot.X;
		sign.AnchorTop = sign.AnchorBottom = spot.Y;
		sign.OffsetLeft = -width / 2f;
		sign.OffsetRight = width / 2f;
		sign.OffsetTop = -height / 2f;
		sign.OffsetBottom = height / 2f;
	}

	/// <summary>Lays a control over the whole of its parent. Not the same as the anchor preset on its
	/// own: that one recomputes the offsets to keep the control exactly where it already is, and a
	/// child added to a button before the button has been given a size is sitting at nothing by
	/// nothing in the corner. Setting the four offsets afterwards is what actually fills the
	/// parent.</summary>
	public static void Fill(Control child)
	{
		child.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		child.OffsetLeft = 0;
		child.OffsetTop = 0;
		child.OffsetRight = 0;
		child.OffsetBottom = 0;
	}
}
