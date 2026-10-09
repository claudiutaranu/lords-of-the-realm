using System;
using System.Collections.Generic;
using Godot;

/// <summary>What the lord has in hand on the field, in the realm's own colours: across the top each
/// side's shield and name and how much of it is still standing — the day is won when the other bar is
/// empty — and along the foot his banner, a card for each of his squads (its weapon, how many are
/// left, and a bar of how many it brought that still stand), and his orders, with the day left to
/// the captains to settle at once (Auto-resolve).</summary>
public partial class BattlefieldBar : Control
{
	private const int CardWide = 84;
	private const int CardHigh = 112;
	private const int StandingWide = 220;
	private const int ShieldSide = 46;
	private const float BannerHigh = 118f;
	private static readonly Color Unchosen = new(0.62f, 0.62f, 0.62f);
	private static readonly Color Navy = new(0.04f, 0.07f, 0.14f, 0.94f);
	private static readonly Color Gilt = new(0.78f, 0.62f, 0.32f);
	private static readonly Color ChargeBlue = new(0.09f, 0.2f, 0.42f, 0.97f);
	private static readonly Color RetreatRed = new(0.38f, 0.07f, 0.07f, 0.97f);

	/// <summary>A squad's card, and what it last said: the bar is brought up every frame, and a label
	/// rewritten every frame is a string built and a relayout asked for every frame.</summary>
	private sealed class Card
	{
		public Control Face;
		public Label Count;
		public ColorRect Health;
		public int Standing = -1;
		public Color Tint;
	}

	private readonly Dictionary<FieldSquad, Card> _cards = new();
	private float _ourShare = -1f;
	private float _theirShare = -1f;
	private string _stateSaid;
	private string _captainSaid;
	private FieldBattle _battle;
	private ColorRect _ourStanding;
	private ColorRect _theirStanding;
	private Label _state;
	private Label _captainWord;

	public void Lay(FieldBattle battle, BattlePanel.Colours us, BattlePanel.Colours them,
		Action<FieldSquad, bool> pick, Action charge, Action hold, Action<bool> captain, Action retreat,
		Action autoResolve)
	{
		_battle = battle;
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Ignore;
		AddChild(Head(us, them));
		AddChild(Foot(battle, us, pick, charge, hold, captain, retreat, autoResolve));

		Label help = Chrome.Line("Left: choose · Right: march, twice to run, or fall on (drag: draw their front) · WASD / Q E / wheel: look · Space: pause", 13, Chrome.Dim);
		help.SetAnchorsPreset(LayoutPreset.BottomLeft);
		help.GrowVertical = GrowDirection.Begin;
		help.Position += new Vector2(16, -4);
		AddChild(help);
	}

	/// <summary>Across the top: each side's shield at its end, its name, and the bar of what stands.</summary>
	private Control Head(BattlePanel.Colours us, BattlePanel.Colours them)
	{
		var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		head.AddThemeConstantOverride("separation", 12);
		head.AddChild(Shield(us));
		head.AddChild(Chrome.Line(us.Name, 20, Chrome.Cream));
		head.AddChild(Standing(out _ourStanding, us.Accent));
		_state = Chrome.Line("", 16, Chrome.Cream);
		_state.CustomMinimumSize = new Vector2(130, 0);
		_state.HorizontalAlignment = HorizontalAlignment.Center;
		head.AddChild(_state);
		head.AddChild(Standing(out _theirStanding, them.Accent));
		head.AddChild(Chrome.Line(them.Name, 20, Chrome.Cream));
		head.AddChild(Shield(them));

		var top = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		top.SetAnchorsPreset(LayoutPreset.TopWide);
		top.OffsetTop = 10;
		top.AddChild(Plate(head, 6));
		return top;
	}

