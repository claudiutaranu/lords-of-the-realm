using Godot;

/// <summary>What the county is given to eat, and out of what.
///
/// The ration is a multiple of a man's bread — half, ordinary, double, triple — and the lord sets it
/// knowing two things at once: a well-fed county thinks better of him, and a well-fed county empties
/// his granary at the same rate. That is the whole trade, and it is why both halves are on one page.
///
/// Where the meal comes from is the other half of the answer. The dairy is milked first because milk
/// spoils, the granary answers for the rest, and the knife comes out only when the granary cannot —
/// so a lord watching beef appear on this table is watching his herd being eaten, which is a
/// different kind of trouble from a thin year and wants a different answer.
///
/// Every figure is the turn's own. The season is played out on a copy of the county and the numbers
/// read off it, so this page cannot disagree with what happens when the turn is ended — see
/// <see cref="EconomySimulation.Preview"/>.</summary>
public partial class RationPanel : CountyPanel
{
	/// <summary>Raised when the ration has moved, so whatever else is showing it can catch up.</summary>
	public event System.Action Changed;

	private ProvinceEconomy _province;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	private Label _ration;
	private Label _achieved;
	private HSlider _mix;
	private Label _eat;
	private Label _dairy;
	private Label _bread;
	private Label _beef;
	private Label _short;
	private Label _lasts;
	private Label _goodwill;

	protected override void Furnish()
	{
		var table = new GridContainer { Columns = 2 };
		table.AddThemeConstantOverride("h_separation", 18);
		table.AddThemeConstantOverride("v_separation", 10);
		Column.AddChild(table);

		table.AddChild(Chrome.Line("Wanted", 22, Chrome.Soft));
		table.AddChild(RationRow());

		// What the county was actually handed, which is the figure that matters and is not always
		// the one above it. Its goodwill hangs off this line, so the heart hangs off it too.
		table.AddChild(Chrome.Line("Achieved", 22, Chrome.Soft));
		var served = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		served.AddThemeConstantOverride("separation", 10);
		served.Alignment = BoxContainer.AlignmentMode.End;
		_achieved = Chrome.Line("", 26, Chrome.Bright);
		served.AddChild(_achieved);
		_goodwill = Chrome.Line("", 22, Chrome.Bright);
		served.AddChild(_goodwill);
		served.AddChild(Chrome.Icon("heart", 30));
		table.AddChild(served);

		Column.AddChild(Mix());

		table = new GridContainer { Columns = 2 };
		table.AddThemeConstantOverride("h_separation", 18);
		table.AddThemeConstantOverride("v_separation", 10);
		Column.AddChild(table);

		table.AddChild(Chrome.Line("They eat", 22, Chrome.Soft));
		_eat = Figure(table, "food");

		_dairy = Under(table, "from the dairy", "livestock");
		_bread = Under(table, "from the granary", "food");
		_beef = Under(table, "from the herd", "livestock");
		_short = Under(table, "going short", "");

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		Column.AddChild(rule);

		var after = new GridContainer { Columns = 2 };
		after.AddThemeConstantOverride("h_separation", 18);
		after.AddThemeConstantOverride("v_separation", 10);
		Column.AddChild(after);

		after.AddChild(Chrome.Line("The granary lasts", 22, Chrome.Soft));
		_lasts = Figure(after, "");
	}

