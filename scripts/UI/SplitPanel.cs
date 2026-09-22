using System.Collections.Generic;
using Godot;

/// <summary>Where a company is cut in two: which men stay under the old banner, and which walk off
/// under the new one.
///
/// Halving it evenly was the easy answer and the wrong one. The whole reason to raise a second
/// banner is that the two bodies of men are for different work — a lord leaving a ford held leaves
/// spearmen on it and takes his archers on with him — so the cut is made kind by kind. The one rule
/// is that neither banner may be raised over nobody: a company of nought men is a banner the map
/// would have to carry and the lord could never give an order to.</summary>
public partial class SplitPanel : Control
{
	private const float FadeSeconds = 0.18f;

	/// <summary>The fewest men either side may be left with. One, and not a round number picked to
	/// sound like an army: what makes a company is that somebody is standing there, and a lord who
	/// wants to leave a single man watching a bridge has said something about the bridge.</summary>
	private const int LeastCompany = 1;

	private VBoxContainer _kinds;
	private Label _tally;
	private Button _raise;
	private FieldArmy _army;
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
		centred.AddChild(Chrome.Framed(column, 28));

		Label title = Chrome.Line("Cut the company in two", 30, Chrome.Cream);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(title);
		column.AddChild(title);

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

		_raise = Order("Raise the new banner", () =>
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
	private static Control Heading()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 14);

		Label kind = Chrome.Line("", 16, Chrome.Dim);
		kind.CustomMinimumSize = new Vector2(150, 0);
		row.AddChild(kind);

		Label stays = Chrome.Line("Stay", 16, Chrome.Dim);
		stays.CustomMinimumSize = new Vector2(70, 0);
		stays.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(stays);

		row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

		Label goes = Chrome.Line("Go", 16, Chrome.Dim);
		goes.CustomMinimumSize = new Vector2(70, 0);
		row.AddChild(goes);
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
	/// business and not a panel's.</summary>
	public void Ask(FieldArmy army, System.Action<Dictionary<string, int>> split)
	{
		_army = army;
		_split = split;
		_going.Clear();
		foreach (Node row in _kinds.GetChildren())
		{
			row.QueueFree();
		}

		// The kinds he actually has, in the order the yard lists them, so this reads like the roster
		// he was looking at a moment ago.
		foreach (string kind in Units.All())
		{
			int men = army.Men.GetValueOrDefault(kind);
			if (men > 0)
			{
				_kinds.AddChild(Kind(kind, men));
			}
		}

		Reckon();
		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	/// <summary>One kind of man, with the grip that says how many of him walk off.</summary>
	private Control Kind(string kind, int men)
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
			Value = men / 2,
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
	/// A lord who has pushed every man across has not split a company, he has renamed it.</summary>
	private void Reckon()
	{
		int goes = 0;
		foreach (HSlider grip in _going.Values)
		{
			goes += (int)grip.Value;
		}

		int stays = _army.Strength - goes;
		_tally.Text = $"{stays:N0} stay under {_army.Home}, {goes:N0} march off";
		bool cut = stays >= LeastCompany && goes >= LeastCompany;
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