	/// <summary>Along the foot: his banner, his squads' cards, and his orders.</summary>
	private Control Foot(FieldBattle battle, BattlePanel.Colours us, Action<FieldSquad, bool> pick, Action charge,
		Action hold, Action<bool> captain, Action retreat, Action autoResolve)
	{
		var foot = new HBoxContainer();
		foot.AddThemeConstantOverride("separation", 10);
		foot.AddChild(DiplomacyArt.Banner(false, BannerHigh, us.Accent));

		foreach (FieldSquad squad in battle.Squads)
		{
			if (squad.IsAttacking)
			{
				foot.AddChild(SquadCard(squad, pick));
			}
		}

		foot.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
		var orders = new GridContainer { Columns = 3 };
		orders.AddThemeConstantOverride("h_separation", 8);
		orders.AddThemeConstantOverride("v_separation", 8);
		orders.AddChild(Order("Charge", "crossed-swords", ChargeBlue, charge, out _));
		orders.AddChild(Order("Hold", "shield", Navy, hold, out _));
		orders.AddChild(Order("", "helmet", Navy, () => captain(!_battle.IsAttackCaptained), out _captainWord));
		orders.AddChild(Order("Retreat", "footsteps", RetreatRed, retreat, out _));
		orders.AddChild(Order("Auto-resolve", "scales", Navy, autoResolve, out _));
		var middle = new CenterContainer();
		middle.AddChild(orders);
		foot.AddChild(middle);

		PanelContainer bottom = Plate(foot, 8);
		bottom.SetAnchorsPreset(LayoutPreset.CenterBottom);
		bottom.GrowHorizontal = GrowDirection.Both;
		bottom.GrowVertical = GrowDirection.Begin;
		bottom.Position -= new Vector2(0, 22);
		return bottom;
	}

	/// <summary>A squad's card: the man, his weapon and how many are left over a bar of what still stands.</summary>
	private Control SquadCard(FieldSquad squad, Action<FieldSquad, bool> pick)
	{
		(Control card, VBoxContainer stack) = UnitCard.Build(squad.Unit, squad.Kind.Name, CardHigh, titled: false,
			() => pick(squad, Input.IsKeyPressed(Key.Shift)));
		card.CustomMinimumSize = new Vector2(CardWide, CardHigh);
		card.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

		var line = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		line.AddThemeConstantOverride("separation", 6);
		line.AddChild(Chrome.Icon(squad.Kind.Icon, 18));
		Label count = Chrome.Line("", 18, Chrome.Cream);
		line.AddChild(count);
		stack.AddChild(line);

		var health = new HBoxContainer { CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore };
		health.AddThemeConstantOverride("separation", 0);
		var filled = new ColorRect { Color = new Color("3a6fd8"), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		var rest = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f), SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
		rest.SizeFlagsStretchRatio = 0.001f;
		health.AddChild(filled);
		health.AddChild(rest);
		stack.AddChild(health);

		_cards[squad] = new Card { Face = card, Count = count, Health = filled, Tint = card.Modulate };
		return card;
	}

	/// <summary>A side's shield: its realm's painted crest, or the plain one in its colour.</summary>
	private static Control Shield(BattlePanel.Colours side)
	{
		string painted = Heraldry.CrestPath(side.Key);
		bool isPainted = ResourceLoader.Exists(painted);
		return new TextureRect
		{
			Texture = isPainted
				? new AtlasTexture { Atlas = GD.Load<Texture2D>(painted), Region = ProvinceSidebar.CrestRegion }
				: GD.Load<Texture2D>($"{Chrome.IconDirectory}/shield.png"),
			Modulate = isPainted ? Colors.White : side.Accent.Lightened(0.2f),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(ShieldSide, ShieldSide),
		};
	}

	/// <summary>The dark blue, gilt-edged plate the bar's two halves are laid on.</summary>
	private static PanelContainer Plate(Control content, int margin)
	{
		var plate = new PanelContainer();
		var style = new StyleBoxFlat { BgColor = Navy, BorderColor = Gilt };
		style.SetBorderWidthAll(2);
		style.SetCornerRadiusAll(4);
		style.SetContentMarginAll(margin);
		style.ShadowColor = new Color(0f, 0f, 0f, 0.5f);
		style.ShadowSize = 6;
		plate.AddThemeStyleboxOverride("panel", style);
		plate.AddChild(content);
		return plate;
	}

