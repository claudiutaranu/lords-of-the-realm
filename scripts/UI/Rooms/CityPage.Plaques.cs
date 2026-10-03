using Godot;

/// <summary>The plaques hung over the painted province: how many of a site's crew stand on each,
/// how its share of the county's hands is reckoned, and redrawn when that changes.</summary>
public partial class CityPage
{
	/// <summary>How far the plaque dips below the top of its building. It hangs above the picture
	/// rather than across the middle of it — a plate over a windmill's sails hides the one thing on
	/// the plot worth looking at — so it is measured from the building's own height.</summary>
	private const float PlaqueInset = 0.025f;

	/// <summary>How far down the page a plaque's middle must stand to clear the stores along the top.</summary>
	private const float HighestPlaque = 0.16f;

	/// <summary>How far the plaque of the site in hand stands off its building.</summary>
	private const float ChosenLift = 14f;

	/// <summary>How many figures a plaque draws before it says the rest in a number: an autumn
	/// harvest can ask for most of the county, and twenty rings across a plate hide the field.</summary>
	private const int MostOnPlaque = 6;

	private const int PlaqueFigure = 24;

	/// <summary>What a count reads when the site has more hands on it than it can use.</summary>
	private static readonly Color Spare = new("6f93c4");

	/// <summary>Redraws the plaques alone. Split out from <see cref="Rebuild"/> because the labour
	/// bar lives in the panel that a full rebuild tears down, and a bar that frees itself halfway
	/// through being dragged takes the drag with it.</summary>
	/// <summary>How much of a site's gang is out on its ground: all of them where it is manned to
	/// what the season asks, none where nobody is on it. It is the same number the plaque carries,
	/// said in men rather than in figures.</summary>
	private float CrewShare(Site site)
	{
		if (_definition == null)
		{
			return 0f;
		}

		if (site.Key == "field")
		{
			return Husbandry.SeasonsToReclaim(Province, _balance) > 0 ? 1f : 0f;
		}

		if (site.Key == "idle")
		{
			return (float)EconomySimulation.Idle(Province, _definition, _balance, _season)
				/ Mathf.Max(1, Province.Workers);
		}

		if (site.Builds)
		{
			return (float)Province.BuildWorkers / Mathf.Max(1, _balance.MasonsPerBuildSeason);
		}

		if (site.Smiths)
		{
			return Province.Forging.Length == 0 ? 0f : Mathf.Min(1f, Province.SmithWorkers / (float)Mathf.Max(1, Wanted(site)));
		}

		if (site.Works == null)
		{
			return 1f; // a place with no hands on it at all is drawn as it was painted
		}

		int demand = EconomySimulation.Demand(site.Works.Value, Province, _definition, _balance, _season);
		return demand <= 0 ? 0f : (float)Allocated(site.Works.Value) / demand;
	}

	private void Replaque()
	{
		foreach (Site site in _sites)
		{
			Control holder = _plaques[site.Key];
			foreach (Node old in holder.GetChildren())
			{
				old.QueueFree();
			}

			Control row = Row(site);
			holder.AddChild(row);

			// Centred on the spot by hand: the holder is a bare Control with no layout of its own,
			// which is the point — a container here would stretch the plaque to the page.
			// A plaque taken in hand lifts off its building. The gold border says which one is
			// chosen, but on a page where nine plates are already gold-edged it says it quietly;
			// the one that moved is the one the eye goes to.
			Vector2 size = row.GetCombinedMinimumSize();
			float lift = site.Key == _chosen ? ChosenLift : 0f;
			row.Position = new Vector2(-size.X / 2f, -size.Y / 2f - lift);
			row.Size = size;
		}
	}

	/// <summary>The site's people on its plaque, in figures while there are few enough to draw and
	/// in a count of them past that. Short-handed reads red and over-manned reads cool, the way
	/// Lords of the Realm marks its own: the first costs the season's yield, the second only costs
	/// hands that could be somewhere else. The hollow rings are the places the work still has, the
	/// ghost figures Lords of the Realm draws under a trade that is short.</summary>
	private Control PlaqueFigures(Site site)
	{
		int people = Province.Workers;
		int on = site.Key == "idle" ? EconomySimulation.Idle(Province, _definition, _balance, _season) : On(site);
		int wanted = site.Key == "idle" ? on : Wanted(site);
		int onFigures = WorkerFigures.Of(on, people);
		int wantedFigures = WorkerFigures.Of(wanted, people);

		var figures = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		figures.AddThemeConstantOverride("separation", 3);
		if (Mathf.Max(onFigures, wantedFigures) <= MostOnPlaque)
		{
			for (int figure = 0; figure < Mathf.Max(onFigures, wantedFigures); figure++)
			{
				Control token = WorkerFigures.Token(figure < onFigures, PlaqueFigure);
				token.SizeFlagsVertical = SizeFlags.ShrinkCenter;
				figures.AddChild(token);
			}

			return figures;
		}

		Control one = WorkerFigures.Token(onFigures > 0, PlaqueFigure);
		one.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		figures.AddChild(one);
		Color tint = site.Key == "idle" ? Short : onFigures < wantedFigures ? Short : onFigures > wantedFigures ? Spare : Bright;
		Label count = Line(site.Key == "idle" ? $"{onFigures}" : $"{onFigures}/{wantedFigures}", 18, tint);
		count.VerticalAlignment = VerticalAlignment.Center;
		figures.AddChild(count);
		return figures;
	}

	private static ResourceType? Industry(string works) => works switch
	{
		"grain" => ResourceType.Grain,
		"cattle" => ResourceType.Cattle,
		"wood" => ResourceType.Wood,
		"stone" => ResourceType.Stone,
		"iron" => ResourceType.Iron,
		_ => null,
	};

	private int Capacity(string needs) => needs switch
	{
		"grain" => _definition.GrainWorkerCapacity,
		"cattle" => _definition.CattleWorkerCapacity,
		"wood" => _definition.WoodWorkerCapacity,
		"stone" => _definition.StoneWorkerCapacity,
		_ => _definition.IronWorkerCapacity,
	};

	private int Allocated(ResourceType works) => works switch
	{
		ResourceType.Grain => Province.GrainWorkers,
		ResourceType.Cattle => Province.CattleWorkers,
		ResourceType.Wood => Province.WoodWorkers,
		ResourceType.Stone => Province.StoneWorkers,
		_ => Province.IronWorkers,
	};
}
