using Godot;

/// <summary>Shown after picking a campaign card: your lord, the rival, the contested
/// territory, and the campaign's stats/objectives, before play begins. Content here is
/// hardcoded to the Royal Crown vs. Northern Watch matchup for now, the only one with a
/// map and rival briefing text ready; generalize once the other four have theirs.</summary>
public partial class CampaignBriefingPage : Control
{
	private const string CampaignScenePath = "res://scene/campaign/campaign.tscn";
	private const string CampaignMapScenePath = "res://scene/campaign-map/campaign_map.tscn";
	private const string LoadingScenePath = "res://scene/loading/loading.tscn";
	private const string DifficultyOptionPath = "%DifficultyOption";
	private const float FadeInSeconds = 0.4f;
	private const float PanelRevealDelaySeconds = 0.3f;
	private const float PanelFadeSeconds = 0.3f;
	private const float PanelStaggerSeconds = 0.08f;

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		// The stats bar already showed a difficulty, as text nobody could change. It is the setting
		// itself now: one place showing it and one place choosing it, rather than a second screen to
		// keep in step with a number the player thought he had already picked. Filled from the enum
		// and not from a list typed out here, so another setting is one member and not one member
		// plus a list somebody forgets.
		var difficulty = GetNode<OptionButton>(DifficultyOptionPath);
		foreach (Difficulty setting in System.Enum.GetValues<Difficulty>())
		{
			difficulty.AddItem(setting.ToString());
		}

		difficulty.Selected = (int)Campaign.Difficulty;
		difficulty.TooltipText = "How well the lords who are not you will play their own counties";
		difficulty.ItemSelected += chosen => Campaign.Difficulty = (Difficulty)chosen;

		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, CampaignScenePath);
		GetNode<Button>("%StartCampaignButton").Pressed += () =>
		{
			LoadingPage.TargetScenePath = CampaignMapScenePath;
			SceneRouter.GoTo(this, LoadingScenePath);
		};

		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeInSeconds);

		// Panels stay hidden through the page fade, then cascade in left to right.
		var panels = new Control[]
		{
			GetNode<Control>("MainRow/YourLordPanel"),
			GetNode<Control>("MainRow/MapPanel"),
			GetNode<Control>("MainRow/RivalPanel"),
			GetNode<Control>("StatsBar"),
		};
		foreach (Control panel in panels)
		{
			panel.Modulate = new Color(1, 1, 1, 0);
		}

		Tween panelTween = CreateTween();
		panelTween.TweenInterval(PanelRevealDelaySeconds);
		panelTween.TweenProperty(panels[0], "modulate:a", 1.0, PanelFadeSeconds);
		for (int i = 1; i < panels.Length; i++)
		{
			panelTween.Parallel().TweenProperty(panels[i], "modulate:a", 1.0, PanelFadeSeconds).SetDelay(PanelStaggerSeconds * i);
		}
	}
}
