using System.Collections.Generic;
using Godot;

/// <summary>Where a county's goodwill came from: the year-by-year record, and the season just gone
/// broken into the things that moved it.
///
/// The breakdown is the panel. Loyalty is the number every other decision in the game is paid for
/// in, and a lord watching it fall with no account of why will either stop reading it or blame the
/// wrong lever — usually the tax, because the tax is the one he knows he touched. Every line here
/// is a figure the turn actually recorded, and they are the whole of what moved the number: the
/// simulation writes down each grievance separately and the world writes down its own, precisely so
/// this page can add up to what happened rather than to what it assumes happened.
///
/// One honest gap. Goodwill is held between nothing and a hundred, so in a county already at the
/// floor the lines say what each thing asked for and the difference between the two seasons is what
/// there was left to take. A county at nought cannot be angered further, and the page does not
/// pretend otherwise by quietly shrinking its own figures to fit.</summary>
public partial class HappinessPanel : CountyPanel
{
	/// <summary>How tall the bars are drawn, and how many years fit before the oldest scroll off.
	/// A reign of forty years would otherwise draw forty bars a few pixels wide, which is a texture
	/// rather than a chart.</summary>
	protected override int Width => 560;

	private const int SceneHeight = 190;
	private const int MostYears = 20;
	private const string TavernArtPath = "res://assets/ui/happiness-tavern.png";

	/// <summary>Every line the season is made of, the original's own: what he charged, what the
	/// realm's rates cost here, how healthy the county is, what it was fed, and what simply happened.
	/// The sons taken for the army are charged the day they are taken, not here.</summary>
	private static readonly (string Name, System.Func<TurnSummary, float> Of)[] Lines =
	{
		("From taxes", season => season.LoyaltyFromTax),
		("From your other counties", season => season.LoyaltyFromNeighbours),
		("From health", season => season.LoyaltyFromHealth),
		("From the ration", season => season.LoyaltyFromRations),
		("From the world", season => season.LoyaltyFromEvents),
	};

	private Label _span;
	private Label _average;
	private Label _before;
	private Label _after;
	private HappinessChart _chart;
	private readonly List<Label> _values = new();

	protected override void Furnish()
	{
		// The years stand in the tavern, the way the old game hung its chart over a picture of the
		// people it is counting. The art is cropped to the frame rather than squashed into it, and
		// darkened under the bars — a chart drawn over a lit room is a chart nobody can read.
		var scene = new PanelContainer { CustomMinimumSize = new Vector2(0, SceneHeight), ClipContents = true };

		// The frame holds its contents off its own edge by the width of its border unless it is told
		// not to, and that gap is what leaves the years hovering above the floor of the picture they
		// are drawn on. The border still draws; it simply stops pushing.
		StyleBoxFlat frame = Chrome.CardStyle(new Color(0.05f, 0.045f, 0.04f, 1f));
		frame.ContentMarginLeft = frame.ContentMarginRight = 0;
		frame.ContentMarginTop = frame.ContentMarginBottom = 0;
		scene.AddThemeStyleboxOverride("panel", frame);
		Column.AddChild(scene);

		scene.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(TavernArtPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		});

		scene.AddChild(new ColorRect
		{
			Color = new Color(0.04f, 0.035f, 0.03f, 0.45f),
			MouseFilter = MouseFilterEnum.Ignore,
		});

		var over = new MarginContainer();
		// Nothing at the bottom: the years stand on the floor of the picture, the way the old game's
		// chart stood on the bottom edge of its own. Room at the top only, for the lids.
		foreach ((string side, int room) in new[] { ("left", 14), ("right", 14), ("top", 14), ("bottom", 0) })
		{
			over.AddThemeConstantOverride($"margin_{side}", room);
		}

		scene.AddChild(over);

		_chart = new HappinessChart { MouseFilter = MouseFilterEnum.Ignore };
		over.AddChild(_chart);

		var under = new HBoxContainer();
		under.AddThemeConstantOverride("separation", 12);
		Column.AddChild(under);

		_span = Chrome.Line("", 17, Chrome.Dim);
		under.AddChild(_span);

		_average = Chrome.Line("", 17, Chrome.Soft);
		_average.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_average.HorizontalAlignment = HorizontalAlignment.Right;
		under.AddChild(_average);

