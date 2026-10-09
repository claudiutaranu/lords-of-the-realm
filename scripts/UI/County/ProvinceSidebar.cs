using System.Collections.Generic;
using Godot;

/// <summary>The province block of the campaign sidebar: whose province it is and how it stands,
/// what it holds and what next turn will bring in, then what it has built and what it musters.
///
/// Built in code rather than in the scene because nearly all of it is one cell repeated — five
/// resources and seven kinds of soldier — and a loop stays even where hand-placed panels drift
/// apart.
///
/// The yields are the same projection the worker panel previews, so moving a worker changes both
/// at once; the page refreshes this when that panel is touched. The muster cards read "—" until
/// the smithy has finished something, rather than showing a number nothing produced.</summary>
public partial class ProvinceSidebar : VBoxContainer
{
	private const string IconDirectory = "res://assets/ui/icons";
	// The crest art carries wide empty margins, and this is the crop the top bar's shield uses too.
	// A realm whose shield is drawn to different margins needs its own crop, not this one.
	internal static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);

	// White, as the rooms are lettered: cream went soft on the dark card. A figure the pointer is over
	// turns gold, since it can no longer brighten.
	private static readonly Color Text = Chrome.Bright;
	private static readonly Color Pointed = new("f0c870");
	private static readonly Color Gain = new("6fbf5f");
	private static readonly Color Lack = new("c65a45");

	private static readonly (ResourceType Type, string Icon)[] Resources =
	{
		(ResourceType.Grain, "food"),
		(ResourceType.Cattle, "livestock"),
		(ResourceType.Wood, "wood"),
		(ResourceType.Stone, "stone"),
		(ResourceType.Iron, "iron"),
	};

	// Keyed by the weapon each one carries, which names both its portrait in assets/units and its
	// glyph in assets/ui/icons. Those portraits belong to no campaign: every realm musters spearmen.
	//
	// A card appears once its portrait exists, so the peasant — who needs no weapon, only people
	// willing to be led — joins the grid the moment assets/units/peasant.png is dropped in.
	/// <summary>What the smithy makes and the yard arms its men out of, in the order the armoury
	/// counts them.</summary>
	internal static readonly (string Weapon, string Icon)[] Arms =
	{
		("sword", "sword"),
		("bow", "bow"),
		("crossbow", "crossbow"),
		("spear", "spear"),
		("mace", "mace"),
		("horse", "helmet"),
	};

	private const string UnclaimedArtPath = "res://assets/ui/unclaimed.png";

	private Control _held;
	private Control _foreign;
	private Label _word;
	private ColorRect _accent;
	private TextureRect _crest;
	private Label _name;
	private Label _realm;
	private string _realmName = "";
	private Label _population;
	private Label _loyalty;
	private Label _tax;
	private Label _ration;
	private bool _isRationShort;
	private readonly Dictionary<ResourceType, Label> _stock = new();
	private readonly Dictionary<ResourceType, Label> _yield = new();
	private readonly Dictionary<ResourceType, TextureRect> _stockIcon = new();
	private readonly List<(Control Divider, Control Cell, Label Count, Label Yield, string Weapon)> _armoury = new();
	private Control _armouryFrame;

	/// <summary>Empty places after the weapons, so one sword stands as wide as one store above it and
	/// not stretched across the whole card.</summary>
	private readonly List<Control> _armouryRoom = new();
	private LabourBar _labour;
	private WorksStrip _works;
	private Control _labourFrame;

	/// <summary>The lord wants a word with the reeve about the tax. Raised rather than handled here:
	/// the sidebar is a readout, and the page above it owns what opens over the map.</summary>
	public event System.Action TaxPressed;

	/// <summary>And with whoever keeps the county's mood. Same arrangement: the sidebar reads, the
	/// page above it opens things.</summary>
	public event System.Action LoyaltyPressed;

	/// <summary>And with whoever keeps the parish roll: how the people have fared, season by season.</summary>
	public event System.Action PeoplePressed;

	/// <summary>And with whoever feeds them.</summary>
	public event System.Action RationPressed;

	/// <summary>And with whoever keeps one of the stores: the cow, the basket, the woodpile.</summary>
	public event System.Action<ResourceType> StorePressed;

	private ProvinceEconomy _economy;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 8);
		BuildHeader();

		// Everything below the header is somebody's books, and you only get to read your own. The
		// two halves are built once and swapped by Refresh, rather than torn down and rebuilt every
		// time the selection crosses a border.
		_held = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_held.AddThemeConstantOverride("separation", 8);
		AddChild(_held);
		BuildStats();
		BuildResources();
		_works = new WorksStrip();
		_held.AddChild(_works);
		BuildLabour();
		BuildArmoury();

		BuildForeign();
	}

	/// <summary>Who this province is. Independent of the economy, so the rival's provinces and the
	/// unclaimed ones still get a header of their own.</summary>
	public void ShowHeader(string provinceName, string realmName, string realmKey, Color accent)
	{
		_name.Text = provinceName;
		_realmName = realmName;
		_realm.Text = realmName;
		_accent.Color = accent;

		// A realm has a crest when someone has drawn one; the accent stripe carries the rest. The slot
		// keeps its place either way — hiding it would shorten the header, and the whole sidebar
		// would shift every time the selection moved between a realm's province and an unclaimed one.
		string crestPath = Heraldry.CrestPath(realmKey);
		_crest.Texture = ResourceLoader.Exists(crestPath)
			? new AtlasTexture { Atlas = GD.Load<Texture2D>(crestPath), Region = CrestRegion }
			: null;
	}

	/// <summary>The province's own numbers, or nothing at all for one no realm is running yet.</summary>
	public void ShowEconomy(ProvinceEconomy economy, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_economy = economy;
		_definition = definition;
		_balance = balance;
		_season = season;
		Refresh();
	}

	/// <summary>Re-reads the economy — after a turn, or after a worker is moved somewhere else.</summary>
	public void Refresh()
	{
		bool held = _economy != null;
		_held.Visible = held;

		// The sky over the county, under whose it is: the one thing about the season a lord cannot
		// order, and the one his harvest turns on.
		_realm.Text = held ? $"{_realmName}  ·  {_economy.Weather}" : _realmName;
		_realm.TooltipText = held ? Sky(_economy.Weather) : "";
		_realm.MouseFilter = held ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
		_foreign.Visible = !held;
		_population.Text = held ? _economy.Population.ToString("N0") : "—";
		_loyalty.Text = held ? Mathf.RoundToInt(_economy.Loyalty).ToString() : "—";
		_tax.Text = held ? $"{_economy.Tax}%" : "—";

		foreach ((ResourceType type, string icon) in Resources)
		{
			// The loaf or the cow ringed in red while its work is short of hands, as in the original.
			// The diggings are never short (Labour.Wanted).
			string job = Labour.JobOf(type);
			bool isWanting = held && Labour.Wanted(_economy, _definition, _balance, _season, job) > Labour.Hands(_economy, job);
			var picture = GD.Load<Texture2D>($"{IconDirectory}/{icon}.png");
			_stockIcon[type].Texture = isWanting ? Stroke.Ringed(picture, Stroke.Wanting, 34) : picture;
			if (!held)
			{
				_stock[type].Text = "—";
				_yield[type].Text = "";
				continue;
			}

			_stock[type].Text = StockOf(type).ToString("N0");
		}

		// What next season will leave the province with, net: the harvest less what the people eat,
		// the seed that goes back into the ground, the herd that goes under the knife in a bad
		// winter. A row that only ever counted up would show a granary gaining every season of a
		// year it is quietly being emptied. A county nobody here runs has no season to play out: the
		// loop above has already blanked its row. Previewing it anyway threw, and since the turn
		// reselects whatever county is in hand, a lord who had last looked at a neighbour's land lost
		// the rest of every turn after it — the fields, the walls and the season on the map.
		TurnSummary next = held ? EconomySimulation.Preview(_economy, _definition, _balance, _season) : null;
		if (held)
		{
			foreach ((ResourceType type, string _) in Resources)
			{
				int change = ChangeIn(next, type);
				Label reading = _yield[type];
				// Nothing at all rather than a zero: a column of zeroes is noise under the numbers.
				reading.Text = change == 0 ? "" : change > 0 ? $"+{change:N0}" : $"−{-change:N0}";
				reading.AddThemeColorOverride("font_color", change < 0 ? Lack : Gain);
			}
		}

		// The table as it will be laid, not as it was ordered: a lord who has sold the barn and the
		// herd out from under a Normal ration is told, in red, that next season they get None.
		RationLevel served = held ? next.Achieved : RationLevel.Normal;
		_isRationShort = held && served < _economy.Ration;
		_ration.Text = held ? served.ToString() : "—";
		_ration.AddThemeColorOverride("font_color", Resting(_ration));

		_labour.Show(held ? _economy : null, _definition, _balance, _season);
		_works.Show(held ? _economy : null, _balance);
		_labourFrame.Visible = held;

		// The weapons waiting in the armoury, not the men holding them: an army raised in the yard
		// marches out of the county, so a list of it here is a list of who has already gone. What
		// the smithy has made stays until somebody is handed it. Only what there is, and what the
		// forge is making, with what next season adds under it the way the stores carry theirs — a
		// lit forge that will turn out nothing says so in red.
		bool shown = false;
		int cells = 0;
		foreach ((Control divider, Control cell, Label count, Label yield, string weapon) in _armoury)
		{
			int stocked = held ? _economy.Armoury.GetValueOrDefault(weapon) : 0;
			bool forging = held && _economy.Forging == weapon;
			count.Text = stocked.ToString("N0");
			yield.Text = forging ? $"+{next.Forged:N0}" : "";
			yield.AddThemeColorOverride("font_color", forging && next.Forged == 0 ? Lack : Gain);
			cell.Visible = stocked > 0 || forging;
			if (divider != null)
			{
				divider.Visible = cell.Visible && shown;
			}

			shown |= cell.Visible;
			cells += cell.Visible ? 1 : 0;
		}

		for (int i = 0; i < _armouryRoom.Count; i++)
		{
			_armouryRoom[i].Visible = i < _armouryRoom.Count - cells;
		}

		_armouryFrame.Visible = shown;
	}

	/// <summary>What the season's weather is doing to the county, as Climate reckons it.</summary>
	/// <summary>A stat's colour when the pointer is not on it: red for a table that cannot be laid.</summary>
	private Color Resting(Label value) => value == _ration && _isRationShort ? Lack : Text;

	private static string Sky(Weather weather) => weather switch
	{
		Weather.Sunny => "Sunny: the corn grows by half again, the harvest comes in half as much again, the herd breeds",
		Weather.Storms => "Storms: half the seed and half the harvest are lost, and the herd suffers",
		Weather.Flooding => "Flooding: a field is ruined, the crop is drowned, the herd dies",
		Weather.Drought => "Drought: a field is ruined, the corn withers to half, the herd dies",
		Weather.Frost => "Frost: half the seed and half the harvest are lost",
		_ => "Cloudy: the land goes on as it would",
	};

	/// <summary>What the scouts say of a county you do not hold, under its keep: not its books, but
	/// enough to know which gate is worth the march.</summary>
	public void ShowWord(string word) => _word.Text = word;

	private static int ChangeIn(TurnSummary summary, ResourceType type) => type switch
	{
		ResourceType.Grain => summary.GrainChange,
		ResourceType.Cattle => summary.CattleChange,
		ResourceType.Wood => summary.WoodChange,
		ResourceType.Stone => summary.StoneChange,
		_ => summary.IronChange,
	};

	private int StockOf(ResourceType type) => type switch
	{
		ResourceType.Grain => _economy.Grain,
		ResourceType.Cattle => _economy.Cattle,
		ResourceType.Wood => _economy.Wood,
		ResourceType.Stone => _economy.Stone,
		_ => _economy.Iron,
	};
}
