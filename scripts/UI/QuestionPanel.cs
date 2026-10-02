using Godot;

/// <summary>A question the map puts to the lord before it does something he cannot take back: two
/// companies in one field joined, a march on another lord's gate. Yes does it; no, anywhere else or
/// Escape changes nothing, because the standing answer to a question is the one that changes nothing.</summary>
public partial class QuestionPanel : Control
{
	private const float FadeSeconds = 0.18f;

	private Label _title;
	private Label _reading;
	private Button _yes;
	private Button _no;
	private System.Action _agreed;

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
		centred.AddChild(Chrome.Painted(column));

		_title = Chrome.Line("", 26, Chrome.Cream);
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_title);
		column.AddChild(_title);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(rule);

		_reading = Chrome.Line("", 17, Chrome.Soft);
		_reading.HorizontalAlignment = HorizontalAlignment.Center;
		_reading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		column.AddChild(_reading);

		_yes = Order("", () =>
		{
			System.Action agreed = _agreed;
			Close();
			agreed?.Invoke();
		});
		column.AddChild(_yes);

		_no = Order("", Close);
		column.AddChild(_no);
	}

	private static Button Order(string what, System.Action pressed)
	{
		var order = new Button { Text = what, CustomMinimumSize = new Vector2(0, 46) };
		order.AddThemeFontSizeOverride("font_size", 18);
		order.Pressed += pressed;
		return order;
	}

	/// <summary>Puts the question. <paramref name="agreed"/> is what yes means — the panel decides
	/// nothing itself, because what it does to the ledger is the ledger's business.</summary>
	public void Ask(string title, string reading, string yes, string no, System.Action agreed)
	{
		_agreed = agreed;
		_title.Text = title;
		_reading.Text = reading;
		_yes.Text = yes;
		_no.Text = no;

		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	private void Close()
	{
		_agreed = null;
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>Anywhere else, or Escape, is no.</summary>
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
