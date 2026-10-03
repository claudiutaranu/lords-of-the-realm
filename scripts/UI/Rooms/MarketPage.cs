using System.Collections.Generic;
using Godot;

/// <summary>The market: the province's own stores, bought and sold over a counter. Nothing here
/// takes a turn — gold changes hands and the stores move the moment the button is pressed, which is
/// what separates this room from the smithy and the yard next door.
///
/// The page decides nothing about money. What a store is worth, how much of it the purse stretches
/// to and whether a trade is allowed at all are the <see cref="Market"/> engine's, so a caravan or
/// an AI lord trading later trades on exactly these terms. This file only draws the answer.
///
/// The JSON beside it says what a stall looks like — its name, its picture, how far a press moves
/// the amount. What it costs is in the balance resource with the rest of the tuning.</summary>
public partial class MarketPage : RoomPage
{
	private const string DataPath = "res://data/market.json";

	/// <summary>One stall on the counter. <paramref name="Step"/> is how far a press moves the
	/// amount: grain by the cartload, iron by the bar. No price here — that is the engine's.
	///
	/// A stall with a <paramref name="Rack"/> is not traded itself. The weapons stall is one sign
	/// over one table, the way the room is filmed, but a sword and a warhorse are different goods at
	/// different prices — so the sign opens a rack and the panel trades whatever is taken off it.</summary>
	private record Good(string Key, string Name, string Icon, string Blurb, int Step,
		List<Good> Rack = null);

	private readonly List<Good> _goods = new();
	private Market _market;
	private Good _chosen;
	/// <summary>What is off the rack, for a stall that has one. Null when the stall is traded
	/// directly.</summary>
	private Good _picked;
	private int _buying;
	private int _selling;

	/// <summary>The good actually being priced and traded: what was taken off the rack, or the
	/// stall itself where there is no rack.</summary>
	private Good Trading => _picked ?? _chosen;

	protected override string RoomName => "Marketplace";

	protected override string Tagline => "Trade goods for your realm";

	// Read off the middle of the loop rather than off its first frame: the camera pushes in as it
	// plays, so the stalls drift outward, and the midpoint is where a sign sits best on average.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new()
	{
		["grain"] = new Vector2(0.135f, 0.345f),
		["cattle"] = new Vector2(0.355f, 0.345f),
		["stone"] = new Vector2(0.790f, 0.345f),
		["wood"] = new Vector2(0.735f, 0.505f),
		["iron"] = new Vector2(0.148f, 0.668f),
		["weapons"] = new Vector2(0.468f, 0.590f),
	};

	/// <summary>The realm's own market, handed over before the room opens. It is not made here any
	/// more: a market built fresh every time the stall doors open would forget every price the player
	/// moved, and a price that resets when you leave the room is a price you can launder a granary
	/// through.</summary>
	public void Brief(Market market) => _market = market;

	protected override void Load()
	{
		// Said plainly rather than left to throw on the first good it prices. Whoever opens this room
		// owes it a market before it enters the tree, and the cost of getting that wrong is a room
		// the player walks into and finds empty — which looks like broken art, not a broken handover,
		// and is the last place anybody would go looking. No falling back to a market of our own:
		// that would hide the mistake behind a counter that forgets every price he ever moved.
		if (_market == null)
		{
			GD.PushError($"{RoomName}: opened with no market. Brief() must be called before the room is added to the tree.");
			return;
		}

		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"{RoomName}: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["goods"].AsGodotArray())
		{
			Good stall = Read(entry.AsGodotDictionary());
			if (stall != null)
			{
				_goods.Add(stall);
			}
		}
	}

