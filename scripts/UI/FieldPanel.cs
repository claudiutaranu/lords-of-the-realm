using Godot;

/// <summary>One field of one county: what is on it, what that is doing this year, and the three
/// things it could be instead.
///
/// A county's land is the one decision in the game with a year's delay on it. Grain sown this spring
/// is bread next autumn, a pasture turned over is a herd with nowhere to stand, and a field rested
/// pays nothing at all until the year after — so the page a lord makes that decision on has to show
/// him the state of the ground and not just its name. The heart of the land is the number that
/// matters and the one he cannot see from the map: two fields the same colour can be a good field
/// and an exhausted one.
///
/// What the season is about to do comes from <see cref="EconomySimulation.Preview"/> — the turn
/// played out on a copy — so the figures here are the turn's own and not a second opinion.</summary>
public partial class FieldPanel : CountyPanel
{
	/// <summary>Raised when the field has been turned over to something else, so the map above can
	/// redraw the ground and the ledgers beside it can catch up.</summary>
	public event System.Action Changed;

	private ProvinceEconomy _province;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;
	private int _field;

	private Label _under;
	private GridContainer _table;
	private HBoxContainer _choices;

	protected override int Width => 480;

	protected override void Furnish()
	{
		_under = Chrome.Line("", 24, Chrome.Cream);
		Column.AddChild(_under);

		_table = new GridContainer { Columns = 2 };
		_table.AddThemeConstantOverride("h_separation", 18);
		_table.AddThemeConstantOverride("v_separation", 10);
		Column.AddChild(_table);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		Column.AddChild(rule);

		Column.AddChild(Chrome.Line("Put this field under", 18, Chrome.Dim));

		_choices = new HBoxContainer();
		_choices.AddThemeConstantOverride("separation", 10);
		Column.AddChild(_choices);

		foreach (FieldUse use in new[] { FieldUse.Fallow, FieldUse.Grain, FieldUse.Pasture })
		{
			FieldUse chosen = use;
			var button = new Button { Text = Name(use), CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = SizeFlags.ExpandFill };
			button.AddThemeFontSizeOverride("font_size", 17);
			button.Pressed += () => Turn(chosen);
			_choices.AddChild(button);
		}
	}

	public void Open(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance,
		Season season, int field)
	{
		_province = province;
		_definition = definition;
		_balance = balance;
		_season = season;
		_field = field;
		Show();
		Reveal(province.ProvinceName);
	}

	/// <summary>Turns the field over. Ploughing a sown field under throws away the seed with it, which
	/// <see cref="EconomySimulation.SetField"/> charges for — this page only asks.</summary>
	private void Turn(FieldUse use)
	{
		if (_province.Fields[_field] == use)
		{
			return;
		}

		EconomySimulation.SetField(_province, _field, use);
		Show();
		Changed?.Invoke();
	}

	private void Show()
	{
		FieldUse use = _province.Fields[_field];
		_under.Text = $"Farmland — {Name(use)}";

		foreach (Node row in _table.GetChildren())
		{
			_table.RemoveChild(row);
			row.QueueFree();
		}

		// The one figure that belongs to THIS field and to nothing else. Everything under it is the
		// county's, because a herd and a harvest are the county's — which is how the old game read
		// too: one field's page, the shire's numbers.
		Row("Heart of the land", $"{Mathf.RoundToInt(_province.Fertility[_field] * 100f)}%",
			_province.Fertility[_field] < _balance.FertilityFloor + 0.1f ? Chrome.Short : Chrome.Bright);

		TurnSummary next = EconomySimulation.Preview(_province, _definition, _balance, _season);
		switch (use)
		{
			case FieldUse.Grain: Corn(next); break;
			case FieldUse.Pasture: Herd(next); break;
			default: Resting(); break;
		}

		// The use it is already under is not on offer: a button that does nothing is a button the
		// player presses once and then distrusts the other two.
		for (int choice = 0; choice < _choices.GetChildCount(); choice++)
		{
			((Button)_choices.GetChild(choice)).Disabled = (FieldUse)Order[choice] == use;
		}
	}

