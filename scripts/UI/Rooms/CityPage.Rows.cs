using Godot;

/// <summary>One row of the province's list: the site's banner, its readings, and the figures drawn
/// under it.</summary>
public partial class CityPage
{
	/// <summary>How tall a painted banner stands on the land.</summary>
	private const float BannerHeight = 76f;

	/// <summary>How far across the land a site stands before its panel opens on the left instead.</summary>
	private const float DockLeftPast = 0.6f;

	/// <summary>One site's plaque: its name and its figures.
	///
	/// The figures sit inside the plate with the name rather than beside it on the grass. Anything
	/// painted straight onto a sunlit field is something nobody can read.</summary>
	private Control Row(Site site)
	{
		if (CityArt.HasPlaque(site.Key))
		{
			return Banner(site);
		}

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);

		var named = new Button
		{
			TooltipText = site.Blurb,
			FocusMode = FocusModeEnum.None,
		};

		// Every plaque takes its site in hand. Where there is a room behind it, the panel offers the
		// door — rather than the plaque being a door and a handle at once, which would mean pressing
		// the thing you want to give people to walks you out of the screen you are giving them on.
		string key = site.Key;
		named.Pressed += () => Choose(key);

		var plate = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		plate.AddThemeConstantOverride("separation", 10);
		named.AddChild(plate);
		Chrome.Fill(plate);

		TextureRect glyph = Icon(site.Icon, 26);
		glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		plate.AddChild(glyph);

		// Ground being put right says so, and says how many more seasons it has at the strength the
		// lord has on it. A dash where the figure goes is a field nobody can be spared for.
		int mending = site.Key == "field" && _definition != null
			? Husbandry.SeasonsToReclaim(Province, _balance)
			: 0;

		Label name = Line(mending == 0 ? site.Name : "Mending", 18, Cream);
		name.VerticalAlignment = VerticalAlignment.Center;
		plate.AddChild(name);

		// Ground being mended counts seasons, not people.
		if (mending != 0)
		{
			Label seasons = Line(mending > 0 ? mending.ToString() : "\u2014", 19, mending > 0 ? Cream : Short);
			seasons.VerticalAlignment = VerticalAlignment.Center;
			plate.AddChild(seasons);
		}
		else if (_definition != null && (Staffed(site) || site.Key == "idle"))
		{
			plate.AddChild(PlaqueFigures(site));
		}

		Chrome.DressPlaque(named, lit: site.Key == _chosen);
		named.CustomMinimumSize = new Vector2(plate.GetCombinedMinimumSize().X + 34, 46);
		row.AddChild(named);
		return row;
	}

	/// <summary>A site that has a painted banner: its name written on the banner's bar, and its figures
	/// hung under it on a dark strip of their own — the banner is gold on blue, and gold figures
	/// laid across it would be lost in it.</summary>
	private Control Banner(Site site)
	{
		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 2);

		var art = GD.Load<Texture2D>(CityArt.Plaque(site.Key));
		var banner = new TextureButton
		{
			TextureNormal = art,
			IgnoreTextureSize = true,
			StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(BannerHeight * art.GetWidth() / (float)art.GetHeight(), BannerHeight),
			TooltipText = site.Blurb,
			FocusMode = FocusModeEnum.None,
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			// The site in hand glows a little warmer than the rest.
			Modulate = site.Key == _chosen ? new Color(1.25f, 1.18f, 1.05f) : Colors.White,
		};
		string key = site.Key;
		banner.Pressed += () => Choose(key);

		// On the bar, clear of the shield on the left and the point on the right. The shield ends a
		// little past a quarter of the way along on every one of these banners, so one set of margins
		// fits them all.
		Label name = Line(site.Name.ToUpperInvariant(), 22, Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		name.VerticalAlignment = VerticalAlignment.Center;
		name.MouseFilter = MouseFilterEnum.Ignore;
		banner.AddChild(name);
		name.AnchorLeft = 0.30f;
		name.AnchorRight = 0.90f;
		name.AnchorTop = 0.30f;
		name.AnchorBottom = 0.76f;
		GoldTitle.Apply(name);
		column.AddChild(banner);

		if (_definition != null && (Staffed(site) || site.Key == "idle"))
		{
			var strip = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
			strip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
			{
				BgColor = new Color(0.04f, 0.05f, 0.09f, 0.85f),
				BorderColor = new Color(0.72f, 0.58f, 0.30f, 0.9f),
				BorderWidthLeft = 1,
				BorderWidthTop = 1,
				BorderWidthRight = 1,
				BorderWidthBottom = 1,
				CornerRadiusTopLeft = 16,
				CornerRadiusTopRight = 16,
				CornerRadiusBottomLeft = 16,
				CornerRadiusBottomRight = 16,
				ContentMarginLeft = 8,
				ContentMarginRight = 8,
				ContentMarginTop = 4,
				ContentMarginBottom = 4,
			});
			strip.AddChild(PlaqueFigures(site));
			column.AddChild(strip);
		}

		return column;
	}

	private Control Reading(string what, string value, Color colour)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		Label name = Line(what, 16, Soft);
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(name);
		row.AddChild(Line(value, 18, colour));
		return row;
	}
}
