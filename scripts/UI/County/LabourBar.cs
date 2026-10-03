using System;
using Godot;

/// <summary>Lords of the Realm's own labour bar: the wheat at one end, the hammer at the other, one
/// long bar between them, and the county's one peasant standing on it. Pushed toward the wheat he
/// sends hands to the farm, toward the hammer to the industry (Labour.Divide), dealt again the moment
/// he moves — the grip stands at the industry's share, so the arrow beside the wheat is the one that
/// feeds the county. Under it, the hands on the farm, the hands in the industry, and whoever neither
/// can use.
///
/// It keeps nothing of its own: the grip is where the county's IndustryShare is, every time the bar
/// is shown.</summary>
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
	private Label _farmShort;
	private Label _industryShort;
	private bool _reading;
	private TextureRect _wheat;
	private TextureRect _hammer;
	private Texture2D _grip;

	/// <summary>What a county with men standing about is written in.</summary>
	private static readonly Color Short = new("d98f6a");

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 4);

		// The wheat, the bar the whole way across to the hammer, the hammer.
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		AddChild(row);

		_wheat = Chrome.Icon("food", 30);
		row.AddChild(_wheat);

		_bar = new HSlider
		{
			MinValue = 0,
			MaxValue = 100,
			Step = 0, // the arrows move it a man at a time, so the grip stands between whole percents
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(40, 0),
			TooltipText = "Toward the wheat: more on the farm. Toward the hammer: more in the industry.",
		};

		// The grip is one of the county's people, as in Lords of the Realm: the man is what is moved.
		_grip = GD.Load<Texture2D>("res://assets/ui/icons/worker-grip.png");

		_bar.ValueChanged += share =>
		{
			if (_reading || _province == null || _definition == null)
			{
				return;
			}

			Labour.Divide(_province, _definition, _balance, _season, (int)share);
			Counts();
			Moved?.Invoke();
		};

		// No fill behind the grip: it would run from the wheat to the peasant and read as the farm's
		// share, which is the other end of the bar from where the farm's share now lies.
		_bar.AddThemeStyleboxOverride("grabber_area", new StyleBoxEmpty());
		_bar.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxEmpty());
		_bar.DragEnded += _ => Settled?.Invoke();
		row.AddChild(Nudge("◀", -1));
		row.AddChild(_bar);
		row.AddChild(Nudge("▶", 1));
		_hammer = Chrome.Icon("hammer", 30);
		row.AddChild(_hammer);

		// The hands under each end, and between them whoever neither end can use.
		var counts = new HBoxContainer();
		counts.AddThemeConstantOverride("separation", 6);
		AddChild(counts);

		_fields = Count(HorizontalAlignment.Left);
		counts.AddChild(_fields);

		// Hands nobody has given work to. It is the one number on this bar the lord did not choose,
		// and the one worth putting in front of him: a county with men standing about is a county
		// paying for a season it is not working.
		_spare = Chrome.Line("", 15, Short);
		_spare.HorizontalAlignment = HorizontalAlignment.Center;
		_spare.VerticalAlignment = VerticalAlignment.Center;
		_spare.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		counts.AddChild(_spare);

		_trades = Count(HorizontalAlignment.Right);
		counts.AddChild(_trades);

		// Under each end, the hands its work still wants: the one thing the bar has to tell a lord
		// before he ends the season with the harvest short.
		var wants = new HBoxContainer();
		wants.AddThemeConstantOverride("separation", 6);
		AddChild(wants);
		_farmShort = Chrome.Line("", 14, Short);
		_farmShort.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		wants.AddChild(_farmShort);
		_industryShort = Chrome.Line("", 14, Short);
		_industryShort.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_industryShort.HorizontalAlignment = HorizontalAlignment.Right;
		wants.AddChild(_industryShort);
	}

	/// <summary>The original's arrows at either end of the bar: one man across a click, held to keep
	/// him walking, for the lord who wants the grip exactly where he means it. A percent a click was
	/// six men in a county of six hundred, and no way to put back the two that tipped the farm short.</summary>
	private Button Nudge(string arrow, int step)
	{
		Button nudge = Chrome.Repeating(arrow, 28, () =>
		{
			if (_province == null || _definition == null)
			{
				return;
			}

			int industry = Labour.Pct(_province.Workers, _province.IndustryShare);
			Labour.Divide(_province, _definition, _balance, _season, Labour.ShareOf(industry + step, _province.Workers));
			Grip();
			Counts();
			Moved?.Invoke();
		});
		nudge.ButtonUp += () => Settled?.Invoke();
		return nudge;
	}

	/// <summary>How many more hands a half's jobs could use before none of them is short. The
	/// diggings are never short (Labour.Wanted), so the industry's want is the masons' and the smiths'.</summary>
	private int Missing(string[] jobs)
	{
		int missing = 0;
		foreach (string job in jobs)
		{
			missing += Mathf.Max(0, Labour.Wanted(_province, _definition, _balance, _season, job) - Labour.Hands(_province, job));
		}

		return missing;
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

		Grip();
		Counts();
	}

	/// <summary>Sets the grip to where the county already stands, which must not re-deal the county.</summary>
	private void Grip()
	{
		_reading = true;
		_bar.Value = _province.IndustryShare;
		_reading = false;
	}

	private void Counts()
	{
		_fields.Text = (_province.GrainWorkers + _province.CattleWorkers + _province.ReclaimWorkers).ToString("N0");
		_trades.Text = (_province.WoodWorkers + _province.StoneWorkers + _province.IronWorkers + _province.SmithWorkers
			+ _province.BuildWorkers).ToString("N0");

		int spare = EconomySimulation.Idle(_province, _definition, _balance, _season);
		// Empty rather than hidden, so the industry's count stays under the hammer.
		_spare.Text = spare > 0 ? $"{spare:N0} idle" : "";

		int farm = Missing(Labour.Farm);
		int industry = Missing(Labour.Industry);
		_farmShort.Text = farm > 0 ? $"{farm:N0} short" : "";
		_industryShort.Text = industry > 0 ? $"{industry:N0} short" : "";

		// As in the original: the peasant is ringed in blue while men stand about; an end of the bar
		// is ringed in red while its work wants hands, and in blue while it has more than its work
		// can use and the rest of them are standing idle on its side.
		Texture2D grip = spare > 0 ? Stroke.Ringed(_grip, Stroke.Idle, _grip.GetWidth()) : _grip;
		_bar.AddThemeIconOverride("grabber", grip);
		_bar.AddThemeIconOverride("grabber_highlight", grip);
		int industryHalf = Labour.Pct(_province.Workers, _province.IndustryShare);
		Ring(_wheat, "food", farm, _province.Workers - industryHalf - OnJobs(Labour.Farm));
		Ring(_hammer, "hammer", industry, industryHalf - OnJobs(Labour.Industry));
	}

	private int OnJobs(string[] jobs)
	{
		int hands = 0;
		foreach (string job in jobs)
		{
			hands += Labour.Hands(_province, job);
		}

		return hands;
	}

	private static void Ring(TextureRect end, string icon, int missing, int idle)
	{
		var picture = GD.Load<Texture2D>($"{Chrome.IconDirectory}/{icon}.png");
		int side = (int)end.CustomMinimumSize.X;
		end.Texture = missing > 0 ? Stroke.Ringed(picture, Stroke.Wanting, side)
			: idle > 0 ? Stroke.Ringed(picture, Stroke.Idle, side)
			: picture;
	}

	private static Label Count(HorizontalAlignment side)
	{
		Label reading = Chrome.Line("0", 17, Chrome.Bright);
		reading.CustomMinimumSize = new Vector2(46, 0);
		reading.HorizontalAlignment = side;
		reading.VerticalAlignment = VerticalAlignment.Center;
		return reading;
	}
}
