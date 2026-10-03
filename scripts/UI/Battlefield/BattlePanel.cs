using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The moment before, and the moment after. Two armies laid out facing each other, every
/// reason the day will go the way it goes written under them, and the orders.
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
public partial class BattlePanel : PaintedPanel
{
	/// <summary>Raised whenever the ledger has moved — men dead, a county changed hands — so the map
	/// above can redraw its banners and its borders. Not raised for a lord who looked and left.</summary>
	public event System.Action Settled;

	/// <summary>Raised with true when the lord goes down onto the field to fight it himself, and with
	/// false when he comes back: the map underneath has nothing to draw and nothing to listen to
	/// while he is down there.</summary>
	public event System.Action<bool> Fielded;

	/// <summary>Whose colours one side of the table is flown under, as the map names them.</summary>
	public readonly record struct Colours(string Name, string Key, Color Accent);

	private const string CrestPath = "res://assets/ui/battle-crest.png";
	private static readonly Vector2 Drawn = new(1240, 1050);

	/// <summary>The drop from the painted ribbon to the county's name under it.</summary>
	private const int RibbonDrop = 16;
	private const int MiddleWide = 320;

	/// <summary>How many roster lines the question, the crossed swords and the captain's verdict need
	/// between them before the frame may be cut any shorter.</summary>
	private const int MiddleRows = 4;

	private static readonly Color Ours = new("d8b26b");
	private static readonly Color Theirs = new("9c5148");
	private static readonly Color Bad = new("c9604e");

	private TurnManager _turns;
	private GameBalance _balance;
	private FieldArmy _attacker;
	private string _county = "";
	private Vector2 _at;
	private Colours _us;
	private Colours _them;

	/// <summary>Whether the fight on the table is the one at the walls. Read off the county rather
	/// than chosen: there is nothing to storm while anybody is still standing in the open.</summary>
	private bool _walls;

	private Label _where;
	private BattleSide _ourSide;
	private BattleSide _theirSide;
	private Label _question;
	private Label _verdict;
	private ColorRect _ourWeight;
	private ColorRect _theirWeight;
	private GridContainer _terms;
	private Button _attack;
	private Button _lead;
	private Label _attackWord;
	private Button _siege;
	private Label _leaveWord;

	protected override Vector2 PanelSize => Drawn;

	/// <summary>Puts two armies in front of the lord. <paramref name="at"/> is where his men are
	/// standing, which is where they will be standing if the county falls.</summary>
	public void Open(TurnManager turns, GameBalance balance, FieldArmy from, string county, Vector2 at,
		Colours us, Colours them)
	{
		_turns = turns;
		_balance = balance;
		_attacker = from;
		_enemy = null;
		_county = county;
		_at = at;
		_us = us;
		_them = them;
		Lay();
		Reveal();
	}

	/// <summary>Puts the lord's company in front of another lord's in open country.</summary>
	public void OpenAgainst(TurnManager turns, GameBalance balance, FieldArmy from, FieldArmy enemy,
		Colours us, Colours them)
	{
		_turns = turns;
		_balance = balance;
		_attacker = from;
		_enemy = enemy;
		_county = enemy.County;
		_us = us;
		_them = them;
		Lay();
		Reveal();
	}

	/// <summary>The company on the far side of the table when the fight is in open country, or null
	/// when it is a county's own defence.</summary>
	private FieldArmy _enemy;

	private Defenders Against() => _enemy != null
		? new Defenders(_enemy.Men, new Dictionary<string, int>(), "", _turns.AnyProvince(_enemy.Home)?.Loyalty ?? 0f,
			InOpenCountry: true)
		: _turns.DefendersOf(_county);

