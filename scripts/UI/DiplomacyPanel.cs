using System.Collections.Generic;
using Godot;

/// <summary>The lords of the realm and what stands between them and the player, one card each, as
/// Lords of the Realm's diplomacy screen had them: who he is and which realm he holds, how well he
/// thinks of the player, whether they are sworn or at war, the letters that can go to him this
/// season, and what the two of them last wrote.
///
/// Letters go out now and are answered in the lords' turn (Diplomacy); a gift is paid as it is sent
/// (TurnManager.Write). One letter a lord a season: once it is written his card says it is on the
/// road instead of offering more.</summary>
public partial class DiplomacyPanel : CountyPanel
{
	/// <summary>A gift is counted out fifty crowns to a press, and starts at the smallest that moves
	/// anybody at all.</summary>
	private const int GiftStep = 50;

	/// <summary>How many letters of each correspondence the card reads back.</summary>
	private const int LettersShown = 3;

	/// <summary>Raised when a letter has gone, so the purse on the page can come down.</summary>
	public event System.Action Wrote;

	private TurnManager _turns;
	private System.Func<string, string> _realmName;
	private System.Func<string, Color> _accent;
	private VBoxContainer _lords;
	private readonly Dictionary<string, int> _gift = new();
	private readonly Dictionary<string, int> _target = new();

	protected override int Width => 680;

	protected override void Furnish()
	{
		_lords = new VBoxContainer();
		_lords.AddThemeConstantOverride("separation", 12);
		Column.AddChild(_lords);
	}

	public void Open(TurnManager turns, System.Func<string, string> realmName, System.Func<string, Color> accent)
	{
		_turns = turns;
		_realmName = realmName;
		_accent = accent;
		Show();
		Reveal("Diplomacy");
	}

	private void Show()
	{
		foreach (Node old in _lords.GetChildren())
		{
			old.QueueFree();
		}

		foreach (string realm in _turns.Rivals())
		{
			Lord lord = Lords.Find(_turns.LordOf.GetValueOrDefault(realm, ""));
			if (lord != null)
			{
				_lords.AddChild(Card(realm, lord));
			}
		}

		if (_lords.GetChildCount() == 0)
		{
			_lords.AddChild(Chrome.Line("There is no lord left in the realm to write to.", 19, Chrome.Soft));
		}
	}

	private Control Card(string realm, Lord lord)
	{
		var card = new PanelContainer();
		StyleBoxFlat frame = Chrome.CardStyle(new Color(0f, 0f, 0f, 0.28f));
		frame.BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.35f);
		frame.SetBorderWidthAll(1);
		frame.SetContentMarginAll(14);
		card.AddThemeStyleboxOverride("panel", frame);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		card.AddChild(column);

		column.AddChild(Heading(realm, lord));
		column.AddChild(Regard(_turns.Diplomacy.StandingOf(realm, _turns.PlayerRealm)));
		column.AddChild(Status(realm));
		column.AddChild(Letters(realm));

		List<Letter> kept = _turns.Diplomacy.Kept.FindAll(letter => letter.From == realm || letter.To == realm);
		for (int index = Mathf.Max(0, kept.Count - LettersShown); index < kept.Count; index++)
		{
			Letter letter = kept[index];
			string who = letter.From == realm ? lord.Title : "You";
			Label line = Chrome.Line($"{who}: {LetterPanel.Words(letter, lord, _realmName)}", 15, Chrome.Dim);
			line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			column.AddChild(line);
		}

