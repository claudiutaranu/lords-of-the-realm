using System.Collections.Generic;
using Godot;

/// <summary>Lords of the Realm's "Send supplies" scroll, grown to carry anything the county keeps: its
/// five stores and whatever is racked in its armoury, loaded out of one of the lord's counties onto a
/// cart for another, the arrows moving each between the store and the load.
///
/// The seasons on the road are on the page before anything is sent. A cart is slow and out in the
/// open, and a lord who sends bread to a county that starves before it arrives has to have been
/// told it would, or he will blame the cart.</summary>
public partial class SupplyPanel : CountyPanel
{
	/// <summary>What a press of an arrow moves: ten of anything counted by the sack or the load, one
	/// of anything counted by the head or the piece. The plates run while held, so ten is fine
	/// enough to aim with and quick enough to empty a barn.</summary>
	private const int BulkStep = 10;

	private static readonly (string Store, string Name, string Icon)[] Stores =
	{
		("grain", "Grain", "food"),
		("cattle", "Cattle", "livestock"),
		("wood", "Wood", "wood"),
		("stone", "Stone", "stone"),
		("iron", "Iron", "iron"),
	};

	/// <summary>Raised when a cart has set out, so the map can put it on the road and the stores
	/// on the page can come down.</summary>
	public event System.Action<Shipment> Dispatched;

	private TurnManager _turns;
	private ProvinceEconomy _source;
	private readonly List<string> _destinations = new();
	private int _to;
	private readonly Dictionary<string, int> _load = new();
	private readonly Dictionary<string, (Label Stored, Label Sent)> _rows = new();

	private Label _destination;
	private Label _journey;
	private VBoxContainer _goods;
	private HBoxContainer _manifest;
	private Button _dispatch;

	protected override int Width => 580;

	/// <summary>The icon a store is drawn with, here and on the cart on the map.</summary>
	public static string IconOf(string store)
	{
		foreach ((string each, string _, string icon) in Stores)
		{
			if (each == store)
			{
				return icon;
			}
		}

		foreach ((string weapon, string icon) in ProvinceSidebar.Arms)
		{
			if (weapon == store)
			{
				return icon;
			}
		}

		return "scroll";
	}

	protected override void Furnish()
	{
		Column.AddChild(Route());
		_goods = new VBoxContainer();
		_goods.AddThemeConstantOverride("separation", 6);
		Column.AddChild(_goods);

		// What is on the cart, drawn as the pile it is — the thing the lord is about to send away.
		_manifest = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_manifest.AddThemeConstantOverride("separation", 16);
		_manifest.CustomMinimumSize = new Vector2(0, 40);
		Column.AddChild(_manifest);

		_dispatch = Chrome.Order("Dispatch shipment", "footsteps", Dispatch, out _);
		Column.AddChild(_dispatch);
	}

	/// <summary>Where the cart is going and how long it takes, in one line: the lord picks the county
	/// and reads the road off the same glance.</summary>
	private Control Route()
	{
		var route = new VBoxContainer();
		route.AddThemeConstantOverride("separation", 4);

		var to = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		to.AddThemeConstantOverride("separation", 12);
		to.AddChild(Chrome.Line("To", 22, Chrome.Soft));
		to.AddChild(Chrome.Plate("◀", 34, () => Aim(-1)));
		_destination = Chrome.Line("", 26, Chrome.Bright);
		GoldTitle.Apply(_destination);
		_destination.CustomMinimumSize = new Vector2(240, 0);
		_destination.HorizontalAlignment = HorizontalAlignment.Center;
		to.AddChild(_destination);
		to.AddChild(Chrome.Plate("▶", 34, () => Aim(1)));
		route.AddChild(to);

		var road = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		road.AddThemeConstantOverride("separation", 8);
		road.AddChild(Chrome.Icon("footsteps", 22));
		_journey = Chrome.Line("", 18, Chrome.Soft);
		road.AddChild(_journey);
		route.AddChild(road);
		return route;
	}

	/// <summary>A heading over a block of rows, ruled off like the steward's ledger.</summary>
	private void Section(string title)
	{
		var heading = new HBoxContainer();
		heading.AddThemeConstantOverride("separation", 10);
		heading.AddChild(Chrome.Line(title, 16, Chrome.Dim));
		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		rule.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		heading.AddChild(rule);
		_goods.AddChild(heading);
	}

	/// <summary>One store on its own card: the thing, what stays behind, and the load between two
	/// arrows. Left takes off the cart and back into the store, right the other way, as the
	/// original's arrows ran.</summary>
	private void Row(string store, string name, string icon, int step)
	{
		var card = new PanelContainer();
		card.AddThemeStyleboxOverride("panel", Card());
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 12);
		card.AddChild(row);

		row.AddChild(Chrome.Icon(icon, 40));

