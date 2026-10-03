using Godot;

/// <summary>The chosen rung read in the panel beside the counters, what it gives the walls, whether
/// the stores can pay for it, and the order to raise it.</summary>
public partial class FortificationsPage
{
	// --- the panel on the right ------------------------------------------------------------------

	protected override void ShowDetail()
	{
		if (Detail == null)
		{
			return;
		}

		ClearDetail();
		if (_chosen == null || Province == null)
		{
			return;
		}

		Label title = Line(_chosen.Name.ToUpperInvariant(), 24, Bright);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(title);

		Control picture = Mount(_chosen.Key, SizeFlags.Fill);
		picture.CustomMinimumSize = new Vector2(0, 150);
		Detail.AddChild(picture);

		Detail.AddChild(BuildLadder());

		// Three lines' worth of room whether the line needs them or not. The blurbs run from two
		// lines to three, and letting the panel shrink to fit moves the Build button up and down
		// under the cursor as the player reads along the ladder.
		Label blurb = Line(_chosen.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		blurb.HorizontalAlignment = HorizontalAlignment.Center;
		blurb.VerticalAlignment = VerticalAlignment.Center;
		blurb.CustomMinimumSize = new Vector2(0, 68);
		Detail.AddChild(blurb);

		Label needs = Line("Requires", 14, Soft);
		needs.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(needs);

		var price = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		price.AddThemeConstantOverride("separation", 16);
		foreach ((string store, int amount) in _chosen.Cost)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 7);
			group.AddChild(Icon(store, 28));
			group.AddChild(Line(amount.ToString("N0"), 20,
				Province.Stored(store) >= amount ? Bright : Short));
			price.AddChild(group);
		}

		Detail.AddChild(price);

		Label takes = Line($"{_chosen.Seasons} season{(_chosen.Seasons == 1 ? "" : "s")} of work",
			14, Soft);
		takes.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(takes);
		Detail.AddChild(Gives(Fortifications.Of(_chosen.Key)));

		bool standing = _chosen.Key == Province.Fortification;
		bool busy = Province.Building.Length > 0;
		var build = new Button
		{
			Text = busy ? "The masons are at work" : standing ? "Already standing" : "Build",
			CustomMinimumSize = new Vector2(0, 52),
			Disabled = busy || standing || !CanAfford(_chosen),
		};
		build.AddThemeFontSizeOverride("font_size", 19);
		build.Pressed += Build;
		Detail.AddChild(build);

		if (busy)
		{
			Label rising = Line(
				$"Raising {NameOf(Province.Building)} — {Province.BuildSeasonsLeft} " +
				$"season{(Province.BuildSeasonsLeft == 1 ? "" : "s")} left",
				14, Bright);
			rising.HorizontalAlignment = HorizontalAlignment.Center;
			Detail.AddChild(rising);
		}
	}

	/// <summary>What the walls are for, as Lords of the Realm told it: how many men they hold, how much
	/// harder those men are to kill, how few can come at them at once, and what the county pays behind
	/// them against open ground.</summary>
	private static Control Gives(Fortifications.Wall wall)
	{
		var table = new GridContainer { Columns = 2 };
		table.AddThemeConstantOverride("h_separation", 16);
		table.AddThemeConstantOverride("v_separation", 2);
		int dues = Mathf.RoundToInt(100f * wall.TaxBase / Fortifications.OpenGroundTaxBase) - 100;
		foreach ((string what, string worth) in new[]
		{
			("Men on the walls", wall.Garrison.ToString("N0")),
			("Each defender is worth", $"×{wall.Defence:0.##}"),
			("Stormed at once by", $"{wall.Frontage:N0} men"),
			("Taxes", $"+{dues}%"),
		})
		{
			Label name = Line(what, 15, Soft);
			name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			table.AddChild(name);
			Label figure = Line(worth, 16, Bright);
			figure.HorizontalAlignment = HorizontalAlignment.Right;
			table.AddChild(figure);
		}

		return table;
	}

	private bool CanAfford(Fort fort)
	{
		foreach ((string store, int amount) in fort.Cost)
		{
			if (Province.Stored(store) < amount)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>Pays for the wall and puts the masons on it. The turn does the rest; what stands in
	/// the province does not change until they are finished.</summary>
	private void Build()
	{
		if (_chosen == null || Province.Building.Length > 0 ||
			_chosen.Key == Province.Fortification || !CanAfford(_chosen))
		{
			return;
		}

		foreach ((string store, int amount) in _chosen.Cost)
		{
			Province.Add(store, -amount);
		}

		Province.Building = _chosen.Key;
		// Priced in hand-seasons: the quoted seasons are what it takes at the nominal gang of masons,
		// and the lord decides whether it gets them.
		Province.BuildLeft = _chosen.Seasons * GameBalance.Engine.MasonsPerBuildSeason;
		Province.BuildSeasonsLeft = _chosen.Seasons;
		Refresh();
	}
}
