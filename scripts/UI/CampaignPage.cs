using Godot;

public partial class CampaignPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string CardScenePath = "res://scene/campaign/campaign_card.tscn";
	// Only the Royal Crown campaign has a briefing (map + rival) ready; the rest are inert until theirs exist.
	private const string RoyalCrownBriefingScenePath = "res://scene/campaign-briefing/campaign_briefing.tscn";
	private const string CardArtFile = "card.png";
	private const float FadeInSeconds = 0.4f;
	private const float CardRevealDelaySeconds = 0.3f;
	private const float CardFadeSeconds = 0.3f;
	private const float CardStaggerSeconds = 0.08f;

	/// <summary><paramref name="Folder"/> is the campaign's own folder under data/campaigns and
	/// assets/campaigns — everything it owns is named from it, so a new campaign is one entry here
	/// and a folder of each kind.</summary>
	private record CampaignData(string Folder, string Title, string Description, int Lords, int EnemyFactions, string Difficulty, Color Accent);

	// Accent ties each realm's glow and info-panel trim to its own identity instead of a shared gold.
	// Append here as the remaining portraits arrive.
	private static readonly CampaignData[] Campaigns =
	{
		new("royal-crown", "The Royal Crown",
			"Unite the fractured kingdoms and restore the true crown.", 1, 4, "Normal", new Color("b23a3a")),
		new("northern-watch", "The Northern Watch",
			"Hold the line against the northern hordes and protect the realm.", 6, 5, "Hard", new Color("5f8fc9")),
		new("sands-of-power", "Sands of Power",
			"Control the trade routes and rise from the desert.", 8, 4, "Normal", new Color("d1a34f")),
		new("emerald-lands", "The Emerald Lands",
			"Defend the ancient forests and their secrets.", 5, 3, "Easy", new Color("4f9d5c")),
		new("sunlit-isles", "The Sunlit Isles",
			"Command the sea lanes and bind the scattered isles beneath one throne.", 7, 6, "Hard", new Color("e0a83e")),
	};

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		GoldTitle.Apply(GetNode<Label>("TitleBlock/Title"));

		var cardRow = GetNode<HBoxContainer>("%CardRow");
		var cardScene = GD.Load<PackedScene>(CardScenePath);
		var cards = new CampaignCard[Campaigns.Length];
		for (int i = 0; i < Campaigns.Length; i++)
		{
			CampaignData data = Campaigns[i];
			var card = cardScene.Instantiate<CampaignCard>();
			cardRow.AddChild(card);
			card.SetData(GD.Load<Texture2D>(Campaign.AssetOf(data.Folder, CardArtFile)), data.Title, data.Lords, data.Accent);
			card.Modulate = new Color(1, 1, 1, 0);
			cards[i] = card;
		}

		// Picking a card is what decides whose provinces, map and art everything downstream reads.
		cards[0].Selected += () =>
		{
			Campaign.Folder = Campaigns[0].Folder;
			Campaign.Name = Campaigns[0].Title;
			SceneRouter.GoTo(this, RoyalCrownBriefingScenePath);
		};

		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, MainMenuScenePath);

		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeInSeconds);

		// Cards stay hidden through the page fade, then cascade in one after another.
		Tween cardTween = CreateTween();
		cardTween.TweenInterval(CardRevealDelaySeconds);
		cardTween.TweenProperty(cards[0], "modulate:a", 1.0, CardFadeSeconds);
		for (int i = 1; i < cards.Length; i++)
		{
			cardTween.Parallel().TweenProperty(cards[i], "modulate:a", 1.0, CardFadeSeconds).SetDelay(CardStaggerSeconds * i);
		}

		// Narration waits for the last card to finish fading in before it speaks.
		var narration = GetNode<AudioStreamPlayer>("%IntroNarration");
		cardTween.TweenCallback(Callable.From(() => narration.Play()));
	}
}
