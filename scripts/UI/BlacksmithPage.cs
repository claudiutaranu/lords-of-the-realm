using System;
using System.Collections.Generic;
using Godot;

/// <summary>The smithy of one province: what it can forge, what each thing costs out of that
/// province's own stockpile, and the order the player places. It opens over the campaign map
/// rather than replacing it, so the turn, the selection and everything else stay exactly as they
/// were behind it.
///
/// What can be forged is engine data (data/weapons.json), not a campaign's: every realm's smithy
/// works the same. The page never edits that file's numbers — it reads them, prices the order
/// against the province, and writes the order onto the province.
///
/// Built in code for the same reason the sidebar is: six choices and four stat bars are the same
/// piece repeated, and a loop keeps them even.</summary>
public partial class BlacksmithPage : Control
{
	private const string WeaponsPath = "res://data/weapons.json";
	private const string IconDirectory = "res://assets/ui/icons";

	private static readonly Color Cream = new("d9cdb4");
	private static readonly Color Dim = new("8d8577");
	private static readonly Color Short = new("c9604e"); // a cost the province cannot meet

	// The stockpiles an order can be priced in, and the icon each one is read by.
	private static readonly (string Key, string Icon)[] Purses =
	{
		("gold", "gold"),
		("grain", "food"),
		("cattle", "livestock"),
		("wood", "wood"),
		("stone", "stone"),
		("iron", "iron"),
	};

	/// <summary>Where each sign hangs, in the video frame's own proportions — over the rack it names.
	/// The player is choosing off the wall of the forge itself, not off a list beside it, so these
	/// are read from the film rather than laid out by a container.</summary>
	private static readonly Dictionary<string, Vector2> SignSpots = new()
	{
		["bow"] = new Vector2(0.105f, 0.330f),
		["crossbow"] = new Vector2(0.225f, 0.470f),
		["sword"] = new Vector2(0.410f, 0.470f),
		["spear"] = new Vector2(0.600f, 0.560f),
		["horse"] = new Vector2(0.745f, 0.570f),
		["mace"] = new Vector2(0.920f, 0.570f),
	};

	private record Weapon(string Key, string Name, string Blurb, int Attack, int Range, int Defence,
		int Speed, int Turns, int Batch, Dictionary<string, int> Cost);

	/// <summary>Raised when the player shuts the smithy, so the map can drop this page and refresh
	/// whatever the order changed.</summary>
	public event Action Closed;

	private readonly List<Weapon> _weapons = new();
	private ProvinceEconomy _province;
	private Weapon _chosen;

	private readonly Dictionary<string, Button> _signs = new();
	private Label _provinceLabel;
	private ResourceBar _stores;
	private VBoxContainer _detail;

	public override void _Ready()
	{
		// Five seconds of forge, played back to back: the room should never stop moving.
		var background = GetNode<VideoStreamPlayer>("Background");
		background.Finished += background.Play;

		LoadWeapons();
		BuildChrome();
	}

	/// <summary>Opens the smithy for one province. Everything on the page is priced against it.</summary>
	public void Open(ProvinceEconomy province)
	{
		_province = province;
		_provinceLabel.Text = province.ProvinceName;
		Choose(_weapons.Count > 0 ? _weapons[0] : null);
		Refresh();
	}

	/// <summary>Re-reads the province: its purses along the top, and whether the chosen order is
	/// still affordable.</summary>
	public void Refresh()
	{
		_stores.Show(_province);
		ShowDetail();
	}

	// --- the page ------------------------------------------------------------------------------