		return card;
	}

	/// <summary>The lord's title with his realm's colour beside it, and the realm he holds under it.</summary>
	private Control Heading(string realm, Lord lord)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(new ColorRect { Color = _accent(realm), CustomMinimumSize = new Vector2(6, 48) });

		var names = new VBoxContainer();
		names.AddThemeConstantOverride("separation", 0);
		Label title = Chrome.Line(lord.Title, 26, Chrome.Cream);
		GoldTitle.Apply(title);
		names.AddChild(title);
		names.AddChild(Chrome.Line(_realmName(realm), 16, Chrome.Dim));
		row.AddChild(names);
		return row;
	}

	/// <summary>How he thinks of the player, on the original's scale of −30 to +30, as a bar that fills
	/// from the middle and a word for it.</summary>
	private Control Regard(int standing)
	{
		int most = _turns.Balance.DiplomacyStandingMost;
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		row.AddChild(Chrome.Line("Regard", 17, Chrome.Soft));
		var bar = new StandingBar { Standing = standing, Most = most, CustomMinimumSize = new Vector2(0, 14) };
		bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(bar);
		string word = standing >= most * 2 / 3 ? "Warm"
			: standing >= most / 3 ? "Friendly"
			: standing > -most / 3 ? "Cool"
			: standing > -most * 2 / 3 ? "Hostile"
			: "Bitter";
		Label reading = Chrome.Line($"{word} ({standing:+0;-0;0})", 17, standing < 0 ? Chrome.Short : standing > 0 ? Chrome.Gain : Chrome.Soft);
		reading.CustomMinimumSize = new Vector2(130, 0);
		reading.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(reading);
		return row;
	}

	private Control Status(string realm)
	{
		Diplomacy book = _turns.Diplomacy;
		string player = _turns.PlayerRealm;
		if (book.Offers.Contains(realm))
		{
			var offer = new HBoxContainer();
			offer.AddThemeConstantOverride("separation", 12);
			Label said = Chrome.Line("He offers you an alliance.", 18, Chrome.Gain);
			said.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			offer.AddChild(said);
			offer.AddChild(Small("Accept", () => { book.Accept(player, realm); Show(); }));
			offer.AddChild(Small("Refuse", () => { book.Decline(realm); Show(); }));
			return offer;
		}

		int warned = book.Warnings.GetValueOrDefault($"{realm}>{player}");
		string status = book.AtWar(realm, player) ? "At war with you, and it will not be made up."
			: book.AllyOf(player) == realm ? "Sworn to you."
			: book.AllyOf(realm).Length > 0 ? $"Sworn to {_realmName(book.AllyOf(realm))}."
			: warned > 0 ? $"He has warned you {(warned == 1 ? "once" : $"{warned} times")}. War follows."
			: Diplomacy.IsAllianceOpen(_turns.Rivals().Count) ? "Sworn to nobody."
			: "The only other lord in the realm: there is nobody to be sworn against.";
		bool isBad = book.AtWar(realm, player) || warned > 0;
		return Chrome.Line(status, 18, isBad ? Chrome.Short : Chrome.Soft);
	}

	/// <summary>What can be written to him this season, or word that it already has been.</summary>
	private Control Letters(string realm)
	{
		string player = _turns.PlayerRealm;
		Letter sent = _turns.Diplomacy.Outbox.Find(letter => letter.To == realm);
		if (sent != null)
		{
			return Chrome.Line($"On the road to him: {LetterPanel.Words(sent, null, _realmName).ToLowerInvariant()}", 17, Chrome.Soft);
		}

		List<string> kinds = _turns.Diplomacy.Writable(player, realm, _turns.Rivals().Count);
		var flow = new HFlowContainer();
		flow.AddThemeConstantOverride("h_separation", 8);
		flow.AddThemeConstantOverride("v_separation", 8);

		if (kinds.Contains(Diplomacy.Gift))
		{
			flow.AddChild(Gift(realm));
		}

		foreach ((string kind, string label) in new[]
		{
			(Diplomacy.Compliment, "Compliment"),
			(Diplomacy.Insult, "Insult"),
			(Diplomacy.OfferAlliance, "Offer alliance"),
			(Diplomacy.AskHelp, "Ask for help"),
			(Diplomacy.BreakAlliance, "End the alliance"),
		})
		{
			if (kinds.Contains(kind))
			{
				flow.AddChild(Small(label, () => Send(new Letter(player, realm, kind))));
			}
		}

		// An ally can be asked to march on anybody but himself and the player.
		List<string> others = _turns.Rivals().FindAll(other => other != realm);
		if (kinds.Contains(Diplomacy.AskAttack) && others.Count > 0)
		{
			int at = _target.GetValueOrDefault(realm) % others.Count;
			flow.AddChild(Small($"Ask him to march on {_realmName(others[at])}", () =>
				Send(new Letter(player, realm, Diplomacy.AskAttack, About: others[at]))));
			flow.AddChild(Chrome.Plate("▶", 34, () => { _target[realm] = at + 1; Show(); }));
		}

		return flow;
	}

	/// <summary>A purse to send: fifty crowns a press, never more than the treasury holds.</summary>
	private Control Gift(string realm)
	{
		int purse = _turns.PlayerGold;
		int gold = Mathf.Clamp(_gift.GetValueOrDefault(realm, GiftStep), 0, purse);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);
		row.AddChild(Chrome.Repeating("◀", 34, () => { _gift[realm] = Mathf.Max(GiftStep, gold - GiftStep); Show(); }));
		Button send = Small($"Gift {gold:N0}", () => Send(new Letter(_turns.PlayerRealm, realm, Diplomacy.Gift, gold)));
		send.Disabled = gold <= 0;
		row.AddChild(send);
		row.AddChild(Chrome.Repeating("▶", 34, () => { _gift[realm] = Mathf.Min(purse, gold + GiftStep); Show(); }));
		return row;
	}

	private void Send(Letter letter)
	{
		if (_turns.Write(letter))
		{
			Wrote?.Invoke();
		}

		Show();
	}

	private static Button Small(string text, System.Action pressed)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 38) };
		button.AddThemeFontSizeOverride("font_size", 16);
		button.Pressed += pressed;
		return button;
	}

	/// <summary>A bar that fills from its middle: to the right in green for goodwill, to the left in
	/// red for ill will.</summary>
	private partial class StandingBar : Control
	{
		public int Standing;
		public int Most = 30;

		public override void _Draw()
		{
			var track = new Rect2(Vector2.Zero, Size);
			DrawRect(track, new Color(0f, 0f, 0f, 0.45f));
			float middle = Size.X / 2f;
			float reach = middle * Mathf.Clamp(Mathf.Abs(Standing) / (float)Mathf.Max(1, Most), 0f, 1f);
			Color fill = Standing >= 0 ? Chrome.Gain : Chrome.Short;
			DrawRect(new Rect2(Standing >= 0 ? middle : middle - reach, 0f, reach, Size.Y), fill);
			DrawLine(new Vector2(middle, -2f), new Vector2(middle, Size.Y + 2f), Chrome.Cream, 2f);
			DrawRect(track, new Color(0.549f, 0.447f, 0.271f, 0.7f), false, 1f);
		}
	}
}
