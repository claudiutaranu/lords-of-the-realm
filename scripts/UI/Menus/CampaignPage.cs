using Godot;

/// <summary>The campaigns, each a realm fought over level after level: England, whose first level is
/// the Royal Crown's map, and Romania, still to be made. A card shows the lords met over the whole
/// campaign; the first level sets the player against only the first of them.</summary>
public partial class CampaignPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string BriefingScenePath = "res://scene/campaign-briefing/campaign_briefing.tscn";
	private const string Art = "res://assets/ui/campaign";
	private const float FadeInSeconds = 0.4f;
	private const float CardRevealDelaySeconds = 0.3f;
	private const float CardFadeSeconds = 0.3f;
	private const float CardStaggerSeconds = 0.12f;

	/// <summary>England's first level: the map, data and art everything downstream reads.</summary>
	private const string FirstLevelFolder = "royal-crown";
	private const string FirstLevelName = "The Royal Crown";

	// Blue for the player and red for the first lord against him, as on every map
	// (CampaignMapPage.PlayerColour/RivalColours); the green and the gold are the lords of the levels after.
	private static readonly Color Blue = new("3a6fd8");
	private static readonly Color Red = new("b23a3a");
	private static readonly Color Green = new("4f8f4a");
	private static readonly Color Gold = new("d1a34f");
	private static readonly Color Ash = new("3a3a3a");

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		GoldTitle.Apply(GetNode<Label>("TitleBlock/Title"));
		GetNode<Label>("TitleBlock/Subtitle").Text = "Two realms. Different destinies. Choose your campaign.";

		var england = CampaignRealmCard.Make("England Campaign", $"{Art}/england.jpg", new CampaignRealmCard.LordFace[]
		{
			new($"{Art}/lord-crown.png", $"res://assets/campaigns/{FirstLevelFolder}/lord.ogv",
				$"{Chrome.IconDirectory}/shield-royal-crown.png", Blue),
			new($"{Art}/lord-margrave.png", "res://assets/video/lords/margrave.ogv",
				$"{Chrome.IconDirectory}/shield-northern-watch.png", Red),
			new($"{Art}/lord-duchess.png", "", "", Green),
			new($"{Art}/lord-marshal.png", "", "", Gold),
		}, isOpen: true, Red, Blue,
			"Take up the crown of a broken kingdom and win it back, county by county, from the lords who carved it up.",
			maps: 2);
		var romania = CampaignRealmCard.Make("Romania Campaign", $"{Art}/romania.jpg",
			System.Array.Empty<CampaignRealmCard.LordFace>(), isOpen: false, Ash, Ash);

		var cardRow = GetNode<HBoxContainer>("%CardRow");
		cardRow.AddChild(england);
		cardRow.AddChild(romania);
		england.Chosen += Begin;

		Button start = Chrome.Order("Start Campaign", "", Begin, out _);
		start.CustomMinimumSize = new Vector2(380, 60);
		start.AnchorLeft = start.AnchorRight = 0.5f;
		start.AnchorTop = start.AnchorBottom = 1f;
		start.OffsetLeft = -190f;
		start.OffsetRight = 190f;
		start.OffsetTop = -96f;
		start.OffsetBottom = -36f;
		AddChild(start);

		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, MainMenuScenePath);

		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeInSeconds);

		// The cards stay hidden through the page's fade, then come in one after the other.
		Control[] cards = { england, romania };
		Tween cardTween = CreateTween();
		cardTween.TweenInterval(CardRevealDelaySeconds);
		for (int i = 0; i < cards.Length; i++)
		{
			Color shown = cards[i].Modulate;
			cards[i].Modulate = new Color(shown, 0f);
			cardTween.Parallel().TweenProperty(cards[i], "modulate:a", shown.A, CardFadeSeconds).SetDelay(CardStaggerSeconds * i);
		}

		// Narration waits for the last card to finish coming in before it speaks.
		var narration = GetNode<AudioStreamPlayer>("%IntroNarration");
		cardTween.TweenCallback(Callable.From(() => narration.Play()));
	}

	/// <summary>England, from its first level.</summary>
	private void Begin()
	{
		Campaign.Folder = FirstLevelFolder;
		Campaign.Name = FirstLevelName;
		SceneRouter.GoTo(this, BriefingScenePath);
	}
}
