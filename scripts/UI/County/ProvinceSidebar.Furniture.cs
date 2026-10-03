using Godot;

/// <summary>The small joinery every part of the sidebar is built from: frames, tile and group
/// styles, dividers, icons and the small print.</summary>
public partial class ProvinceSidebar
{
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
}
