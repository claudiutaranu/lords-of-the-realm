using Godot;

/// <summary>The painted table a company is laid out on: the map dimmed behind it, the blue field in
/// its gold border, a title lettered on the ribbon across the top and a cross in the corner to put
/// it away.
///
/// Shared, because a lord who opens his own company and then marches it at somebody else's gate has
/// to see the same table both times. Two frames drawn separately are two frames that drift — one
/// fades at another speed, one swallows Escape — and the second of them looks like it came from
/// another game.</summary>
public abstract partial class PaintedPanel : Control
{
	private const string FramePath = "res://assets/ui/army-panel.png";
	private const float FadeSeconds = 0.16f;

	/// <summary>Where the painted frame's writing begins and ends, as a share of however tall the
	/// frame is drawn. Shares and not a number of pixels, because the ribbon across its top and the
	/// rail across its foot are part of the picture: a panel cut shorter draws them nearer together,
	/// and the title has to stay on the ribbon rather than under it.</summary>
	private const float RibbonShare = 0.119f;
	private const float FootShare = 0.154f;
	private const float CloseShare = 0.127f;
	private const int FrameInset = 74;

	/// <summary>The column a panel fills, from the ribbon down to the rail.</summary>
	protected VBoxContainer Column { get; private set; }

	/// <summary>Lettered on the painted ribbon.</summary>
	protected Label Title { get; private set; }

	private CenterContainer _centred;
	private TextureRect _frame;
	private MarginContainer _inside;
	private Button _close;

	/// <summary>How big the frame is drawn when nothing asks it to be shorter.</summary>
	protected abstract Vector2 PanelSize { get; }

	/// <summary>Fills the column, under the title. Called once.</summary>
	protected abstract void Furnish();

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

		_centred = new CenterContainer();
		Chrome.Fill(_centred);
		_centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_centred);

		_frame = new TextureRect
		{
			Texture = GD.Load<Texture2D>(FramePath),
			CustomMinimumSize = PanelSize,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Stop,
		};
		_centred.AddChild(_frame);

		_inside = new MarginContainer();
		Chrome.Fill(_inside);
		_inside.AddThemeConstantOverride("margin_left", FrameInset);
		_inside.AddThemeConstantOverride("margin_right", FrameInset);
		_frame.AddChild(_inside);

		Column = new VBoxContainer();
		Column.AddThemeConstantOverride("separation", 3);
		_inside.AddChild(Column);

		Title = Chrome.Line("", 36, Chrome.Cream);
		Title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(Title);
		Column.AddChild(Title);

		Furnish();

		// The way out, in the corner of the frame, where a painted panel keeps it.
		_close = new Button
		{
			Text = "✕",
			CustomMinimumSize = new Vector2(38, 38),
			Flat = true,
		};
		_close.AddThemeFontSizeOverride("font_size", 24);
		_close.AddThemeColorOverride("font_color", Chrome.Cream);
		_close.Pressed += Close;
		_frame.AddChild(_close);
		Draw(PanelSize.Y);
	}

	/// <summary>Draws the frame this tall, and sets the writing away from the painted ribbon at the
	/// top and the rail at the foot to match.</summary>
	protected void Draw(float high)
	{
		_frame.CustomMinimumSize = new Vector2(PanelSize.X, high);
		_inside.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(high * RibbonShare));
		_inside.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(high * FootShare));

		// The way out rides in the frame's own top corner, which comes down with it.
		_close.Position = new Vector2(PanelSize.X - 134f, high * CloseShare);
	}

	/// <summary>The share of the frame the ribbon and the rail take, so a panel cut shorter can take
	/// its share of them away with it.</summary>
	protected static float Writable => 1f - RibbonShare - FootShare;

	/// <summary>Brings the panel in, centred on the ground and not on the window: the lord's
	/// furniture takes the right-hand side of the screen, so the panel goes in the middle of what is
	/// left of the map.</summary>
	protected void Reveal()
	{
		Control furniture = GetParent()?.GetNodeOrNull<Control>("Sidebar");
		_centred.OffsetRight = furniture is { Visible: true } ? -furniture.Size.X : 0f;
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

	/// <summary>A press on the dimmed map behind closes it, and so does Escape.</summary>
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
