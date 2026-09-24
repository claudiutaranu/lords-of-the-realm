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
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string LoadGameScenePath = "res://scene/load-game/load_game.tscn";
	private const string OptionsScenePath = "res://scene/options/options.tscn";
	private const string SelfScenePath = "res://scene/campaign-map/campaign_map.tscn";
	private const string LeaveFarewellPath = "res://assets/audio/quit-farewell.mp3";
	private const string OpeningVoicePath = "res://assets/audio/campaign-opening-briefing.mp3";
	private const string SavingVoicePath = "res://assets/audio/saving-game.mp3";
	private const string CastleCompleteVoicePath = "res://assets/audio/castle-complete.mp3";
	private const string DisbandVoicePath = "res://assets/audio/disband-troops.mp3";
	private const string SquareButtonPath = "res://assets/ui/button-square.png";
	private const string MercenaryBadgePath = "res://assets/ui/icons/mercenaries.png";
	private const string ProvincesDataFile = "provinces.json";
	private const string RoadsDataFile = "map-roads.json";
	// Where each village stands, written by the map generator: its seat, unless the level ground its
	// village needs would not fit there — see flatten_yards in tools/generate_campaign_map.py.
	private const string YardsDataFile = "map-yards.json";

	/// <summary>How far apart the steps of a march are laid, in map pixels. Close enough to read as a
	/// road being walked, far enough that a long march is a trail and not a stripe.</summary>
	private const float StepSpacing = 34f;

	/// <summary>How long past the longest rival march the turn waits before it stops waiting.</summary>
	private const float DeadlineSlack = 1f;

	/// <summary>How far above the waterline the ground has to stand before an army will set foot on
	/// it, and how steeply it may climb across one cell before it is a wall rather than a hill.
	///
	/// The rise is in world units, so it follows the map's own height scale: flattening the relief
	/// to make the roads legible also flattened every mountain out of an army's way, and this came
	/// down with it. A lord should still have to go round the spine of the island.</summary>
	private const float ShoreClearance = 0.35f;
	private const string DitchFile = "map-ditch.png";
	private const float MarchableRise = 0.9f;

	/// <summary>Where freshly raised men stand, from their county's seat: north-west of the town,
	/// opposite the corner the castle is built on.</summary>
	private static readonly Vector2 MusterGround = new(-37f, -37f);

	/// <summary>How far round the town each company after the first musters, in radians, so two
	/// banners raised in the same county are two things to point at.</summary>
	private const float MusterApart = 0.9f;

	/// <summary>How close two of a lord's companies have to halt before he is asked whether they are
	/// one army, in map pixels. A field, not a county: men who cannot see each other are not
	/// standing together.</summary>
	private const float JoinReach = 70f;
	private const string RecruitsScenePath = "res://scene/campaign-map/recruits.tscn";
	private const string BlacksmithScenePath = "res://scene/campaign-map/blacksmith.tscn";
	private const string MarketScenePath = "res://scene/campaign-map/market.tscn";
	private const string HallScenePath = "res://scene/campaign-map/hall.tscn";
	private const string FortificationsScenePath = "res://scene/campaign-map/fortifications.tscn";
	private const string CityScenePath = "res://scene/campaign-map/city.tscn";
	private const float TurnFadeInSeconds = 0.4f;
	private const float TurnHoldSeconds = 1.1f;
	private const float TurnFadeOutSeconds = 0.5f;
	private const float ToastFadeInSeconds = 0.15f;
	private const float ToastHoldSeconds = 1.6f;
	private const float ToastFadeOutSeconds = 0.6f;

	/// <summary>What the narrator says over the map on the first turn, and what stays on screen after
	/// he has stopped. Kept to what a first turn can actually do, because a briefing nobody can act
	/// on is a cutscene with a Close button.</summary>
	private const string Briefing =
		"Greetings, sire. The crown is yours, and with it Kingsreach \u2014 one seat out of eight " +
		"on this map. The Northern Watch holds another, and six lie unclaimed between you.\n\n" +
		"Fill your stores, raise an army, and take the rest.";

	/// <summary>A realm as the campaign describes it: what it is called, and the colour everything
	/// belonging to it is drawn in.</summary>
	private record RealmData(string Name, Color Accent);

	/// <summary>One province of the played campaign. <paramref name="Realm"/> is who holds it when
	/// the campaign opens, which for most of them is nobody. <paramref name="EconomyFile"/> names the
	/// ProvinceDefinition describing its land, and is empty where none is authored yet.
	/// <paramref name="MapPosition"/> is its seat — the pin, the end of its roads, where its army
	/// stands; <paramref name="TownPosition"/> is where its village, fields and castle are laid out,
	/// which is the seat itself unless the generator had to move the village clear of a cliff.</summary>
	private record ProvinceData(string Name, Vector2 MapPosition, string Realm, bool IsCapital, string EconomyFile,
		Vector2 TownPosition);

	private readonly Dictionary<string, RealmData> _realms = new();
	// Which realm is yours. The others' provinces, and the unclaimed ones, run on nobody's orders yet.
	private string _playerRealm = "";
	// And which realm means nobody. A province of this realm is not simulated at all — it is not
	// handed to the TurnManager — so it keeps its authored numbers until somebody takes it.
	private string _unclaimedRealm = "";

	/// <summary>The highest rung the other lords may build on this map (provinces.json).</summary>
	private string _rivalWallsUpTo = "";
	// Authored order is load-bearing: it is the ID map's index + 1 encoding, so a province's place
	// in provinces.json is what ties it to its pixels on the map.
	private readonly List<ProvinceData> _provinces = new();

	private SubViewportContainer _map;
	private CampaignMap3D _world;
	private int _hovered = -1;
	private Label _turnLabel;
	private Label _seasonLabel;
	private Control _sectionPanel;
	private Label _sectionTitle;
	private Label _sectionBody;
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
	private RationPanel _rations;
	private FieldPanel _fields;
	/// <summary>One line the turn owes the player that the advisor has no words for — men walking
	/// away unpaid, so far. Shown once he is done talking.</summary>
	private string _turnNote = "";

	/// <summary>Whether the masons finished a wall this season, which the steward announces once the
	/// advisor has had his say — spoken over him, one of the two would not be heard.</summary>
	private bool _wallRaised;

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
	private readonly List<(Vector2 At, bool County, bool Reachable)> _trailSteps = new();
	private MarchTrail _trail;
	private ArmyPanel _army;
	private JoinPanel _join;
	private SplitPanel _splitting;
	private BattlePanel _battle;
	private Label _marchLabel;
	private string _leaveTarget;
	private Control _turnTransition;

	public override void _Ready()
	{
		_map = GetNode<SubViewportContainer>("%Map");
		_world = GetNode<CampaignMap3D>("%World");
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

		LoadCampaignProvinces();

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
		};
		// Read before the pending save is consumed: it is the only thing that tells a campaign
		// being started from a campaign being resumed.
		bool opening = SaveGame.Pending == null;
		if (SaveGame.Pending != null)
		{
			_turnManager.Restore(SaveGame.Pending.Turn, SaveGame.Pending.Provinces, SaveGame.Pending.Prices,
				SaveGame.Pending.Difficulty);
			SaveGame.Pending = null; // consumed, so starting a fresh campaign later doesn't reopen it
		}

		UpdateTurnDisplay();


		_map.GuiInput += OnMapGuiInput;

		Control markers = GetNode<Control>("%Markers");
		foreach (ProvinceData province in _provinces)
		{
			var marker = new ProvinceMarker();
			markers.AddChild(marker);
			marker.Configure(_realms[province.Realm].Accent, province.IsCapital);
			_markers.Add(marker);
		}

		// The advisor stands over everything, so he goes in last and stays: a message about a county
		// must not be arrived at through the panel of the room the player happened to leave open.
		_advisor = new AdvisorPanel();
		AddChild(_advisor);

		// And over the advisor, the end of it all, when there is nothing left to be advised about.
		_fallen = new FallenPanel();
		AddChild(_fallen);
		_fallen.Chosen += load => SceneRouter.GoTo(this, load ? LoadGameScenePath : MainMenuScenePath);

		_taxes = new TaxPanel();
		AddChild(_taxes);
		// The rate is the county's the moment the arrow is pressed, so the readout beside it is out
		// of date the moment after.
		_taxes.Changed += _sidebar.Refresh;
		_sidebar.TaxPressed += OpenTaxes;

		_happiness = new HappinessPanel();
		AddChild(_happiness);
		_sidebar.LoyaltyPressed += OpenHappiness;

		_rations = new RationPanel();
		AddChild(_rations);
		_rations.Changed += _sidebar.Refresh;
		_sidebar.RationPressed += OpenRations;
		_sidebar.MarchPressed += () =>
		{
			if (_selected != null)
			{
				// The county's button means the company it would send. Which one that is, the county
				// answers — every other company is taken hold of by its own banner on the map.
				TakeUpArmy(_turnManager.GetProvince(_provinces[_markers.IndexOf(_selected)].Name)?.Readiest());
			}
		};

		// What the cursor is standing on while an army is in hand: the county and what it would cost
		// to get there. Beside the pointer rather than in a panel, because the question being asked
		// is about the place under the pointer.
		_marchLabel = Chrome.Line("", 16, Chrome.Bright);
		_marchLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.9f));
		_marchLabel.AddThemeConstantOverride("shadow_offset_x", 1);
		_marchLabel.AddThemeConstantOverride("shadow_offset_y", 2);
		_marchLabel.Visible = false;
		_marchLabel.MouseFilter = MouseFilterEnum.Ignore;

		// In among the province pins rather than on the page: the trail belongs to the ground, so it
		// has to pass under the panels the way the pins do. Added to the page itself it would be
		// drawn over the advisor and over the sidebar, which is a trail crossing furniture.
		_trail = new MarchTrail();
		Control markerLayer = GetNode<Control>("%Markers");
		markerLayer.AddChild(_trail);

		AddChild(_marchLabel);

		// Over the map and over the rail: a company is looked at where it stands.
		_army = new ArmyPanel();
		AddChild(_army);
		_army.MarchPressed += TakeUpArmy;

		// Two of the lord's companies standing in the same field raise one question, and this is
		// where it is asked: one army now, or two?
		_join = new JoinPanel();
		AddChild(_join);

		// And what happens when the men standing there are somebody else's.
		_battle = new BattlePanel();
		AddChild(_battle);
		_battle.Settled += () =>
		{
			// A day's fighting moves men, walls and sometimes a border: everything drawn out there
			// is stale, and so is the county in the sidebar.
			ShowArmies();
			ShowFortifications();
			ShowSettlements();
			LayGround();
			if (_selected != null)
			{
				SelectProvince(_markers.IndexOf(_selected));
			}
		};

		// Which men stay and which walk off is the lord's to say, kind by kind, before anything moves.
		_splitting = new SplitPanel();
		AddChild(_splitting);
		_army.SplitPressed += army => _splitting.Ask(army, taken =>
		{
			FieldArmy half = _turnManager.GetProvince(army.Home)?.Split(army, taken);
			if (half == null)
			{
				return;
			}

			ShowArmies();
			_sidebar.Refresh();
			ShowSaveToast($"{half.Strength:N0} men of {army.Home} now march under their own banner");
		});
		_army.GarrisonPressed += AskWalls;
		_army.DisbandPressed += army =>
		{
			ProvinceEconomy home = _turnManager.GetProvince(army.Home);
			if (home == null)
			{
				return;
			}

			// Sent home is sent HOME. They were taken out of the county's people the day they were
			// raised, so a lord who lets an army go gets his hands back into the fields — otherwise
			// disbanding would quietly be the most expensive thing on the panel.
			// ponytail: hired men walk into the county's people with everybody else — the band is
			// counted on the county and not on the company, so there is nothing here to tell them
			// apart by.
			home.Population += army.Strength;
			home.Disband(army);
			Narrator.Say(DisbandVoicePath);
			ShowArmies();
			_sidebar.Refresh();
			ShowSaveToast($"{army.Strength:N0} men of {army.Home} have gone back to the fields");
		};

		_fields = new FieldPanel();
		AddChild(_fields);
		_fields.Changed += () =>
		{
			// The ground itself changed, so the county out there is wrong until it is drawn again.
			_sidebar.Refresh();
			if (_selected != null)
			{
				ShowFields(_provinces[_markers.IndexOf(_selected)]);
			}
		};
		// Anything the turn has to report that is not the advisor's waits until he has finished, or
		// it is shown behind him and read by nobody.
		_advisor.Emptied += () =>
		{
			if (_turnNote.Length > 0)
			{
				ShowSaveToast(_turnNote);
				_turnNote = "";
			}

			if (_wallRaised)
			{
				Narrator.Say(CastleCompleteVoicePath);
				_wallRaised = false;
			}
		};

		_turnTransition = GetNode<Control>("%TurnTransition");
		GoldTitle.Apply(GetNode<Label>("%TransitionSeason"));
		var endTurn = GetNode<Button>("%EndTurnButton");
		// Fires on the press, not the release. The turn curtain covers the whole screen and comes down
		// under a cursor that has not moved, which takes the button's hover away; when the curtain
		// lifts, Godot does not hand the hover back until the mouse moves again. A button that fires
		// on release checks it is still hovered as it lets go, finds it is not, and does nothing — so
		// a lord who ended his turn and pressed again without touching the mouse pressed a dead button,
		// as many times as he liked. AdvanceTurn already refuses a second turn while one is running.
		endTurn.ActionMode = BaseButton.ActionModeEnum.Press;
		endTurn.Pressed += AdvanceTurn;
		_sectionPanel = GetNode<Control>("%SectionPanel");
		_sectionTitle = GetNode<Label>("%SectionTitle");
		_sectionBody = GetNode<Label>("%SectionBody");
		GoldTitle.Apply(_sectionTitle);
		var navRail = GetNode<NavRail>("%NavRail");
		navRail.SectionChosen += ShowSection;
		GetNode<Button>("%SectionClose").Pressed += () => _sectionPanel.Visible = false;

		// The crest is the pause menu: save, load, or leave the campaign.
		var gameMenu = GetNode<Control>("%GameMenu");
		GetNode<Button>("%MenuShieldButton").Pressed += () => gameMenu.Visible = !gameMenu.Visible;
		BuildMinimapButtons(gameMenu);
		GetNode<Button>("%SaveButton").Pressed += () =>
		{
			// Spoken on the press rather than after the write: the save itself is instant, and a
			// voice that starts once the work is already done is a voice answering nothing.
			Narrator.Say(SavingVoicePath);
			SaveGame.Write(Campaign.Name, _turnManager.Turn, _turnManager.Provinces,
				_turnManager.Market.Pressure, _turnManager.Difficulty);
			gameMenu.Visible = false; // out of the way, so the confirmation lands on the map itself
			ShowSaveToast($"Saved · Turn {_turnManager.Turn}");
		};
		GetNode<Button>("%ResumeButton").Pressed += () => gameMenu.Visible = false;
		GetNode<Button>("%LoadButton").Pressed += () => ConfirmLeave(LoadGameScenePath);
		_leaveConfirm = GetNode<Control>("%LeaveConfirm");
		GetNode<Button>("%LeaveCancelButton").Pressed += () =>
		{
			_leaveConfirm.Visible = false;
			Narrator.Hush(); // staying: he doesn't get to finish the farewell
		};
		GetNode<Button>("%LeaveConfirmButton").Pressed += () => SceneRouter.GoTo(this, _leaveTarget);
		GetNode<Button>("%OptionsButton").Pressed += () =>
		{
			// Options is its own scene, so the running campaign rides along in memory rather
			// than through a file, and comes back when Back returns here.
			SaveGame.Pending = SaveGame.Snapshot(Campaign.Name, _turnManager.Turn, _turnManager.Provinces,
				_turnManager.Market.Pressure, _turnManager.Difficulty);
			OptionsPage.ReturnScenePath = SelfScenePath;
			SceneRouter.GoTo(this, OptionsScenePath);
		};
		GetNode<Button>("%QuitButton").Pressed += () => ConfirmLeave(MainMenuScenePath);

		// After the sites, and after any save has been restored: a loaded game's walls are whatever
		// that save built, not whatever the campaign started with. The same goes for its armies.
		ShowFortifications();
		ShowSettlements();
		ShowArmies();

		// The ground an army may cross, cut once the map and the ledger both exist: it needs the
		// height of the land, the roads drawn on it, and which counties are already somebody's.
		LayGround();

		// And handed to the turn, so the other lords march over the same ground by the same rules.
		// Through a closure on the field, because a county taken lays the ground again.
		var towns = new Dictionary<string, Vector2>();
		foreach (ProvinceData province in _provinces)
		{
			if (_definitionsByName.ContainsKey(province.Name))
			{
				towns[province.Name] = province.TownPosition;
			}
		}

		_turnManager.Survey((from, to) => _ground.Way(from, to, float.MaxValue), pixel =>
		{
			int county = _world.CountyAt(pixel);
			return county >= 0 && county < _provinces.Count ? _provinces[county].Name : "";
		}, towns, MapDecoration.TownRing);

		// What the counties are growing — the first thing back on the ground, because a field is not
		// decoration: it is the one thing out here a lord changes from a screen and then sees from the
		// road. The generator levels the ground round every seat for them (level_seats).
		ShowFields();

		// The woods: the baked trees (tools/decimate_meshy.py), sown last of the things that grow so
		// none is planted on a field.
		_world.Sow();

		// The rest of what stands on the map comes back one piece at a time, once each looks right:
		// ShowProvinceTrade is still here and still works.

		// A save can be loaded into a season that already has men standing about for hire.
		ShowMercenaries();

		// After the sites are placed, so a loaded save's crops open in their own season rather than
		// being sown green and repainted a frame later.
		_world.SetSeason(_turnManager.CurrentSeason);

		SelectProvince(0); // Kingsreach, the capital — shows something real before any click.

		if (opening)
		{
			OpenBriefing();
		}
	}

	/// <summary>The tax table for the county in hand. It is the one decision on this page that is not
	/// made in a room: the reeve comes to the lord, not the other way round.</summary>
	private void OpenTaxes()
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null)
		{
			ShowSaveToast($"{province.Name} is not yours to tax");
			return;
		}

		_taxes.Open(economy, _balance, _turnManager.OtherCounties(economy));
	}

	/// <summary>Lays a road out as beads: one every so far along it, and a larger numbered one wherever
	/// it crosses into another county — which is the only place on a free march where anything
	/// actually changes hands.</summary>
	private void LayTrail(List<(Vector2 At, float Spent)> road, float budget)
	{
		_trailSteps.Clear();
		if (road.Count == 0)
		{
			_trail.Lay(System.Array.Empty<(Vector2, int, bool, bool)>());
			return;
		}

		int county = _world.CountyAt(road[0].At);
		float walked = StepSpacing;
		for (int step = 0; step < road.Count; step++)
		{
			int here = _world.CountyAt(road[step].At);
			bool crossed = here != county && here >= 0;
			county = here;

			walked += step == 0 ? 0f : road[step - 1].At.DistanceTo(road[step].At);
			if (!crossed && walked < StepSpacing && step < road.Count - 1)
			{
				continue;
			}

			walked = 0f;
			_trailSteps.Add((road[step].At, crossed || step == road.Count - 1, road[step].Spent <= budget));
		}
	}

	/// <summary>Puts the trail where the camera currently has it. Done every frame with the pins, for
	/// the same reason: the map moves under them.</summary>
	private void ProjectTrail()
	{
		var beads = new List<(Vector2, int, bool, bool)>(_trailSteps.Count);
		int step = 0;
		foreach ((Vector2 at, bool county, bool reachable) in _trailSteps)
		{
			// Numbered in walking order and not in drawing order: a bead behind the camera is skipped
			// from the picture, not from the count, or the numbers would renumber themselves every
			// time the lord panned the map.
			step++;
			if (_world.TryScreenPosition(at, out Vector2 onScreen))
			{
				beads.Add((onScreen, step, county, reachable));
			}
		}

		_trail.Lay(beads.ToArray());
	}

	private Vector2 SeatOf(string county)
	{
		foreach (ProvinceData province in _provinces)
		{
			if (province.Name == county)
			{
				return province.MapPosition;
			}
		}

		return Vector2.Zero;
	}

	/// <summary>What the ground under the cursor would cost to reach, beside the cursor, and the road
	/// the men would take laid out in front of them. Worked out afresh on every movement of the
	/// mouse, because that is the question being asked.</summary>
	private void ShowMarchCost(Vector2 where)
	{
		_marchLabel.Position = where + new Vector2(18, 14);
		_marchLabel.Visible = true;

		FieldArmy army = _marchingArmy;
		if (army == null || !_world.TryMapPixel(where, out Vector2 ground))
		{
			_marchLabel.Text = "Nowhere to march";
			_marchLabel.AddThemeColorOverride("font_color", Chrome.Dim);
			LayTrail(new List<(Vector2, float)>(), 0f);
			return;
		}

		List<(Vector2 At, float Spent)> road = _ground.Way(ArmyPixel(army), ground, Beyond(army));
		if (road.Count == 0)
		{
			_marchLabel.Text = "No ground for an army";
			_marchLabel.AddThemeColorOverride("font_color", Chrome.Dim);
			LayTrail(road, 0f);
			return;
		}

		LayTrail(road, army.MarchLeft);

		// The county named is the one he would actually END the season in, which on a long road is
		// not the one he is pointing at. Naming the far one would be a promise the season cannot
		// keep.
		(Vector2 stop, float spent) = road[Mathf.Max(0, Halting(road, army.MarchLeft))];
		int county = _world.CountyAt(stop);
		string reached = county >= 0 && county < _provinces.Count ? _provinces[county].Name : "open country";
		int left = Mathf.RoundToInt((army.MarchLeft - spent) / _balance.MarchCostByRoad);
		bool short_ = spent < road[^1].Spent;

		_marchLabel.Text = short_
			? $"{reached} — as far as this season takes them"
			: $"{reached} — {left} paces left after";
		_marchLabel.AddThemeColorOverride("font_color", short_ ? Chrome.Soft : Chrome.Bright);
	}

	/// <summary>Takes an army in hand: from here until it is sent or let go, the map is asking one
	/// question — where do these men go — and every movement of the mouse answers it.</summary>
	private void TakeUpArmy(FieldArmy army)
	{
		if (army == null || army.Strength == 0 || army.MarchLeft <= 0f)
		{
			ShowSaveToast(army == null
				? "There is nobody here with a season left in their legs"
				: $"The men of {army.Home} have no ground left this season");
			return;
		}

		_marching = true;
		_marchingArmy = army;
	}

	/// <summary>Puts the army down again, however that came about — sent, refused, or thought better
	/// of. One way out, so no trail is left burning across the map.</summary>
	private void LayDownArmy()
	{
		_marching = false;
		_marchingArmy = null;
		_marchLabel.Visible = false;
		LayTrail(new List<(Vector2, float)>(), 0f);
	}

	/// <summary>How far past this season's legs a road is still worth working out, so the lord can be
	/// shown where it goes and how far along it he would get. Not unbounded: a search told to find
	/// the far side of the world will walk the whole map to say no.</summary>
	private float Beyond(FieldArmy army) => army.MarchLeft * 4f;

	/// <summary>The last step of a road the army can actually pay for, or -1 when it cannot take a
	/// single one.</summary>
	private static int Halting(List<(Vector2 At, float Spent)> road, float budget)
	{
		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			if (road[step].Spent <= budget)
			{
				halt = step;
			}
		}

		return halt;
	}

	/// <summary>Where a company is standing. Men just raised have never been put anywhere, and they
	/// are at their own county's seat — which is where they were raised.</summary>
	private Vector2 ArmyPixel(FieldArmy army)
	{
		var at = new Vector2(army.X, army.Y);
		if (at != Vector2.Zero)
		{
			return at;
		}

		// Men who have never been sent anywhere are mustered beside their own town rather than on top
		// of its pin — the pin is what the lord clicks to read the county, and a banner standing in
		// it would be in the way of the one thing that square of ground is already for. Each company
		// takes its own ground around the town, so a second one raised there is a second banner the
		// lord can point at rather than a figure hidden inside the first.
		return SeatOf(army.Home) + MusterGround.Rotated((army.Id - 1) * MusterApart);
	}

	/// <summary>Whether halting here means a fight: the lord's own men, standing on the seat of a
	/// county that is not his. Its town and its castle are the same square of ground — the walls are
	/// raised on the seat — so one question covers both, and everything else in the county is ground
	/// to be walked over.</summary>
	private bool Contested(FieldArmy army, string county, Vector2 at)
	{
		if (county.Length == 0 || army.Strength == 0 || _turnManager.RealmOf(army) != _playerRealm
			|| _world.TownAt(at) != county)
		{
			return false;
		}

		ProvinceEconomy theirs = _turnManager.AnyProvince(county);
		return theirs == null || theirs.Realm != _playerRealm;
	}

	/// <summary>The other company of the lord's standing where this one has just halted, or null.
	/// What the join is offered over: men who could see each other across the same field.</summary>
	private FieldArmy Beside(FieldArmy army)
	{
		foreach (FieldArmy other in _turnManager.Armies())
		{
			if (other != army && other.Strength > 0 && other.County == army.County
				&& _turnManager.RealmOf(other) == _playerRealm
				&& ArmyPixel(other).DistanceTo(ArmyPixel(army)) <= JoinReach)
			{
				return other;
			}
		}

		return null;
	}

	/// <summary>Sends the men where the cursor was pointing. Everything about the ground was settled
	/// when the trail was drawn; this pays for it and walks it.</summary>
	private bool March(FieldArmy army, Vector2 where)
	{
		if (army == null || _rivalsMarching || !_world.TryMapPixel(where, out Vector2 ground))
		{
			return false;
		}

		List<(Vector2 At, float Spent)> road = _ground.Way(ArmyPixel(army), ground, Beyond(army));
		int halt = Halting(road, army.MarchLeft);
		if (halt < 0)
		{
			ShowSaveToast("There is no way there");
			return false;
		}

		// As far as the season carries them along that road, and no further. An order to a far county
		// is not refused — it is obeyed for as long as there are legs for it, which is what a lord
		// pointing at the horizon actually means.
		road = road.GetRange(0, halt + 1);
		int county = _world.CountyAt(road[^1].At);
		string into = county >= 0 && county < _provinces.Count ? _provinces[county].Name : "";
		if (into.Length == 0 || !_turnManager.March(army, into, road[^1].At, road[^1].Spent))
		{
			// A rival's border is no longer what stops a march — his ground is walked into like
			// anybody's — so what is left to refuse is the sea, and men with nothing in their legs.
			ShowSaveToast(into.Length == 0
				? "Nobody's land lies that way"
				: $"The men of {army.Home} cannot make that march");
			return false;
		}

		// The banner walks it while the board waits. Everything that has to be redrawn is redrawn
		// when it arrives — a county changing hands moves its walls, its fields and its crest, and
		// doing that at the first step would have the map jump ahead of the man walking across it.
		var strides = new List<Vector2>(road.Count);
		foreach ((Vector2 step, float _) in road)
		{
			strides.Add(step);
		}

		_world.WalkArmy(army.Key, strides, () =>
		{
			ShowArmies();
			ShowFortifications();
			ShowSettlements();
			ShowFields(_provinces[county]);
			SelectProvince(county);
			LayGround(); // a county taken is a county open to walk through

			// A county is taken at its own gate and nowhere else. Men who have halted on another
			// lord's seat — his town, or the walls raised on it — are standing where the thing worth
			// taking is, and that is the one place the fighting happens.
			if (Contested(army, into, strides[^1]))
			{
				_battle.Open(_turnManager, _balance, army, into, strides[^1],
					ColoursOf(_turnManager.RealmOf(army)), ColoursOf(HolderOf(_provinces[county])));
				return;
			}

			// Halted at his own walls, the question is whether they go up onto them. Asked, not done:
			// a company marched home to the castle may only be passing through.
			if ((_world.TownAt(strides[^1]) == into || _world.AtCastle(into, strides[^1])) && WallsFor(army) != null)
			{
				AskWalls(army);
				return;
			}

			// Where they have halted beside another of the lord's companies, the two of them are one
			// army only if he says so. Nothing has joined by the time this is asked.
			FieldArmy beside = Beside(army);
			if (beside != null)
			{
				_join.Ask(army, beside, () =>
				{
					_turnManager.Merge(beside, army);
					ShowArmies();
					_sidebar.Refresh();
				});
			}
		});

		return true;
	}

	/// <summary>The field the click landed on, if it landed on one at all and the county is the
	/// player's. False otherwise, and the press means whatever it meant before.</summary>
	private bool OpenField(int index, Vector2 where)
	{
		ProvinceData province = _provinces[index];
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null
			|| !_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition)
			|| !_world.TryMapPixel(where, out Vector2 pixel))
		{
			return false;
		}

		int field = _world.PlotAt(province.Name, pixel);
		if (field < 0 || field >= economy.Fields.Length)
		{
			return false;
		}

		_fields.Open(economy, definition, _balance, _turnManager.CurrentSeason, field);
		return true;
	}

	/// <summary>What the county is given to eat. It needs the land as well as the ledger, because
	/// what the season will actually take is worked out by playing the season on a copy of the
	/// county — and a county's year depends on the fields it has.</summary>
	private void OpenRations()
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null || !_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition))
		{
			ShowSaveToast($"{province.Name} is not yours to feed");
			return;
		}

		_rations.Open(economy, definition, _balance, _turnManager.CurrentSeason);
	}

	/// <summary>Where the county's goodwill went, and where it has stood year by year. Read-only, so
	/// unlike the tax table nothing here has to be told about afterwards.</summary>
	private void OpenHappiness()
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null)
		{
			ShowSaveToast($"{province.Name} keeps its own counsel");
			return;
		}

		_happiness.Open(economy, _balance, _turnManager.LastSeason(province.Name), TurnManager.StartYear);
	}

	/// <summary>Opens one of the province's rooms — the smithy, the training yard — over this page
	/// rather than in place of it, so the turn, the camera and the selection are all exactly where
	/// they were when it closes.</summary>
	private RoomPage OpenRoom(string scenePath, string verb)
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null)
		{
			// A province you do not hold has none of your rooms in it. Say so rather than letting the
			// press do nothing at all.
			ShowSaveToast($"{province.Name} is not yours to {verb} in");
			return null;
		}

		// A stack rather than one room: the town is a room too, and the smithy opens over it the way
		// the town opens over the map. Closing one uncovers whatever it was opened from.
		var room = GD.Load<PackedScene>(scenePath).Instantiate<RoomPage>();

		// The stall trades on the realm's market and not one of its own, and it has to be handed
		// over BEFORE the room goes into the tree. A room builds itself in _Ready, and AddChild runs
		// _Ready there and then — hand the market over on the line after, and the stall has already
		// tried to price its goods against a market that does not exist. It throws on the first one
		// and the room comes up as bare scenery with not a card on it.
		if (room is MarketPage stall)
		{
			stall.Brief(_turnManager.Market);
		}

		AddChild(room);
		_rooms.Add(room);

		room.Open(economy);
		room.Closed += () =>
		{
			_rooms.Remove(room);
			room.QueueFree();
			// An order spends the province's stores and its people, so everything that reads them is
			// stale by now — the map behind, and any room this one was opened from.
			_sidebar.Refresh();
			UpdateResourceBar(economy);
			// The labour room turns fields over, so the land out here is stale too. Redrawn whichever
			// room was closed: ten plots cost nothing to lay, and asking which rooms can change the
			// land is how a room added later quietly stops updating it.
			ShowFields(province);
			ShowArmies();
			if (_rooms.Count > 0)
			{
				_rooms[^1].Refresh();
			}
		};

		return room;
	}

	/// <summary>The province's town: every building it has raised, and the way into each of them.
	/// The sites it shows are the ones the province actually has, which is why it is handed the
	/// definition as well as the economy a room normally gets.</summary>
	private void OpenCity()
	{
		if (OpenRoom(CityScenePath, "rule") is not CityPage city)
		{
			return;
		}

		if (_definitionsByName.TryGetValue(Selected().Name, out ProvinceDefinition definition))
		{
			city.Brief(definition, _balance, _turnManager.CurrentSeason);
		}

		city.RoomChosen += room => Enter(room);
	}

	/// <summary>Opens one of the town's buildings. Most rooms need nothing but the province's own
	/// stores; the labour room needs the land it is working and the season it is working it in,
	/// neither of which a room is handed, so it is told separately.</summary>
	private void Enter(string room)
	{
		RoomPage opened = OpenRoom($"res://scene/campaign-map/{room}.tscn", "use");
		if (opened is LabourPage labour
			&& _definitionsByName.TryGetValue(Selected().Name, out ProvinceDefinition definition))
		{
			labour.Brief(definition, _balance, _turnManager.CurrentSeason);
		}
	}

	private ProvinceData Selected() =>
		_provinces[_selected != null ? _markers.IndexOf(_selected) : 0];

	/// <summary>The column of buttons beside the minimap: the places a lord returns to most, and the
	/// crest menu. Each wears the square button plate, with the glyph inset inside its frame.</summary>
	private void BuildMinimapButtons(Control gameMenu)
	{
		var column = GetNode<VBoxContainer>("%MinimapButtons");

		// Art where there is art, the drawn glyph where there is not yet: an icon file name here is
		// all it takes to replace one.
		(NavRailIcon.Glyph Glyph, string Art, string Tip, Action Open)[] entries =
		{
			(NavRailIcon.Glyph.Crown, null, "Court", () => ShowSection(NavRail.Section.Court)),
			(NavRailIcon.Glyph.Book, "castle", "Fortifications", () => ShowSection(NavRail.Section.Fortifications)),
			(NavRailIcon.Glyph.Helmet, "shield", "Military", () => ShowSection(NavRail.Section.Military)),
			(NavRailIcon.Glyph.Gear, null, "Menu", () => gameMenu.Visible = !gameMenu.Visible),
		};

		foreach ((NavRailIcon.Glyph glyph, string art, string tip, Action open) in entries)
		{
			// Square, touching, and sized so four of them stand exactly as tall as the 228 square
			// map beside them.
			var button = new Button
			{
				CustomMinimumSize = new Vector2(57, 57),
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, // against the frame's right edge
				TooltipText = tip,
			};
			button.AddThemeStyleboxOverride("normal", SquareButtonPlate(Colors.White));
			button.AddThemeStyleboxOverride("hover", SquareButtonPlate(new Color(1.3f, 1.2f, 1.05f)));
			button.AddThemeStyleboxOverride("focus", SquareButtonPlate(new Color(1.3f, 1.2f, 1.05f)));
			button.AddThemeStyleboxOverride("pressed", SquareButtonPlate(new Color(0.78f, 0.76f, 0.72f)));

			button.Pressed += () => open();
			column.AddChild(button);

			Control icon = art == null
				? new NavRailIcon { Kind = glyph, MouseFilter = Control.MouseFilterEnum.Ignore }
				: new TextureRect
				{
					Texture = GD.Load<Texture2D>($"res://assets/ui/icons/{art}.png"),
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
			button.AddChild(icon);
			// An even inset all round, so the glyph sits centred in the button with room to breathe.
			icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			icon.OffsetLeft = 13;
			icon.OffsetTop = 13;
			icon.OffsetRight = -13;
			icon.OffsetBottom = -13;
		}
	}

	/// <summary>The square button plate. The art is square and so is the button, so it simply
	/// stretches — no nine-slice, nothing to keep in step with the button's size. The tint is what
	/// separates resting from hovered and pressed.</summary>
	private static StyleBoxTexture SquareButtonPlate(Color tint) => new()
	{
		Texture = GD.Load<Texture2D>(SquareButtonPath),
		ModulateColor = tint,
	};

	/// <summary>Reads the played campaign's realms, which of them is yours, and its provinces. Who
	/// holds what and where each seat sits are the campaign's own file; this page only draws it.</summary>
	/// <summary>The lines the campaign's roads are drawn along. The same file the map draws them
	/// from, so the cheap ground an army looks for is exactly the stone the player can see under
	/// it — a second idea of where the roads are would be wrong the first time somebody moved
	/// one.</summary>
	private void LoadRoads()
	{
		var file = GD.Load<Json>(Campaign.Data(RoadsDataFile));
		if (file?.Data.VariantType != Variant.Type.Array)
		{
			return;
		}

		foreach (Variant entry in file.Data.AsGodotArray())
		{
			Godot.Collections.Dictionary road = entry.AsGodotDictionary();
			string from = road["from"].AsString();
			string to = road["to"].AsString();

			// The line the road is drawn along, kept both ways round: an army marching the other way
			// walks the same road, and reversing it at the point of use is how the two drift apart.
			var walked = new List<Vector2>();
			foreach (Variant point in road["points"].AsGodotArray())
			{
				Godot.Collections.Array pair = point.AsGodotArray();
				walked.Add(new Vector2(pair[0].AsSingle(), pair[1].AsSingle()));
			}

			_roadLines[(from, to)] = walked;
			var back = new List<Vector2>(walked);
			back.Reverse();
			_roadLines[(to, from)] = back;
		}
	}

	/// <summary>Cuts the country into ground an army can be marched over: what can be walked, what it
	/// costs, and whose county it is. Built once, off the same height and ID images the map itself is
	/// drawn from, so what the player sees and what his army can cross are the same thing.
	///
	/// A rival's county is shut outright. There is no way to fight for it yet, and ground an army can
	/// walk across but not stop on would be a border it could ignore.</summary>
	private void LayGround()
	{
		LoadRoads();

		Vector2I pixels = _world.MapPixels;
		// The ditches along the borders, and the gaps left in them. Read, not worked out here: the
		// map generator digs the ditch and writes down where it dug it, so the trench a player sees
		// and the line an army cannot cross are the same line.
		Image ditch = GD.Load<Image>(Campaign.Asset(DitchFile));
		_ground = new MarchGrid(pixels.X, pixels.Y);
		_ground.Describe(pixel =>
		{
			float height = _world.HeightAt(pixel);
			if (height <= _world.WaterLine + ShoreClearance)
			{
				return (false, 0f, -1); // the sea, and the sand it breaks on
			}

			if (ditch != null && ditch.GetPixel(
				Mathf.Clamp((int)pixel.X, 0, ditch.GetWidth() - 1),
				Mathf.Clamp((int)pixel.Y, 0, ditch.GetHeight() - 1)).R > 0.5f)
			{
				return (false, 0f, _world.CountyAt(pixel)); // a border ditch; cross at a ford or a road
			}

			// How hard the ground climbs across one cell. A wall of rock is not a road with a price
			// on it, it is somewhere an army does not go.
			float rise = Mathf.Max(
				Mathf.Abs(_world.HeightAt(pixel + (Vector2.Right * MarchGrid.CellSize)) - height),
				Mathf.Abs(_world.HeightAt(pixel + (Vector2.Down * MarchGrid.CellSize)) - height));

			return rise > MarchableRise
				? (false, 0f, _world.CountyAt(pixel))
				: (true, _balance.MarchCostOffRoad, _world.CountyAt(pixel));
		});

		foreach (List<Vector2> road in _roadLines.Values)
		{
			_ground.LayRoad(road, _balance.MarchCostByRoad);
		}

		// A rival's county used to be shut to an army — there was no way to fight for it, so there
		// was no reason to let anybody walk into it. There is one now: the men cross his border, and
		// what happens when they reach his town happens at his town. His ditches still stop them
		// everywhere but at a ford or a road, which is what a ditch is for.
	}

	private void LoadCampaignProvinces()
	{
		var file = GD.Load<Json>(Campaign.Data(ProvincesDataFile));
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Campaign '{Campaign.Folder}' has no readable {ProvincesDataFile}");
			return;
		}

		Godot.Collections.Dictionary data = file.Data.AsGodotDictionary();
		_playerRealm = data["player"].AsString();
		_unclaimedRealm = data["unclaimed"].AsString();
		_rivalWallsUpTo = data.TryGetValue("rivalWallsUpTo", out Variant walls) ? walls.AsString() : "";
		foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> realm in data["realms"].AsGodotDictionary())
		{
			Godot.Collections.Dictionary fields = realm.Value.AsGodotDictionary();
			_realms[realm.Key.AsString()] =
				new RealmData(fields["name"].AsString(), new Color(fields["accent"].AsString()));
		}

		Dictionary<string, Vector2> yards = LoadYards();
		foreach (Variant entry in data["provinces"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			string name = fields["name"].AsString();
			var seat = new Vector2(fields["x"].AsSingle(), fields["y"].AsSingle());
			_provinces.Add(new ProvinceData(
				name,
				seat,
				fields["realm"].AsString(),
				fields.ContainsKey("capital") && fields["capital"].AsBool(),
				fields.TryGetValue("economy", out Variant economyFile) ? economyFile.AsString() : "",
				yards.GetValueOrDefault(name, seat)));
		}
	}

	/// <summary>Where the generator stood each village. Missing — a map generated before it wrote
	/// them — every village simply stands on its seat.</summary>
	private static Dictionary<string, Vector2> LoadYards()
	{
		var yards = new Dictionary<string, Vector2>();
		var file = GD.Load<Json>(Campaign.Data(YardsDataFile));
		if (file?.Data.VariantType != Variant.Type.Array)
		{
			return yards;
		}

		foreach (Variant entry in file.Data.AsGodotArray())
		{
			Godot.Collections.Dictionary yard = entry.AsGodotDictionary();
			yards[yard["province"].AsString()] = new Vector2(yard["x"].AsSingle(), yard["y"].AsSingle());
		}

		return yards;
	}

	// Both ways out of a campaign drop everything since the last save, so neither goes through
	// on a single click. Options isn't in here: it comes back to this same run.
	private void ConfirmLeave(string scenePath)
	{
		_leaveTarget = scenePath;
		_leaveConfirm.Visible = true;
		// The old man asks it out loud while the panel asks it in writing. He speaks over the
		// campaign he is being left, so the line starts with the panel rather than after it.
		Narrator.Say(LeaveFarewellPath);
	}

	/// <summary>True while the other lords' men are walking, after End Turn and before the season:
	/// the map can be looked around but not given orders.</summary>
	private bool _rivalsMarching;

	private FallenPanel _fallen;

	/// <summary>Whether the player has lost his last county, and if so the end of the reign said
	/// over the map. Asked after the other lords have marched and after the season, the two moments
	/// a county can change hands without the player lifting a finger.</summary>
	private bool Fell()
	{
		if (!_turnManager.PlayerFallen)
		{
			return false;
		}

		_fallen.Announce(_turnManager.CurrentSeason, _turnManager.CurrentYear, _turnManager.Turn);
		return true;
	}

	/// <summary>End Turn, the way Lords of the Realm plays it: the other lords take their turn in
	/// front of the player — their banners walk the roads they chose, all at once — and only when
	/// the last of them has halted does the season turn over behind the curtain. What their marches
	/// won or lost is already settled; the banners are redrawn once they have all arrived.</summary>
	private void AdvanceTurn()
	{
		if (_turnTransition.Visible || _rivalsMarching || _fallen.Visible)
		{
			return; // already mid-turn, or the reign is over; a second click must not queue another season
		}

		ShowArmies(); // everybody where they stand, before anybody moves
		List<LordsCampaign.RivalMarch> marches = _turnManager.RivalsTurn();
		if (marches.Count == 0)
		{
			TurnTheSeason();
			return;
		}

		_rivalsMarching = true;
		int walking = marches.Count;
		int longest = 0;
		void Halted()
		{
			if (!_rivalsMarching)
			{
				return; // already settled — by the last banner, or by the deadline below
			}

			_rivalsMarching = false;
			ShowArmies();
			ShowFortifications();
			ShowSettlements();
			LayGround(); // a county they took is somebody else's ground now
			if (!Fell())
			{
				TurnTheSeason();
			}
		}

		foreach (LordsCampaign.RivalMarch march in marches)
		{
			// Stood where they set out from — a company raised this season has no banner yet — and
			// walked from there.
			FieldArmy army = _turnManager.ArmyOf(march.Army);
			if (army != null && army.Strength > 0)
			{
				_world.SetArmy(march.Army, march.From, true,
					_realms.TryGetValue(_turnManager.RealmOf(army), out RealmData lord) ? lord.Accent : Colors.White,
					army.Strength);
			}

			longest = Mathf.Max(longest, march.Road.Count);
			_world.WalkArmy(march.Army, march.Road, () =>
			{
				if (--walking == 0)
				{
					Halted();
				}
			});
		}

		// And settled by the clock if a banner never reports in. One whose figure was taken off the
		// board mid-stride — merged, beaten, retired — takes its walk and its word with it, and the
		// turn sat waiting for a man who no longer existed.
		GetTree().CreateTimer(longest * MapDecoration.StrideSeconds + DeadlineSlack).Timeout += Halted;
	}

	/// <summary>A turn passes behind a curtain: the screen fades out, the season turns over while
	/// nothing is visible, and the new season is named before the map comes back. The simulation
	/// runs at the darkest point, so the numbers never visibly jump under the player's eyes.</summary>
	private void TurnTheSeason()
	{
		_turnTransition.Visible = true;

		Tween tween = CreateTween();
		tween.TweenProperty(_turnTransition, "modulate:a", 1.0, TurnFadeInSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			List<TurnSummary> summaries = _turnManager.AdvanceTurn();
			_turnNote = MenLost(summaries);
			_wallRaised = summaries.Exists(summary => summary.WallRaised.Length > 0);
			UpdateTurnDisplay();
			SelectProvince(_selected != null ? _markers.IndexOf(_selected) : 0);

			// A season's masons may have finished a wall; the map has to say so the moment they do,
			// and it is hidden behind the transition while this happens. The fields turn over with
			// it: what was standing gold in autumn is ploughed earth by winter.
			ShowFortifications();
			ShowSettlements(); // a rival may have taken a county: its banners change hands

			// The fields turn over with the season: what was standing gold in autumn is ploughed
			// earth by winter, and it is redrawn behind the curtain so nobody watches it change.
			ShowFields();
			ShowMercenaries();
			// Men raised, men lost and a rival's men on the march all land in the same turn; the
			// banners are redrawn once here, behind the curtain, rather than by each of the three.
			ShowArmies();

			Season season = _turnManager.CurrentSeason;
			_world.SetSeason(season); // the map turns over here too, while nothing of it is visible
			GetNode<Label>("%TransitionSeason").Text = season.ToString();
			GetNode<Label>("%TransitionTurn").Text = $"Turn {_turnManager.Turn}";
			GetNode<Label>("%TransitionFlavor").Text = season switch
			{
				Season.Spring => "The thaw opens the roads.",
				Season.Summer => "Long days, and the fields stand full.",
				Season.Autumn => "The harvest comes in before the cold.",
				_ => "Snow closes the passes.",
			};
		}));
		tween.TweenInterval(TurnHoldSeconds);
		tween.TweenProperty(_turnTransition, "modulate:a", 0.0, TurnFadeOutSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			_turnTransition.Visible = false;

			// After the curtain, never through it: the lord is told what happened to his county with
			// the county in front of him, so he can see the flooded fields the advisor is describing.
			// A season where nothing happened tells nothing and the panel is never seen. A reign that
			// ended this season is told that, and nothing else.
			if (!Fell())
			{
				_advisor.Tell(_turnManager.News);
			}
		}));
	}

	/// <summary>Men who walked away this season because the treasury could not pay them. Desertion
	/// is the one thing a turn does that the player would otherwise only find by counting his own
	/// garrison twice — it has no recorded line, so it is said plainly instead of not at all.</summary>
	private static string MenLost(System.Collections.Generic.List<TurnSummary> summaries)
	{
		foreach (TurnSummary summary in summaries)
		{
			if (summary.Deserted > 0)
			{
				return $"{summary.Deserted:N0} unpaid men left {summary.ProvinceName}";
			}
		}

		return "";
	}

	/// <summary>Draws what a province digs: its two strongest extractive industries become visible
	/// sites on its ground. Capacity times modifier is the same product the economy pays out on, so
	/// a quarry on the map means quarry income in the ledger, not decoration.
	///
	/// Grain and the herd are not in here. They are worked on fields, and the fields are drawn as
	/// fields — see <see cref="ShowFields()"/> — off what the land is actually under this season,
	/// not off a capacity it might one day use.</summary>
	private void ShowProvinceTrade(ProvinceDefinition definition)
	{
		Vector2 seat = Vector2.Zero;
		foreach (ProvinceData province in _provinces)
		{
			if (province.Name == definition.ProvinceName)
			{
				seat = province.TownPosition;
			}
		}

		var industries = new (MapDecoration.SiteKind Kind, float Weight)[]
		{
			(MapDecoration.SiteKind.Wood, definition.WoodWorkerCapacity * definition.WoodModifier),
			(MapDecoration.SiteKind.Stone, definition.StoneWorkerCapacity * definition.StoneModifier),
			(MapDecoration.SiteKind.Iron, definition.IronWorkerCapacity * definition.IronModifier),
		};

		System.Array.Sort(industries, (left, right) => right.Weight.CompareTo(left.Weight));
		for (int i = 0; i < 2; i++)
		{
			_world.AddSite(seat, industries[i].Kind, industries[i].Weight);
		}
	}

	/// <summary>Lays every described province's fields on its ground — what its land is under this
	/// season, plot by plot.</summary>
	private void ShowFields()
	{
		foreach (ProvinceData province in _provinces)
		{
			ShowFields(province);
		}
	}

	/// <summary>One province's fields. Yours are worked turn by turn and drawn off the economy the
	/// labour room is changing; everyone else's stand as the campaign authored them, because nothing
	/// is simulating them yet. Both read the same array, so the day an unclaimed province starts
	/// taking turns the map already draws what it does with its land.
	///
	/// A province the campaign has not described has no fields to draw, the way it has no industries
	/// to show.</summary>
	private void ShowFields(ProvinceData province)
	{
		if (!_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition))
		{
			// A county the campaign has not written an economy for yet (Frostgate, Icemere Reach). Its
			// people still farm: it is drawn as an ordinary county — the definition's own defaults —
			// until the campaign writes it one. It left those seats with a village and no fields.
			definition = new ProvinceDefinition { ProvinceName = province.Name };
		}

		ProvinceEconomy economy = _turnManager.AnyProvince(province.Name);
		if (economy == null)
		{
			// Nobody is running this county's year, so it has never had a spring and its fields
			// would stand bare for ever. It plainly feeds itself, so it is drawn as a province that
			// sowed — one number, and the day the province starts taking turns its own sowing
			// replaces it.
			economy = ProvinceEconomy.FromDefinition(definition);
			economy.StandingCrop = definition.InitialGrain;
		}

		_world.SetFields(province.Name, province.TownPosition, economy, _turnManager.CurrentSeason);
	}

	/// <summary>Every county's village, yours and everyone else's, flying the colour of whoever holds
	/// it now. Safe to call again: a village already standing only has its banners changed.</summary>
	private void ShowSettlements()
	{
		foreach (ProvinceData province in _provinces)
		{
			_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition);
			_world.AddSettlement(province.Name, province.TownPosition, SettlementFor(definition, province.IsCapital),
				_realms[HolderOf(province)].Accent);
		}
	}

	private BattlePanel.Colours ColoursOf(string realmKey) =>
		_realms.TryGetValue(realmKey, out RealmData realm)
			? new BattlePanel.Colours(realm.Name, realmKey, realm.Accent)
			: new BattlePanel.Colours(realmKey, realmKey, Chrome.Dim);

	/// <summary>Who holds a county now, which after a march is not who the campaign file says. The
	/// authored realm is only the opening position; the runtime one is the answer to "whose is this".</summary>
	private string HolderOf(ProvinceData province)
	{
		string realmKey = _turnManager.AnyProvince(province.Name)?.Realm ?? province.Realm;
		return _realms.ContainsKey(realmKey) ? realmKey : province.Realm;
	}

	/// <summary>How big a place stands on a province's seat: a realm's capital is a town whatever
	/// its land, and the rest are read off the hands that land can work, because land that can work
	/// more hands has more hands living on it. So the map says what a province is worth before the
	/// sidebar does.
	///
	/// The threshold sits inside the authored spread rather than on a round number — every province
	/// on this campaign holds between 230 and 260, and a round 250 would have made them all one
	/// thing, which is no map at all.
	///
	/// Walls are not part of this. A town is what the province grew; a castle is what its lord
	/// built, and that stands beside it and changes as it is built.</summary>
	private static MapDecoration.Settlement SettlementFor(ProvinceDefinition definition, bool capital)
	{
		if (capital)
		{
			return MapDecoration.Settlement.Town;
		}

		if (definition == null)
		{
			// A province this campaign has not written an economy for. A hamlet is the honest guess:
			// it says people live there without claiming a size nobody has decided on.
			return MapDecoration.Settlement.Hamlet;
		}

		int hands = definition.GrainWorkerCapacity + definition.CattleWorkerCapacity
			+ definition.WoodWorkerCapacity + definition.StoneWorkerCapacity
			+ definition.IronWorkerCapacity;

		return hands >= 245 ? MapDecoration.Settlement.Town : MapDecoration.Settlement.Hamlet;
	}

	/// <summary>Puts a banner on every county that has men standing in it, whoever holds it. An army
	/// is the one thing on this map worth seeing from across the realm, and a rival's is worth seeing
	/// most of all.</summary>
	private void ShowArmies()
	{
		var standing = new HashSet<string>();
		foreach (FieldArmy army in _turnManager.Armies())
		{
			if (army.Strength == 0)
			{
				continue;
			}

			string realm = _turnManager.RealmOf(army);
			standing.Add(army.Key);

			// Where the men actually are, which after a march is a hillside and not a market square.
			_world.SetArmy(army.Key, ArmyPixel(army), true,
				_realms.TryGetValue(realm, out RealmData lord) ? lord.Accent : Colors.White, army.Strength);
		}

		// A company merged into another, disbanded or killed to the last man leaves a banner behind
		// otherwise, and a banner with nobody under it is one the lord will try to give orders to.
		_world.RetireArmies(standing);
	}

	/// <summary>Hangs the hire-mark over every county with a company standing in it this season.
	/// This is the whole announcement: no panel, no voice, nothing to dismiss — a lord glancing at
	/// his realm sees where there are men to be had, and goes there if he wants them.</summary>
	private void ShowMercenaries()
	{
		var mark = GD.Load<Texture2D>(MercenaryBadgePath);
		for (int index = 0; index < _provinces.Count && index < _markers.Count; index++)
		{
			// Only his own counties: nobody is offering a company to a lord who does not hold the
			// ground, and a mark over a rival's seat would be an offer he cannot take.
			ProvinceEconomy economy = _turnManager.GetProvince(_provinces[index].Name);
			_markers[index].ShowBadge(Mercenaries.Standing(economy) != null ? mark : null);
		}
	}

	/// <summary>Puts every province's walls on the map as the ledger has them. Called once the world
	/// is built and again after each turn, because a build that finished this season has to show up
	/// on the ground the moment it does.</summary>
	/// <summary>The player's walls this company could go up onto: the county it is standing in, if
	/// that is his, walled, not under siege and not full. Null otherwise.
	/// ponytail: anywhere in the county counts as at the gate, not only the seat's square; hold them
	/// to TownPosition if a march to the far corner of a county starts reading as a cheat.</summary>
	private ProvinceEconomy WallsFor(FieldArmy army)
	{
		ProvinceEconomy walls = _turnManager.GetProvince(army.County);
		return _turnManager.RealmOf(army) == _playerRealm && walls is { WallRoom: > 0, BesiegedFrom.Length: 0 }
			? walls
			: null;
	}

	/// <summary>Which of this company go up onto the walls of the county it stands in, kind by kind
	/// on the panel a split is cut on, filled up to the room there is.</summary>
	private void AskWalls(FieldArmy army)
	{
		ProvinceEconomy walls = WallsFor(army);
		if (walls != null)
		{
			_splitting.Ask(army, going => Garrisoned(army, going),
				SplitPanel.Garrisoning(walls.ProvinceName, walls.WallRoom));
		}
	}

	private void Garrisoned(FieldArmy army, Dictionary<string, int> going)
	{
		string county = army.County;
		int up = _turnManager.Garrison(army, going);
		ShowArmies();
		ShowFortifications(); // men on the walls, so the flag goes up
		_sidebar.Refresh();
		ShowSaveToast($"{up:N0} men go up onto the walls of {county}");
	}

	private void ShowFortifications()
	{
		foreach (ProvinceData province in _provinces)
		{
			// Everyone's walls, not just the player's: a rival raising a castle is the one thing about
			// his county a lord could hardly miss from the next valley over.
			ProvinceEconomy economy = _turnManager.AnyProvince(province.Name);
			_world.SetFortification(province.Name, province.TownPosition, economy?.Fortification ?? "",
				economy?.Building ?? "",
				_realms[HolderOf(province)].Accent, economy is { CastleMen: > 0 });
		}
	}

	/// <summary>The briefing a campaign opens on: what you hold, and what to do before you end your
	/// first turn. It borrows the rail's own panel rather than standing up a second one in the same
	/// place in the same frame, and closes by the same button.
	///
	/// The voice is optional. Until it is recorded the briefing simply reads itself.</summary>
	private void OpenBriefing()
	{
		_sectionTitle.Text = $"{_turnManager.CurrentSeason}, {_turnManager.CurrentYear}";
		_sectionBody.Text = Briefing;
		_sectionPanel.Visible = true;

		Narrator.Say(OpeningVoicePath);
	}

	// A save is instant and silent otherwise: the toast holds long enough to be read, then
	// clears itself so nothing stays parked over the map.
	private void ShowSaveToast(string message)
	{
		var toast = GetNode<Control>("%SaveToast");
		var label = GetNode<Label>("%SaveToastLabel");
		label.Text = message;

		Tween tween = CreateTween();
		tween.TweenProperty(toast, "modulate:a", 1.0, ToastFadeInSeconds);
		tween.TweenInterval(ToastHoldSeconds);
		tween.TweenProperty(toast, "modulate:a", 0.0, ToastFadeOutSeconds);
	}

	// Each rail destination is its own screen that doesn't exist yet, so the rail opens a shell
	/// <summary>Opens the hall of lords over the map. It reads the whole realm rather than the
	/// province in hand, so it takes the turn manager and not one economy.
	///
	/// It opens over the map for now, the way the rooms do. If it becomes the screen the campaign is
	/// actually played from, this is the call that turns around: the map opens from the hall's
	/// Province Affairs door instead.</summary>
	private void OpenHall()
	{
		if (_hall != null)
		{
			return;
		}

		_hall = GD.Load<PackedScene>(HallScenePath).Instantiate<HallPage>();
		AddChild(_hall);
		_hall.Open(_turnManager);

		_hall.Closed += () =>
		{
			_hall.QueueFree();
			_hall = null;
		};

		// The doors are named but most of them open onto nothing yet. The two that do lead
		// somewhere go there; the rest say so rather than doing nothing at all.
		_hall.DoorChosen += door =>
		{
			switch (door)
			{
				case "army": OpenRoom(RecruitsScenePath, "raise men"); break;
				case "treasury": OpenRoom(MarketScenePath, "trade"); break;
				// Walls are what developing a province means so far, so Province Affairs opens onto
				// them. It becomes a door of its own once there is more than one thing behind it.
				case "provinces": OpenRoom(FortificationsScenePath, "build"); break;
				default: ShowSaveToast($"{door} is not built yet"); break;
			}
		};

		_hall.TurnEnded += () =>
		{
			AdvanceTurn();
			_hall?.Refresh();
		};
	}

	// naming what belongs there. Replace a case with a real panel as that system gets built.
	private void ShowSection(NavRail.Section section)
	{
		// Three of them open onto a room of the province in hand rather than onto a panel of text.
		// The hammer is the smithy, not the town. The town is reached the way you would reach it —
		// by walking into it off the map, with a second press on the seat already in hand.
		if (section == NavRail.Section.Buildings)
		{
			OpenRoom(BlacksmithScenePath, "forge");
			return;
		}

		if (section == NavRail.Section.Military)
		{
			OpenRoom(RecruitsScenePath, "raise men");
			return;
		}

		if (section == NavRail.Section.Fortifications)
		{
			OpenRoom(FortificationsScenePath, "build");
			return;
		}

		if (section == NavRail.Section.Trade)
		{
			OpenRoom(MarketScenePath, "trade");
			return;
		}

		if (section == NavRail.Section.Court)
		{
			OpenHall();
			return;
		}

		_sectionTitle.Text = section switch
		{
			NavRail.Section.Fortifications => "Fortifications",
			NavRail.Section.Military => "Military",
			NavRail.Section.Buildings => "Buildings",
			NavRail.Section.Court => "Court",
			_ => "Trade",
		};
		_sectionBody.Text = section switch
		{
			NavRail.Section.Fortifications => "The walls of every province, and what they cost to raise. Not built yet.",
			NavRail.Section.Military => "Every army you command, where it stands and what it costs. Not built yet.",
			NavRail.Section.Buildings => "What each province has raised, and what it can raise next. Not built yet.",
			NavRail.Section.Court => "Your lords, advisors and heirs. Not built yet.",
			_ => "What the realm buys, sells and ships, and at what price. Not built yet.",
		};
		_sectionPanel.Visible = true;
	}

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

	private void OnMapGuiInput(InputEvent @event)
	{
		_world.HandleInput(@event); // pan and zoom; selection is the left button, below
		if (_rivalsMarching)
		{
			return; // the other lords are moving: look, but give no orders until they have
		}

		if (@event is InputEventMouseMotion motion)
		{
			int hovered = _world.ProvinceAt(motion.Position);
			if (hovered != _hovered)
			{
				_hovered = hovered;
				_world.SetHighlight(_selected != null ? _markers.IndexOf(_selected) : -1, _hovered);
			}

			if (_marching)
			{
				ShowMarchCost(motion.Position);
			}

			return;
		}

		// The right button asks about a thing rather than doing something with it: a company under
		// the pointer is opened and told in full, whoever it belongs to.
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } asked
			&& !_marching && _world.TryMapPixel(asked.Position, out Vector2 under))
		{
			FieldArmy company = _turnManager.ArmyOf(_world.ArmyAt(under));
			if (company is { Strength: > 0 })
			{
				string realmKey = _turnManager.RealmOf(company);
				_army.Show(company, _turnManager.AnyProvince(company.Home), _balance,
					_realms.TryGetValue(realmKey, out RealmData lord) ? lord.Name : realmKey, realmKey,
					realmKey == _playerRealm);
				_army.OfferWalls(WallsFor(company) != null);
			}

			return;
		}

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click)
		{
			int index = _world.ProvinceAt(click.Position);
			if (_marching)
			{
				FieldArmy sent = _marchingArmy;
				LayDownArmy();
				if (March(sent, click.Position))
				{
					return;
				}
			}

			// A banner is taken hold of before the county under it: a lord jabbing at his own army
			// means the army, not the ground it happens to be standing on.
			if (!_marching && _world.TryMapPixel(click.Position, out Vector2 ground))
			{
				FieldArmy banner = _turnManager.ArmyOf(_world.ArmyAt(ground));
				if (banner != null && _turnManager.RealmOf(banner) == _playerRealm)
				{
					int stands = _provinces.FindIndex(province => province.Name == banner.County);
					SelectProvince(stands >= 0
						? stands
						: _provinces.FindIndex(province => province.Name == banner.Home));
					TakeUpArmy(banner);
					return;
				}
			}

			if (index >= 0 && index < _provinces.Count)
			{
				// The first press is how you look a province over. After that the ground answers for
				// itself: a press on one of the county's own fields opens that field, and a press on
				// the village opens the town. Everywhere else — its woods, its hills, the road between
				// them — is ground, and a lord pointing at it is not asking to be taken indoors.
				if (_selected != null && _markers.IndexOf(_selected) == index)
				{
					if (!OpenField(index, click.Position) && _world.TryMapPixel(click.Position, out Vector2 streets)
						&& _world.TownAt(streets) == _provinces[index].Name)
					{
						OpenCity();
					}
				}
				else
				{
					SelectProvince(index);
				}
			}
		}
	}

	private void SelectProvince(int index)
	{
		_selected?.SetSelected(false);
		ProvinceMarker marker = _markers[index];
		marker.SetSelected(true);
		_selected = marker;

		_world.SetHighlight(index, _hovered);

		ProvinceData province = _provinces[index];

		string realmKey = HolderOf(province);

		string realmName = _realms[realmKey].Name;
		_markers[index].Configure(_realms[realmKey].Accent, province.IsCapital);

		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy != null)
		{
			UpdateResourceBar(economy);
		}

		// The sidebar takes the province whether or not anyone runs it: an unclaimed one still has a
		// name, a crest and the land under it, it just has no numbers of its own to show.
		_sidebar.ShowHeader(province.Name, realmName, realmKey, _realms[realmKey].Accent);
		_sidebar.ShowEconomy(economy, _definitionsByName.GetValueOrDefault(province.Name),
			_balance, _turnManager.CurrentSeason);
	}

	// Markers are 2D art pinned to 3D ground, so every frame the camera moves they have to be
	// re-projected; one unproject per province is cheaper than tracking whether it moved.
	public override void _Process(double delta)
	{
		if (_trailSteps.Count > 0)
		{
			ProjectTrail();
		}

		for (int i = 0; i < _provinces.Count; i++)
		{
			bool onScreen = _world.TryScreenPosition(_provinces[i].MapPosition, out Vector2 screenPosition);
			_markers[i].Visible = onScreen;
			if (onScreen)
			{
				_markers[i].Position = screenPosition - _markers[i].Size / 2f;
				_markers[i].SetBadgeScale(_world.Closeness);
			}
		}
	}
}
