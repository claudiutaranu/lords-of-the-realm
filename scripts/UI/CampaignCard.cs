using System;
using Godot;

/// <summary>One campaign entry in the selection grid. The title is plain outlined
/// text overlaid on the portrait (no backing plate), so it reads over any of the
/// baked (frame + character art + map) portraits regardless of their art. A
/// two-layer sandstorm (slow hazy far layer, faster grit near layer) rises behind
/// the card while hovered, tinted to the realm's accent, and the same accent trims
/// the info panel so each card reads as its own realm.</summary>
public partial class CampaignCard : PanelContainer
{
	private const float GlowFadeSeconds = 0.1f;
	private const float NearAlpha = 0.85f;
	private const float FarAlpha = 0.55f;

	/// <summary>Where the lord's window lies in a card's painting, as shares of it (card.png is
	/// 1024×1536, the window 142..878 across and 92..758 down).</summary>
	private static readonly Rect2 Window = new(142f / 1024f, 92f / 1536f, 736f / 1024f, 666f / 1536f);

	public event Action Selected;

	private const int HeldAfterFrames = 3;
	private const float UnderGilt = 3f;

	private Control _film;
	private TextureRect _still;
	private Control _window;
	private string _filmPath = "";

	public override void _Ready()
	{
		MouseEntered += () =>
		{
			SetGlow(true);
			Stir();
		};
		MouseExited += () => SetGlow(false);
		// The painting's own size, not the card's: the card is sized before the painting inside it,
		// and laid out off the card the window was measured at nothing and stood empty until a hover.
		GetNode<TextureRect>("%Portrait").Resized += Place;
	}

	/// <summary>The lord in the card's window: a campaign with a film of him has its window cut
	/// clear in card.png, and behind it stands his painting with the film over that: held on its
	/// first look until the pointer rests on the card, then played once and held on its last. Behind
	/// and not over the card, so the gilt round the window is always on top of him; and the painting
	/// under the film, so the window is never empty while a film is starting. A campaign with no
	/// film keeps its own painting.</summary>
	public void SetFilm(string film, string still)
	{
		_filmPath = film;
		if (!ResourceLoader.Exists(film) || !ResourceLoader.Exists(still))
		{
			return;
		}

		// The window clips them: both are square, as wide as the window, and stood from its top, so
		// the lord keeps his shape and loses a little below the chest, as on the card's own painting.
		_window = new Control { ClipContents = true, ShowBehindParent = true, MouseFilter = MouseFilterEnum.Ignore };
		GetNode<TextureRect>("%Portrait").AddChild(_window);
		_still = new TextureRect
		{
			Texture = GD.Load<Texture2D>(still),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		_window.AddChild(_still);
		Roll(holds: true);
	}

	private void Stir() => Roll(holds: false);

	/// <summary>Puts up a fresh film: a player once stopped will not start the same film again, and
	/// stood blank. <paramref name="holds"/> stops it on its first frame.</summary>
	private void Roll(bool holds)
	{
		if (_still == null || LordPortrait.Moving(_filmPath, 1) is not VideoStreamPlayer film)
		{
			return;
		}

		_film?.QueueFree();
		film.CustomMinimumSize = Vector2.Zero;
		_window.AddChild(film);
		_film = film;
		Place();
		if (holds)
		{
			Hold(film);
		}
	}

	/// <summary>Stops a film on the frame it is showing, once it has one to show, unless the pointer
	/// has come to it since.</summary>
	private async void Hold(VideoStreamPlayer film)
	{
		for (int frame = 0; frame < HeldAfterFrames; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		if (film == _film && IsInstanceValid(film))
		{
			film.Paused = true;
		}
	}

	/// <summary>Lays the film over the window in what is drawn of the painting, which is kept to
	/// its shape and centred in the rect, with a little to spare under the gilt.</summary>
	private void Place()
	{
		var portrait = GetNode<TextureRect>("%Portrait");
		if (_still == null || portrait.Texture == null)
		{
			return;
		}

		Vector2 art = portrait.Texture.GetSize();
		float scale = Mathf.Min(portrait.Size.X / art.X, portrait.Size.Y / art.Y);
		Vector2 drawn = art * scale;
		Vector2 origin = (portrait.Size - drawn) / 2f;
		_window.Position = origin + (Window.Position * drawn) - (Vector2.One * UnderGilt);
		_window.Size = (Window.Size * drawn) + (Vector2.One * UnderGilt * 2f);
		foreach (Control layer in new[] { _still, _film })
		{
			if (layer != null)
			{
				layer.Position = Vector2.Zero;
				layer.Size = Vector2.One * _window.Size.X;
			}
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
		{
			Selected?.Invoke();
		}
	}

	public void SetData(Texture2D portrait, string title, int lords, Color accent)
	{
		GetNode<TextureRect>("%Portrait").Texture = portrait;
		GetNode<Label>("%TitleOverlay").Text = title;
		GetNode<Label>("%Lords").Text = $"Lords: {lords}";
		ApplyAccent(accent);
	}

	private void ApplyAccent(Color accent)
	{
		GetNode<CpuParticles2D>("%Near").Color = new Color(accent, NearAlpha);
		GetNode<CpuParticles2D>("%Far").Color = new Color(accent, FarAlpha);

		var infoPanel = GetNode<PanelContainer>("Content/InfoPanel");
		var infoStyle = (StyleBoxFlat)infoPanel.GetThemeStylebox("panel").Duplicate();
		infoStyle.BorderWidthTop = 3;
		infoStyle.BorderColor = accent;
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);
	}

	private void SetGlow(bool isOn)
	{
		GetNode<CpuParticles2D>("%Near").Emitting = isOn;
		GetNode<CpuParticles2D>("%Far").Emitting = isOn;

		var glow = GetNode<Node2D>("%Glow");
		glow.CreateTween().TweenProperty(glow, "modulate:a", isOn ? 1.0 : 0.0, GlowFadeSeconds);
	}
}
