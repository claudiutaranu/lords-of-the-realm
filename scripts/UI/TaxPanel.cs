using Godot;

/// <summary>The tax table for one county: what the lord is asking of it, what that brings in, and
/// what it costs him — here, and in every other county he holds.
///
/// The three lines under the rate are the whole point of the screen. A tax control that shows only
/// the rate makes the player set a number and wait a season to find out what it did, which is how a
/// lord ends up bouncing between too little gold and a county in revolt without ever learning which
/// point of tax was the one that did it. Here the whole bargain is on one page before he commits:
/// the crowns on one side, the goodwill on the other, and the reminder that his other counties are
/// watching how he treats this one.
///
/// Every figure is read from <see cref="EconomySimulation"/>, never worked out again here. A screen
/// that does its own arithmetic is a screen that will one day quietly disagree with the turn, and
/// the player will believe the screen.</summary>
public partial class TaxPanel : CountyPanel
{
	private const string PurseArtPath = "res://assets/ui/tax-purse.png";

	/// <summary>Raised when the rate has moved, so whatever else is showing it can catch up.</summary>
	public event System.Action Changed;

	private ProvinceEconomy _province;
	private GameBalance _balance;
	private int _otherCounties;

	private Label _rate;
	private Label _due;
	private Label _here;
	private Label _elsewhere;

	protected override void Furnish()
	{
		// The table on the left and the reeve's purse on the right, the way the old game hung its
		// tax collector's hat beside the figures. It is the one picture on the panel and it is the
		// only thing telling the player at a glance which of his decisions he is looking at.
		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 20);
		Column.AddChild(body);

		var table = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		table.AddThemeConstantOverride("h_separation", 18);
		table.AddThemeConstantOverride("v_separation", 12);
		body.AddChild(table);

		table.AddChild(Chrome.Line("Tax rate", 20, Chrome.Soft));
		table.AddChild(RateRow());

		table.AddChild(Chrome.Line("People pay", 20, Chrome.Soft));
		_due = Chrome.Line("", 26, Chrome.Bright);
		table.AddChild(_due);

		table.AddChild(Chrome.Line("This county", 20, Chrome.Soft));
		_here = Hearts(table);

		table.AddChild(Chrome.Line("Other counties", 20, Chrome.Soft));
		_elsewhere = Hearts(table);

		body.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(PurseArtPath),
			// Both sides of it, and not just the width: a TextureRect told to shrink to its minimum
			// with no minimum height is a texture drawn at no height at all — it takes its column
			// and renders nothing in it. The pair is the art's own proportions at the size the panel
			// has room for.
			CustomMinimumSize = new Vector2(210, 165),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		});

	}

	/// <summary>The rate, with the two buttons that move it. A point at a time and no slider: the
	/// interesting range is a dozen points wide and the difference between one of them and the next
	/// is a county's goodwill, which is not a thing to drag past.</summary>
	private Control RateRow()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(Chrome.Repeating("▲", 34, () => Move(1)));
		row.AddChild(Chrome.Repeating("▼", 34, () => Move(-1)));

		_rate = Chrome.Line("", 30, Chrome.Bright);
		_rate.VerticalAlignment = VerticalAlignment.Center;
		_rate.CustomMinimumSize = new Vector2(84, 0);
		_rate.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(_rate);
		return row;
	}

	private static Label Hearts(GridContainer table)
	{
		var cell = new HBoxContainer();
		cell.AddThemeConstantOverride("separation", 6);
		Label value = Chrome.Line("", 26, Chrome.Bright);
		value.CustomMinimumSize = new Vector2(62, 0);
		value.HorizontalAlignment = HorizontalAlignment.Right;
		cell.AddChild(value);
		// The heart is drawn with a wide margin inside its own square — at the text's own size the
		// shape lands at about half the height of the letters beside it and reads as a speck.
		cell.AddChild(Chrome.Icon("heart", 36));
		table.AddChild(cell);
		return value;
	}

	/// <summary>Opens on one county. <paramref name="otherCounties"/> is how many more the same lord
	/// holds, because what a rate costs elsewhere is nothing at all when there is no elsewhere — and
	/// a line reading "-2" over an empty realm is a cost the player would go looking for.</summary>
	public void Open(ProvinceEconomy province, GameBalance balance, int otherCounties)
	{
		_province = province;
		_balance = balance;
		_otherCounties = otherCounties;
		Show();
		Reveal(province.ProvinceName);
	}

	private void Move(int points)
	{
		_province.Tax = Mathf.Clamp(_province.Tax + points, 0, Livelihood.MostTax);
		Show();
		Changed?.Invoke();
	}

	private void Show()
	{
		_rate.Text = $"{_province.Tax}%";
		_due.Text = $"{EconomySimulation.TaxDue(_province, _balance):N0} crowns";

		// The county's own rate against five, and its own share of what the realm's rates cost.
		Reads(_here, Livelihood.TaxTerm(_province.Tax) + Livelihood.EmpireTerm(_province.Tax));

		// A lord who holds one county has no elsewhere, and a flat "0" there reads as "this rate
		// costs nobody else anything" — which is not the same statement, and is the one that makes
		// a player think the rule is not implemented. A dash says there is nothing to count yet.
		if (_otherCounties == 0)
		{
			_elsewhere.Text = "—";
			_elsewhere.AddThemeColorOverride("font_color", Chrome.Dim);
			return;
		}

		Reads(_elsewhere, Livelihood.EmpireTerm(_province.Tax));
	}

	/// <summary>A goodwill figure, in the whole points the original moves it in, coloured by which
	/// way it cuts.</summary>
	private static void Reads(Label label, float hearts)
	{
		label.Text = $"{hearts:+0;-0;0}";
		// Red costs, green earns, and nothing at all is WHITE and not grey: a lord reading "no
		// change" has to be able to read it, and dimming the one figure that says "this rate is
		// free" hides the answer he came here for.
		label.AddThemeColorOverride("font_color",
			hearts < 0f ? Chrome.Short : hearts > 0f ? Chrome.Gain : Chrome.Bright);
	}

}
