using System;
using Godot;

/// <summary>Lords of the Realm's own labour bar: bread at one end, tools at the other, and the
/// county's hands slid between them.
///
/// What each half does with the men it is handed is the season's business — grain before cattle,
/// timber before iron before stone — and the plaques are there for the lord who wants to argue
/// with that. A half given more hands than it has work for leaves the rest standing about, which
/// is the point of a bar: it is a decision, and men sent to the mine do not quietly walk back to
/// the harvest because the harvest was short.
///
/// It keeps nothing of its own. Where the grip sits is read off the allocation every time the bar
/// is shown, so it can never disagree with the plaques: move a man with a plaque and the bar has
/// already moved.</summary>
public partial class LabourBar : VBoxContainer
{
	/// <summary>Raised while the grip is moving: whatever else reads the allocation should follow
	/// it, but nothing may tear this bar down — freeing it under the hand that is dragging it takes
	/// the drag with it.</summary>
	public event Action Moved;

	/// <summary>Raised when the hand lets go, which is when a panel may rebuild itself.</summary>
	public event Action Settled;

	private ProvinceEconomy _province;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;
	private HSlider _bar;
	private Label _fields;
	private Label _trades;
	private Label _spare;
	private bool _reading;

	/// <summary>What a county with men standing about is written in.</summary>
	private static readonly Color Short = new("d98f6a");

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 4);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		AddChild(row);

		row.AddChild(Chrome.Icon("food", 30));

		_fields = Count(HorizontalAlignment.Right);
		row.AddChild(_fields);

		// A pair of arrows beside the grip, because a hand on a slider moves in jumps and a lord
		// shifting the last twenty men off the harvest should not have to fight it.
		row.AddChild(Chrome.Plate("\u25c2", 34, () => Nudge(-1)));

		_bar = new HSlider
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 5,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(120, 0),
			TooltipText = "The fields on the left, the trades on the right.",
		};

		_bar.ValueChanged += share =>
		{
			// Setting the grip to where the county already stands must not re-deal the county.
			if (_reading || _province == null || _definition == null)
			{
				return;
			}

			_province.LabourSplit = (float)share / 100f;
			EconomySimulation.Split(_province, _definition, _balance, _season, _province.LabourSplit);
			Counts();
			Moved?.Invoke();
		};

		_bar.DragEnded += _ => Settled?.Invoke();
		row.AddChild(_bar);

		row.AddChild(Chrome.Plate("\u25b8", 34, () => Nudge(1)));

		_trades = Count(HorizontalAlignment.Left);
		row.AddChild(_trades);
		row.AddChild(Chrome.Icon("hammer", 30));

		// Hands nobody has given work to. It is the one number on this bar the lord did not choose,
		// and the one worth putting in front of him: a county with men standing about is a county
		// paying for a season it is not working.
		_spare = Chrome.Line("", 15, Short);
		_spare.HorizontalAlignment = HorizontalAlignment.Center;
		AddChild(_spare);
	}

	/// <summary>One step of the bar, from a button rather than from the grip. The split is settled
	/// straight away: there is no drag to be taken out from under.</summary>
	private void Nudge(int step)
	{
		if (_province == null)
		{
			return;
		}

		_bar.Value = Mathf.Clamp(_bar.Value + step * _bar.Step, _bar.MinValue, _bar.MaxValue);
		Settled?.Invoke();
	}

	/// <summary>Points the bar at a county, or at none — a province nobody is running has no hands
	/// to divide and no bar to divide them with.</summary>
	public void Show(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance,
		Season season)
	{
		_province = province;
		_definition = definition;
		_balance = balance;
		_season = season;
		Visible = province != null && definition != null;
		if (!Visible)
		{
			return;
		}

		_reading = true;
		_bar.Value = Mathf.RoundToInt(100f * (province.LabourSplit >= 0f
			? province.LabourSplit
			: EconomySimulation.TradesShare(province)));
		_reading = false;
		Counts();
	}

	private void Counts()
	{
		_fields.Text = (_province.GrainWorkers + _province.CattleWorkers).ToString("N0");
		_trades.Text = (_province.WoodWorkers + _province.StoneWorkers + _province.IronWorkers).ToString("N0");

		int spare = EconomySimulation.Idle(_province, _definition, _balance, _season);
		_spare.Text = spare > 0 ? $"{spare:N0} standing idle" : "";
		_spare.Visible = spare > 0;
	}

	private static Label Count(HorizontalAlignment side)
	{
		Label reading = Chrome.Line("0", 17, new Color("f4e4c1"));
		reading.CustomMinimumSize = new Vector2(46, 0);
		reading.HorizontalAlignment = side;
		reading.VerticalAlignment = VerticalAlignment.Center;
		return reading;
	}
}
