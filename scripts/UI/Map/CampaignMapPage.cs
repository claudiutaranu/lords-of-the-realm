using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Phase 1 of the campaign: a clickable map of two realms' frontier, an End Turn counter, and a
/// TurnManager-driven economy for every province somebody holds — the player's, and the rival
/// lords', which LordAI runs on the same turn. Unclaimed provinces are held by nobody and stay
/// exactly as the campaign authored them. No armies or combat yet.
///
/// Nothing here is about one particular campaign: which realms, which provinces, where their seats
/// sit and which of them the player runs all come from the played campaign's own provinces.json
/// (see Campaign), so a second campaign is a second folder rather than a second copy of this page.
///
/// The map itself is 3D (CampaignMap3D): a displaced terrain mesh the player pans and zooms.
/// This page owns the UI over it and the 2D province markers, which are re-projected from the
/// camera every frame. Province identity still comes from the campaign's map-ids.png, an unseen
/// image of the same layout where each pixel's red channel is its province index + 1 (0 = water)
/// — the standard technique this genre uses (Paradox's province bitmaps work the same way), here
/// read at whatever point the click raycast lands on.
/// </summary>
public partial class CampaignMapPage : Control
{
	private SubViewportContainer _map;
	private CampaignMap3D _world;
	private int _hovered = -1;
	private Label _turnLabel;
	private Label _seasonLabel;
	private readonly List<ProvinceMarker> _markers = new();
	private ProvinceMarker _selected;
	private TurnManager _turnManager;
	private GameBalance _balance;
	private readonly Dictionary<string, ProvinceDefinition> _definitionsByName = new();
	private Label _goldLabel;
	private Label _grainLabel;
	private Label _woodLabel;
	private Label _stoneLabel;
	private Label _ironLabel;
	private Label _populationLabel;
	private ProvinceSidebar _sidebar;
	private readonly List<RoomPage> _rooms = new();
	private HallPage _hall;
	private Control _leaveConfirm;
	private AdvisorPanel _advisor;
	private TaxPanel _taxes;
	private HappinessPanel _happiness;
	private PeoplePanel _people;
	private RationPanel _rations;
	private SupplyPanel _supply;
	private StorePanel _stores;
	private LetterPanel _letters;
	private DiplomacyPanel _diplomacy;
	private FieldPanel _fields;
	/// <summary>One line the turn owes the player that the advisor has no words for — men walking
	/// away unpaid, so far. Shown once he is done talking.</summary>
	private string _turnNote = "";

	/// <summary>The walls the masons finished this season in the lord's counties, announced once the
	/// advisor has had his say — shown over him, one of the two would not be read.</summary>
	private readonly List<(string County, string Fort, int OnTheWalls)> _wallsRaised = new();

	private ConstructionPanel _construction;
	private ConquestPanel _conquest;

	/// <summary>The counties the lord held when last asked, for telling which of them are new.</summary>
	private HashSet<string> _held;
	private GarrisonPanel _garrison;

	/// <summary>Whether the next click on a county is a destination rather than a selection. One flag
	/// and not a mode with its own screen: the lord has already said march, and the only thing left
	/// to say is where — a click anywhere else means he thought better of it.</summary>
	private bool _marching;

	/// <summary>The company in hand while an order is being given — that company and not its county,
	/// because a county can have several and the order is for the one the lord took hold of. Thrown
	/// away the moment it is given or abandoned.</summary>
	private FieldArmy _marchingArmy;
	private readonly Dictionary<(string From, string To), List<Vector2>> _roadLines = new();
	private MarchGrid _ground;

	/// <summary>The trail the cursor is pointing at, in map pixels: a bead every so far along the way,
	/// and a larger one where it crosses a border. Kept in map terms and projected to the screen
	/// every frame, the same as the province pins, so it stays on the ground while the lord pans.</summary>
	private readonly List<(Vector2 At, MarchTrail.Mark Mark, int Season, bool Reachable)> _trailSteps = new();
	private MarchTrail _trail;

	/// <summary>The question the trail on the map answers: which company, from where, to which cell,
	/// with how much of its season left. The mouse moves a good deal more often than it changes cell.</summary>
	private (FieldArmy Army, Vector2 From, Vector2I Cell, float Left)? _marchAsked;

	/// <summary>The camera and window the pins and the trail were last projected for (see _Process).</summary>
	private (int Camera, Vector2 Window)? _projectedFor;
	private ArmyPanel _army;
	private QuestionPanel _ask;
	private SplitPanel _splitting;
	private BattlePanel _battle;
	private Label _marchLabel;
	private string _leaveTarget;
	private Control _turnTransition;

	public override void _Ready()
	{
		FindTheFurniture();
		LoadCampaignProvinces();
		bool opening = OpenTheTurn();
		WatchTheMap();
		RaiseCountyPanels();
		LayMarchLayer();
		RaiseArmyPanels();
		RaiseFieldsAndLetters();
		WireTurnAndMenu();
		DrawTheMap();

		SelectProvince(0); // Kingsreach, the capital — shows something real before any click.

		if (opening)
		{
			OpenBriefing();
		}
	}

