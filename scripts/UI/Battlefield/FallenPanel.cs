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

	private const string DefeatVideoPath = "res://assets/video/defeat-background.ogv";

	private Label _when;
	private Label _fell;
	private Label _how;
	private VideoStreamPlayer _burning;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		// The fallen seat burning behind the panel, looped, with the map gone from sight.
		_burning = new VideoStreamPlayer
		{
			Stream = GD.Load<VideoStream>(DefeatVideoPath),
			Expand = true,
			Loop = true,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Chrome.Fill(_burning);
		AddChild(_burning);
		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.25f),
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
		centred.AddChild(Chrome.Painted(column));

		// The heading on the painted ribbon, what fell and when under it.
		Label heading = Chrome.Line("Defeat", 36, Chrome.Cream);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(heading);
		column.AddChild(heading);

		_fell = Chrome.Line("", 30, Chrome.Cream);
		_fell.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_fell);
		column.AddChild(_fell);

		_when = Chrome.Line("", 15, Chrome.Dim);
		_when.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_when);

		column.AddChild(Chrome.Rule(520));

		Label body = Chrome.Line(
			"Your armies are broken, your lands are lost, and your rule has come to an end.", 20, Chrome.Soft);
		body.AutowrapMode = TextServer.AutowrapMode.Word;
		body.HorizontalAlignment = HorizontalAlignment.Center;
		body.CustomMinimumSize = new Vector2(0, 130);
		body.VerticalAlignment = VerticalAlignment.Center;
		column.AddChild(body);

		// How the last gate fell, when it fell to an army this turn: the men who came, the men we lost.
		_how = Chrome.Line("", 16, Chrome.Dim);
		_how.AutowrapMode = TextServer.AutowrapMode.Word;
		_how.HorizontalAlignment = HorizontalAlignment.Center;
		_how.CustomMinimumSize = new Vector2(720, 0);
		column.AddChild(_how);

		var ways = new HBoxContainer();
		ways.AddThemeConstantOverride("separation", 12);
		column.AddChild(ways);
		ways.AddChild(Way("Load Save", () => Chosen?.Invoke(true)));
		ways.AddChild(Way("Main Menu", () => Chosen?.Invoke(false)));
	}

	/// <summary>Brought up over everything else on the page, including an advisor still talking.</summary>
	/// <param name="seat">The county the reign ended at: the capital, or the last one held.</param>
	/// <param name="how">The steward's account of the last battle, or null.</param>
	public void Announce(string seat, Season season, int year, int turn, string how = null)
	{
		_how.Text = how ?? "";
		_how.Visible = how != null;
		_fell.Text = $"{seat} has fallen";
		_when.Text = $"{season.ToString().ToUpperInvariant()} {year} · TURN {turn}";
		_burning.Play();
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
