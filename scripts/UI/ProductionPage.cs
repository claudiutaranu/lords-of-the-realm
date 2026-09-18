using System;
using System.Collections.Generic;
using Godot;

/// <summary>A place in a province where something is ordered and then waited for: the smithy
/// forging weapons, the yard training men. Both work the same way — a room on film, signs hung over
/// what can be ordered from it, and a panel that reads the chosen one and takes the order — so the
/// room itself is all a page has to supply.
///
/// It opens over the campaign map rather than replacing it, so the turn, the camera and the
/// selection are exactly where they were when it closes. What can be ordered is engine data, not a
/// campaign's: every realm's smithy and every realm's yard work alike.
///
/// A page supplies: what it is called, where its data is, where its signs hang, which stockpiles an
/// order is priced in, and what placing one does to the province. Everything else is here.</summary>
public abstract partial class ProductionPage : Control
{
	protected const string IconDirectory = "res://assets/ui/icons";
	private const string SquarePlatePath = "res://assets/ui/button-square.png";

	protected static readonly Color Cream = new("d9cdb4");
	protected static readonly Color Dim = new("8d8577");
	protected static readonly Color Short = new("c9604e"); // a cost the province cannot meet

	/// <summary>One thing that can be ordered here. <paramref name="Icon"/> is usually the key
	/// itself, and is named separately where the thing is better known by something other than
	/// itself — cavalry by a helm rather than by a horse's head.</summary>
	protected record Item(string Key, string Name, string Icon, string Blurb, int Attack, int Range,
		int Defence, int Speed, int Turns, int Batch, Dictionary<string, int> Cost);

	/// <summary>Raised when the player shuts the room, so the map can drop this page and refresh
	/// whatever the order changed.</summary>
	public event Action Closed;

	protected ProvinceEconomy Province { get; private set; }

	protected readonly List<Item> Items = new();
	private readonly Dictionary<string, Button> _signs = new();
	private Item _chosen;
	private int _count;
	private Label _provinceLabel;
	/// <summary>The row across the foot of the page: whatever a room lays its choices out in goes
	/// here, to the left of the panel that reads them.</summary>
	protected HBoxContainer Body { get; private set; }

	private HBoxContainer _purseRow;
	private VBoxContainer _detail;

	// --- what each room says for itself --------------------------------------------------------

	protected abstract string DataPath { get; }

	protected abstract string RoomName { get; }

	/// <summary>Where each sign hangs, in the video frame's own proportions — over the thing it
	/// names. Read off a still of the film rather than laid out by a container: the player is
	/// choosing off the room itself, not off a list beside it.</summary>
	protected abstract Dictionary<string, Vector2> SignSpots { get; }

	/// <summary>The stores read along the top of the page. Both rooms show the province's own, so it
	/// is the same strip in each; a room with something else to count says so. The yard reads its
	/// people and its armoury in a band of its own instead.</summary>
	protected virtual (string Key, string Icon)[] Purses { get; } =
	{
		("gold", "gold"),
		("grain", "food"),
		("wood", "wood"),
		("stone", "stone"),
		("iron", "iron"),
	};

	protected abstract int Held(string purse);

	protected abstract void Pay(string purse, int amount);

	/// <summary>What is on the bench now, and how many turns are left on it. An empty key means the
	/// room is idle and will take an order.</summary>
	protected abstract (string Making, int TurnsLeft) InHand { get; }

	/// <summary>Writes the order onto the province: what, and how many of it.</summary>
	protected abstract void Begin(Item item, int count);

	/// <summary>A line under the room's name, where it has one.</summary>
	protected virtual string Tagline => null;

	/// <summary>Whether an order here is sized by the player. A smith takes a commission as it
	/// comes; a captain is asked how many men.</summary>
	protected virtual bool SizedOrder => false;

	protected virtual int OrderStep => 5;

	protected abstract string BusyLine { get; }

	protected abstract string OrderLine { get; }

	/// <summary>How an order reads when it lands: forged and delivered, or trained and mustered.</summary>
	protected abstract string DeliveryLine(Item item);

	/// <summary>How the work in hand reads while it is still on the bench. A room wraps this in what
	/// it is doing to the thing — forging it, drilling it.</summary>
	protected virtual string MakingLine(string name, int turns) =>
		$"{name} — {turns} turn{(turns == 1 ? "" : "s")} left";

