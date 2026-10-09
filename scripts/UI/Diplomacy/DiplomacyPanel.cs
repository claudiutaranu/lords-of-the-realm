using System.Collections.Generic;
using Godot;

/// <summary>The lords of the realm and what stands between them and the player, as Lords of the
/// Realm's diplomacy screen had them, on the army's painted table: the lord's face, his realm's
/// shield, how well he thinks of the player, whether they are sworn or at war, and along the foot
/// the letters that can go to him this season. One lord at a time; with more than one, the arrows
/// beside his name turn between them.
///
/// Letters go out now and are answered in the lords' turn (Diplomacy); a gift is paid as it is sent
/// (TurnManager.Write). One letter a lord a season: once it is written, the foot of the table says it
/// is on the road instead of offering more.</summary>
public partial class DiplomacyPanel : PaintedPanel
{
	/// <summary>A gift is counted out fifty crowns to a press, and starts at the smallest that moves
	/// anybody at all.</summary>
	private const int GiftStep = 50;

	private const int PortraitSide = 300;
	private const int BannerHeight = 400;

	/// <summary>The frame's inner top corners, in the panel's own pixels, where the banners' poles
	/// meet; and how far in from the column's edge the writing keeps clear of their cloth.</summary>
	private static readonly Vector2 Corner = new(52, 106);
	private const int ClearOfBanner = 120;
	private const int ShieldSide = 108;
	private static readonly Color Hostile = new("d2493f");

	/// <summary>Raised when a letter has gone, so the purse on the page can come down.</summary>
	public event System.Action Wrote;

	private TurnManager _turns;
	private System.Func<string, string> _realmName;
	private System.Func<string, Color> _accent;
	private VBoxContainer _table;
	private int _shown;
	private readonly Dictionary<string, int> _gift = new();
	private readonly Dictionary<string, int> _target = new();
	private readonly List<Control> _banners = new();

	protected override Vector2 PanelSize => new(1440, 860);

	protected override void Furnish()
	{
		// Clear of the ribbon: the realm's name stood on its lower edge.
		Column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
		_table = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		_table.AddThemeConstantOverride("separation", 16);
		Column.AddChild(_table);
	}

	public void Open(TurnManager turns, System.Func<string, string> realmName, System.Func<string, Color> accent)
	{
		_turns = turns;
		_realmName = realmName;
		_accent = accent;
		Title.Text = "Diplomacy";
		Lay();
		Reveal();
	}

	/// <summary>Lays the table out again for the lord in hand.</summary>
	private void Lay()
	{
		foreach (Node old in _table.GetChildren())
		{
			old.QueueFree();
		}

		var lords = new List<(string Realm, Lord Lord)>();
		foreach (string realm in _turns.Rivals())
		{
			if (Lords.Find(_turns.LordOf.GetValueOrDefault(realm, "")) is Lord lord)
			{
				lords.Add((realm, lord));
			}
		}

		if (lords.Count == 0)
		{
			Label nobody = Chrome.Line("There is no lord left in the realm to write to.", 22, Chrome.Soft);
			nobody.HorizontalAlignment = HorizontalAlignment.Center;
			_table.AddChild(nobody);
			return;
		}

		_shown = Mathf.PosMod(_shown, lords.Count);
		(string shown, Lord his) = lords[_shown];
		HangBanners(_accent(shown));
		_table.AddChild(Face(shown, his, lords.Count > 1));
		_table.AddChild(Foot(shown));
	}

	/// <summary>His banners in the table's two top corners, dyed his colour; they change with the lord.</summary>
	private void HangBanners(Color lord)
	{
		foreach (Control old in _banners)
		{
			old.QueueFree();
		}

		_banners.Clear();
		foreach (bool isRight in new[] { false, true })
		{
			Control banner = DiplomacyArt.Banner(isRight, BannerHeight, lord);
			float wide = banner.CustomMinimumSize.X;
			banner.Position = new Vector2(isRight ? PanelSize.X - Corner.X - wide : Corner.X, Corner.Y);
			banner.Size = banner.CustomMinimumSize;
			Hang(banner);
			_banners.Add(banner);
		}
	}

	/// <summary>The lord himself, between his banners: his face in its gilt frame down the left, and
	/// down the right his shield, his realm, his mood and his regard. What the two of them last wrote is
	/// not read back here: it pushed the letters off the foot of the table (the user's call).</summary>
	private Control Face(string realm, Lord lord, bool hasOthers)
	{
		// Stood in the middle of the room the ribbon and the tiles leave, as much above it as below.
		var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.Expand | SizeFlags.ShrinkCenter };
		row.AddThemeConstantOverride("separation", 18);
		row.AddChild(new Control { CustomMinimumSize = new Vector2(ClearOfBanner, 0) });
		row.AddChild(DiplomacyArt.Framed(LordPortrait.Moving(lord, PortraitSide), PortraitSide));