	/// <summary>Reads one stall, and the rack under it where it has one. A stall the engine prices
	/// at nothing is dropped with a word about it rather than hung up unsellable.</summary>
	private Good Read(Godot.Collections.Dictionary fields)
	{
		string key = fields["key"].AsString();
		List<Good> rack = null;
		if (fields.TryGetValue("rack", out Variant entries))
		{
			rack = new List<Good>();
			foreach (Variant entry in entries.AsGodotArray())
			{
				Good good = Read(entry.AsGodotDictionary());
				if (good != null)
				{
					rack.Add(good);
				}
			}

			if (rack.Count == 0)
			{
				GD.PushError($"{RoomName}: the {key} rack has nothing on it the engine will price");
				return null;
			}
		}
		else if (!_market.Trades(key))
		{
			GD.PushError($"{RoomName}: {key} is on the stall but the engine prices it at nothing");
			return null;
		}

		return new Good(
			key,
			fields["name"].AsString(),
			fields.TryGetValue("icon", out Variant icon) ? icon.AsString() : key,
			fields["blurb"].AsString(),
			fields["step"].AsInt32(),
			rack);
	}

	/// <summary>The market labels its own stalls: a sign over each one.</summary>
	protected override void BuildChoosers()
	{
		foreach (Good good in _goods)
		{
			Good chosen = good;
			HangSign(good.Key, good.Name, good.Icon, good.Blurb, () => Choose(chosen));
		}
	}

	protected override void Opened() => Choose(_goods.Count > 0 ? _goods[0] : null);

	private void Choose(Good good)
	{
		_chosen = good;
		// A rack opens on its first entry, so the panel always has something priced in it.
		_picked = good?.Rack?[0];
		_buying = _selling = Trading?.Step ?? 0;
		LightSign(good?.Key);
		ShowDetail();
	}

	private void Pick(Good good)
	{
		_picked = good;
		_buying = _selling = good.Step;
		ShowDetail();
	}

	// --- the counter ---------------------------------------------------------------------------

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
		heading.AddChild(Icon(Trading.Icon, 32));
		Label title = Line(Trading.Name, 27, Bright);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		Detail.AddChild(heading);

		if (_chosen.Rack != null)
		{
			Detail.AddChild(BuildRack());
		}