		var named = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		named.AddThemeConstantOverride("separation", 0);
		named.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		named.AddChild(Chrome.Line(name, 21, Chrome.Bright));
		Label stored = Chrome.Line("", 15, Chrome.Dim);
		named.AddChild(stored);
		row.AddChild(named);

		row.AddChild(Chrome.Repeating("◀", 34, () => Load(store, -step)));
		Label sent = Chrome.Line("", 26, Chrome.Bright);
		sent.CustomMinimumSize = new Vector2(84, 0);
		sent.HorizontalAlignment = HorizontalAlignment.Center;
		sent.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(sent);
		row.AddChild(Chrome.Repeating("▶", 34, () => Load(store, step)));

		_goods.AddChild(card);
		_rows[store] = (stored, sent);
	}

	private static StyleBoxFlat Card()
	{
		StyleBoxFlat card = Chrome.CardStyle(new Color(0f, 0f, 0f, 0.28f));
		card.BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.35f);
		card.SetBorderWidthAll(1);
		card.SetContentMarginAll(8);
		card.ContentMarginLeft = 12;
		return card;
	}

	/// <summary>Opens on the county the goods come out of. Every other county the lord holds is a
	/// place they can go; with none, there is nowhere to send anything and the panel says so rather
	/// than opening on an empty choice.</summary>
	public bool Open(TurnManager turns, ProvinceEconomy source)
	{
		_destinations.Clear();
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			if (county.Realm == turns.PlayerRealm && county != source)
			{
				_destinations.Add(county.ProvinceName);
			}
		}

		if (_destinations.Count == 0)
		{
			return false;
		}

		_turns = turns;
		_source = source;
		_to = 0;
		_load.Clear();
		Furnish(source);
		Show();
		Reveal(source.ProvinceName);
		return true;
	}

	/// <summary>The rows for this county: its five stores always, and a rack of its armoury only
	/// where there is something on it.</summary>
	private void Furnish(ProvinceEconomy source)
	{
		foreach (Node old in _goods.GetChildren())
		{
			old.QueueFree();
		}

		_rows.Clear();
		Section("Stores");
		foreach ((string store, string name, string icon) in Stores)
		{
			Row(store, name, icon, store == "cattle" ? 1 : BulkStep);
		}

		bool isArmed = false;
		foreach ((string weapon, string icon) in ProvinceSidebar.Arms)
		{
			if (source.Stored(weapon) <= 0)
			{
				continue;
			}

			if (!isArmed)
			{
				Section("Armoury");
				isArmed = true;
			}

			Row(weapon, $"{char.ToUpperInvariant(weapon[0])}{weapon[1..]}s", icon, 1);
		}
	}

	private void Aim(int by)
	{
		_to = (_to + by + _destinations.Count) % _destinations.Count;
		Show();
	}

	private void Load(string store, int by)
	{
		_load[store] = Mathf.Clamp(_load.GetValueOrDefault(store) + by, 0, _source.Stored(store));
		Show();
	}

	private void Dispatch()
	{
		Shipment cart = _turns.Dispatch(_source.ProvinceName, _destinations[_to], new Dictionary<string, int>(_load));
		if (cart != null)
		{
			Dispatched?.Invoke(cart);
			Close();
		}
	}

	private void Show()
	{
		string to = _destinations[_to];
		_destination.Text = to;

		int loaded = 0;
		foreach (Node old in _manifest.GetChildren())
		{
			old.QueueFree();
		}

		foreach ((string store, (Label stored, Label sent)) in _rows)
		{
			int load = _load.GetValueOrDefault(store);
			stored.Text = $"{_source.Stored(store) - load:N0} stay behind";
			sent.Text = $"{load:N0}";
			sent.AddThemeColorOverride("font_color", load > 0 ? Chrome.Bright : Chrome.Dim);
			if (load > 0)
			{
				loaded++;
				_manifest.AddChild(Carried(store, load));
			}
		}

		if (loaded == 0)
		{
			_manifest.AddChild(Chrome.Line("The cart stands empty.", 17, Chrome.Dim));
		}

		int seasons = _turns.SupplySeasons(_source.ProvinceName, to);
		_journey.Text = seasons < 0 ? "No road runs there."
			: seasons == 1 ? "One season on the road." : $"{seasons} seasons on the road.";
		_journey.AddThemeColorOverride("font_color", seasons < 0 ? Chrome.Short : Chrome.Soft);
		_dispatch.Disabled = seasons < 0 || loaded == 0;
	}

	private static Control Carried(string store, int amount)
	{
		var pile = new HBoxContainer();
		pile.AddThemeConstantOverride("separation", 4);
		pile.AddChild(Chrome.Icon(IconOf(store), 28));
		Label count = Chrome.Line($"{amount:N0}", 18, Chrome.Gain);
		count.VerticalAlignment = VerticalAlignment.Center;
		pile.AddChild(count);
		return pile;
	}
}
