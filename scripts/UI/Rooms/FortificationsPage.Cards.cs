using System.Collections.Generic;
using Godot;

/// <summary>The two counters of cards: one card a rung, mounted and dressed for what the county
/// holds, and the rung picked from them read aloud.</summary>
public partial class FortificationsPage
{
	/// <summary>One card: the fort as it will look, its name, and what it costs. The picture may not
	/// have been drawn yet, in which case the mount stands empty rather than the card vanishing.</summary>
	private Control BuildCard(Fort fort)
	{
		var card = new Button
		{
			CustomMinimumSize = new Vector2(150, 258),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = fort.Blurb,
		};
		Fort chosen = fort;
		card.Pressed += () => Pick(chosen);
		_cards[fort.Key] = card;

		var inset = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", 8);
		}

		card.AddChild(inset);
		inset.SetAnchorsPreset(LayoutPreset.FullRect);

		var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		stack.AddThemeConstantOverride("separation", 6);
		inset.AddChild(stack);

		stack.AddChild(Mount(fort.Key, SizeFlags.ExpandFill));

		Label name = Line(fort.Name.ToUpperInvariant(), 14, Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		name.MouseFilter = MouseFilterEnum.Ignore;
		// Two lines, always: "Motte and Double Bailey" does not fit across a card on one, and a
		// label that grows only on that card would leave its picture shorter than its neighbours'.
		name.AutowrapMode = TextServer.AutowrapMode.Word;
		name.VerticalAlignment = VerticalAlignment.Center;
		name.CustomMinimumSize = new Vector2(0, 38);
		stack.AddChild(name);

		// Two to a row rather than all across: a card is narrower than three prices and a duration,
		// and the last one was being cut off at the edge of the counter. The seasons take the last
		// cell rather than a line of their own — what a wall costs and how long it takes are one
		// reading, not two.
		var price = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
		price.AddThemeConstantOverride("h_separation", 14);
		price.AddThemeConstantOverride("v_separation", 3);

		// Held to two rows whether a rung needs one or three, so every card's footing is the same
		// depth and the pictures above them line up across the counter.
		var centred = new CenterContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(0, 59),
		};
		centred.AddChild(price);
		stack.AddChild(centred);

		foreach ((string store, int amount) in fort.Cost)
		{
			var group = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Icon(store, 24));
			Label cost = Line(amount.ToString("N0"), 16, Bright);
			_prices[$"{fort.Key}/{store}"] = cost;
			group.AddChild(cost);
			price.AddChild(group);
		}

		Label takes = Line($"{fort.Seasons} season{(fort.Seasons == 1 ? "" : "s")}", 14, Soft);
		takes.VerticalAlignment = VerticalAlignment.Center;
		takes.MouseFilter = MouseFilterEnum.Ignore;
		price.AddChild(takes);

		DressCard(fort.Key);
		return card;
	}

	/// <summary>The frame a fort's picture sits in, dark enough that a drawing's sky does not run
	/// into the card's own border. Empty until the art exists.</summary>
	private static Control Mount(string fort, SizeFlags vertical)
	{
		var mount = new PanelContainer
		{
			SizeFlagsVertical = vertical,
			ClipContents = true,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		mount.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.06f, 1f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.55f),
		});

		if (FortArt.Has(fort))
		{
			mount.AddChild(new TextureRect
			{
				Texture = GD.Load<Texture2D>(FortArt.Picture(fort)),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}

		return mount;
	}

	/// <summary>A card is on the counter, lit as the one being read, ringed green as the one already
	/// standing, or ringed blue as the one going up. What the province has and what it is getting
	/// both beat what is merely being read: those are the two a player scans for.</summary>
	private void DressCard(string fort)
	{
		bool standing = fort == Province?.Fortification;
		bool rising = fort == Province?.Building;
		bool lit = _chosen != null && fort == _chosen.Key;

		StyleBoxFlat style = CardStyle(lit
			? new Color(0.20f, 0.15f, 0.06f, 0.94f)
			: new Color(0.078f, 0.075f, 0.078f, 0.9f));
		style.BorderColor = standing
			? new Color(0.60f, 0.86f, 0.55f, 1f)   // already raised
			: rising ? new Color(0.55f, 0.75f, 1f, 1f)  // the masons are on it
			: lit ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.549f, 0.447f, 0.271f, 0.8f);
		style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom =
			lit || standing || rising ? 3 : 2;

		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			_cards[fort].AddThemeStyleboxOverride(state, style);
		}
	}

	/// <summary>What pressing a card does: reads the rung, stands it in the valley, and names it
	/// aloud. Distinct from Choose, which sets the page without saying anything — the page settles
	/// on a rung when it opens too, and a room that announces itself the moment you walk in wears
	/// thin by the third province.</summary>
	private void Pick(Fort fort)
	{
		Choose(fort, fort?.Key);
		Speak(fort);
	}

	private void Speak(Fort fort)
	{
		string path = fort == null ? null : $"{VoiceDirectory}/{fort.Key}.mp3";
		if (path == null || !ResourceLoader.Exists(path))
		{
			return; // not recorded yet
		}

		Narrator.Say(path);
	}

	/// <summary>Reads one rung while the valley shows another — or none. The two part company once,
	/// on opening a province that has built nothing: the panel has to say something, and the valley
	/// must not say you own a palisade you have never paid for.</summary>
	private void Choose(Fort fort, string showing)
	{
		_chosen = fort;
		ShowWall(showing);
		foreach (string key in _cards.Keys)
		{
			DressCard(key);
		}

		ShowDetail();
	}

	private Fort Find(string key)
	{
		foreach (Ladder ladder in _ladders)
		{
			Fort found = ladder.Forts.Find(fort => fort.Key == key);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	/// <summary>The ladder the chosen fort stands on, read left to right with the chosen rung lit:
	/// what a province can raise next, and what it is working towards.</summary>
	private Control BuildLadder()
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 4);

		List<Fort> rungs = _ladders.Find(ladder => ladder.Key == _chosen.Material).Forts;
		for (int step = 0; step < rungs.Count; step++)
		{
			if (step > 0)
			{
				Label arrow = Line("›", 15, Dim);
				arrow.VerticalAlignment = VerticalAlignment.Center;
				row.AddChild(arrow);
			}

			bool here = rungs[step].Key == _chosen.Key;
			Label rung = Line(rungs[step].Short, 13, here ? Bright : Dim);
			rung.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(rung);
		}

		return row;
	}
}