	/// <summary>The map, its camera and the bar across the top, found in the scene once.</summary>
	private void FindTheFurniture()
	{
		_map = GetNode<SubViewportContainer>("%Map");
		_world = GetNode<CampaignMap3D>("%World");

		// The sidebar covers the east of the screen; the camera is told how much, so the east coast
		// can still be scrolled out from under it.
		var furniture = GetNode<Control>("Sidebar");
		void Covered() => _world.CoveredRight = furniture.Size.X / Mathf.Max(1f, GetViewportRect().Size.X);
		furniture.Resized += Covered;
		Covered();
		_turnLabel = GetNode<Label>("%TurnValue");
		_seasonLabel = GetNode<Label>("%SeasonValue");
		_goldLabel = GetNode<Label>("%GoldValue");
		_grainLabel = GetNode<Label>("%GrainValue");
		_woodLabel = GetNode<Label>("%WoodValue");
		_stoneLabel = GetNode<Label>("%StoneValue");
		_ironLabel = GetNode<Label>("%IronValue");
		_populationLabel = GetNode<Label>("%PopulationValue");
		_sidebar = GetNode<ProvinceSidebar>("%ProvinceSidebar");
		// Only the date reads gilded; the stockpile numbers stay cream so they carry at a glance
		// against the dark bar.
		foreach (string valueName in new[] { "SeasonValue", "TurnValue" })
		{
			GoldTitle.Apply(GetNode<Label>($"%{valueName}"));
		}
	}

	/// <summary>The turn itself: every county's definition read, the held ones handed to a new
	/// TurnManager, and a pending save laid over it. True when this is a campaign being started
	/// rather than resumed.</summary>
	private bool OpenTheTurn()
	{
		// Balance is the engine's, not the campaign's: every campaign is simulated by the same rules.
		_balance = GD.Load<GameBalance>("res://data/game-balance.tres");
		// A campaign opens with one seat each and everything else unclaimed, so a province having a
		// definition and a province being run are two different things: the land is described either
		// way — that is what puts its industries on the map — but only a province somebody holds
		// takes a turn. Taking an unclaimed one is what will hand its definition over.
		var held = new List<ProvinceDefinition>();
		var unheld = new List<ProvinceDefinition>();
		var realmByProvince = new Dictionary<string, string>();
		foreach (ProvinceData province in _provinces)
		{
			if (province.EconomyFile.Length == 0)
			{
				continue; // no economy authored for it yet
			}

			var definition = GD.Load<ProvinceDefinition>(Campaign.Data($"provinces/{province.EconomyFile}.tres"));
			_definitionsByName[definition.ProvinceName] = definition;

			if (province.Realm == _unclaimedRealm)
			{
				unheld.Add(definition);
				continue;
			}

			held.Add(definition);
			realmByProvince[definition.ProvinceName] = province.Realm;
		}

		_turnManager = new TurnManager(_balance, held, realmByProvince, _playerRealm, Campaign.Difficulty,
			unheld)
		{
			RivalWallsUpTo = _rivalWallsUpTo,
			Neighbours = _neighbours,
			LordOf = _lordOf,
		};
		// Read before the pending save is consumed: it is the only thing that tells a campaign
		// being started from a campaign being resumed.
		bool opening = SaveGame.Pending == null;
		if (SaveGame.Pending != null)
		{
			_turnManager.Restore(SaveGame.Pending);
			SaveGame.Pending = null; // consumed, so starting a fresh campaign later doesn't reopen it
		}

		UpdateTurnDisplay();

		return opening;
	}

	/// <summary>The map answering the lord: clicks on it, the county the camera comes to rest over,
	/// and a pin for every county.</summary>
	private void WatchTheMap()
	{
		_map.GuiInput += OnMapGuiInput;

		// The county under the middle of the screen is the one the sidebar reads, as the camera pans —
		// but not while an army is in hand, which a change of county would take out of it, nor while
		// the rivals walk.
		_world.LookedAt += index =>
		{
			if (index >= 0 && index < _provinces.Count && !_marching && !_rivalsMarching
				&& (_selected == null || _markers.IndexOf(_selected) != index))
			{
				SelectProvince(index);
			}
		};

		Control markers = GetNode<Control>("%Markers");
		foreach (ProvinceData province in _provinces)
		{
			var marker = new ProvinceMarker();
			markers.AddChild(marker);
			marker.Configure(_realms[province.Realm].Accent, province.IsCapital);
			_markers.Add(marker);
		}
	}

	private string RealmName(string realm) => _realms.TryGetValue(realm, out RealmData data) ? data.Name : realm;

	private Color RealmAccent(string realm) => _realms.TryGetValue(realm, out RealmData data) ? data.Accent : Colors.White;

	private ProvinceData Selected() =>
		_provinces[_selected != null ? _markers.IndexOf(_selected) : 0];

	private void UpdateTurnDisplay()
	{
		_turnLabel.Text = _turnManager.CurrentYear.ToString();
		_seasonLabel.Text = _turnManager.CurrentSeason.ToString();
	}

	// Each province keeps its own stockpile — this shows whichever one is currently
	// selected, not an empire-wide total.
	private void UpdateResourceBar(ProvinceEconomy economy)
	{
		_goldLabel.Text = economy.Gold.ToString("N0");
		_grainLabel.Text = economy.Grain.ToString("N0");
		_woodLabel.Text = economy.Wood.ToString("N0");
		_stoneLabel.Text = economy.Stone.ToString("N0");
		_ironLabel.Text = economy.Iron.ToString("N0");
		_populationLabel.Text = economy.Population.ToString("N0");
	}
}
