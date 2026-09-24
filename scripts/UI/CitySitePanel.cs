using Godot;

/// <summary>The province screen's side of one site: who is on it, what it is asking for, and the
/// panel that opens when it is taken in hand — its picture, what it brings in, its figures and the
/// buttons that move them, and the figures behind all that.</summary>
public partial class CityPage
{
	private const int PanelFigure = 38;

	// --- the hands on a site -------------------------------------------------------------------

	/// <summary>The job a site's people are dealt to (Labour), or null for a site whose people are
	/// read rather than set — the idle yard.</summary>
	private static string JobOf(Site site) =>
		site.Builds ? Labour.Castle
		: site.Smiths ? Labour.Smith
		: site.Key == "field" ? Labour.Reclaim
		: site.Works is { } works ? Labour.JobOf(works)
		: null;

	private static bool Staffed(Site site) => JobOf(site) != null;

	private int On(Site site) => JobOf(site) is { } job ? Labour.Hands(Province, job) : 0;

	/// <summary>What the site needs before it is short-handed. The diggings are never short.</summary>
	private int Wanted(Site site) =>
		_definition == null || JobOf(site) is not { } job ? 0
		: Labour.Wanted(Province, _definition, _balance, _season, job);

	/// <summary>Asks for one figure's worth more or fewer on a site. The proportions are rewritten to
	/// match and the county dealt again (Labour.Ask), so the figure comes from, or goes back to, the
	/// rest of the site's half — and asking at a site the lord had shut opens it.</summary>
	private void Shift(Site site, int figures)
	{
		string job = JobOf(site);
		int by = figures * WorkerFigures.Size(Province.Workers);
		Labour.Ask(Province, _definition, _balance, _season, job,
			Mathf.Clamp(Labour.Hands(Province, job) + by, 0, Labour.Most(Province, _definition, _balance, _season, job)));
		Rebuild();
	}

	private void Toggle(Site site)
	{
		Labour.Toggle(Province, _definition, _balance, _season, JobOf(site));
		Rebuild();
	}

	// --- the panel -----------------------------------------------------------------------------

	/// <summary>The site in hand, the way a steward would put it to his lord: the place, what it
	/// makes, who is on it and what more it could take.</summary>
	private void ShowSite(Site site)
	{
		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 12);
		heading.AddChild(Icon(site.Icon, 40));
		Label title = Line(site.Name.ToUpperInvariant(), 28, Cream);
		GoldTitle.Apply(title);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		Detail.AddChild(heading);

		if (CityArt.HasTile(site.Key))
		{
			var picture = new TextureRect
			{
				Texture = GD.Load<Texture2D>(CityArt.Tile(site.Key)),
				CustomMinimumSize = new Vector2(0, 170),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			};
			Detail.AddChild(Chrome.Framed(picture, 4));
		}

		Label blurb = Line(site.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		Detail.AddChild(blurb);

		(string icon, string yield) = Production(site);
		if (yield.Length > 0)
		{
			Detail.AddChild(Section("Production"));
			var made = new HBoxContainer();
			made.AddThemeConstantOverride("separation", 12);
			made.AddChild(Icon(icon, 34));
			Label reads = Line(yield, 19, Bright);
			reads.VerticalAlignment = VerticalAlignment.Center;
			reads.AutowrapMode = TextServer.AutowrapMode.Word;
			reads.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			made.AddChild(reads);
			Detail.AddChild(made);
		}

		if (Staffed(site) || site.Key == "idle")
		{
			Detail.AddChild(Section(site.Key == "idle" ? "Standing about" : "Workers"));
			Detail.AddChild(Figures(site));
		}

		Detail.AddChild(Section("Details"));
		foreach ((string what, string value) in Particulars(site))
		{
			Detail.AddChild(Reading(what, value, Bright));
		}

		// The original's click on the building: a site can be shut, and its people dealt elsewhere.
		if (JobOf(site) is { } job && System.Array.IndexOf(Labour.Sites, job) >= 0)
		{
			bool shut = Province.IsShut(job);
			var toggle = new Button { Text = shut ? "Open it again" : "Shut it", CustomMinimumSize = new Vector2(0, 42) };
			toggle.AddThemeFontSizeOverride("font_size", 17);
			toggle.Pressed += () => Toggle(site);
			Detail.AddChild(toggle);
		}

		if (site.Room != null)
		{
			string room = site.Room;
			var door = new Button { Text = $"Go into the {site.Name}  ›", CustomMinimumSize = new Vector2(0, 46) };
			door.AddThemeFontSizeOverride("font_size", 18);
			door.Pressed += () => RoomChosen?.Invoke(room);
			Detail.AddChild(door);
		}
	}

	/// <summary>The site's figures, with the count of them and the plates that move one.</summary>
	private Control Figures(Site site)
	{
		int people = Province.Workers;
		int on = site.Key == "idle"
			? EconomySimulation.Idle(Province, _definition, _balance, _season)
			: On(site);
		int wanted = site.Key == "idle" ? on : Wanted(site);
		int onFigures = WorkerFigures.Of(on, people);
		int wantedFigures = WorkerFigures.Of(wanted, people);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);

		var tokens = new HBoxContainer();
		tokens.AddThemeConstantOverride("separation", 12);
		Control row = WorkerFigures.Row(onFigures, wantedFigures, PanelFigure);
		row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		tokens.AddChild(row);
		// A diggings is never short: it reads what it has, not what it lacks.
		bool bottomless = site.Key != "idle" && wanted == 0 && Staffed(site);
		Label count = Line(bottomless ? $"{onFigures}" : $"{onFigures} / {wantedFigures}", 20,
			bottomless ? Bright : on < wanted ? Short : on > wanted ? Spare : Bright);
		count.VerticalAlignment = VerticalAlignment.Center;
		tokens.AddChild(count);
		column.AddChild(tokens);

