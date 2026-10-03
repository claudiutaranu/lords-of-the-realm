using Godot;

/// <summary>What the steward would say of one of the county's stores, the way Lords of the Realm
/// answers a click on the cow or the basket: who works it, how much there is, and where next
/// season's change comes from, line by line down to the whole of it.
///
/// Every figure is the turn's own, read off the season played out on a copy of the county
/// (EconomySimulation.Preview), so this table cannot promise a calf the turn does not deliver.</summary>
public partial class StorePanel : CountyPanel
{
	private VBoxContainer _body;

	/// <summary>The lord wants to send some of this store to another of his counties. The page above
	/// owns the cart (SupplyPanel); this table only asks for it.</summary>
	public event System.Action SupplyPressed;

	private static readonly string[] Crowding =
	{
		"Light herd crowding.",
		"Average herd crowding.",
		"Heavy herd crowding.",
		"The pasture is overcrowded.",
	};

	protected override void Furnish()
	{
		_body = new VBoxContainer();
		_body.AddThemeConstantOverride("separation", 10);
		Column.AddChild(_body);
	}

	public void Open(ResourceType type, ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		foreach (Node old in _body.GetChildren())
		{
			old.QueueFree();
		}

		TurnSummary next = EconomySimulation.Preview(p, def, b, season);
		string job = Labour.JobOf(type);
		int hands = Labour.Hands(p, job);
		int wanted = Labour.Wanted(p, def, b, season, job);

		switch (type)
		{
			case ResourceType.Cattle:
				Heading("livestock", $"{hands:N0} herdsmen", $"{p.Cattle:N0} head");
				int pastures = p.FieldsUnder(FieldUse.Pasture);
				Say(pastures == 0 ? "No pasture: the herd is dying off." : Crowding[Husbandry.CrowdingBand(p.Cattle, pastures)]);
				int weather = Climate.Herd(p.Weather);
				Say(weather > 0 ? $"The {Named(p.Weather)} weather is kind to the herd."
					: weather < 0 ? $"The {Named(p.Weather)} weather is hard on the herd."
					: "Nothing outside the pasture touches the herd this season.");
				Wanting(wanted - hands, "herdsmen");
				Rule();
				Figure("Calves born", next.CalvesBorn, "head");
				Figure("Cows dead", -next.CowsDied, "head");
				Figure("Change from farming", next.CalvesBorn - next.CowsDied);
				Figure("Change from eating", -next.Slaughtered);
				Rule();
				Figure("Overall change", next.CattleChange, bold: true);
				Send();
				break;

			case ResourceType.Grain:
				Heading("food", $"{hands:N0} farmers", $"{p.Grain:N0} sacks");
				Say($"{p.FieldsUnder(FieldUse.Grain)} fields under grain, {p.FieldsUnder(FieldUse.Fallow)} lying fallow.");
				if (p.StandingCrop > 0)
				{
					Say($"{p.StandingCrop:N0} sacks standing in the fields.");
				}

				Wanting(wanted - hands, "farmers");
				Rule();
				Figure("Seed sown", -next.Sown);
				Figure("Harvest", next.Harvest);
				Figure("Eaten", -next.Bread);
				Rule();
				Figure("Overall change", next.GrainChange, bold: true);
				Send();
				break;

			default:
				string icon = type.ToString().ToLowerInvariant();
				Heading(icon, $"{hands:N0} at the {Labour.SiteName(job)}", $"{Stock(p, type):N0} in store");
				Say(p.Occupied.ContainsKey(job) ? "An enemy has stood on it: nobody can work it yet."
					: p.IsShut(job) ? "The lord has shut it."
					: $"The hands are {p.EfficiencyOf(job)}% practised at it.");
				Rule();
				Figure("Dug this season", EconomySimulation.ProjectedYield(type, hands, p, def, b, season));
				Rule();
				Figure("Overall change", Change(next, type), bold: true);
				Send();
				break;
		}

		Reveal(Title(type));
	}

	private static string Title(ResourceType type) => type switch
	{
		ResourceType.Cattle => "Cattle Farming",
		ResourceType.Grain => "Grain Farming",
		ResourceType.Wood => "Woodcutting",
		ResourceType.Stone => "Quarrying",
		_ => "Mining",
	};

	private static string Named(Weather weather) => weather.ToString().ToLowerInvariant();

	private static int Stock(ProvinceEconomy p, ResourceType type) => type switch
	{
		ResourceType.Wood => p.Wood,
		ResourceType.Stone => p.Stone,
		_ => p.Iron,
	};

	private static int Change(TurnSummary next, ResourceType type) => type switch
	{
		ResourceType.Wood => next.WoodChange,
		ResourceType.Stone => next.StoneChange,
		_ => next.IronChange,
	};

	// --- the lines -----------------------------------------------------------------------------

	private void Heading(string icon, string workers, string stock)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);
		row.AddChild(Chrome.Icon(icon, 44));
		Label who = Chrome.Line(workers, 22, Chrome.Bright);
		who.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		who.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(who);
		Label held = Chrome.Line(stock, 22, Chrome.Bright);
		held.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(held);
		_body.AddChild(row);
	}

	/// <summary>Every store goes by cart to another of the lord's counties (SupplyPanel).</summary>
	private void Send()
	{
		var send = new Button { Text = "Send supplies", CustomMinimumSize = new Vector2(0, 42) };
		send.AddThemeFontSizeOverride("font_size", 18);
		send.Pressed += () =>
		{
			Close();
			SupplyPressed?.Invoke();
		};
		_body.AddChild(send);
	}

	private void Say(string line) => _body.AddChild(Chrome.Line(line, 18, Chrome.Soft));

	/// <summary>The one line in red: the work is short of hands, and by how many.</summary>
	private void Wanting(int missing, string who)
	{
		if (missing > 0)
		{
			_body.AddChild(Chrome.Line($"{missing:N0} more {who} wanted.", 18, Stroke.Wanting));
		}
	}

	private void Rule()
	{
		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_body.AddChild(rule);
	}

	private void Figure(string what, int amount, string unit = "", bool bold = false)
	{
		var row = new HBoxContainer();
		Label name = Chrome.Line(what, bold ? 21 : 19, Chrome.Soft);
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(name);
		string sign = amount > 0 ? "+" : amount < 0 ? "−" : "";
		string text = amount == 0 ? "—" : $"{sign}{Mathf.Abs(amount):N0}{(unit.Length > 0 ? " " + unit : "")}";
		row.AddChild(Chrome.Line(text, bold ? 23 : 20, amount > 0 ? Chrome.Gain : amount < 0 ? Chrome.Short : Chrome.Dim));
		_body.AddChild(row);
	}
}
