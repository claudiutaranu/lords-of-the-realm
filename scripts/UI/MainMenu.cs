using System.Linq;
using Godot;

public partial class MainMenu : Control
{
	private const string OptionsScenePath = "res://scene/options/options.tscn";
	private const string AboutScenePath = "res://scene/about/about.tscn";
	private const string CampaignScenePath = "res://scene/campaign/campaign.tscn";
	private const string LoadGameScenePath = "res://scene/load-game/load_game.tscn";
	private const float SceneFadeOutSeconds = 0.4f;

	// Survives scene reloads (static field), reset only when the game restarts.
	private static bool s_hasPlayedIntroClip;

	public override void _Ready()
	{
		bool isFirstVisit = !s_hasPlayedIntroClip;

		// The clip plays once per game session; returning from Options/About just shows its last frame
		// (the BackgroundStill image underneath, since the video itself is hidden and never restarted).
		var background = GetNode<VideoStreamPlayer>("Background");
		if (s_hasPlayedIntroClip)
		{
			background.Visible = false;
		}
		else
		{
			s_hasPlayedIntroClip = true;
			background.Play();
		}

		GetNode<MusicPlayer>("/root/Music").EnsurePlaying();

		// Back at the menu the campaign is over; drop any state still parked for a restore so a
		// new one starts from the province definitions.
		SaveGame.Pending = null;

		string version = ProjectSettings.GetSetting("application/config/version", "0.0.0").AsString();
		GetNode<Label>("%BuildVersion").Text = $"v{version}";

		Button[] buttons = GetNode("%Buttons").GetChildren().OfType<Button>().ToArray();

		// Hover moves keyboard focus, so only one button is ever highlighted.
		foreach (Button button in buttons)
		{
			button.MouseEntered += button.GrabFocus;
		}

		GetNode<Button>("%LoadGameButton").Pressed += () => SceneRouter.GoTo(this, LoadGameScenePath);
		GetNode<Button>("%OptionsButton").Pressed += () => SceneRouter.GoTo(this, OptionsScenePath);
		GetNode<Button>("%AboutButton").Pressed += () => SceneRouter.GoTo(this, AboutScenePath);
		GetNode<Button>("%QuitButton").Pressed += () => GetTree().Quit();
		GetNode<Button>("%CampaignButton").Pressed += () => FadeOutAndChangeScene(CampaignScenePath);
		GetNode<Button>("%CampaignButton").GrabFocus();

		if (isFirstVisit)
		{
			var banner = GetNode<CanvasItem>("BannerSlot/Banner");
			var logo = GetNode<CanvasItem>("BannerSlot/Banner/Margin/Content/Logo");
			FadeInMenu(banner, logo, buttons);
		}
	}

	private const float BannerFadeSeconds = 0.35f;
	private const float LogoFadeSeconds = 0.3f;
	private const float ButtonFadeSeconds = 0.25f;
	private const float ButtonStaggerSeconds = 0.05f;

	// The banner frame fades in first, then the logo, then each button in sequence.
	// Video plays immediately, no fade. Only played on the first visit this session;
	// returning from Options/About skips straight to the end state.
	private static void FadeInMenu(CanvasItem banner, CanvasItem logo, Button[] buttons)
	{
		banner.Modulate = new Color(1, 1, 1, 0);
		logo.Modulate = new Color(1, 1, 1, 0);
		foreach (Button button in buttons)
		{
			button.Modulate = new Color(1, 1, 1, 0);
		}

		Tween tween = banner.CreateTween();
		tween.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tween.TweenProperty(banner, "modulate:a", 1.0, BannerFadeSeconds);
		tween.TweenProperty(logo, "modulate:a", 1.0, LogoFadeSeconds);
		// Overlapping (not sequential) fades: each button starts a beat after the previous one
		// while it's still fading, so the cascade reads as one fluid wave instead of stop-start.
		for (int i = 0; i < buttons.Length; i++)
		{
			tween.Parallel().TweenProperty(buttons[i], "modulate:a", 1.0, ButtonFadeSeconds).SetDelay(ButtonStaggerSeconds * i);
		}
	}

	// Fades a black overlay in (not the menu's own modulate, which would reveal the raw
	// viewport clear color behind it), then swaps to the destination scene.
	private void FadeOutAndChangeScene(string scenePath)
	{
		var overlay = GetNode<ColorRect>("%FadeOverlay");
		Tween tween = CreateTween();
		tween.TweenProperty(overlay, "color:a", 1.0, SceneFadeOutSeconds);
		tween.TweenCallback(Callable.From(() => SceneRouter.GoTo(this, scenePath)));
	}
}
