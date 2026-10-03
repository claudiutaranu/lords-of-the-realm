using System;
using System.Collections.Generic;
using Godot;

/// <summary>What the lord has in hand on the field: across the top, how much of each side is still
/// standing — the day is won when the other bar is empty — and along the foot a card for each of
/// his squads with how many are left in it, and his orders.</summary>
public partial class BattlefieldBar : Control
{
	private const int CardWide = 104;
	private const int CardHigh = 132;
	private const int StandingWide = 260;
	private static readonly Color Unchosen = new(0.62f, 0.62f, 0.62f);

	private readonly Dictionary<FieldSquad, (Control Card, Label Count)> _cards = new();
	private FieldBattle _battle;
	private ColorRect _ourStanding;
	private ColorRect _theirStanding;
	private Label _state;
	private Label _captainWord;
	private Action<bool> _captain;

	public void Lay(FieldBattle battle, BattlePanel.Colours us, BattlePanel.Colours them,
		Action<FieldSquad, bool> pick, Action charge, Action hold, Action<bool> captain, Action retreat)
	{
		_battle = battle;
		_captain = captain;
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Ignore;

		var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		head.AddThemeConstantOverride("separation", 14);
		head.AddChild(Chrome.Line(us.Name, 20, us.Accent));
		head.AddChild(Standing(out _ourStanding, us.Accent));
		_state = Chrome.Line("", 18, Chrome.Cream);
		_state.CustomMinimumSize = new Vector2(150, 0);
		_state.HorizontalAlignment = HorizontalAlignment.Center;
		head.AddChild(_state);
		head.AddChild(Standing(out _theirStanding, them.Accent));
		head.AddChild(Chrome.Line(them.Name, 20, them.Accent));
		var top = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		top.SetAnchorsPreset(LayoutPreset.TopWide);
		top.OffsetTop = 12;
		top.AddChild(Chrome.Framed(head, 8));
		AddChild(top);

		var foot = new HBoxContainer();
		foot.AddThemeConstantOverride("separation", 10);
		foreach (FieldSquad squad in battle.Squads)
		{
			if (!squad.IsAttacking)
			{
				continue;
			}

			(Control card, VBoxContainer stack) = UnitCard.Build(squad.Unit, squad.Kind.Name, CardHigh, titled: false,
				() => pick(squad, Input.IsKeyPressed(Key.Shift)));
			card.CustomMinimumSize = new Vector2(CardWide, CardHigh);
			card.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
			Label count = Chrome.Line("", 20, Chrome.Cream);
			count.HorizontalAlignment = HorizontalAlignment.Center;
			stack.AddChild(count);
			foot.AddChild(card);
			_cards[squad] = (card, count);
		}

		foot.AddChild(new Control { CustomMinimumSize = new Vector2(24, 0) });
		var orders = new GridContainer { Columns = 2 };
		orders.AddThemeConstantOverride("h_separation", 8);
		orders.AddThemeConstantOverride("v_separation", 8);
		orders.AddChild(Order("Charge", "crossed-swords", charge, out _));
		orders.AddChild(Order("Hold", "shield", hold, out _));
		orders.AddChild(Order("", "helmet", () => captain(!_battle.IsAttackCaptained), out _captainWord));
		orders.AddChild(Order("Retreat", "footsteps", retreat, out _));
		foot.AddChild(orders);

		PanelContainer bottom = Chrome.Framed(foot, 10);
		bottom.SetAnchorsPreset(LayoutPreset.CenterBottom);
		bottom.GrowHorizontal = GrowDirection.Both;
		bottom.GrowVertical = GrowDirection.Begin;
		bottom.Position -= new Vector2(0, 12);
		AddChild(bottom);

		Label help = Chrome.Line("Left: choose · Right: march, twice to run, or fall on (drag: draw their front) · WASD / Q E / wheel: look · Space: pause", 15, Chrome.Dim);
		help.SetAnchorsPreset(LayoutPreset.BottomLeft);
		help.GrowVertical = GrowDirection.Begin;
		help.Position += new Vector2(16, -8);
		AddChild(help);
	}

	/// <summary>Brings the bar up to the moment: who is left, who is in hand, and how the day stands.</summary>
	public void Refresh(ICollection<FieldSquad> chosen, bool isPaused)
	{
		foreach ((FieldSquad squad, (Control card, Label count)) in _cards)
		{
			count.Text = $"{squad.Standing:N0}";
			card.Modulate = !squad.IsStanding ? new Color(0.35f, 0.35f, 0.35f)
				: chosen.Contains(squad) ? Colors.White : Unchosen;
		}

		Fill(_ourStanding, 1f - _battle.AttackLoss);
		Fill(_theirStanding, 1f - _battle.DefenceLoss);
		_captainWord.Text = _battle.IsAttackCaptained ? "Command" : "Captain";
		_state.Text = _battle.IsAttackFallen ? "Our last man is down"
			: _battle.IsDefenceFallen ? "The field is ours!"
			: isPaused ? "Paused"
			: _battle.IsAttackCaptained ? "The captain leads"
			: "";
	}

	/// <summary>A bar of how much of a side is still standing.</summary>
	private static Control Standing(out ColorRect filled, Color colour)
	{
		var bar = new HBoxContainer { CustomMinimumSize = new Vector2(StandingWide, 14), SizeFlagsVertical = SizeFlags.ShrinkCenter };
		bar.AddThemeConstantOverride("separation", 0);
		filled = new ColorRect { Color = colour, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		var rest = new ColorRect { Color = new Color(0f, 0f, 0f, 0.5f), SizeFlagsHorizontal = SizeFlags.ExpandFill };
		bar.AddChild(filled);
		bar.AddChild(rest);
		filled.SizeFlagsStretchRatio = 0.001f;
		rest.SizeFlagsStretchRatio = 1f;
		return bar;
	}

	private static void Fill(ColorRect filled, float share)
	{
		filled.SizeFlagsStretchRatio = Mathf.Max(0.001f, share);
		filled.GetParent().GetChild<ColorRect>(1).SizeFlagsStretchRatio = Mathf.Max(0.001f, 1f - share);
	}

	private static Button Order(string text, string icon, Action pressed, out Label word)
	{
		Button order = Chrome.Order(text, icon, pressed, out word);
		order.CustomMinimumSize = new Vector2(230, 56);
		return order;
	}
}
