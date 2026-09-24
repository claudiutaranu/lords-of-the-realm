using System.Collections.Generic;
using Godot;

/// <summary>A county taken: the army on the ridge over the whole screen, the title in gold, and the
/// taking itself playing in a gilded frame in the middle. More than one taken in a season are shown
/// one after the other.</summary>
public partial class ConquestPanel : Control
{
	private const float FadeSeconds = 0.4f;
	private const string BackgroundPath = "res://assets/ui/conquest/background.jpg";
	private const string VideoPath = "res://assets/video/conquered.ogv";
	private static readonly Vector2 VideoSize = new(840, 473);

	private readonly Queue<string> _waiting = new();
	private Label _subtitle;
	private VideoStreamPlayer _video;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		var backdrop = new TextureRect
		{
			Texture = GD.Load<Texture2D>(BackgroundPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		Chrome.Fill(backdrop);
		AddChild(backdrop);

		// Darkened, so the frame in the middle is the thing the eye goes to.
		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.45f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		Chrome.Fill(centred);
		AddChild(centred);

		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 16);
		centred.AddChild(column);

		column.AddChild(Rule());
		Label title = Chrome.Line("Region Conquered", 56, Chrome.Cream);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(title);
		column.AddChild(title);

		_subtitle = Chrome.Line("", 22, Chrome.Bright);
		_subtitle.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_subtitle);

		_video = new VideoStreamPlayer
		{
			Stream = GD.Load<VideoStream>(VideoPath),
			Expand = true,
			Loop = true,
			CustomMinimumSize = VideoSize,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = Colors.Black,
			BorderColor = new Color(0.78f, 0.62f, 0.32f),
			BorderWidthLeft = 4,
			BorderWidthTop = 4,
			BorderWidthRight = 4,
			BorderWidthBottom = 4,
			ShadowColor = new Color(0, 0, 0, 0.6f),
			ShadowSize = 18,
		});
		frame.AddChild(_video);
		column.AddChild(frame);

		var go = new Button { Text = "Continue", CustomMinimumSize = new Vector2(320, 56), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		go.AddThemeFontSizeOverride("font_size", 22);
		go.Pressed += Next;
		column.AddChild(go);
	}

	private static Control Rule()
	{
		Control rule = Chrome.Rule(860);
		rule.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		return rule;
	}

	/// <summary>Counties the lord has just taken, by name.</summary>
	public void Announce(IEnumerable<string> taken)
	{
		foreach (string county in taken)
		{
			_waiting.Enqueue(county);
		}

		if (!Visible)
		{
			Next();
		}
	}

	private void Next()
	{
		if (_waiting.Count == 0)
		{
			_video.Stop();
			Tween tween = CreateTween();
			tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
			tween.TweenCallback(Callable.From(() => Visible = false));
			return;
		}

		string county = _waiting.Dequeue();
		_subtitle.Text = $"{county} is now under your control.\nYour banner flies over its lands.";
		_video.Play();
		if (!Visible)
		{
			MoveToFront();
			Visible = true;
			CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("ui_accept")))
		{
			Next();
			GetViewport().SetInputAsHandled();
		}
	}
}