		if (Staffed(site))
		{
			var plates = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			plates.AddThemeConstantOverride("separation", 18);
			// Dead where there is nothing to move: nobody on it to take off, or no room for another.
			Button fewer = Chrome.Plate("−", 52, () => Shift(site, -1));
			fewer.Disabled = on == 0;
			plates.AddChild(fewer);
			TextureRect worker = Icon("worker", 44);
			worker.SizeFlagsVertical = SizeFlags.ShrinkCenter;
			plates.AddChild(worker);
			Button more = Chrome.Plate("+", 52, () => Shift(site, 1));
			more.Disabled = on >= Labour.Most(Province, _definition, _balance, _season, JobOf(site));
			plates.AddChild(more);
			column.AddChild(plates);
		}

		Label scale = Line($"One figure is {WorkerFigures.Size(people):N0} people", 14, Dim);
		scale.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(scale);
		return column;
	}

	/// <summary>What the site brings in at the strength it is manned, in one line under its icon.</summary>
	private (string Icon, string Line) Production(Site site)
	{
		if (site.Smiths)
		{
			if (Province.Forging.Length == 0)
			{
				return ("hammer", "The forge is cold. Choose what it makes inside.");
			}

			int made = Mathf.Min(EconomySimulation.Forgeable(Province, _balance),
				Smithy.Affordable(Province, Province.Forging));
			return (WeaponIcon(Province.Forging), $"+{made:N0} {Plural(Province.Forging)} a season");
		}

		if (site.Builds)
		{
			if (Province.Building.Length == 0)
			{
				return ("castle", "Nothing is going up. Order a wall inside.");
			}

			return ("castle", Province.BuildWorkers == 0
				? "The scaffolding stands empty"
				: $"Finished in {Mathf.CeilToInt(Province.BuildLeft / (float)Province.BuildWorkers)} seasons at this strength");
		}

		if (site.Key == "field")
		{
			int mending = EconomySimulation.SeasonsToMend(Province, _balance, _season);
			return ("food", mending == 0 ? "The ground is in good heart"
				: mending < 0 ? "Torn ground, and nobody reclaiming it"
				: $"Reclaimed in {mending} season{(mending == 1 ? "" : "s")}");
		}

		if (site.Works is not { } works)
		{
			return ("", "");
		}

		int yield = EconomySimulation.ProjectedYield(works, On(site), Province, _definition, _balance, _season);
		string when = works == ResourceType.Grain && _season != Season.Autumn ? "at the harvest" : "this season";
		return (site.Icon, $"+{yield:N0} {when}");
	}

	/// <summary>The figures behind the site, for the lord who wants to check the reeve's sums.</summary>
	private (string What, string Value)[] Particulars(Site site)
	{
		if (site.Key == "idle")
		{
			return new[] { ("Hands in the county", Province.Workers.ToString("N0")) };
		}

		if (site.Key == "field")
		{
			return new[]
			{
				("Reclaiming", Province.ReclaimWorkers.ToString("N0")),
				("Work left", Province.FieldRepair.ToString("N0")),
				("Most in one season", _balance.ReclaimPerSeason.ToString("N0")),
			};
		}

		string onIt = On(site).ToString("N0");
		string wanted = Wanted(site).ToString("N0");
		if (site.Smiths)
		{
			return new[]
			{
				("At the anvil", onIt),
				("The stores would keep busy", wanted),
				("Smiths to a weapon", _balance.SmithsPerWeapon.ToString()),
				("Practised", $"{Province.EfficiencyOf(Labour.Smith)}%"),
				("The stores pay for", Province.Forging.Length == 0 ? "—" : Smithy.Affordable(Province, Province.Forging).ToString("N0")),
			};
		}

		if (site.Builds)
		{
			return new[]
			{
				("On the scaffolding", onIt),
				("Work left on the wall", Province.BuildLeft.ToString("N0")),
			};
		}

		if (site.Works is not { } works)
		{
			return System.Array.Empty<(string, string)>();
		}

		// The fields and the herd have a need; a diggings has a rate, and a practice it builds up.
		return works is ResourceType.Grain or ResourceType.Cattle
			? new[]
			{
				("Hands on it", onIt),
				("Asked for this season", wanted),
				("In store", Province.Stored(Store(works)).ToString("N0")),
			}
			: new[]
			{
				("Hands on it", onIt),
				("Practised", $"{Province.EfficiencyOf(Labour.JobOf(works))}%"),
				("Share of the industry", $"{(Province.Shares.TryGetValue(Labour.JobOf(works), out int share) ? share / 100 : 0)}%"),
				("In store", Province.Stored(Store(works)).ToString("N0")),
			};
	}

	private static Control Section(string title)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		Label name = Line(title, 19, Cream);
		row.AddChild(name);
		Control rule = Chrome.Rule(10);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(rule);
		return row;
	}

	private static string Store(ResourceType works) => works switch
	{
		ResourceType.Grain => "grain",
		ResourceType.Cattle => "cattle",
		ResourceType.Wood => "wood",
		ResourceType.Stone => "stone",
		_ => "iron",
	};

	// The cavalry are known by their helm, not by the horse under it.
	private static string WeaponIcon(string weapon) => weapon == "horse" ? "helmet" : weapon;

	private static string Plural(string weapon) => weapon == "horse" ? "cavalry kits" : $"{weapon}s";
}