	private void LoadWeapons()
	{
		var file = GD.Load<Json>(WeaponsPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Blacksmith: no readable {WeaponsPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["weapons"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			var cost = new Dictionary<string, int>();
			foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> line in fields["cost"].AsGodotDictionary())
			{
				cost[line.Key.AsString()] = line.Value.AsInt32();
			}

			_weapons.Add(new Weapon(
				fields["key"].AsString(),
				fields["name"].AsString(),
				fields["blurb"].AsString(),
				fields["attack"].AsInt32(),
				fields["range"].AsInt32(),
				fields["defence"].AsInt32(),
				fields["speed"].AsInt32(),
				fields["turns"].AsInt32(),
				fields["batch"].AsInt32(),
				cost));
		}
	}

	private void BuildChrome()
	{
		// The video underneath is a room, not a backdrop: the wash is only enough to stop the title
		// dissolving into the firelight. The panels carry their own dark, so this stays light.
		var wash = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.14f) };
		wash.SetAnchorsPreset(LayoutPreset.FullRect);
		wash.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(wash);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "left", "right", "bottom" })
		{
			margin.AddThemeConstantOverride($"margin_{side}", 28);
		}

		margin.AddThemeConstantOverride("margin_top", 10);

		AddChild(margin);

		var page = new VBoxContainer();
		page.AddThemeConstantOverride("separation", 4);
		margin.AddChild(page);

		page.AddChild(BuildTopRow());
		page.AddChild(BuildTitle());

		var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 20);
		body.Alignment = BoxContainer.AlignmentMode.End;
		page.AddChild(body);

		body.AddChild(BuildDetailPanel());
		HangCloseButton();
		HangSigns();
	}

	/// <summary>The shared stockpile strip, centred over the page: the same component every scene
	/// that spends a province's goods puts at the top.</summary>
	private Control BuildTopRow()
	{
		var centre = new CenterContainer();
		_stores = new ResourceBar();
		centre.AddChild(_stores);
		return centre;
	}

	/// <summary>The way out, in the corner it is always in — anchored to the page rather than laid
	/// out with the purses, so centring them cannot push it around.</summary>
	private void HangCloseButton()
	{
		var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(52, 52) };
		close.AddThemeFontSizeOverride("font_size", 22);
		close.Pressed += () => Closed?.Invoke();
		AddChild(close);

		close.SetAnchorsPreset(LayoutPreset.TopRight);
		close.OffsetLeft = -80;
		close.OffsetTop = 12;
		close.OffsetRight = -28;
		close.OffsetBottom = 80;
	}

	private Control BuildTitle()
	{
		var titles = new VBoxContainer();
		titles.AddThemeConstantOverride("separation", 2);

		var name = new Label
		{
			Text = "BLACKSMITH",
			HorizontalAlignment = HorizontalAlignment.Center,
			ThemeTypeVariation = "GildedTitle",
		};
		name.AddThemeFontSizeOverride("font_size", 58);
		GoldTitle.Apply(name);
		titles.AddChild(name);

		// A rule with a diamond on it, the way the rest of the game separates a heading from what
		// follows it.
		var rule = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		rule.AddThemeConstantOverride("separation", 10);
		rule.AddChild(Rule(150));
		rule.AddChild(Line("◆", 12, new Color(0.72f, 0.60f, 0.36f)));
		rule.AddChild(Rule(150));
		titles.AddChild(rule);

		Label tagline = Line("Forge stronger armies", 19, Cream);
		tagline.HorizontalAlignment = HorizontalAlignment.Center;
		titles.AddChild(tagline);

		_provinceLabel = Line("", 15, Dim);
		_provinceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titles.AddChild(_provinceLabel);
		return titles;
	}

	/// <summary>A hairline of the same gold the frames use, for the rule under the title.</summary>
	private static Control Rule(int width)
	{
		var rule = new ColorRect
		{
			Color = new Color(0.549f, 0.447f, 0.271f, 0.55f),
			CustomMinimumSize = new Vector2(width, 1),
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		return rule;
	}

	/// <summary>The smith's wares, as one row of cards across the centre of the page. They used to be
	/// pinned over the racks in the film itself, which scattered them across the room and dropped one
	/// on top of the title; a row reads as a set of choices, which is what they are.</summary>
	private void HangSigns()
	{
		foreach (Weapon weapon in _weapons)
		{
			if (!SignSpots.TryGetValue(weapon.Key, out Vector2 spot))
			{
				continue; // no place on the wall picked for it yet
			}

			// The button's own icon and label, not a container laid inside it: a Button centres those
			// itself, where a child container has to be given a size and quietly sat in the corner
			// when it was not.
			var sign = new Button
			{
				TooltipText = weapon.Blurb,
				Text = weapon.Name.ToUpperInvariant(),
				Icon = GD.Load<Texture2D>($"{IconDirectory}/{weapon.Key}.png"),
				Alignment = HorizontalAlignment.Center,
				IconAlignment = HorizontalAlignment.Left,
				ExpandIcon = false,
			};
			sign.AddThemeFontSizeOverride("font_size", 20);
			sign.AddThemeColorOverride("font_color", Cream);
			sign.AddThemeColorOverride("font_hover_color", new Color(1f, 0.92f, 0.72f));
			sign.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.92f, 0.72f));
			sign.AddThemeConstantOverride("h_separation", 12);
			sign.AddThemeConstantOverride("icon_max_width", 34);
			Weapon chosen = weapon;
			sign.Pressed += () => Choose(chosen);
			AddChild(sign);

			sign.AnchorLeft = sign.AnchorRight = spot.X;
			sign.AnchorTop = sign.AnchorBottom = spot.Y;
			sign.OffsetLeft = -116;
			sign.OffsetRight = 116;
			sign.OffsetTop = -32;
			sign.OffsetBottom = 32;

			_signs[weapon.Key] = sign;
			DressSign(weapon.Key, lit: false);
		}
	}

	/// <summary>A card is either waiting on the bench or lit as the one the smith is working to.
	/// The lit one carries a gold edge and a warm glow off the forge; the rest sit back in the dark,
	/// and lift a little under the cursor so the row answers the mouse.</summary>
	private void DressSign(string key, bool lit)
	{
		Button sign = _signs[key];

		StyleBoxFlat resting = CardStyle(lit
			? new Color(0.19f, 0.135f, 0.06f, 0.96f)   // lit by the forge
			: new Color(0.085f, 0.065f, 0.048f, 0.92f)); // dark oak
		resting.BorderColor = lit ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.51f, 0.41f, 0.25f, 0.9f);
		resting.BorderWidthLeft = resting.BorderWidthTop = resting.BorderWidthRight = resting.BorderWidthBottom = lit ? 3 : 2;
		resting.CornerRadiusTopLeft = resting.CornerRadiusTopRight = 3;
		resting.CornerRadiusBottomLeft = resting.CornerRadiusBottomRight = 3;
		resting.ContentMarginLeft = resting.ContentMarginRight = 14;
		resting.ContentMarginTop = resting.ContentMarginBottom = 8;
		// Every plaque throws a shadow on the wall; the chosen one throws firelight instead.
		resting.ShadowColor = lit ? new Color(1f, 0.74f, 0.33f, 0.45f) : new Color(0f, 0f, 0f, 0.55f);
		resting.ShadowSize = lit ? 14 : 6;
		resting.ShadowOffset = lit ? Vector2.Zero : new Vector2(2, 3);

		StyleBoxFlat hovered = (StyleBoxFlat)resting.Duplicate();
		hovered.BgColor = lit ? new Color(0.27f, 0.2f, 0.09f, 0.96f) : new Color(0.10f, 0.088f, 0.075f, 0.92f);
		hovered.BorderColor = new Color(1f, 0.84f, 0.45f, lit ? 1f : 0.85f);

		sign.AddThemeStyleboxOverride("normal", resting);
		sign.AddThemeStyleboxOverride("focus", resting);
		sign.AddThemeStyleboxOverride("hover", hovered);
		sign.AddThemeStyleboxOverride("pressed", hovered);
	}

	private Control BuildDetailPanel()
	{
		_detail = new VBoxContainer { CustomMinimumSize = new Vector2(430, 0) };
		_detail.AddThemeConstantOverride("separation", 10);

		var holder = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkEnd };
		holder.AddThemeStyleboxOverride("panel", CardStyle(new Color(0.05f, 0.045f, 0.04f, 0.92f)));

		var inset = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", 16);
		}

		holder.AddChild(inset);
		inset.AddChild(_detail);
		return holder;
	}

	private void Choose(Weapon weapon)
	{
		if (_chosen != null && _signs.ContainsKey(_chosen.Key))
		{
			DressSign(_chosen.Key, lit: false);
		}

		_chosen = weapon;
		if (weapon != null && _signs.ContainsKey(weapon.Key))
		{
			DressSign(weapon.Key, lit: true);
		}

		ShowDetail();
	}

	// --- the panel on the right ----------------------------------------------------------------

	private void ShowDetail()
	{
		if (_detail == null)
		{
			return;
		}

		foreach (Node child in _detail.GetChildren())
		{
			child.QueueFree();
		}

		if (_chosen == null || _province == null)
		{
			return;
		}

		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 10);
		heading.AddChild(Icon(_chosen.Key, 30));
		Label title = Line(_chosen.Name, 24, Cream);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		_detail.AddChild(heading);

		// The weapon itself down the left, the reading of it down the right.
		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 14);
		_detail.AddChild(body);
		body.AddChild(Icon(_chosen.Key, 96));

		var reading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		reading.AddThemeConstantOverride("separation", 8);
		body.AddChild(reading);

		Label blurb = Line(_chosen.Blurb, 14, Dim);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		reading.AddChild(blurb);

		reading.AddChild(Bar("Attack", _chosen.Attack));
		reading.AddChild(Bar("Range", _chosen.Range));
		reading.AddChild(Bar("Defence", _chosen.Defence));
		reading.AddChild(Bar("Speed", _chosen.Speed));

		var terms = new HBoxContainer();
		terms.AddThemeConstantOverride("separation", 14);
		terms.AddChild(Line("Production cost", 14, Dim));
		foreach ((string key, int amount) in _chosen.Cost)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Icon(IconFor(key), 22));
			// Red when the province is short of it: the reason the button below is dead.
			group.AddChild(Line(amount.ToString(), 16, Held(key) >= amount ? Cream : Short));
			terms.AddChild(group);
		}

		_detail.AddChild(terms);
		_detail.AddChild(Line($"{_chosen.Batch} delivered in {_chosen.Turns} turn{(_chosen.Turns == 1 ? "" : "s")}", 14, Dim));

		bool busy = _province.Forging.Length > 0;
		var order = new Button
		{
			Text = busy ? "The forge is busy" : "Start production",
			CustomMinimumSize = new Vector2(0, 52),
			Disabled = busy || !CanAfford(_chosen),
		};
		order.AddThemeFontSizeOverride("font_size", 19);
		order.Pressed += PlaceOrder;
		_detail.AddChild(order);

		if (busy)
		{
			_detail.AddChild(Line(
				$"Forging {NameOf(_province.Forging)} — {_province.ForgeTurnsLeft} turn{(_province.ForgeTurnsLeft == 1 ? "" : "s")} left",
				14, Cream));
		}
	}

	/// <summary>Pays for the order and puts it on the anvil. The turn does the rest.</summary>
	private void PlaceOrder()
	{
		if (_chosen == null || _province.Forging.Length > 0 || !CanAfford(_chosen))
		{
			return;
		}

		foreach ((string key, int amount) in _chosen.Cost)
		{
			Pay(key, amount);
		}

		_province.Forging = _chosen.Key;
		_province.ForgeTurnsLeft = _chosen.Turns;
		_province.ForgeBatch = _chosen.Batch;
		Refresh();
	}

	private bool CanAfford(Weapon weapon)
	{
		foreach ((string key, int amount) in weapon.Cost)
		{
			if (Held(key) < amount)
			{
				return false;
			}
		}

		return true;
	}

	private string NameOf(string key) => _weapons.Find(weapon => weapon.Key == key)?.Name ?? key;

	private static string IconFor(string purse) => purse switch
	{
		"grain" => "food",
		"cattle" => "livestock",
		_ => purse,
	};

	private int Held(string purse) => purse switch
	{
		"gold" => _province?.Gold ?? 0,
		"grain" => _province?.Grain ?? 0,
		"cattle" => _province?.Cattle ?? 0,
		"wood" => _province?.Wood ?? 0,
		"stone" => _province?.Stone ?? 0,
		_ => _province?.Iron ?? 0,
	};

	private void Pay(string purse, int amount)
	{
		switch (purse)
		{
			case "gold": _province.Gold -= amount; break;
			case "grain": _province.Grain -= amount; break;
			case "cattle": _province.Cattle -= amount; break;
			case "wood": _province.Wood -= amount; break;
			case "stone": _province.Stone -= amount; break;
			default: _province.Iron -= amount; break;
		}
	}

	// --- small parts ---------------------------------------------------------------------------

	private static Control Bar(string label, int value)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);

		Label name = Line(label, 14, Dim);
		name.CustomMinimumSize = new Vector2(80, 0);
		row.AddChild(name);

		var track = new ProgressBar
		{
			MinValue = 0,
			MaxValue = 10,
			Value = value,
			ShowPercentage = false,
			CustomMinimumSize = new Vector2(0, 12),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		track.AddThemeStyleboxOverride("background", new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.11f, 0.10f, 0.9f),
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
		});
		track.AddThemeStyleboxOverride("fill", new StyleBoxFlat
		{
			BgColor = new Color(0.85f, 0.75f, 0.52f, 1f),
			CornerRadiusTopLeft = 2,
			CornerRadiusTopRight = 2,
			CornerRadiusBottomLeft = 2,
			CornerRadiusBottomRight = 2,
		});
		row.AddChild(track);
		return row;
	}

	private static PanelContainer Framed(Control content, int margin)
	{
		var panel = new PanelContainer();
		panel.AddThemeStyleboxOverride("panel", CardStyle(new Color(0.05f, 0.045f, 0.04f, 0.88f)));

		var inset = new MarginContainer();
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", margin);
		}

		panel.AddChild(inset);
		inset.AddChild(content);
		return panel;
	}

	private static StyleBoxFlat CardStyle(Color background) => new()
	{
		BgColor = background,
		BorderWidthLeft = 2,
		BorderWidthTop = 2,
		BorderWidthRight = 2,
		BorderWidthBottom = 2,
		BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.85f),
		CornerRadiusTopLeft = 4,
		CornerRadiusTopRight = 4,
		CornerRadiusBottomRight = 4,
		CornerRadiusBottomLeft = 4,
	};

	private static Label Line(string text, int size, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private static TextureRect Icon(string name, int side) => new()
	{
		Texture = GD.Load<Texture2D>($"{IconDirectory}/{name}.png"),
		CustomMinimumSize = new Vector2(side, side),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
	};
}
