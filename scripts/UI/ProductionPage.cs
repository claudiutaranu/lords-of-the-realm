using System.Collections.Generic;
using Godot;

/// <summary>A room where something is ordered and then waited for: the smithy forging weapons, the
/// yard training men. Both work the same way — signs over what can be ordered, a panel that reads
/// the chosen one and takes the order, and a count of turns on whatever is already on the bench.
///
/// What can be ordered is engine data, not a campaign's: every realm's smithy and every realm's
/// yard work alike. The room itself — the film, the stores, the title, the way out — is RoomPage's.
///
/// A page supplies: what it is called, where its data is, where its signs hang, what an order is
/// priced in, and what placing one does to the province.</summary>
public abstract partial class ProductionPage : RoomPage
{
	/// <summary>One thing that can be ordered here. <paramref name="Icon"/> is usually the key
	/// itself, and is named separately where the thing is better known by something other than
	/// itself — cavalry by a helm rather than by a horse's head.</summary>
	protected record Item(string Key, string Name, string Icon, string Blurb, int Attack, int Range,
		int Defence, int Speed, int Turns, int Batch, Dictionary<string, int> Cost);

	protected readonly List<Item> Items = new();
	private Item _chosen;
	private int _count;

	// --- what each room says for itself --------------------------------------------------------

	protected abstract string DataPath { get; }

	protected abstract int Held(string purse);

	protected abstract void Pay(string purse, int amount);

	/// <summary>What is on the bench now, and how many turns are left on it. An empty key means the
	/// room is idle and will take an order.</summary>
	protected abstract (string Making, int TurnsLeft) InHand { get; }

	/// <summary>Writes the order onto the province: what, and how many of it.</summary>
	protected abstract void Begin(Item item, int count);

	/// <summary>Whether an order here is sized by the player. A smith takes a commission as it
	/// comes; a captain is asked how many men.</summary>
	protected virtual bool SizedOrder => false;

	protected virtual int OrderStep => 5;

	/// <summary>The largest order the room will take, for a room that sizes its orders — what the
	/// slider runs up to. Zero where nothing caps it.</summary>
	protected virtual int OrderCeiling(Item item) => 0;

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

	protected override void Opened() => Choose(Items.Count > 0 ? Items[0] : null);

	protected override void Load()
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

	/// <summary>The smith labels his own wall: a sign over every rack.</summary>
	protected void HangSigns()
	{
		foreach (Item item in Items)
		{
			Item chosen = item;
			HangSign(item.Key, item.Name, item.Icon, item.Blurb, () => Choose(chosen));
		}
	}

	protected void Choose(Item item)
	{
		_chosen = item;
		_count = item?.Batch ?? 0;
		if (SizedOrder && item != null)
		{
			_count = Mathf.Clamp(_count, OrderStep, Ceiling(item));
		}

		LightSign(item?.Key);
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

	protected override void ShowDetail()
	{
		if (Detail == null)
		{
			return;
		}

		ClearDetail();
		if (_chosen == null || Province == null)
		{
			return;
		}

		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 10);
		heading.AddChild(Icon(_chosen.Icon, 32));
		Label title = Line(_chosen.Name, 27, Bright);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		Detail.AddChild(heading);

		// The thing itself down the left, the reading of it down the right.
		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 14);
		Detail.AddChild(body);
		body.AddChild(Icon(_chosen.Icon, 96));

		var reading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		reading.AddThemeConstantOverride("separation", 8);
		body.AddChild(reading);

		Label blurb = Line(_chosen.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		reading.AddChild(blurb);

		reading.AddChild(Bar("Attack", _chosen.Attack));
		reading.AddChild(Bar("Range", _chosen.Range));
		reading.AddChild(Bar("Defence", _chosen.Defence));
		reading.AddChild(Bar("Speed", _chosen.Speed));

		var terms = new HBoxContainer();
		terms.AddThemeConstantOverride("separation", 14);
		terms.AddChild(Line(SizedOrder ? "Each needs" : "Production cost", 16, Soft));
		foreach ((string key, int amount) in _chosen.Cost)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 6);
			group.AddChild(Icon(IconFor(key), 26));
			// Red when the province is short of what the whole order needs: the reason the button
			// below is dead.
			int owed = PriceOf(_chosen, key);
			group.AddChild(Line(amount.ToString(), 19, Held(key) >= owed ? Bright : Short));
			terms.AddChild(group);
		}

		Detail.AddChild(terms);

		if (SizedOrder)
		{
			Detail.AddChild(Stepper(null, _count, OrderStep, Ceiling(_chosen), settled =>
			{
				_count = settled;
				ShowDetail();
			}));
		}
		else
		{
			Detail.AddChild(Line(DeliveryLine(_chosen), 16, Soft));
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
		Detail.AddChild(order);

		if (busy)
		{
			Detail.AddChild(Line(
				MakingLine(NameOf(making), turnsLeft), 16, Bright));
		}
		else if (SizedOrder)
		{
			var total = new HBoxContainer();
			total.AddThemeConstantOverride("separation", 10);
			total.AddChild(Line("Will cost", 15, Soft));
			foreach ((string key, int _) in _chosen.Cost)
			{
				int owed = PriceOf(_chosen, key);
				var group = new HBoxContainer();
				group.AddThemeConstantOverride("separation", 5);
				group.AddChild(Line(owed.ToString(), 15, Held(key) >= owed ? Bright : Short));
				group.AddChild(Icon(IconFor(key), 19));
				total.AddChild(group);
			}

			total.AddChild(Line(DeliveryLine(_chosen), 15, Soft));
			Detail.AddChild(total);
		}
	}

	/// <summary>The room's ceiling on one order, never below one step: a slider whose end is under
	/// its start has no handle to drag.</summary>
	private int Ceiling(Item item) => Mathf.Max(OrderStep, OrderCeiling(item));

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

	private static Control Bar(string label, int value)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);

		Label name = Line(label, 16, Soft);
		name.CustomMinimumSize = new Vector2(92, 0);
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

}
