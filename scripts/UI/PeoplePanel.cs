using System.Collections.Generic;
using Godot;

/// <summary>A county's people over the seasons: how healthy they have been, and how many were born
/// and how many died each season, drawn (<see cref="PeopleChart"/>), with the season just gone and the
/// totals under it in figures. Read-only: nothing is decided here, a lord comes to see whether his
/// county is growing or burying itself, and why.</summary>
public partial class PeoplePanel : CountyPanel
{
	protected override int Width => 560;

	/// <summary>How tall the chart is, and how many seasons it shows before the oldest drop off:
	/// five years, twenty columns, each wide enough to read.</summary>
	private const int ChartHeight = 240;
	private const int MostSeasons = 20;
	private const int SeasonsPerYear = 4;

	private static readonly Color HealthInk = new(0.93f, 0.83f, 0.58f);
	private static readonly Color BornInk = new(0.50f, 0.66f, 0.39f);
	private static readonly Color DiedInk = new(0.78f, 0.35f, 0.28f);

	private PeopleChart _chart;
	private Label _span;
	private Label _people;
	private Label _health;
	private Label _born;
	private Label _died;
	private Label _bornAll;
	private Label _diedAll;
	private Label _spanTotals;

	protected override void Furnish()
	{
		// In the tavern, like the goodwill: the two charts of a county are one set.
		_chart = new PeopleChart();
		Column.AddChild(ChartArt.Scene(_chart, ChartHeight, (14, 14, 14, 14)));

		var legend = new HBoxContainer();
		legend.AddThemeConstantOverride("separation", 16);
		Column.AddChild(legend);
		legend.AddChild(Chrome.Line("● Health", 16, HealthInk));
		legend.AddChild(Chrome.Line("■ Born", 16, BornInk));
		legend.AddChild(Chrome.Line("■ Died", 16, DiedInk));
		_span = Chrome.Line("", 16, Chrome.Dim);
		_span.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_span.HorizontalAlignment = HorizontalAlignment.Right;
		legend.AddChild(_span);

		var table = new GridContainer { Columns = 2 };
		table.AddThemeConstantOverride("h_separation", 18);
		table.AddThemeConstantOverride("v_separation", 6);
		Column.AddChild(table);

		table.AddChild(Chrome.Line("Last season", 22, Chrome.Cream));
		table.AddChild(new Control());
		_people = Row(table, "People");
		_health = Row(table, "Health");
		_born = Row(table, "Born");
		_died = Row(table, "Died");

		_spanTotals = Chrome.Line("", 22, Chrome.Cream);
		table.AddChild(_spanTotals);
		table.AddChild(new Control());
		_bornAll = Row(table, "Born");
		_diedAll = Row(table, "Died");
	}

	private static Label Row(GridContainer table, string name)
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

	public void Open(ProvinceEconomy province, int firstYear)
	{
		List<PeopleSeason> kept = province.PeopleBySeason;
		int from = Mathf.Max(0, kept.Count - MostSeasons);
		PeopleSeason[] shown = kept.GetRange(from, kept.Count - from).ToArray();
		_chart.Draws(shown);

		_people.Text = province.Population.ToString("N0");
		_health.Text = $"{province.Health}  ({Livelihood.BandOf(province.Health)})";
		if (shown.Length == 0)
		{
			// Before its first turn a county has no season to show, and says so rather than drawing a
			// row of noughts that look like a finding.
			_span.Text = "No season has turned yet";
			_born.Text = _died.Text = _bornAll.Text = _diedAll.Text = "—";
			_spanTotals.Text = "";
			Reveal(province.ProvinceName);
			return;
		}

		PeopleSeason last = shown[^1];
		_born.Text = last.Born.ToString("N0");
		_died.Text = last.Died.ToString("N0");

		int born = 0;
		int died = 0;
		foreach (PeopleSeason season in shown)
		{
			born += season.Born;
			died += season.Died;
		}

		string first = SeasonName(shown[0].Turn, firstYear);
		_span.Text = shown.Length == 1 ? first : $"{first} – {SeasonName(last.Turn, firstYear)}";
		_spanTotals.Text = shown.Length == 1 ? "That season" : $"Over these {shown.Length} seasons";
		_bornAll.Text = born.ToString("N0");
		_diedAll.Text = died.ToString("N0");
		Reveal(province.ProvinceName);
	}

	/// <summary>"Spring 1268" for a turn, counted from the campaign's first.</summary>
	private static string SeasonName(int turn, int firstYear) =>
		$"{(Season)((turn - 1) % SeasonsPerYear)} {firstYear + ((turn - 1) / SeasonsPerYear)}";
}
