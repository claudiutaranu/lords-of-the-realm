using Godot;

/// <summary>The one question two of a lord's companies raise by halting in the same field: are they
/// one army now, or two?
///
/// Asked and not assumed, in either direction. A map that quietly poured every arriving company
/// into the one already standing there would take away the only reason to raise a second — men left
/// holding a ford while the rest go on — and a map that never joined them would leave a lord
/// shepherding four banners across his own county for the rest of the campaign.</summary>
public partial class JoinPanel : Control
{
	private const float FadeSeconds = 0.18f;

	private Label _reading;
	private System.Action _joined;

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

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
		column.AddThemeConstantOverride("separation", 12);
		centred.AddChild(Chrome.Framed(column, 26));

		Label title = Chrome.Line("Two banners, one field", 26, Chrome.Cream);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(title);
		column.AddChild(title);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(rule);

		_reading = Chrome.Line("", 17, Chrome.Soft);
		_reading.HorizontalAlignment = HorizontalAlignment.Center;
		_reading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		column.AddChild(_reading);

		column.AddChild(Order("Put them under one banner", () =>
		{
			System.Action joining = _joined;
			Close();
			joining?.Invoke();
		}));

		column.AddChild(Order("Leave them as they are", Close));
	}

	private static Button Order(string what, System.Action pressed)
	{
		var order = new Button { Text = what, CustomMinimumSize = new Vector2(0, 46) };
		order.AddThemeFontSizeOverride("font_size", 18);
		order.Pressed += pressed;
		return order;
	}

	/// <summary>Asks it, over these two. <paramref name="joined"/> is what the lord saying yes
	/// means — the panel decides nothing itself, because what joining does to the ledger is the
	/// ledger's business.</summary>
	public void Ask(FieldArmy arriving, FieldArmy standing, System.Action joined)
	{
		_joined = joined;

		// Two companies of the same county are the usual case, and naming it three times in one
		// sentence reads like a ledger rather than like a lord being told something.
		string who = arriving.Home == standing.Home
			? $"{arriving.Strength:N0} men of {arriving.Home} have halted beside {standing.Strength:N0} more "
				+ "of their own"
			: $"{arriving.Strength:N0} men of {arriving.Home} have halted beside {standing.Strength:N0} "
				+ $"of {standing.Home}";

		string where = arriving.County == arriving.Home
			? "on their own ground"
			: $"in {arriving.County}";

		_reading.Text = $"{who}, {where}. Under one banner they march as one company, with the ground "
			+ "the slower of them has left.";

		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	private void Close()
	{
		_joined = null;
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>Anywhere else, or Escape, is "leave them as they are": the standing answer to a
	/// question the lord did not ask for is the one that changes nothing.</summary>
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
