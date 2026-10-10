using System.Linq;
using Godot;

/// <summary>The campaigns, each a realm fought over level after level: England, whose first level is
/// the Royal Crown's map, and Romania, still to be made. A card shows the lords met over the whole
/// campaign; the first level sets the player against only the first of them.</summary>
public partial class CampaignPage : Control
{
	private const string LordSelectScenePath = "res://scene/lord-select/lord_select.tscn";
	private const string BriefingScenePath = "res://scene/campaign-briefing/campaign_briefing.tscn";
	private const string Art = "res://assets/ui/campaign";
	private const float FadeInSeconds = 0.4f;
	private const float CardRevealDelaySeconds = 0.3f;
	private const float CardFadeSeconds = 0.3f;
	private const float CardStaggerSeconds = 0.12f;

	/// <summary>England's first level: the map, data and art everything downstream reads.</summary>
	private const string FirstLevelFolder = "england/maps/royal-crown";

	// The lords wear their own colours (lords.json); a campaign not yet made wears ash.
	private static readonly Color Ash = new("3a3a3a");

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		GoldTitle.Apply(GetNode<Label>("TitleBlock/Title"));
		GetNode<Label>("TitleBlock/Subtitle").Text = "Two realms. Different destinies. Choose your campaign.";

		// The lord the player chose, first and larger, then the four the campaign is fought against in
		// the order its maps bring them in; whoever is not played, the Crown among them, is fought.
		Lord yours = Lords.Find(Campaign.Player) ?? Lords.Find(Campaign.Crown);
		var lords = new System.Collections.Generic.List<CampaignRealmCard.LordFace> { Face(yours, isYours: true) };
		foreach (Lord lord in Lords.All)
		{
			if (lord != yours)
			{
				lords.Add(Face(lord));
			}
		}

		// The banners either side of the view: the first lord against you on the left, you on the right.
		var england = CampaignRealmCard.Make("England Campaign", $"{Art}/england.jpg", lords, isOpen: true,
			(lords[1].Colour, lords[1].Key), (lords[0].Colour, lords[0].Key),
			"Take up the crown of a broken kingdom and win it back, county by county, from the lords who carved it up.",
			maps: 4);
		var romania = CampaignRealmCard.Make("Romania Campaign", $"{Art}/romania.jpg",
			System.Array.Empty<CampaignRealmCard.LordFace>(), isOpen: false, (Ash, ""), (Ash, ""));

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

		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, LordSelectScenePath);

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

	private static CampaignRealmCard.LordFace Face(Lord lord, bool isYours = false) => new(
		lord.Key, $"{Art}/lord-{lord.Key}.png", $"res://assets/video/lords/{lord.Key}.ogv",
		$"{Chrome.IconDirectory}/shield-{lord.Key}.png", lord.Colour ?? Ash, isYours);

	/// <summary>England, from its first level.</summary>
	private void Begin()
	{
		// A game started on a later map (Campaign's --map) is begun there.
		Campaign.Open(OS.GetCmdlineUserArgs().Any(arg => arg.StartsWith("--map=")) ? Campaign.Folder : FirstLevelFolder);
		SceneRouter.GoTo(this, BriefingScenePath);
	}
}
