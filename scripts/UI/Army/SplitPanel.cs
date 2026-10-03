using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>Where a company is cut in two: which men stay under the old banner, and which walk off
/// under the new one.
///
/// Halving it evenly was the easy answer and the wrong one. The whole reason to raise a second
/// banner is that the two bodies of men are for different work — a lord leaving a ford held leaves
/// spearmen on it and takes his archers on with him — so the cut is made kind by kind. The one rule
/// is that neither banner may be raised over nobody: a company of nought men is a banner the map
/// would have to carry and the lord could never give an order to.
///
/// The same question, kind by kind, is what a castle asks when more men come to its gate than it has
/// room for, so the panel asks that too (Garrisoning): who goes up, and who stays in the field.</summary>
public partial class SplitPanel : Control
{
	/// <summary>What is being asked. <paramref name="Most"/> is how many may go; <paramref name="Whole"/>
	/// lets every man go — the walls can take a company entire, a new banner cannot — and opens the
	/// grips filled up to Most rather than halved. <paramref name="Tally"/> says the answer back,
	/// from how many stay and how many go.</summary>
	public sealed record Cut(string Title, string Order, string Stay, string Go, int Most, bool Whole,
		System.Func<FieldArmy, int, int, string> Tally);

	public static readonly Cut InTwo = new("Cut the company in two", "Raise the new banner", "Stay", "Go",
		int.MaxValue, false, (army, stays, goes) => $"{stays:N0} stay under {army.Home}, {goes:N0} march off");

	public static Cut Garrisoning(string county, int room) => new($"The walls of {county}", "Send them up",
		"Field", "Walls", room, true, (_, _, goes) => $"{goes:N0} go up onto the walls, room for {room:N0}");

	private const float FadeSeconds = 0.18f;

	/// <summary>The fewest men either side may be left with. One, and not a round number picked to
	/// sound like an army: what makes a company is that somebody is standing there, and a lord who
	/// wants to leave a single man watching a bridge has said something about the bridge.</summary>
	private const int LeastCompany = 1;

	private VBoxContainer _kinds;
	private Label _title;
	private Label _stayHead;
	private Label _goHead;
	private Label _tally;
	private Button _raise;
	private FieldArmy _army;
	private Cut _cut = InTwo;
	private System.Action<Dictionary<string, int>> _split;

