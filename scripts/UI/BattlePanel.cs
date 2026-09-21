using System.Collections.Generic;
using Godot;

/// <summary>The moment before, and the moment after. Two armies laid out facing each other, every
/// reason the day will go the way it goes written between them, and one button.
///
/// It is the whole of what an auto-calculated battle can be. A lord who fights his own battles sees
/// why he lost; a lord whose captain fights them has to be TOLD why, before he commits and not
/// after, or a campaign turns into a slot machine he feeds men into. So every line under the bar is
/// one of the numbers <see cref="Battle"/> actually multiplies by — the walls, the frontage, the
/// town, the county's goodwill, and what the road did to his legs. Nothing is on this panel that
/// does not decide the fight, and nothing decides the fight that is not on this panel.
///
/// TWO FIGHTS, and the panel is the thing that knows which one is on the table. Whatever stands in
/// the open is fought first; if they fall back behind their walls, the same panel comes back asking
/// a second and much worse question. A lord may stop between the two — that is what a beaten field
/// army and an unbroken castle is FOR, and walking away from it is a real answer.</summary>
public partial class BattlePanel : CountyPanel
{
	/// <summary>Raised whenever the ledger has moved — men dead, a county changed hands — so the map
	/// above can redraw its banners and its borders. Not raised for a lord who looked and left.</summary>
	public event System.Action Settled;

	private const int CardSide = 88;
	private const int CardTall = 152;

	private static readonly Color Ours = new("d8b26b");
	private static readonly Color Theirs = new("9c5148");
	private static readonly Color Bad = new("c9604e");

	private TurnManager _turns;
	private GameBalance _balance;
	private string _from = "";
	private string _county = "";
	private Vector2 _at;

	/// <summary>Whether the fight on the table is the one at the walls. Read off the county rather
	/// than chosen: there is nothing to storm while anybody is still standing in the open.</summary>
	private bool _walls;

	private HBoxContainer _hosts;
	private VBoxContainer _ourHost;
	private VBoxContainer _theirHost;
	private Label _ourCount;
	private Label _theirCount;
	private ColorRect _ourWeight;
	private ColorRect _theirWeight;
	private GridContainer _terms;
	private Label _verdict;
	private HBoxContainer _orders;
	private Button _attack;
	private Button _siege;

	protected override int Width => 780;

	/// <summary>Puts two armies in front of the lord. <paramref name="at"/> is where his men are
	/// standing, which is where they will be standing if the county falls.</summary>
	public void Open(TurnManager turns, GameBalance balance, string from, string county, Vector2 at)
	{
		_turns = turns;
		_balance = balance;
		_from = from;
		_county = county;
		_at = at;
		Lay();
		Reveal(county);
	}