		var reading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		reading.AddThemeConstantOverride("separation", 10);
		row.AddChild(reading);
		row.AddChild(new Control { CustomMinimumSize = new Vector2(ClearOfBanner, 0) });

		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 18);
		heading.AddChild(Shield(realm));
		var names = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
		names.AddThemeConstantOverride("separation", 6);
		Label held = Chrome.Line(_realmName(realm), 32, Chrome.Cream);
		GoldTitle.Apply(held);
		names.AddChild(held);
		names.AddChild(Chrome.Line(lord.Title, 22, Chrome.Soft));
		names.AddChild(DiplomacyArt.Rule());
		names.AddChild(Mood(realm));
		heading.AddChild(names);
		if (hasOthers)
		{
			heading.AddChild(Chrome.Plate("◀", 40, () => { _shown--; Lay(); }));
			heading.AddChild(Chrome.Plate("▶", 40, () => { _shown++; Lay(); }));
		}

		reading.AddChild(heading);
		reading.AddChild(DiplomacyArt.Rule());
		reading.AddChild(Regard(_turns.Diplomacy.StandingOf(realm, _turns.PlayerRealm)));
		reading.AddChild(DiplomacyArt.Rule());
		reading.AddChild(Status(realm));

		return row;
	}

	/// <summary>His realm's shield where one has been painted, and the plain one in his colour where not.</summary>
	private Control Shield(string realm)
	{
		string painted = Heraldry.CrestPath(realm);
		bool isPainted = ResourceLoader.Exists(painted);
		return new TextureRect
		{
			Texture = isPainted
				? new AtlasTexture { Atlas = GD.Load<Texture2D>(painted), Region = ProvinceSidebar.CrestRegion }
				: GD.Load<Texture2D>($"{Chrome.IconDirectory}/shield.png"),
			Modulate = isPainted ? Colors.White : _accent(realm).Lightened(0.25f),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(ShieldSide, ShieldSide * 1.15f),
		};
	}

	/// <summary>How he stands toward the player in a word, the original's: at war, sworn, or what his
	/// regard comes to.</summary>
	private Control Mood(string realm)
	{
		Diplomacy book = _turns.Diplomacy;
		string player = _turns.PlayerRealm;
		int standing = book.StandingOf(realm, player);
		int most = _turns.Balance.DiplomacyStandingMost;
		(string word, string icon, Color colour) = book.AtWar(realm, player) ? ("At war", "crossed-swords", Hostile)
			: book.AllyOf(player) == realm ? ("Sworn ally", "scroll", Chrome.Gain)
			: standing >= most * 2 / 3 ? ("Warm", "laurel", Chrome.Gain)
			: standing >= most / 3 ? ("Friendly", "laurel", Chrome.Gain)
			: standing > -most / 3 ? ("Cool", "scales", Chrome.Soft)
			: standing > -most * 2 / 3 ? ("Hostile", "crossed-swords", Hostile)
			: ("Bitter", "crossed-swords", Hostile);
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		TextureRect sign = Chrome.Icon(icon, 36);
		sign.Modulate = colour;
		row.AddChild(sign);
		Label said = Chrome.Line(word, 30, colour);
		said.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(said);
		return row;
	}

	/// <summary>His regard on the original's scale of −30 to +30, as a bar that fills from the middle.</summary>
	private Control Regard(int standing)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		row.AddChild(Chrome.Line("Relation", 21, Chrome.Bright));
		var bar = new DiplomacyArt.StandingBar { Standing = standing, Most = _turns.Balance.DiplomacyStandingMost, CustomMinimumSize = new Vector2(0, 26) };
		bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		row.AddChild(bar);
		Label figure = Chrome.Line($"{standing:+0;-0;0}", 24, standing < 0 ? Hostile : standing > 0 ? Chrome.Gain : Chrome.Soft);
		figure.CustomMinimumSize = new Vector2(52, 0);
		figure.HorizontalAlignment = HorizontalAlignment.Right;
		row.AddChild(figure);
		return row;
	}

	private Control Status(string realm)
	{
		Diplomacy book = _turns.Diplomacy;
		string player = _turns.PlayerRealm;
		int warned = book.Warnings.GetValueOrDefault($"{realm}>{player}");
		string status = book.Offers.Contains(realm) ? "He offers you an alliance."
			: book.AtWar(realm, player) ? "At war with you, and it will not be made up."
			: book.AllyOf(player) == realm ? "Sworn to you."
			: book.AllyOf(realm).Length > 0 ? $"Sworn to {_realmName(book.AllyOf(realm))}."
			: warned > 0 ? $"He has warned you {(warned == 1 ? "once" : $"{warned} times")}. War follows."
			: Diplomacy.IsAllianceOpen(_turns.Rivals().Count) ? "Sworn to nobody."
			: "The only other lord in the realm: there is nobody to be sworn against.";
		bool isBad = book.AtWar(realm, player) || warned > 0;
		Label said = Chrome.Line(status, 20, isBad ? Hostile : Chrome.Bright);
		said.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		return said;
	}

	/// <summary>Along the foot, four tiles as the table was drawn: a gift, a compliment, the alliance
	/// and the insult. Sworn to him, the alliance tile asks for his help, the last one ends it, and a
	/// fifth asks him to march. A letter that cannot go this season
	/// stands greyed with the reason; his offer, while it stands, is answered here; and once a letter
	/// is written the foot says it is on the road.</summary>
	private Control Foot(string realm)
	{
		Diplomacy book = _turns.Diplomacy;
		string player = _turns.PlayerRealm;
		var tiles = new HBoxContainer();
		tiles.AddThemeConstantOverride("separation", 18);

		if (book.Offers.Contains(realm))
		{
			tiles.AddChild(DiplomacyArt.Tile("Accept", "scroll", () => { book.Accept(player, realm); Lay(); }));
			tiles.AddChild(DiplomacyArt.Tile("Refuse", "crossed-swords", () => { book.Decline(realm); Lay(); }));
			return tiles;
		}

		Letter sent = book.Outbox.Find(letter => letter.To == realm);
		if (sent != null)
		{
			// Nothing more to write him this season, so the foot is the word that it has gone, and
			// the way back to the map.
			var gone = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 132) };
			gone.Alignment = BoxContainer.AlignmentMode.Center;
			gone.AddThemeConstantOverride("separation", 14);
			Label road = Chrome.Line($"On the road to him: {LetterPanel.Words(sent, null, _realmName).ToLowerInvariant()}", 20, Chrome.Soft);
			road.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			road.HorizontalAlignment = HorizontalAlignment.Center;
			gone.AddChild(road);
			Button close = Chrome.Order("Close", "", Close, out _);
			close.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			close.CustomMinimumSize = new Vector2(320, 56);
			gone.AddChild(close);
			tiles.AddChild(gone);
			return tiles;
		}

		List<string> kinds = book.Writable(player, realm, _turns.Rivals().Count);
		bool isSworn = book.AllyOf(player) == realm;
		tiles.AddChild(Gift(realm, kinds.Contains(Diplomacy.Gift)));
		if (isSworn)
		{
			tiles.AddChild(Letter(realm, kinds, Diplomacy.AskHelp, "Ask for help", "shield"));
			List<string> others = _turns.Rivals().FindAll(other => other != realm);
			if (others.Count > 0)
			{
				int at = _target.GetValueOrDefault(realm) % others.Count;
				Button march = Letter(realm, kinds, Diplomacy.AskAttack, $"March on {_realmName(others[at])}", "footsteps", others[at]);
				DiplomacyArt.Under(march, Chrome.Plate("▶", 30, () => { _target[realm] = at + 1; Lay(); }));
				tiles.AddChild(march);
			}

			tiles.AddChild(Letter(realm, kinds, Diplomacy.BreakAlliance, "End the alliance", "scroll"));
			return tiles;
		}

		tiles.AddChild(Letter(realm, kinds, Diplomacy.Compliment, "Compliment", "laurel"));
		Button alliance = Letter(realm, kinds, Diplomacy.OfferAlliance, "Negotiate", "scroll");
		if (!Diplomacy.IsAllianceOpen(_turns.Rivals().Count))
		{
			alliance.TooltipText = "No alliance while he is the only other lord: there would be nobody left to fight.";
		}

		tiles.AddChild(alliance);
		tiles.AddChild(Letter(realm, kinds, Diplomacy.Insult, "Insult", "crossed-swords"));
		return tiles;
	}

	/// <summary>A tile for one kind of letter, greyed where that letter cannot go to him this season.</summary>
	private Button Letter(string realm, List<string> kinds, string kind, string label, string icon,
		string about = "")
	{
		Button tile = DiplomacyArt.Tile(label, icon,
			() => Send(new Letter(_turns.PlayerRealm, realm, kind, About: about)));
		tile.Disabled = !kinds.Contains(kind);
		return tile;
	}

	/// <summary>A purse to send: fifty crowns a press of the arrows, never more than the treasury holds.</summary>
	private Control Gift(string realm, bool isOpen)
	{
		int purse = _turns.PlayerGold;
		int gold = Mathf.Clamp(_gift.GetValueOrDefault(realm, GiftStep), 0, purse);
		Button send = DiplomacyArt.Tile("Send gift", "coins",
			() => Send(new Letter(_turns.PlayerRealm, realm, Diplomacy.Gift, gold)));
		send.Disabled = !isOpen || gold <= 0;

		var purseRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		purseRow.AddThemeConstantOverride("separation", 6);
		purseRow.AddChild(Chrome.Repeating("◀", 28, () => { _gift[realm] = Mathf.Max(GiftStep, gold - GiftStep); Lay(); }));
		purseRow.AddChild(Chrome.Icon("gold", 22));
		Label amount = Chrome.Line($"{gold:N0}", 19, Chrome.Bright);
		amount.VerticalAlignment = VerticalAlignment.Center;
		purseRow.AddChild(amount);
		purseRow.AddChild(Chrome.Repeating("▶", 28, () => { _gift[realm] = Mathf.Min(purse, gold + GiftStep); Lay(); }));
		DiplomacyArt.Under(send, purseRow);
		return send;
	}

	private void Send(Letter letter)
	{
		if (_turns.Write(letter))
		{
			Wrote?.Invoke();
		}

		Lay();
	}
}
