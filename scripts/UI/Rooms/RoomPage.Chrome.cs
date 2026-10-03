using Godot;

/// <summary>The room's frame, built once for every room alike: the top row, the close button, the
/// title on its ribbon, the shadow under it, and the detail panel.</summary>
public partial class RoomPage
{
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

		// The chrome is laid out over the whole page, so every box it is laid out in would otherwise
		// swallow presses meant for what the room has painted underneath — a building on the land,
		// a sign on a wall. The boxes let presses through; what is actually drawn in them, the
		// panels and the buttons, still takes its own.
		var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "left", "right", "bottom" })
		{
			margin.AddThemeConstantOverride($"margin_{side}", 28);
		}

		margin.AddThemeConstantOverride("margin_top", 10);

		AddChild(margin);

		var page = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		page.AddThemeConstantOverride("separation", 4);
		margin.AddChild(page);

		page.AddChild(BuildTopRow());

		// Built either way: the province's own name lives inside it, and a page that hides the block
		// still expects to be able to set that. A hidden control takes no room in the column.
		Control titles = BuildTitle();
		titles.Visible = ShowsTitle;
		page.AddChild(titles);

		var body = new HBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		body.AddThemeConstantOverride("separation", 20);
		body.Alignment = BoxContainer.AlignmentMode.End;
		page.AddChild(body);

		Body = body;
		body.AddChild(BuildDetailPanel());
		BuildChoosers();

		// Last, so it stands over whatever a room lays over its page — a room that brings its panels
		// to the front must not bury the way out under them.
		HangCloseButton();
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
		// On the square plate every icon button in the game wears.
		Button close = Chrome.Plate("✕", 52, () => Closed?.Invoke());
		AddChild(close);

		close.SetAnchorsPreset(LayoutPreset.TopRight);
		close.OffsetLeft = -80;
		close.OffsetTop = 12;
		close.OffsetRight = -28;
		close.OffsetBottom = 64;
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

	private Control BuildDetailPanel()
	{
		Detail = new VBoxContainer { CustomMinimumSize = new Vector2(DetailWidth, 0) };
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
}