	/// <summary>Bread at one end, the herd at the other, and the lord's hand somewhere between. This
	/// is the whole of "what do they eat": the two ends are the same meal paid for out of different
	/// stores, and each store has a different bill. Bread is spent and gone; a cow eaten is also the
	/// milk it would have given for years.</summary>
	private Control Mix()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);
		row.AddChild(Chrome.Icon("food", 34));

		_mix = new HSlider
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 5,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(240, 0),
		};

		_mix.ValueChanged += share =>
		{
			_province.BeefShare = (int)share;
			Show();
			Changed?.Invoke();
		};

		row.AddChild(_mix);
		row.AddChild(Chrome.Icon("livestock", 34));
		return row;
	}

	/// <summary>The ration, with the two plates that move it. Named steps rather than a slider,
	/// because there are five of them and each one is a different county.</summary>
	private Control RationRow()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(Chrome.Repeating("▲", 34, () => Move(1)));
		row.AddChild(Chrome.Repeating("▼", 34, () => Move(-1)));

		_ration = Chrome.Line("", 26, Chrome.Bright);
		_ration.VerticalAlignment = VerticalAlignment.Center;
		_ration.CustomMinimumSize = new Vector2(110, 0);
		_ration.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(_ration);
		return row;
	}

	/// <summary>A figure in the right-hand column, with the thing it counts beside it where there is
	/// a glyph for it.</summary>
	private static Label Figure(GridContainer table, string icon)
	{
		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cell.AddThemeConstantOverride("separation", 8);
		cell.Alignment = BoxContainer.AlignmentMode.End;

		Label value = Chrome.Line("", 24, Chrome.Bright);
		cell.AddChild(value);
		if (icon.Length > 0)
		{
			cell.AddChild(Chrome.Icon(icon, 30));
		}

		table.AddChild(cell);
		return value;
	}

	/// <summary>One of the places the meal comes from, indented under the meal itself.</summary>
	private static Label Under(GridContainer table, string name, string icon)
	{
		var indent = new MarginContainer();
		indent.AddThemeConstantOverride("margin_left", 22);
		indent.AddChild(Chrome.Line(name, 18, Chrome.Soft));
		table.AddChild(indent);

		Label value = Figure(table, icon);
		value.AddThemeFontSizeOverride("font_size", 18);
		return value;
	}

	public void Open(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_province = province;
		_definition = definition;
		_balance = balance;
		_season = season;
		Show();
		Reveal(province.ProvinceName);
	}

	private void Move(int steps)
	{
		int most = System.Enum.GetValues<RationLevel>().Length - 1;
		_province.Ration = (RationLevel)Mathf.Clamp((int)_province.Ration + steps, 0, most);
		Show();
		Changed?.Invoke();
	}

	private void Show()
	{
		_ration.Text = _province.Ration.ToString();
		_mix.SetValueNoSignal(_province.BeefShare);

		// The season played out on a copy of the county. Not a formula of this page's own: a table
		// that works out its own dinner is a table that will one day promise a meal the turn does
		// not serve, and the player will believe the table.
		TurnSummary next = EconomySimulation.Preview(_province, _definition, _balance, _season);

		_eat.Text = $"{next.Needed:N0}";
		_dairy.Text = Some(next.Dairy);
		_bread.Text = Some(next.Bread);
		_beef.Text = next.Slaughtered > 0 ? $"{next.Slaughtered:N0} head" : "—";

		_short.Text = Some(next.FoodShort);
		_short.AddThemeColorOverride("font_color", next.FoodShort > 0 ? Chrome.Short : Chrome.Dim);

		// How long the barn holds out at this ration, counted on what this season is about to take.
		// A county eating nothing out of the granary is not "for ever" — it is a county eating its
		// herd, which the line above has already said.
		int seasons = next.Bread > 0 ? _province.Grain / next.Bread : -1;
		_lasts.Text = seasons < 0 ? "—" : seasons == 1 ? "1 season" : $"{seasons:N0} seasons";

		// Red when the county was handed less than it was promised: that gap is the lord's problem
		// whatever the reason for it, and it is the one thing on this page he might otherwise miss
		// while reading the order he gave himself.
		_achieved.Text = next.Achieved.ToString();
		_achieved.AddThemeColorOverride("font_color",
			next.Achieved < _province.Ration ? Chrome.Short : Chrome.Bright);

		float hearts = _balance.RationLoyaltyDelta[(int)next.Achieved];
		_goodwill.Text = Mathf.Abs(hearts) < 0.05f ? "(0)" : $"({hearts:+0.0;-0.0})";
		_goodwill.AddThemeColorOverride("font_color",
			hearts < 0f ? Chrome.Short : hearts > 0f ? Chrome.Gain : Chrome.Dim);
	}

	private static string Some(int amount) => amount > 0 ? $"{amount:N0}" : "—";
}