	/// <summary>The icon a stockpile is read by, where it is not simply its own name.</summary>
	protected virtual string IconFor(string purse) => purse;

	// --- the page ------------------------------------------------------------------------------

	public override void _Ready()
	{
		// A few seconds of the room, played back to back: it should never stop moving.
		var background = GetNode<VideoStreamPlayer>("Background");
		background.Finished += background.Play;

		LoadItems();
		BuildChrome();
	}

	/// <summary>Opens the room for one province. Everything on the page is priced against it.</summary>
	public void Open(ProvinceEconomy province)
	{
		Province = province;
		_provinceLabel.Text = province.ProvinceName;
		Choose(Items.Count > 0 ? Items[0] : null);
		Refresh();
	}

	/// <summary>Re-reads the province: its stockpiles along the top, and whether the chosen order is
	/// still affordable.</summary>
	public virtual void Refresh()
	{
		foreach (Node cell in _purseRow.GetChildren())
		{
			cell.QueueFree();
		}

		foreach ((string key, string icon) in Purses)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 6);
			group.AddChild(Icon(icon, 26));

			var amount = new Label { Text = Held(key).ToString("N0"), VerticalAlignment = VerticalAlignment.Center };
			amount.AddThemeFontSizeOverride("font_size", 18);
			amount.AddThemeColorOverride("font_color", Cream);
			group.AddChild(amount);
			_purseRow.AddChild(group);
		}

		ShowDetail();
	}

	private void LoadItems()
	{
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"{RoomName}: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["items"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			var cost = new Dictionary<string, int>();
			foreach (KeyValuePair<Variant, Variant> line in fields["cost"].AsGodotDictionary())
			{
				cost[line.Key.AsString()] = line.Value.AsInt32();
			}

			string key = fields["key"].AsString();
			Items.Add(new Item(
				key,
				fields["name"].AsString(),
				fields.TryGetValue("icon", out Variant icon) ? icon.AsString() : key,
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
		// dissolving into it. The panels carry their own dark, so this stays light.
		var wash = new ColorRect { Color = new Color(0.04f, 0.035f, 0.03f, 0.14f) };
		wash.SetAnchorsPreset(LayoutPreset.FullRect);
		wash.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(wash);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			margin.AddThemeConstantOverride($"margin_{side}", 28);
		}

		AddChild(margin);

		var page = new VBoxContainer();
		page.AddThemeConstantOverride("separation", 14);
		margin.AddChild(page);

		page.AddChild(BuildTopRow());
		page.AddChild(BuildTitle());

		var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		body.AddThemeConstantOverride("separation", 20);
		body.Alignment = BoxContainer.AlignmentMode.End;
		page.AddChild(body);

		Body = body;
		Control band = BuildBand();
		if (band != null)
		{
			page.AddChild(band);
			page.MoveChild(band, 2);
		}

		body.AddChild(BuildDetailPanel());
		BuildChoosers();
	}

	private Control BuildTopRow()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);

		_purseRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_purseRow.AddThemeConstantOverride("separation", 22);
		PanelContainer stores = Framed(_purseRow, 10);
		stores.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(stores);

		var close = new Button { Text = "✕", CustomMinimumSize = new Vector2(52, 52) };
		close.AddThemeFontSizeOverride("font_size", 22);
		close.Pressed += () => Closed?.Invoke();
		row.AddChild(close);
		return row;
	}

	private Control BuildTitle()
	{
		var titles = new VBoxContainer();
		titles.AddThemeConstantOverride("separation", 4);

		Label realm = Line("LORDS OF THE REALM", 16, Dim);
		realm.HorizontalAlignment = HorizontalAlignment.Center;
		titles.AddChild(realm);

		var name = new Label
		{
			Text = RoomName,
			HorizontalAlignment = HorizontalAlignment.Center,
			ThemeTypeVariation = "GildedTitle",
		};
		name.AddThemeFontSizeOverride("font_size", 46);
		GoldTitle.Apply(name);
		titles.AddChild(name);

		if (Tagline != null)
		{
			Label tagline = Line(Tagline, 17, Cream);
			tagline.HorizontalAlignment = HorizontalAlignment.Center;
			titles.AddChild(tagline);
		}

		_provinceLabel = Line("", 15, Dim);
		_provinceLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titles.AddChild(_provinceLabel);
		return titles;
	}

	/// <summary>How the room offers its choices. A wall of signs, a row of cards — each room says.</summary>
	protected abstract void BuildChoosers();

	/// <summary>A band under the title, where a room has something more to say about what it is
	/// working from. Nothing, for a room that does not.</summary>
	protected virtual Control BuildBand() => null;

	/// <summary>Hangs a sign over each thing the room offers, the way a smith labels his own wall.
	/// They are children of the page rather than of a container, anchored by the fractions in
	/// SignSpots, so each stays over its own corner however the window is shaped.</summary>
	protected void HangSigns()
	{
		foreach (Item item in Items)
		{
			if (!SignSpots.TryGetValue(item.Key, out Vector2 spot))
			{
				continue; // no place in the room picked for it yet
			}

			var sign = new Button { TooltipText = item.Blurb };
			Item chosen = item;
			sign.Pressed += () => Choose(chosen);
			AddChild(sign);

			sign.AnchorLeft = sign.AnchorRight = spot.X;
			sign.AnchorTop = sign.AnchorBottom = spot.Y;
			sign.OffsetLeft = -82;
			sign.OffsetRight = 82;
			sign.OffsetTop = -21;
			sign.OffsetBottom = 21;

			var plate = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			plate.AddThemeConstantOverride("separation", 8);
			plate.Alignment = BoxContainer.AlignmentMode.Center;
			sign.AddChild(plate);
			plate.SetAnchorsPreset(LayoutPreset.FullRect);

			plate.AddChild(Icon(item.Icon, 22));
			Label name = Line(item.Name, 15, Cream);
			name.VerticalAlignment = VerticalAlignment.Center;
			plate.AddChild(name);

			_signs[item.Key] = sign;
			DressSign(item.Key, lit: false);
		}
	}

	/// <summary>A sign is either hanging on the wall or lit up as the one being worked to.</summary>
	private protected void DressSign(string key, bool lit)
	{
		StyleBoxFlat style = CardStyle(lit
			? new Color(0.20f, 0.15f, 0.06f, 0.94f)
			: new Color(0.05f, 0.045f, 0.04f, 0.86f));
		style.BorderColor = lit ? new Color(1f, 0.84f, 0.45f, 1f) : new Color(0.549f, 0.447f, 0.271f, 0.85f);

		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			_signs[key].AddThemeStyleboxOverride(state, style);
		}
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

	protected void Choose(Item item)
	{
		if (_chosen != null && _signs.ContainsKey(_chosen.Key))
		{
			DressSign(_chosen.Key, lit: false);
		}

		_chosen = item;
		_count = item?.Batch ?? 0;
		if (item != null && _signs.ContainsKey(item.Key))
		{
			DressSign(item.Key, lit: true);
		}

		Chosen(item);
		ShowDetail();
	}

	/// <summary>Told when the choice changes, for a room that marks it somewhere of its own.</summary>
	protected virtual void Chosen(Item item)
	{
	}

	/// <summary>How many the order is for, and what that costs of one purse.</summary>
	protected int OrderSize => _count;

	protected int PriceOf(Item item, string purse) => item.Cost[purse] * (SizedOrder ? _count : 1);

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

		if (_chosen == null || Province == null)
		{
			return;
		}

		// Just the name up here: the picture of the thing is directly below, and twice is once too
		// many.
		_detail.AddChild(Line(_chosen.Name, 24, Cream));

		// The thing itself down the left, the reading of it down the right.
		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 14);
		_detail.AddChild(body);
		body.AddChild(Icon(_chosen.Icon, 96));

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
		terms.AddChild(Line(SizedOrder ? "Each needs" : "Cost", 14, Dim));
		foreach ((string key, int amount) in _chosen.Cost)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Icon(IconFor(key), 22));
			// Red when the province is short of what the whole order needs: the reason the button
			// below is dead.
			int owed = PriceOf(_chosen, key);
			group.AddChild(Line(amount.ToString(), 16, Held(key) >= owed ? Cream : Short));
			terms.AddChild(group);
		}

		_detail.AddChild(terms);

		if (SizedOrder)
		{
			_detail.AddChild(BuildStepper());
		}
		else
		{
			_detail.AddChild(Line(DeliveryLine(_chosen), 14, Dim));
		}

		(string making, int turnsLeft) = InHand;
		bool busy = making.Length > 0;
		var order = new Button
		{
			Text = busy ? BusyLine : OrderLine,
			CustomMinimumSize = new Vector2(0, 52),
			Disabled = busy || !CanAfford(_chosen),
		};
		order.AddThemeFontSizeOverride("font_size", 19);
		order.Pressed += PlaceOrder;
		_detail.AddChild(order);

		if (busy)
		{
			_detail.AddChild(Line(
				MakingLine(NameOf(making), turnsLeft), 14, Cream));
		}
		else if (SizedOrder)
		{
			var total = new HBoxContainer();
			total.AddThemeConstantOverride("separation", 10);
			total.AddChild(Line("Will cost", 13, Dim));
			foreach ((string key, int _) in _chosen.Cost)
			{
				int owed = PriceOf(_chosen, key);
				var group = new HBoxContainer();
				group.AddThemeConstantOverride("separation", 4);
				group.AddChild(Line(owed.ToString(), 13, Held(key) >= owed ? Cream : Short));
				group.AddChild(Icon(IconFor(key), 16));
				total.AddChild(group);
			}

			total.AddChild(Line(DeliveryLine(_chosen), 13, Dim));
			_detail.AddChild(total);
		}
	}

	/// <summary>The minus, the number and the plus: how many men the captain is asked for.</summary>
	private Control BuildStepper()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		row.Alignment = BoxContainer.AlignmentMode.Center;

		row.AddChild(Step("−", -OrderStep));

		Label count = Line(_count.ToString(), 20, Cream);
		count.HorizontalAlignment = HorizontalAlignment.Center;
		count.CustomMinimumSize = new Vector2(76, 0);
		count.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(count);

		row.AddChild(Step("+", OrderStep));
		return row;
	}

	/// <summary>One end of the stepper. It wears the square plate the rest of the game's icon buttons
	/// wear: the theme's own button is a gilded lozenge, which at this size reads as an ornament
	/// rather than as something to press.</summary>
	private Button Step(string sign, int by)
	{
		var button = new Button { Text = sign, CustomMinimumSize = new Vector2(44, 44) };
		button.AddThemeFontSizeOverride("font_size", 22);
		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			button.AddThemeStyleboxOverride(state, new StyleBoxTexture
			{
				Texture = GD.Load<Texture2D>(SquarePlatePath),
				ModulateColor = state == "normal" ? Colors.White : new Color(1.3f, 1.2f, 1.05f),
			});
		}

		button.Pressed += () => Resize(by);
		return button;
	}

	private void Resize(int by)
	{
		_count = Mathf.Max(OrderStep, _count + by);
		ShowDetail();
	}

	/// <summary>Pays for the order and puts it on the bench. The turn does the rest.</summary>
	protected void PlaceOrder()
	{
		if (_chosen == null || InHand.Making.Length > 0 || !CanAfford(_chosen))
		{
			return;
		}

		foreach ((string key, int _) in _chosen.Cost)
		{
			Pay(key, PriceOf(_chosen, key));
		}

		Begin(_chosen, SizedOrder ? _count : _chosen.Batch);
		Refresh();
	}

	protected bool CanAfford(Item item)
	{
		foreach ((string key, int _) in item.Cost)
		{
			if (Held(key) < PriceOf(item, key))
			{
				return false;
			}
		}

		return true;
	}

	private string NameOf(string key) => Items.Find(item => item.Key == key)?.Name ?? key;

	// --- small parts ---------------------------------------------------------------------------

	private protected static Control Bar(string label, int value)
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

	private protected static PanelContainer Framed(Control content, int margin)
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

	private protected static Label Line(string text, int size, Color color)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", color);
		return label;
	}

	private protected static TextureRect Icon(string name, int side) => new()
	{
		Texture = GD.Load<Texture2D>($"{IconDirectory}/{name}.png"),
		CustomMinimumSize = new Vector2(side, side),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
	};
}
