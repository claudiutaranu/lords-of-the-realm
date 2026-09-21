using System.Collections.Generic;
using Godot;

/// <summary>The old man who tells the lord what his season cost him.
///
/// One message at a time, over a dimmed map, with the county it concerns named at the top and the
/// line read aloud. Whatever else the turn threw up waits behind it and is shown when this one is
/// dismissed, so a bad winter arrives as three things the lord hears in order rather than as three
/// panels stacked on top of each other.
///
/// The recording is the point of the pacing. A line takes fifteen seconds to speak and the button
/// is there from the first frame: a lord who already knows what a famine is presses on, and one who
/// wants to hear it out can. Nothing here waits for the audio to finish — a panel that will not let
/// go until the voice is done is how a player learns to dread his own turn button.</summary>
public partial class AdvisorPanel : Control
{
	private const float FadeSeconds = 0.25f;

	private readonly Queue<FiredEvent> _waiting = new();
	private Label _county;
	private Label _heading;
	private Label _body;

	/// <summary>Raised when the last message has been dismissed and the map is the player's again —
	/// and straight away when there was nothing to say. Whatever else the turn has to report has to
	/// wait for this, or it is shown behind him and nobody ever reads it.</summary>
	public event System.Action Emptied;

	/// <summary>Puts the turn's news in the advisor's mouth. Called with an empty list — the usual
	/// case, a season where nothing happened — it dismisses itself without ever being seen.</summary>
	public void Tell(List<FiredEvent> news)
	{
		foreach (FiredEvent item in news)
		{
			_waiting.Enqueue(item);
		}

		Next();
	}

	public override void _Ready()
	{
		// Over the whole map and eating its clicks: the county underneath is not to be re-arranged
		// while its lord is being told what happened to it. Chrome.Fill rather than the anchor preset
		// on its own — this panel is built before its page has a size, and the preset alone leaves it
		// nothing by nothing, which is the advisor speaking from the top left corner with the map
		// dimmed only behind his own frame.
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.55f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer();
		Chrome.Fill(centred);
		centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centred);

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(640, 0) };
		column.AddThemeConstantOverride("separation", 12);
		centred.AddChild(Chrome.Framed(column, 28));

		_county = Chrome.Line("", 15, Chrome.Dim);
		_county.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_county);

		_heading = Chrome.Line("", 30, Chrome.Cream);
		_heading.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_heading);
		column.AddChild(_heading);

		column.AddChild(Chrome.Rule(520));

		_body = Chrome.Line("", 19, Chrome.Soft);
		_body.AutowrapMode = TextServer.AutowrapMode.Word;
		_body.HorizontalAlignment = HorizontalAlignment.Center;
		_body.CustomMinimumSize = new Vector2(0, 150);
		_body.VerticalAlignment = VerticalAlignment.Center;
		column.AddChild(_body);

		var dismiss = new Button { Text = "As you say, sire", CustomMinimumSize = new Vector2(0, 46) };
		dismiss.AddThemeFontSizeOverride("font_size", 18);
		dismiss.Pressed += Next;
		column.AddChild(dismiss);
	}

	/// <summary>The next thing waiting, or the end of it. Each message stops the one before it
	/// mid-sentence: two advisors talking over each other is worse than an interrupted one.</summary>
	private void Next()
	{
		Narrator.Hush();
		if (_waiting.Count == 0)
		{
			Dismiss();
			return;
		}

		FiredEvent item = _waiting.Dequeue();
		_county.Text = item.ProvinceName.ToUpperInvariant();
		_heading.Text = item.Said.Heading;
		_body.Text = item.Said.Text;

		if (!Visible)
		{
			// Over whatever the lord happens to have open. The turn can be ended from inside the
			// hall, and an advisor speaking from behind the hall's own panel is an advisor nobody
			// hears — the audio plays and the message is never seen.
			MoveToFront();
			Visible = true;
			CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
		}

		string voice = EventEngine.VoicePath(item.Said);
		if (voice.Length == 0)
		{
			return; // a line written but not yet recorded: it reads itself
		}

		Narrator.Say(voice);
	}

	private void Dismiss()
	{
		if (!Visible)
		{
			Emptied?.Invoke(); // the usual season: nothing happened and he was never called for
			return;
		}

		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			Visible = false;
			Emptied?.Invoke();
		}));
	}
}