		var table = new GridContainer { Columns = 2 };
		table.AddThemeConstantOverride("h_separation", 18);
		table.AddThemeConstantOverride("v_separation", 6);
		Column.AddChild(table);

		_before = Headline(table, "Last season");
		foreach ((string name, System.Func<TurnSummary, float> _) in Lines)
		{
			_values.Add(Breakdown(table, name));
		}

		_after = Headline(table, "This season");

	}

	/// <summary>One of the two figures the lord actually remembers: where the county stood before
	/// the season and where it stands after. Set in the larger hand, with a heart on it.</summary>
	private static Label Headline(GridContainer table, string name)
	{
		table.AddChild(Chrome.Line(name, 24, Chrome.Cream));

		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cell.AddThemeConstantOverride("separation", 8);
		cell.Alignment = BoxContainer.AlignmentMode.End;

		Label value = Chrome.Line("", 26, Chrome.Bright);
		cell.AddChild(value);
		cell.AddChild(Chrome.Icon("heart", 30));
		table.AddChild(cell);
		return value;
	}

	/// <summary>One line of the account, indented under the season it belongs to.</summary>
	private static Label Breakdown(GridContainer table, string name)
	{
		var indent = new MarginContainer();
		indent.AddThemeConstantOverride("margin_left", 22);
		indent.AddChild(Chrome.Line(name, 18, Chrome.Soft));
		table.AddChild(indent);

		Label value = Chrome.Line("", 18, Chrome.Soft);
		value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		value.HorizontalAlignment = HorizontalAlignment.Right;
		table.AddChild(value);
		return value;
	}

	public void Open(ProvinceEconomy province, GameBalance balance, TurnSummary lastSeason, int firstYear)
	{
		Chart(province, balance, firstYear);
		Season(province, lastSeason);
		Reveal(province.ProvinceName);
	}

	/// <summary>A bar a year, the most recent twenty. Coloured by where the year stood rather than
	/// all one colour: the shape of a reign is what this chart is for, and "the three bad years"
	/// should be findable without reading a single number.</summary>
	private void Chart(ProvinceEconomy province, GameBalance balance, int firstYear)
	{
		List<float> years = province.HappinessByYear;
		int from = Mathf.Max(0, years.Count - MostYears);
		int counted = years.Count - from;

		var shown = new float[Mathf.Max(0, counted)];
		float total = 0f;
		for (int year = from; year < years.Count; year++)
		{
			total += years[year];
			shown[year - from] = years[year];
		}

		_chart.Draws(shown, balance);

		if (counted == 0)
		{
			_span.Text = "";
			_average.Text = "No year has turned yet";
			return;
		}

		_span.Text = counted == 1
			? $"{firstYear + from}"
			: $"{firstYear + from} – {firstYear + years.Count - 1}";
		_average.Text = $"Average happiness	  {Mathf.RoundToInt(total / counted)}";
	}

	/// <summary>The season just gone. Before its first turn a county has no season to account for,
	/// and the page says so rather than drawing a column of zeroes that look like findings.</summary>
	private void Season(ProvinceEconomy province, TurnSummary lastSeason)
	{
		_after.Text = Mathf.RoundToInt(province.Loyalty).ToString();

		if (lastSeason == null)
		{
			_before.Text = "—";
			foreach (Label value in _values)
			{
				value.Text = "";
			}

			return;
		}

		_before.Text = Mathf.RoundToInt(lastSeason.LoyaltyBefore).ToString();
		for (int line = 0; line < Lines.Length; line++)
		{
			float hearts = Lines[line].Of(lastSeason);

			// Nothing at all is written as nothing. A column of "0.0" down a page is seven answers
			// where there is one, and the eye has to read every one of them to find the line that
			// actually did something.
			//
			// Tested against a hair rather than against zero, and not left to the format string's own
			// empty third section — that one quietly falls back to the positive section, so a line
			// that did nothing prints "+0.0" and reads as a finding. A grievance too small to round
			// to a tenth is a grievance nobody has.
			bool nothing = Mathf.Abs(hearts) < 0.05f;
			_values[line].Text = nothing ? "" : $"{hearts:+0.0;-0.0}";
			_values[line].AddThemeColorOverride("font_color", hearts < 0f ? Chrome.Short : Chrome.Gain);
		}
	}

}