	/// <summary>Brings the bar up to the moment: who is left, who is in hand, and how the day stands.</summary>
	public void Refresh(ICollection<FieldSquad> chosen, bool isPaused)
	{
		foreach ((FieldSquad squad, Card card) in _cards)
		{
			if (card.Standing != squad.Standing)
			{
				card.Standing = squad.Standing;
				card.Count.Text = $"{squad.Standing:N0}";
				float share = squad.Brought > 0 ? squad.Standing / (float)squad.Brought : 0f;
				card.Health.SizeFlagsStretchRatio = Mathf.Max(0.001f, share);
				card.Health.GetParent().GetChild<ColorRect>(1).SizeFlagsStretchRatio = Mathf.Max(0.001f, 1f - share);
			}

			Color tint = !squad.IsStanding ? new Color(0.35f, 0.35f, 0.35f)
				: chosen.Contains(squad) ? Colors.White : Unchosen;
			if (card.Tint != tint)
			{
				card.Tint = tint;
				card.Face.Modulate = tint;
			}
		}

		Fill(_ourStanding, 1f - _battle.AttackLoss, ref _ourShare);
		Fill(_theirStanding, 1f - _battle.DefenceLoss, ref _theirShare);
		Say(_captainWord, _battle.IsAttackCaptained ? "Command" : "Captain", ref _captainSaid);
		Say(_state, _battle.IsAttackFallen ? "Our last man is down"
			: _battle.IsDefenceFallen ? "The field is ours!"
			: isPaused ? "Paused"
			: _battle.IsAttackCaptained ? "The captain leads"
			: "", ref _stateSaid);
	}

	private static void Say(Label label, string text, ref string said)
	{
		if (text != said)
		{
			said = text;
			label.Text = text;
		}
	}

	/// <summary>A bar of how much of a side is still standing.</summary>
	private static Control Standing(out ColorRect filled, Color colour)
	{
		var bar = new HBoxContainer { CustomMinimumSize = new Vector2(StandingWide, 12), SizeFlagsVertical = SizeFlags.ShrinkCenter };
		bar.AddThemeConstantOverride("separation", 0);
		filled = new ColorRect { Color = colour, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var rest = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		bar.AddChild(filled);
		bar.AddChild(rest);
		filled.SizeFlagsStretchRatio = 0.001f;
		rest.SizeFlagsStretchRatio = 1f;
		return bar;
	}

	private static void Fill(ColorRect filled, float share, ref float shown)
	{
		if (share == shown)
		{
			return;
		}

		shown = share;
		filled.SizeFlagsStretchRatio = Mathf.Max(0.001f, share);
		filled.GetParent().GetChild<ColorRect>(1).SizeFlagsStretchRatio = Mathf.Max(0.001f, 1f - share);
	}

	/// <summary>An order on its plate, as wide as its word needs: dark blue in a gilt edge, the charge
	/// in a brighter blue and the retreat in red, its glyph beside its word.</summary>
	private static Button Order(string text, string icon, Color ground, Action pressed, out Label word)
	{
		var order = new Button { CustomMinimumSize = new Vector2(196, 52) };
		foreach ((string state, Color fill, Color edge) in new[]
		{
			("normal", ground, Gilt),
			("focus", ground, Gilt),
			("hover", ground.Lightened(0.15f), Gilt.Lightened(0.3f)),
			("pressed", ground.Darkened(0.25f), Gilt),
		})
		{
			var face = new StyleBoxFlat { BgColor = fill, BorderColor = edge };
			face.SetBorderWidthAll(2);
			face.SetCornerRadiusAll(3);
			face.ShadowColor = new Color(0f, 0f, 0f, 0.4f);
			face.ShadowSize = 3;
			order.AddThemeStyleboxOverride(state, face);
		}

		order.Pressed += pressed;
		var said = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
		said.AddThemeConstantOverride("separation", 12);
		said.SetAnchorsPreset(LayoutPreset.FullRect);
		said.AddChild(Chrome.Icon(icon, 30));
		word = Chrome.Line(text, 22, Chrome.Cream);
		word.VerticalAlignment = VerticalAlignment.Center;
		said.AddChild(word);
		order.AddChild(said);
		return order;
	}
}