	protected override void Furnish()
	{
		Title.Text = "A Battle Awaits";
		Column.AddChild(new Control { CustomMinimumSize = new Vector2(0, RibbonDrop) });
		_where = Chrome.Line("", 20, Chrome.Soft);
		_where.HorizontalAlignment = HorizontalAlignment.Center;
		Column.AddChild(_where);

		var table = new HBoxContainer();
		table.AddThemeConstantOverride("separation", 18);
		Column.AddChild(table);

		_ourSide = new BattleSide(far: false);
		_theirSide = new BattleSide(far: true);
		table.AddChild(_ourSide.Root);
		table.AddChild(Middle());
		table.AddChild(_theirSide.Root);

		var reckoning = new VBoxContainer();
		reckoning.AddThemeConstantOverride("separation", 8);
		var weights = new HBoxContainer { CustomMinimumSize = new Vector2(0, 12) };
		weights.AddThemeConstantOverride("separation", 3);
		_ourWeight = new ColorRect { Color = Ours, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_theirWeight = new ColorRect { Color = Theirs, SizeFlagsHorizontal = SizeFlags.ExpandFill };
		weights.AddChild(_ourWeight);
		weights.AddChild(_theirWeight);
		reckoning.AddChild(weights);

		_terms = new GridContainer { Columns = 4 };
		_terms.AddThemeConstantOverride("h_separation", 18);
		_terms.AddThemeConstantOverride("v_separation", 4);
		reckoning.AddChild(_terms);
		Column.AddChild(Chrome.Framed(reckoning, 8));

		// Whatever the frame has left over sits here, so the orders stay on its bottom rail.
		Column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
		var orders = new HBoxContainer();
		orders.AddThemeConstantOverride("separation", 16);
		Column.AddChild(orders);

		_attack = Chrome.Order("Take the Field", "crossed-swords", () => Strike(), out _attackWord);
		_lead = Chrome.Order("Lead in Person", "sword", Lead, out Label _);
		_siege = Chrome.Order("Lay Siege", "castle", Sit, out _siegeWord);
		orders.AddChild(_attack);
		orders.AddChild(_lead);
		orders.AddChild(_siege);
		orders.AddChild(Chrome.Order("Retreat", "footsteps", Close, out _leaveWord));
	}

	/// <summary>Between the two armies: the question, the crossed swords, and what the captain makes
	/// of it.</summary>
	private Control Middle()
	{
		var middle = new VBoxContainer { CustomMinimumSize = new Vector2(MiddleWide, 0) };
		middle.AddThemeConstantOverride("separation", 10);

		_question = Chrome.Line("", 22, Chrome.Cream);
		_question.HorizontalAlignment = HorizontalAlignment.Center;
		_question.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		middle.AddChild(_question);

		Control rule = Chrome.Rule(MiddleWide / 2);
		rule.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		middle.AddChild(rule);

		middle.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(CrestPath),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			MouseFilter = MouseFilterEnum.Ignore,
		});

		_verdict = Chrome.Line("", 20, Chrome.Cream);
		_verdict.HorizontalAlignment = HorizontalAlignment.Center;
		_verdict.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		middle.AddChild(_verdict);
		return middle;
	}

	/// <summary>Draws the table as the county stands right now. Called when the panel opens and
	/// again between the two fights, because the second one is asked of a different army.</summary>
	private void Lay()
	{
		FieldArmy ours = _attacker;
		Defenders against = Against();
		if (ours == null)
		{
			return;
		}

		// Nothing to storm while anybody is still standing in front of the walls — and nothing to
		// storm at all where there are no walls, or nobody on them. An empty town used to be offered
		// as "Storm the walls" behind open ground; it is walked into, the way TurnManager.Attack
		// takes it (a county that is not Held falls to the field fight).
		_walls = _enemy == null && ProvinceEconomy.Men(against.Field) == 0 && against.Held;
		Dictionary<string, int> holding = _walls ? against.Castle : against.Field;
		bool spent = ours.MarchLeft <= 0f;

		_where.Text = _enemy != null ? $"In the fields of {_county}" : _county;
		_question.Text = _walls ? "Will you storm the walls?" : "Will you take the field?";
		ShowSides(ours.Men, holding);

		(float mine, float theirs) = Battle.Weighed(ours.Men, against, _walls, spent, _balance);
		_ourWeight.SizeFlagsStretchRatio = Mathf.Max(0.02f, mine);
		_theirWeight.SizeFlagsStretchRatio = Mathf.Max(0.02f, theirs);

		ShowTerms(against, ours.Men, spent);
		_verdict.Text = Reckoned(mine, theirs);
		if (ProvinceEconomy.Men(holding) == 0)
		{
			// Nobody to weigh the day against: the walls and the people's heart are multipliers on
			// men who are not there, and listing them would be telling the lord about a fight that
			// is not going to happen.
			foreach (Node old in _terms.GetChildren())
			{
				old.QueueFree();
			}

			Note("Nobody stands in the way");
			_verdict.Text = "The gate stands open, my lord.";
		}

		_verdict.AddThemeColorOverride("font_color", Chrome.Cream);
		_attack.Visible = true;
		_attackWord.Text = _walls ? "Storm the Walls" : ProvinceEconomy.Men(holding) == 0 ? "March In" : "Take the Field";
		_leaveWord.Text = "Retreat";

		Besieged(against);

		// The field can be fought by hand; the walls, not yet — the captain climbs them.
		_lead.Visible = !_walls && ProvinceEconomy.Men(holding) > 0;
	}
}