		// The good itself down the left, what it is worth down the right.
		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 14);
		Detail.AddChild(body);
		body.AddChild(Icon(Trading.Icon, 96));

		var reading = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		reading.AddThemeConstantOverride("separation", 10);
		body.AddChild(reading);

		Label blurb = Line(Trading.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		reading.AddChild(blurb);

		var terms = new HBoxContainer();
		terms.AddThemeConstantOverride("separation", 22);
		// Both sides of the counter, because they are no longer the same number: the merchant's cut
		// is what a round trip costs, and a player who cannot see it will keep trying to make money
		// out of one.
		terms.AddChild(Reading("Buy at", "gold", _market.Asking(Trading.Key), Bright));
		terms.AddChild(Reading("Sell at", "gold", _market.Offered(Trading.Key), Bright));
		terms.AddChild(Reading("In stock", Trading.Icon, Province.Stored(Trading.Key), Bright));
		// Counted in sacks, not in coins, so it wears the good's own icon beside the holding it is
		// meant to be read against.
		terms.AddChild(Reading("You can buy", Trading.Icon, Affordable, Affordable > 0 ? Bright : Short));
		reading.AddChild(terms);
		reading.AddChild(Weather(Trading.Key));

		// A slider for each side of the counter, each running as far as that side can go: buying to
		// what the purse will pay for, selling to what is in the store.
		Detail.AddChild(BuildCounter(buying: true));
		Detail.AddChild(BuildCounter(buying: false));
	}

	/// <summary>The rack under a stall's name: one plate per thing on it, the taken one lit. The
	/// number under each is what the province already holds of it, so the rack doubles as the
	/// armoury read at a glance.</summary>
	private Control BuildRack()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		foreach (Good good in _chosen.Rack)
		{
			bool taken = good.Key == Trading.Key;
			var plate = new Button
			{
				TooltipText = $"{good.Name} — buy at {_market.Asking(good.Key):N0}, sell at {_market.Offered(good.Key):N0} gold",
				CustomMinimumSize = new Vector2(0, 58),
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			Good picked = good;
			plate.Pressed += () => Pick(picked);

			StyleBoxFlat style = CardStyle(taken
				? new Color(0.20f, 0.15f, 0.06f, 0.96f)
				: new Color(0.085f, 0.065f, 0.048f, 0.92f));
			style.BorderColor = taken ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.51f, 0.41f, 0.25f, 0.9f);
			style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom =
				taken ? 3 : 2;
			foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
			{
				plate.AddThemeStyleboxOverride(state, style);
			}

			var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			stack.AddThemeConstantOverride("separation", 0);
			stack.Alignment = BoxContainer.AlignmentMode.Center;
			plate.AddChild(stack);
			stack.SetAnchorsPreset(LayoutPreset.FullRect);

			var art = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
			art.AddChild(Icon(good.Icon, 26));
			stack.AddChild(art);

			int held = Province.Stored(good.Key);
			Label count = Line(held.ToString("N0"), 13, held > 0 ? Bright : Soft);
			count.HorizontalAlignment = HorizontalAlignment.Center;
			count.MouseFilter = MouseFilterEnum.Ignore;
			stack.AddChild(count);

			row.AddChild(plate);
		}

		return row;
	}

	/// <summary>A price or a holding: what it is called, and the number under it beside its icon.</summary>
	private static Control Reading(string label, string icon, int value, Color colour)
	{
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 2);
		column.AddChild(Line(label, 14, Soft));

		var line = new HBoxContainer();
		line.AddThemeConstantOverride("separation", 7);
		line.AddChild(Icon(icon, 26));
		Label amount = Line(value.ToString("N0"), 20, colour);
		amount.VerticalAlignment = VerticalAlignment.Center;
		line.AddChild(amount);
		column.AddChild(line);
		return column;
	}

	/// <summary>Buy and sell, side by side, each dead when the till or the store cannot answer for
	/// it. The line under them is what the trade comes to either way.</summary>
	/// <summary>One side of the counter: how many, and the button that trades them at the price
	/// written beside it — red on the buying side when the purse cannot cover it.</summary>
	private Control BuildCounter(bool buying)
	{
		int amount = buying ? _buying : _selling;
		int most = buying ? Affordable : Province.Stored(Trading.Key);
		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 4);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		column.AddChild(row);

		Control stepper = Stepper(buying ? "Buy" : "Sell", amount, Trading.Step, most, settled =>
		{
			if (buying)
			{
				_buying = settled;
			}
			else
			{
				_selling = settled;
			}

			ShowDetail();
		});
		stepper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(stepper);

		int worth = _market.Worth(Trading.Key, amount, buying);
		bool can = amount > 0 && (buying ? Province.Gold >= worth : Province.Stored(Trading.Key) >= amount);
		var trade = new Button
		{
			Text = buying ? $"Buy for {worth:N0}" : $"Sell for {worth:N0}",
			CustomMinimumSize = new Vector2(190, 48),
			Disabled = !can,
		};
		trade.AddThemeFontSizeOverride("font_size", 17);
		trade.Pressed += buying ? Buy : Sell;
		row.AddChild(trade);
		return column;
	}

	/// <summary>Why the price is what it is today. A moving price the player cannot read the reason
	/// for is indistinguishable from a random one, and he will stop planning around it — which is
	/// the only thing it exists to make him do.</summary>
	private Control Weather(string store)
	{
		int ordinary = _market.Base(store);
		int now = _market.Price(store);
		if (ordinary <= 0)
		{
			return new Control();
		}

		int off = Mathf.RoundToInt((now - ordinary) * 100f / ordinary);
		string note = off switch
		{
			> 25 => "Dear — the country is short of it.",
			> 8 => "A little above its worth.",
			< -25 => "Cheap — the market is glutted with it.",
			< -8 => "A little under its worth.",
			_ => "Trading at about what it is worth.",
		};

		return Line(off == 0 ? note : $"{note}  ({(off > 0 ? "+" : "")}{off}%)", 15,
			off > 8 ? Short : off < -8 ? Gain() : Dim);
	}

	private static Color Gain() => Chrome.Gain;

	/// <summary>How many of the chosen good the province's gold will stretch to.</summary>
	private int Affordable => _market.Affordable(Province, Trading.Key);

	private void Buy()
	{
		if (_market.Buy(Province, Trading.Key, _buying))
		{
			Refresh();
		}
	}

	private void Sell()
	{
		if (_market.Sell(Province, Trading.Key, _selling))
		{
			Refresh();
		}
	}
}