	/// <summary>How many of each kind the lord has pushed across to the new banner.</summary>
	private readonly Dictionary<string, HSlider> _going = new();

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.5f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer();
		Chrome.Fill(centred);
		centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centred);

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(760, 0) };
		column.AddThemeConstantOverride("separation", 14);
		centred.AddChild(Chrome.Painted(column));

		_title = Chrome.Line("", 30, Chrome.Cream);
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_title);
		column.AddChild(_title);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(rule);

		column.AddChild(Heading());

		_kinds = new VBoxContainer();
		_kinds.AddThemeConstantOverride("separation", 10);
		column.AddChild(_kinds);

		column.AddChild(Chrome.Rule(0));
		_tally = Chrome.Line("", 20, Chrome.Bright);
		_tally.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_tally);

		var orders = new HBoxContainer();
		orders.AddThemeConstantOverride("separation", 14);
		column.AddChild(orders);

		_raise = Order("", () =>
		{
			Dictionary<string, int> going = Going();
			System.Action<Dictionary<string, int>> cut = _split;
			Close();
			cut?.Invoke(going);
		});
		orders.AddChild(_raise);
		orders.AddChild(Order("Leave them as they are", Close));
	}

	/// <summary>Which end of every row is which, said once at the top instead of on each line.</summary>
	private Control Heading()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);

		Label kind = Chrome.Line("", 16, Chrome.Dim);
		kind.CustomMinimumSize = new Vector2(150, 0);
		row.AddChild(kind);

		_stayHead = Chrome.Line("", 16, Chrome.Dim);
		_stayHead.CustomMinimumSize = new Vector2(70, 0);
		_stayHead.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(_stayHead);

		row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

		_goHead = Chrome.Line("", 16, Chrome.Dim);
		_goHead.CustomMinimumSize = new Vector2(70, 0);
		row.AddChild(_goHead);
		return row;
	}

	private static Button Order(string what, System.Action pressed)
	{
		var order = new Button
		{
			Text = what,
			CustomMinimumSize = new Vector2(0, 52),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		order.AddThemeFontSizeOverride("font_size", 20);
		order.Pressed += pressed;
		return order;
	}

	/// <summary>Asks it, over this company. <paramref name="split"/> is what the lord saying yes
	/// means — how many of each kind go — because what a split does to the ledger is the ledger's
	/// business and not a panel's. <paramref name="cut"/> is which question; a split in two when
	/// left out.</summary>
	public void Ask(FieldArmy army, System.Action<Dictionary<string, int>> split, Cut cut = null)
	{
		_army = army;
		_split = split;
		_cut = cut ?? InTwo;
		_title.Text = _cut.Title;
		_stayHead.Text = _cut.Stay;
		_goHead.Text = _cut.Go;
		_raise.Text = _cut.Order;
		_going.Clear();
		foreach (Node row in _kinds.GetChildren())
		{
			row.QueueFree();
		}

		// Walls are filled bows first: a man who shoots is worth twice as much behind a parapet as in
		// front of one, and a lord who wants his spearmen up there instead can say so.
		var kinds = new List<string>(Units.All());
		var going = new Dictionary<string, int>();
		int room = _cut.Most;
		foreach (string kind in kinds.OrderByDescending(kind => Units.Of(kind).Range))
		{
			int men = army.Men.GetValueOrDefault(kind);
			going[kind] = _cut.Whole ? Mathf.Min(men, room) : men / 2;
			room -= going[kind];
		}

		// Listed in the order the yard lists them, so this reads like the roster he was looking at a
		// moment ago.
		foreach (string kind in kinds)
		{
			int men = army.Men.GetValueOrDefault(kind);
			if (men > 0)
			{
				_kinds.AddChild(Kind(kind, men, going[kind]));
			}
		}

		Reckon();
		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	/// <summary>One kind of man, with the grip that says how many of him walk off.</summary>
	private Control Kind(string kind, int men, int going)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);

		Label name = Chrome.Line(Units.Of(kind).Name, 20, Chrome.Bright);
		name.CustomMinimumSize = new Vector2(150, 0);
		name.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(name);

		Label stays = Chrome.Line("", 20, Chrome.Bright);
		stays.CustomMinimumSize = new Vector2(70, 0);
		stays.HorizontalAlignment = HorizontalAlignment.Right;
		stays.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(stays);

		var grip = new HSlider
		{
			MinValue = 0,
			MaxValue = men,
			Step = 1,
			Value = going,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(240, 0),
		};
		row.AddChild(grip);

		Label goes = Chrome.Line("", 20, Chrome.Bright);
		goes.CustomMinimumSize = new Vector2(70, 0);
		goes.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(goes);

		grip.ValueChanged += _ =>
		{
			stays.Text = (men - (int)grip.Value).ToString("N0");
			goes.Text = ((int)grip.Value).ToString("N0");
			Reckon();
		};

		stays.Text = (men - (int)grip.Value).ToString("N0");
		goes.Text = ((int)grip.Value).ToString("N0");
		_going[kind] = grip;
		return row;
	}

	private Dictionary<string, int> Going()
	{
		var going = new Dictionary<string, int>();
		foreach ((string kind, HSlider grip) in _going)
		{
			going[kind] = (int)grip.Value;
		}

		return going;
	}

	/// <summary>What the two banners come to as the grips stand, and whether that is a cut at all.
	/// A lord who has pushed every man across has not split a company, he has renamed it — unless
	/// what he is sending them into is walls — and nobody goes where there is no room for them.</summary>
	private void Reckon()
	{
		int goes = 0;
		foreach (HSlider grip in _going.Values)
		{
			goes += (int)grip.Value;
		}

		int stays = _army.Strength - goes;
		_tally.Text = _cut.Tally(_army, stays, goes);
		bool cut = (_cut.Whole || stays >= LeastCompany) && goes >= LeastCompany && goes <= _cut.Most;
		_raise.Disabled = !cut;
		_tally.AddThemeColorOverride("font_color", cut ? Chrome.Bright : Chrome.Short);
	}

	private void Close()
	{
		_split = null;
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>Anywhere else, or Escape, leaves the company as it is: the standing answer to a
	/// question nobody has finished asking is the one that changes nothing.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Visible && @event is InputEventMouseButton { Pressed: true })
		{
			Close();
			AcceptEvent();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