	protected override void Furnish()
	{
		_hosts = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_hosts.AddThemeConstantOverride("separation", 22);
		Column.AddChild(_hosts);

		(_ourHost, _ourCount) = Host();
		(_theirHost, _theirCount) = Host();
		_hosts.AddChild(_ourHost);
		_hosts.AddChild(_theirHost);

		var weights = new HBoxContainer { CustomMinimumSize = new Vector2(0, 14) };
		weights.AddThemeConstantOverride("separation", 3);
		Column.AddChild(weights);
		_ourWeight = new ColorRect { Color = Ours, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_theirWeight = new ColorRect { Color = Theirs, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		weights.AddChild(_ourWeight);
		weights.AddChild(_theirWeight);

		_terms = new GridContainer { Columns = 2 };
		_terms.AddThemeConstantOverride("h_separation", 18);
		_terms.AddThemeConstantOverride("v_separation", 6);
		Column.AddChild(_terms);

		_verdict = Chrome.Line("", 20, Chrome.Cream);
		_verdict.HorizontalAlignment = HorizontalAlignment.Center;
		_verdict.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		Column.AddChild(_verdict);

		_orders = new HBoxContainer();
		_orders.AddThemeConstantOverride("separation", 12);
		Column.AddChild(_orders);

		_attack = Order("Attack", Strike);
		_siege = Order("Sit down before the gate", Sit);
	}

	private Button Order(string what, System.Action pressed)
	{
		var order = new Button
		{
			Text = what,
			CustomMinimumSize = new Vector2(0, 46),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		order.AddThemeFontSizeOverride("font_size", 19);
		order.Pressed += pressed;
		_orders.AddChild(order);
		return order;
	}

	/// <summary>One army's side of the table: a heading, a row of companies, and a count.</summary>
	private static (VBoxContainer Host, Label Count) Host()
	{
		var host = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		host.AddThemeConstantOverride("separation", 8);

		Label title = Chrome.Line("", 18, Chrome.Cream);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.Name = "Title";
		host.AddChild(title);

		var companies = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		companies.AddThemeConstantOverride("separation", 6);
		companies.Name = "Companies";
		host.AddChild(companies);

		Label count = Chrome.Line("", 17, Chrome.Soft);
		count.HorizontalAlignment = HorizontalAlignment.Center;
		host.AddChild(count);
		return (host, count);
	}

	/// <summary>Draws the table as the county stands right now. Called when the panel opens and
	/// again between the two fights, because the second one is asked of a different army.</summary>
	private void Lay()
	{
		ProvinceEconomy ours = _turns.GetProvince(_from);
		Defenders against = _turns.DefendersOf(_county);
		if (ours == null)
		{
			return;
		}

		// Nothing to storm while anybody is still standing in front of the walls.
		_walls = ProvinceEconomy.Men(against.Field) == 0;
		Dictionary<string, int> holding = _walls ? against.Castle : against.Field;
		bool spent = ours.MarchLeft <= 0f;

		Fill(_ourHost, _ourCount, _from, ours.Garrison);
		Fill(_theirHost, _theirCount, _county, holding);

		(float mine, float theirs) = Battle.Weighed(ours.Garrison, against, _walls, spent, _balance);
		_ourWeight.SizeFlagsStretchRatio = Mathf.Max(0.02f, mine);
		_theirWeight.SizeFlagsStretchRatio = Mathf.Max(0.02f, theirs);

		ShowTerms(against, ours.Garrison, spent);
		_verdict.Text = Reckoned(mine, theirs);
		_verdict.AddThemeColorOverride("font_color", Chrome.Cream);
		_attack.Visible = true;
		_attack.Text = _walls ? "Storm the walls" : "Attack";

		// Sitting down in front of it is the other way, and against the stone rungs it is the only
		// way. It costs the army every season it lasts — they are standing here and nowhere else —
		// so it is offered as an order of its own and not as a thing that happens by waiting.
		_siege.Visible = _walls && ProvinceEconomy.Men(against.Castle) > 0;
	}

	private static void Fill(VBoxContainer host, Label count, string name, Dictionary<string, int> roster)
	{
		host.GetNode<Label>("Title").Text = name.ToUpperInvariant();

		var companies = host.GetNode<HBoxContainer>("Companies");
		foreach (Node old in companies.GetChildren())
		{
			old.QueueFree();
		}

		foreach ((string unit, int men) in roster)
		{
			Units.Unit kind = Units.Of(unit);
			(Control tile, VBoxContainer stack) = UnitCard.Build(unit, kind.Name, CardTall, titled: false);
			tile.CustomMinimumSize = new Vector2(CardSide, CardTall);
			tile.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
			Label tally = Chrome.Line(men.ToString("N0"), 18, Chrome.Bright);
			tally.HorizontalAlignment = HorizontalAlignment.Center;
			stack.AddChild(tally);
			companies.AddChild(tile);
		}

		int all = ProvinceEconomy.Men(roster);
		count.Text = all == 1 ? "1 man" : $"{all:N0} men";
	}

	/// <summary>Every reason the day will go the way it goes, in the order a captain would say them.
	/// Each line is a number <see cref="Battle"/> genuinely multiplies by — there is nothing on this
	/// list for flavour, because a lord who learns that one of these lines is decoration stops
	/// believing the rest of them.</summary>
	private void ShowTerms(Defenders against, Dictionary<string, int> ours, bool spent)
	{
		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		if (_walls)
		{
			Fortifications.Wall wall = Fortifications.Of(against.Fortification);
			Term($"Behind the {wall.Name}", wall.Defence, good: false);
			Term("A man on a ladder cannot defend himself", 1f / _balance.AssaultExposure, good: false);

			int men = ProvinceEconomy.Men(ours);
			if (men > wall.Frontage)
			{
				Term($"Only {wall.Frontage:N0} of our {men:N0} can reach the wall",
					wall.Frontage / (float)men, good: false);
			}

			foreach ((string unit, int riders) in ours)
			{
				if (Units.Of(unit).Mounted)
				{
					Note($"Our {riders:N0} {Units.Of(unit).Name.ToLowerInvariant()} are no use on a ladder");
				}
			}

			// What a besieging captain would put at: the size of the place and the number of mouths
			// in it. It is the whole basis of choosing to sit down rather than climb, so a lord who
			// cannot see it is choosing blind.
			int mouths = Mathf.CeilToInt(ProvinceEconomy.Men(against.Castle)
				/ _balance.PeoplePerGrain * _balance.SoldierAppetite);
			int seasons = mouths <= 0 ? 0 : Mathf.CeilToInt(wall.Stores / (float)mouths);
			Note($"A place that size holds perhaps {seasons:N0} seasons of bread");
		}

		Term("Holding their own town", _balance.TownDefence, good: false);

		float heart = 1f + ((Mathf.Clamp(against.Loyalty, 0f, 100f) - 50f) / 50f * _balance.LoyaltyDefence);
		if (Mathf.Abs(heart - 1f) > 0.01f)
		{
			Term(heart > 1f ? "The people are with them" : "The people have had enough of them",
				heart, good: heart < 1f);
		}

		if (spent)
		{
			Term("Our men have walked all season", _balance.MarchedOutOffence, good: false);
		}
	}

	private void Term(string what, float weight, bool good)
	{
		_terms.AddChild(Chrome.Line(what, 16, Chrome.Soft));
		Label figure = Chrome.Line($"×{weight:0.00}", 16, good ? Chrome.Gain : Bad);
		figure.HorizontalAlignment = HorizontalAlignment.Right;
		figure.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_terms.AddChild(figure);
	}

	private void Note(string what)
	{
		_terms.AddChild(Chrome.Line(what, 16, Chrome.Dim));
		_terms.AddChild(new Control());
	}

	/// <summary>What the captain makes of it, in words rather than in a percentage. He is looking at
	/// the same two figures the bar is drawn from, so he cannot say one thing while it says
	/// another.</summary>
	private static string Reckoned(float ours, float theirs)
	{
		float share = ours / Mathf.Max(0.01f, ours + theirs);
		return share switch
		{
			>= 0.72f => "The day should be ours, my lord.",
			>= 0.58f => "We have the better of them.",
			>= 0.45f => "It will be a close thing.",
			>= 0.30f => "They have the better of us, my lord.",
			_ => "This would be a slaughter. Ours.",
		};
	}

	/// <summary>Sits the army down in front of the gate instead of climbing it. Nothing happens
	/// today — that is the point of it — so the panel says so and closes.</summary>
	private void Sit()
	{
		if (!_turns.Besiege(_from, _county))
		{
			return;
		}

		Settled?.Invoke();
		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		Note("Our men hold the ground and nothing else, for as long as it takes");
		Note("The county pays its lord nothing while we sit here");
		_verdict.Text = $"We sit down before {_county}.";
		_verdict.AddThemeColorOverride("font_color", Chrome.Cream);
		_attack.Visible = false;
		_siege.Visible = false;
	}

	/// <summary>Fights the one on the table, and says what it cost. Whether the county has changed
	/// hands is read back off the ledger rather than worked out here — the ledger is what decides
	/// it, and a panel with a second opinion about who owns a county is a panel that will one day
	/// be wrong.</summary>
	private void Strike()
	{
		Battle.Result day = _turns.Attack(_from, _county, _at, _walls);
		Settled?.Invoke();

		bool taken = _turns.GetProvince(_county) != null;
		bool fellBack = day.AttackerWon && !taken;

		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		ProvinceEconomy ours = _turns.GetProvince(taken ? _county : _from);
		Fill(_ourHost, _ourCount, taken ? _county : _from,
			ours?.Garrison ?? new Dictionary<string, int>());
		Defenders left = _turns.DefendersOf(_county);
		Fill(_theirHost, _theirCount, _county,
			ProvinceEconomy.Men(left.Field) > 0 ? left.Field : left.Castle);

		Note($"We lost {day.AttackerFell:N0}");
		Note($"They lost {day.DefenderFell:N0}");

		_verdict.Text = taken
			? $"{_county} is yours."
			: fellBack
				? "The field is ours. They have fallen back behind their walls."
				: "We are thrown back, my lord.";
		_verdict.AddThemeColorOverride("font_color", taken || fellBack ? Chrome.Cream : Bad);

		// A beaten field army with a castle still standing is the second question, and it is a real
		// one: the lord may storm it now, on what the first fight left him, or leave it and come
		// back with more men next season.
		_attack.Visible = fellBack && ProvinceEconomy.Men(left.Castle) > 0;
		_siege.Visible = _attack.Visible;
		if (_attack.Visible)
		{
			_walls = true;
			_attack.Text = "Storm the walls";
		}
	}
}