	private static readonly int[] Order = { (int)FieldUse.Fallow, (int)FieldUse.Grain, (int)FieldUse.Pasture };

	private void Corn(TurnSummary next)
	{
		int fields = _province.FieldsUnder(FieldUse.Grain);
		Row("Fields under grain", $"{fields}", Chrome.Bright);
		// Seed and harvest are not the same sack. Twenty in the ground come back as hundreds, so the
		// two lines are named for what they are — a page that called both of them "grain" would look
		// like it was contradicting itself every autumn.
		Row("Seed in the ground", Some(_province.StandingCrop, "sacks"), Chrome.Bright);

		// And the harvest only when there is one to report. Outside autumn the honest answer is when
		// it comes, not a figure worked out from a year that has not happened: the reapers have to be
		// in the field for it, and who is in the field in October is not decided in April.
		Row("Harvest", _season == Season.Autumn ? Some(next.Harvest, "sacks") : "comes in autumn",
			_season == Season.Autumn ? Chrome.Gain : Chrome.Dim);
		Row("Hands at the plough", Hands(ResourceType.Grain, _province.GrainWorkers), Chrome.Bright);
		Row("The land loses each cropped year", $"-{Mathf.RoundToInt(_balance.FertilityCropped * 100f)}%", Chrome.Short);
	}

	private void Herd(TurnSummary next)
	{
		int room = _province.FieldsUnder(FieldUse.Pasture) * _balance.CowsPerField;
		int calves = next.CattleChange + next.Slaughtered;

		Row("The herd", $"{_province.Cattle:N0} animals", Chrome.Bright);
		Row("Room on the pasture", $"{room:N0} animals", room < _province.Cattle ? Chrome.Short : Chrome.Bright);
		Row("Crowding", Crowding(_province.Cattle, room), room > 0 && _province.Cattle > room ? Chrome.Short : Chrome.Soft);
		Row("Dairy maids", Hands(ResourceType.Cattle, _province.CattleWorkers), Chrome.Bright);
		Row("Milk expected", Some(next.Dairy, "of food"), Chrome.Gain);
		Row("Calves expected", calves > 0 ? $"+{calves:N0}" : "—", Chrome.Gain);
		Row("Eaten this season", next.Slaughtered > 0 ? $"-{next.Slaughtered:N0}" : "—", Chrome.Short);
		Row("Overall change", next.CattleChange == 0 ? "—" : $"{next.CattleChange:+0;-0}",
			next.CattleChange < 0 ? Chrome.Short : Chrome.Gain);
	}

	private void Resting()
	{
		Row("It gains each year", $"+{Mathf.RoundToInt(_balance.FertilityRested * 100f)}%", Chrome.Gain);
		Row("Nothing is sown here", "and nothing grazes", Chrome.Dim);
	}

	/// <summary>What the county has on a task against what the task can use. Both numbers, because
	/// a field with nobody on it and a field with everybody on it look the same from the map.</summary>
	private string Hands(ResourceType task, int working)
	{
		int demand = EconomySimulation.Demand(task, _province, _definition, _balance, _season);
		return demand <= 0 ? "nothing to do this season" : $"{working:N0} of {demand:N0}";
	}

	private static string Crowding(int herd, int room)
	{
		if (room <= 0)
		{
			return "no pasture at all";
		}

		float packed = (float)herd / room;
		return packed < 0.5f ? "Low" : packed < 1f ? "Fair" : packed < 1.5f ? "Crowded" : "Packed in";
	}

	private void Row(string name, string value, Color colour)
	{
		_table.AddChild(Chrome.Line(name, 19, Chrome.Soft));

		Label figure = Chrome.Line(value, 19, colour);
		figure.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		figure.HorizontalAlignment = HorizontalAlignment.Right;
		_table.AddChild(figure);
	}

	private static string Some(int amount, string unit) => amount > 0 ? $"{amount:N0} {unit}" : "—";

	private static string Name(FieldUse use) => use switch
	{
		FieldUse.Grain => "Grain",
		FieldUse.Pasture => "Cattle",
		_ => "Resting",
	};
}
