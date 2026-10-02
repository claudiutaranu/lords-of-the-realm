using Godot;

/// <summary>The diplomacy table's own furniture, cut from the painting it was designed in
/// (assets/ui/diplomacy): the lord's banners in the corners, dyed his colour, the gilt frame he looks
/// out of, the knotted rule, the letter tiles, and the bar his regard is read off.</summary>
public static partial class DiplomacyArt
{
	private const string Directory = "res://assets/ui/diplomacy";

	/// <summary>How much of a tile's painting is its gilt corner, which the nine-patch keeps whole.</summary>
	private const int TileCorner = 30;

	/// <summary>The banner painting's own shape, pole and all.</summary>
	private const float BannerAspect = 770f / 1497f;

	/// <summary>How far in from the frame's edge the face starts, at the frame's own size.</summary>
	private const float FrameInset = 0.06f;

	/// <summary>A lord's banner on its pole, for a top corner of the table: the cloth dyed his colour,
	/// the pole, the fringe and the lion left gold. The right-hand one is the left's mirror.</summary>
	public static Control Banner(bool isRight, float high, Color lord)
	{
		var banner = new Control
		{
			CustomMinimumSize = new Vector2(high * BannerAspect, high),
			SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		foreach ((string layer, Color tint) in new[] { ("cloth", lord), ("trim", Colors.White) })
		{
			var part = new TextureRect
			{
				Texture = GD.Load<Texture2D>($"{Directory}/banner-{layer}.png"),
				FlipH = isRight,
				Modulate = tint,
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspect,
				MouseFilter = Control.MouseFilterEnum.Ignore,
			};
			part.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			banner.AddChild(part);
		}

		return banner;
	}

	public static TextureRect Rule() => new()
	{
		Texture = GD.Load<Texture2D>($"{Directory}/rule.png"),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.Scale,
		CustomMinimumSize = new Vector2(0, 18),
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	/// <summary>The lord's face inside the gilt frame, fleur-de-lis over him and the lion under.</summary>
	public static Control Framed(Control face, int side)
	{
		var holder = new Control { CustomMinimumSize = new Vector2(side, side), MouseFilter = Control.MouseFilterEnum.Ignore };
		if (face != null)
		{
			float inset = side * FrameInset;
			// The least size first: a size set under the old least is pushed straight back up to it.
			face.CustomMinimumSize = new Vector2(side - (2 * inset), side - (2 * inset));
			face.Size = face.CustomMinimumSize;
			face.Position = new Vector2(inset, inset);
			holder.AddChild(face);
		}

		var frame = new TextureRect
		{
			Texture = GD.Load<Texture2D>($"{Directory}/portrait-frame.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			Size = new Vector2(side, side),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		holder.AddChild(frame);
		return holder;
	}

	/// <summary>One letter on its tile: the sign of it over its name. A tile that cannot be sent this
	/// season stands greyed, with why.</summary>
	public static Button Tile(string text, string icon, System.Action pressed)
	{
		var tile = new Button { CustomMinimumSize = new Vector2(0, 132), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipContents = true };
		var cloth = GD.Load<Texture2D>($"{Directory}/tile.png");
		foreach ((string state, Color tint) in new[]
		{
			("normal", Colors.White),
			("focus", Colors.White),
			("hover", new Color(1.25f, 1.18f, 1.05f)),
			("pressed", new Color(0.8f, 0.78f, 0.74f)),
			("disabled", new Color(0.55f, 0.55f, 0.55f)),
		})
		{
			var face = new StyleBoxTexture { Texture = cloth, ModulateColor = tint };
			face.SetTextureMarginAll(TileCorner);
			tile.AddThemeStyleboxOverride(state, face);
		}

		tile.Pressed += pressed;

		var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
		stack.AddThemeConstantOverride("separation", 6);
		stack.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		TextureRect sign = Chrome.Icon(icon, 46);
		sign.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		stack.AddChild(sign);
		Label name = Chrome.Line(text, 20, Chrome.Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(name);
		tile.AddChild(stack);
		return tile;
	}

	/// <summary>Hangs a small control at the foot of a tile, inside it.</summary>
	public static void Under(Button tile, Control part)
	{
		part.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
		tile.GetChild(0).AddChild(part);
	}

	/// <summary>A lord's regard on the original's −30..+30, in ten cells that fill from the middle: to
	/// the right in green for goodwill, to the left in red for ill will.</summary>
	public partial class StandingBar : Control
	{
		private const int Cells = 10;

		public int Standing;
		public int Most = 30;

		public override void _Draw()
		{
			var track = new Rect2(Vector2.Zero, Size);
			DrawRect(track, new Color(0f, 0f, 0f, 0.55f));
			float middle = Size.X / 2f;
			float reach = middle * Mathf.Clamp(Mathf.Abs(Standing) / (float)Mathf.Max(1, Most), 0f, 1f);
			Color fill = Standing >= 0 ? Chrome.Gain : new Color("c8443c");
			DrawRect(new Rect2(Standing >= 0 ? middle : middle - reach, 2f, reach, Size.Y - 4f), fill);
			for (int cell = 1; cell < Cells; cell++)
			{
				float x = Size.X * cell / Cells;
				DrawLine(new Vector2(x, 2f), new Vector2(x, Size.Y - 2f), new Color(0f, 0f, 0f, 0.6f), 2f);
			}

			DrawLine(new Vector2(middle, -3f), new Vector2(middle, Size.Y + 3f), Chrome.Cream, 2f);
			DrawRect(track, new Color(0.78f, 0.62f, 0.32f, 0.9f), false, 1.5f);
		}
	}
}
