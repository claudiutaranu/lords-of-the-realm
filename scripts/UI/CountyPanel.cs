using Godot;

/// <summary>The shell every county table is served on: the map dimmed behind it, one column in the
/// painted frame every modal wears (Chrome.Painted) with the county's name on its ribbon, and a way
/// out of it.
///
/// Here rather than copied into each table because the way out is the part that goes wrong. Three
/// panels written separately are three chances for one of them to swallow Escape, or to leave the
/// map clickable underneath, or to fade at a different speed — and a player who finds that one
/// panel closes differently from the others has learned something untrue about the game.</summary>
public abstract partial class CountyPanel : Control
{
	private const float FadeSeconds = 0.18f;

	/// <summary>The column a table fills. Everything a panel adds goes in here, between the county's
	/// name and the way out.</summary>
	protected VBoxContainer Column { get; private set; }

	private Label _county;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		// The map behind is dimmed and takes no clicks: a decision is being made on this county, and
		// a stray click landing on another one would move the whole page under the player's hand.
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

		Column = new VBoxContainer { CustomMinimumSize = new Vector2(Width, 0) };
		Column.AddThemeConstantOverride("separation", 12);
		centred.AddChild(Chrome.Painted(Column));

		_county = Chrome.Line("", 32, Chrome.Cream);
		_county.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_county);
		Column.AddChild(_county);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		Column.AddChild(rule);

		Furnish();

		var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(0, 42) };
		done.AddThemeFontSizeOverride("font_size", 18);
		done.Pressed += Close;
		Column.AddChild(done);
	}

	/// <summary>How wide the table wants to be. The frame grows past it where the contents ask for
	/// more; this is the floor, so a panel with little to say is not a narrow strip.</summary>
	protected virtual int Width => 520;

	/// <summary>Fills the column. Called once, with the name and the way out already in place.</summary>
	protected abstract void Furnish();

	/// <summary>Puts the county's name up and brings the panel in. A table calls this once it has
	/// its own figures ready, so nothing is ever shown half filled.</summary>
	protected void Reveal(string county)
	{
		_county.Text = county;
		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	protected void Close()
	{
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>A click on the dimmed map behind closes it, and so does Escape. Nothing here is
	/// confirmed or cancelled — a table changes the county as it goes — so there is one way out and
	/// it is the obvious one.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true })
		{
			Close();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			Close();
			AcceptEvent();
		}
	}
}
