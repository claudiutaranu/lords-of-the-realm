using Godot;

/// <summary>The end of the reign: the lord's last county has fallen and there is nothing left for him
/// to rule. Over the whole map and eating its clicks, the way the advisor stands over it — nothing
/// underneath can be given an order any more — with the only two ways left out of it: back to the
/// main menu, or back to a saved season.</summary>
public partial class FallenPanel : Control
{
	private const float FadeSeconds = 0.8f;

	/// <summary>Pressed to leave the lost campaign: true for the load screen, false for the menu.</summary>
	public event System.Action<bool> Chosen;

	private Label _when;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.75f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer();
		Chrome.Fill(centred);
		centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centred);

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
		column.AddThemeConstantOverride("separation", 14);
		centred.AddChild(Chrome.Framed(column, 28));

		_when = Chrome.Line("", 15, Chrome.Dim);
		_when.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_when);

		Label heading = Chrome.Line("The Crown Has Fallen", 34, Chrome.Cream);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(heading);
		column.AddChild(heading);

		column.AddChild(Chrome.Rule(520));

		Label body = Chrome.Line(
			"The last of your counties has opened its gate to another lord. There is no hall left to "
			+ "call yours, no field that pays you its tithe, and no man who will march at your word. "
			+ "Your reign is over.", 19, Chrome.Soft);
		body.AutowrapMode = TextServer.AutowrapMode.Word;
		body.HorizontalAlignment = HorizontalAlignment.Center;
		body.CustomMinimumSize = new Vector2(0, 130);
		body.VerticalAlignment = VerticalAlignment.Center;
		column.AddChild(body);

		var ways = new HBoxContainer();
		ways.AddThemeConstantOverride("separation", 12);
		column.AddChild(ways);
		ways.AddChild(Way("Load a saved season", () => Chosen?.Invoke(true)));
		ways.AddChild(Way("Main menu", () => Chosen?.Invoke(false)));
	}

	/// <summary>Brought up over everything else on the page, including an advisor still talking.</summary>
	public void Announce(Season season, int year, int turn)
	{
		_when.Text = $"{season.ToString().ToUpperInvariant()} {year} · TURN {turn}";
		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	private static Button Way(string text, System.Action pressed)
	{
		var way = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 46),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		way.AddThemeFontSizeOverride("font_size", 18);
		way.Pressed += pressed;
		return way;
	}
}
