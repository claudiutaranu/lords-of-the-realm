using Godot;

/// <summary>The map won: no lord but the player holds a county on it. Over the whole map and eating
/// its clicks, the conquered seat's film behind it, with the way on to the campaign's next map.</summary>
public partial class VictoryPanel : Control
{
	private const float FadeSeconds = 0.8f;

	/// <summary>Pressed to go on to the next map.</summary>
	public event System.Action Next;

	private const string VideoPath = "res://assets/video/conquered.ogv";

	private Label _when;
	private Label _fell;
	private Label _how;
	private VideoStreamPlayer _film;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		// The banners going up over the taken seat, behind the panel, with the map gone from sight.
		_film = new VideoStreamPlayer
		{
			Stream = GD.Load<VideoStream>(VideoPath),
			Expand = true,
			Loop = true,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Chrome.Fill(_film);
		AddChild(_film);
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
		Label heading = Chrome.Line("Victory", 36, Chrome.Cream);
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
			"Congratulations, my lord. Every rival who held land here has been driven from it, and the realm answers to you alone. But the crown is not yet whole: beyond these borders other lords still hold what is yours.", 20, Chrome.Soft);
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
		ways.AddChild(Way("Next Map", () => Next?.Invoke()));
	}

	/// <summary>Brought up over everything else on the page, including an advisor still talking.</summary>
	/// <param name="realm">The player's realm, now the only one on the map.</param>
	/// <param name="how">How long it took, said under the rest.</param>
	public void Announce(string realm, Season season, int year, int turn, string how = null)
	{
		_how.Text = how ?? "";
		_how.Visible = how != null;
		_fell.Text = $"{realm} stands alone";
		_when.Text = $"{season.ToString().ToUpperInvariant()} {year} · TURN {turn}";
		_film.Play();
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
