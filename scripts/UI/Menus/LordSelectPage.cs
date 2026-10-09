using System.Collections.Generic;
using Godot;

/// <summary>Who the player will be, chosen before the campaign: the Crown or any of the four lords
/// (lords.json), each in his frame with his shield, his realm and his motto. The chosen one comes
/// alive and his fire burns behind him; under the pointer any lord's fire burns. Whoever is chosen
/// starts where the player always starts, in his own colours, and the Crown takes his place on any
/// map that seats him (Campaign.Seated, the user's call).</summary>
public partial class LordSelectPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string CampaignScenePath = "res://scene/campaign/campaign.tscn";
	private const float FadeInSeconds = 0.4f;
	private const float CardRevealDelaySeconds = 0.3f;
	private const float CardFadeSeconds = 0.3f;
	private const float CardStaggerSeconds = 0.08f;
	private const int FaceSide = 318;
	private const int ShieldSide = 88;
	private const int CardWidth = 354;
	private const int CardHeight = 535;
	private const int CardBorder = 3;
	private const int RuleWidth = 220;
	// The cards, the way on and the way back, one under the other with this between them.
	private const int StackGap = 18;
	private static readonly Vector2 BackSize = new(200, 56);

	// A lord not chosen stands a little back, so the chosen one is seen at a glance.
	private static readonly Color Unchosen = new(0.5f, 0.5f, 0.5f);
	private static readonly Color Chosen = new("f0c870");
	private static readonly Color Gilt = new(0.549f, 0.447f, 0.271f, 0.85f);
	private static readonly Color Field = new(0.05f, 0.045f, 0.04f, 0.92f);

	private readonly Dictionary<string, PanelContainer> _cards = new();
	private readonly Dictionary<string, Embers> _fires = new();
	private readonly Dictionary<string, Control> _faces = new();
	private const string FacesDirectory = "res://assets/ui/campaign";

	// A lord's fire is his colour at its fullest and brightest: added onto the hall, a dark colour adds
	// almost nothing, and the Castellan's black nothing at all. Gathered a tenth in from the card's
	// sides, so it rises from behind him rather than spilling over his neighbours.
	private const float FireSaturation = 1.25f;
	private const float FireBrightness = 0.95f;
	private const float FireWidth = 0.9f;
	private const int BackgroundLayer = -2;
	private const int FireLayer = -1;

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;
		// Under everything, so the fires can be laid between it and the cards (FireLayer).
		background.ZIndex = BackgroundLayer;
		GoldTitle.Apply(GetNode<Label>("TitleBlock/Title"));

		var row = GetNode<HBoxContainer>("%CardRow");
		foreach (Lord lord in Lords.All)
		{
			PanelContainer card = Card(lord);
			_cards[lord.Key] = card;
			Embers fire = Embers.Behind(card, Blaze(lord.Colour ?? Chosen), FireWidth);
			// Behind every card and not only its own: cards stand close, and a fire drawn after the
			// card beside it rose over that one's face.
			fire.ZAsRelative = false;
			fire.ZIndex = FireLayer;
			_fires[lord.Key] = fire;
			card.MouseEntered += () => fire.Glow(true);
			card.MouseExited += () => fire.Glow(lord.Key == Campaign.Player);
			row.AddChild(card);
		}

		Choose(_cards.ContainsKey(Campaign.Player) ? Campaign.Player : Campaign.Crown);

		// The way on straight under the cards, and the way back under it, rather than at the foot
		// of the screen, a long way from where the eye is.
		var stack = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		stack.AddThemeConstantOverride("separation", StackGap);
		Node area = row.GetParent();
		area.RemoveChild(row);
		stack.AddChild(row);
		area.AddChild(stack);

		Button onward = Chrome.Order("Choose Campaign", "", () => SceneRouter.GoTo(this, CampaignScenePath), out _);
		onward.CustomMinimumSize = new Vector2(380, 60);
		onward.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		stack.AddChild(onward);

		var back = GetNode<Button>("%BackButton");
		back.Reparent(stack, false);
		back.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		// Its size came from the anchors it had at the foot of the screen.
		back.CustomMinimumSize = BackSize;
		back.Pressed += () => SceneRouter.GoTo(this, MainMenuScenePath);

		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeInSeconds);
		Tween cards = CreateTween();
		cards.TweenInterval(CardRevealDelaySeconds);
		int order = 0;
		foreach (PanelContainer card in _cards.Values)
		{
			Color shown = card.Modulate;
			card.Modulate = new Color(shown, 0f);
			cards.Parallel().TweenProperty(card, "modulate:a", shown.A, CardFadeSeconds).SetDelay(CardStaggerSeconds * order++);
		}
	}

	/// <summary>A lord's card: his face in the gilt frame with his shield over its foot, his realm,
	/// his name and his motto. The whole card is pressed to choose him.</summary>
	private PanelContainer Card(Lord lord)
	{
		var card = new PanelContainer
		{
			CustomMinimumSize = new Vector2(CardWidth, CardHeight),
			MouseFilter = MouseFilterEnum.Stop,
			TooltipText = $"Play as {lord.Realm}",
		};
		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 6);
		var inset = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", 16);
		}

		inset.AddChild(column);
		card.AddChild(inset);

		var stand = new Control
		{
			CustomMinimumSize = new Vector2(FaceSide, FaceSide + (ShieldSide / 2f)),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		// His painting, still; the film is put over it only when he is chosen (Choose).
		var face = new Control { MouseFilter = MouseFilterEnum.Ignore };
		var still = new TextureRect
		{
			Texture = GD.Load<Texture2D>($"{FacesDirectory}/lord-{lord.Key}.png"),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		still.SetAnchorsPreset(LayoutPreset.FullRect);
		face.AddChild(still);
		_faces[lord.Key] = face;
		stand.AddChild(DiplomacyArt.Framed(face, FaceSide));
		string crest = $"{Chrome.IconDirectory}/shield-{lord.Key}.png";
		if (ResourceLoader.Exists(crest))
		{
			stand.AddChild(new TextureRect
			{
				Texture = new AtlasTexture { Atlas = GD.Load<Texture2D>(crest), Region = ProvinceSidebar.CrestRegion },
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				Position = new Vector2((FaceSide - ShieldSide) / 2f, FaceSide - (ShieldSide / 2f)),
				Size = new Vector2(ShieldSide, ShieldSide),
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}

		column.AddChild(stand);
		Label realm = Chrome.Line(lord.Realm, 25, Chrome.Cream);
		realm.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(realm);
		column.AddChild(realm);
		if (lord.Name.Length > 0 && lord.Name != lord.Realm)
		{
			Label name = Chrome.Line(lord.Name, 19, Chrome.Cream);
			name.HorizontalAlignment = HorizontalAlignment.Center;
			column.AddChild(name);
		}

		TextureRect rule = DiplomacyArt.Rule();
		rule.CustomMinimumSize = new Vector2(RuleWidth, rule.CustomMinimumSize.Y);
		rule.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		column.AddChild(rule);

		Label motto = Chrome.Line($"\"{lord.Motto}\"", 16, Chrome.Soft);
		motto.HorizontalAlignment = HorizontalAlignment.Center;
		motto.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		column.AddChild(motto);

		card.GuiInput += pointer =>
		{
			if (pointer is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				Choose(lord.Key);
			}
		};
		return card;
	}

	private static Color Blaze(Color colour) =>
		Color.FromHsv(colour.H, Mathf.Min(1f, colour.S * FireSaturation), Mathf.Max(colour.V, FireBrightness));

	/// <summary>A chosen lord comes alive, played once and held on his last look: a fresh film each
	/// time, since a stopped one will not start the same film again. One no longer chosen goes back
	/// to his painting.</summary>
	private void Stir(string lord, bool isChosen)
	{
		Control face = _faces[lord];
		if (face.GetChildCount() > 1)
		{
			face.GetChild(1).QueueFree();
		}

		if (isChosen && LordPortrait.Moving($"res://assets/video/lords/{lord}.ogv", 1) is Control moving)
		{
			moving.CustomMinimumSize = Vector2.Zero;
			moving.SetAnchorsPreset(LayoutPreset.FullRect);
			face.AddChild(moving);
		}
	}

	/// <summary>Makes him the player's lord, and shows it: his card framed in bright gold with his fire
	/// behind it, the others in the plain gilt and stood back in the shadow.</summary>
	private void Choose(string key)
	{
		Campaign.Player = key;
		foreach ((string lord, PanelContainer card) in _cards)
		{
			bool isChosen = lord == key;
			_fires[lord].Glow(isChosen);
			Stir(lord, isChosen);
			StyleBoxFlat style = Chrome.CardStyle(Field);
			style.SetBorderWidthAll(CardBorder);
			style.BorderColor = isChosen ? Chosen : Gilt;
			card.AddThemeStyleboxOverride("panel", style);
			card.SelfModulate = isChosen ? Colors.White : Unchosen;
			foreach (Node part in card.GetChildren())
			{
				if (part is Control shown)
				{
					shown.Modulate = isChosen ? Colors.White : Unchosen;
				}
			}
		}
	}
}
