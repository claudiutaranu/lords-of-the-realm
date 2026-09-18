using System;
using Godot;

/// <summary>The card a soldier is shown on: his name across the top and his picture under it,
/// standing on the ground he is drilled on, inside a gilded frame.
///
/// One builder for both places that show him — the sidebar's muster, where the footer is how many
/// stand in the garrison, and the yard's roster, where it is what an intake of him costs. The
/// caller adds its own footer to the stack it gets back.</summary>
public static class UnitCard
{
	/// <summary>Builds the frame and returns the column inside it, for the caller to finish.</summary>
	public static (Control Tile, VBoxContainer Stack) Build(string unit, string name, int height,
		bool titled, Action pressed = null)
	{
		// A card you can pick is a button wearing the card's frame; one you only read is a panel.
		Control tile = pressed == null ? new PanelContainer() : new Button();
		tile.CustomMinimumSize = new Vector2(0, height);
		tile.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		tile.TooltipText = name;
		if (pressed == null)
		{
			tile.AddThemeStyleboxOverride("panel", Frame());
		}
		else
		{
			((Button)tile).Pressed += pressed;
			foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
			{
				tile.AddThemeStyleboxOverride(state, Frame());
			}
		}

		var pad = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			pad.AddThemeConstantOverride($"margin_{side}", 5);
		}

		tile.AddChild(pad);
		if (pressed != null)
		{
			pad.MouseFilter = Control.MouseFilterEnum.Ignore;
		}


		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 4);
		pad.AddChild(stack);

		if (titled)
		{
			var title = new Label
			{
				Text = name.ToUpper(),
				HorizontalAlignment = HorizontalAlignment.Center,
				TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			};
			title.AddThemeFontSizeOverride("font_size", 12);
			title.AddThemeColorOverride("font_color", new Color("d9cdb4"));
			stack.AddChild(title);
		}

		// The picture has a frame of its own inside the card's. A PanelContainer lays every child over
		// the same rect, so the ground goes in first and the man stands on it.
		var picture = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, ClipContents = true };
		picture.AddThemeStyleboxOverride("panel", Mount());
		stack.AddChild(picture);

		picture.AddChild(Fill(UnitArt.Backdrop(unit)));
		picture.AddChild(Fill(UnitArt.Portrait(unit)));
		return (tile, stack);
	}

	private static TextureRect Fill(string path) => new()
	{
		Texture = GD.Load<Texture2D>(path),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
	};

	private static StyleBoxFlat Frame() => new()
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

	/// <summary>The thinner mount the picture sits in, dark enough that a portrait's sky does not run
	/// into the frame.</summary>
	private static StyleBoxFlat Mount() => new()
	{
		BgColor = new Color(0.05f, 0.05f, 0.06f, 1f),
		BorderWidthLeft = 1,
		BorderWidthTop = 1,
		BorderWidthRight = 1,
		BorderWidthBottom = 1,
		BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.55f),
		ContentMarginLeft = 0,
		ContentMarginTop = 0,
		ContentMarginRight = 0,
		ContentMarginBottom = 0,
	};
}
