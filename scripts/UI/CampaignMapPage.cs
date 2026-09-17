using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Phase 1 of the campaign: a clickable map of two realms' frontier, an End Turn counter, and a
/// TurnManager-driven economy for the provinces the player holds. The other realm's provinces sit
/// inert until there's AI to run them (Phase 4). No armies or combat yet.
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
	private const string SquareButtonPath = "res://assets/ui/button-square.png";
	private const string ProvincesDataFile = "provinces.json";
	private const float TurnFadeInSeconds = 0.4f;
	private const float TurnHoldSeconds = 1.1f;
	private const float TurnFadeOutSeconds = 0.5f;
	private const float ToastFadeInSeconds = 0.15f;
	private const float ToastHoldSeconds = 1.6f;
	private const float ToastFadeOutSeconds = 0.6f;

	/// <summary>A realm as the campaign describes it: what it is called, and the colour everything
	/// belonging to it is drawn in.</summary>
	private record RealmData(string Name, Color Accent);

	/// <summary>One province of the played campaign. <paramref name="Realm"/> is who holds it when
	/// the campaign opens, which for most of them is nobody. <paramref name="EconomyFile"/> names the
	/// ProvinceDefinition describing its land, and is empty where none is authored yet.</summary>
	private record ProvinceData(string Name, Vector2 MapPosition, string Realm, bool IsCapital, string EconomyFile);

	private readonly Dictionary<string, RealmData> _realms = new();
	// Which realm is yours. The others' provinces, and the unclaimed ones, run on nobody's orders yet.
	private string _playerRealm = "";
	// Authored order is load-bearing: it is the ID map's index + 1 encoding, so a province's place
	// in provinces.json is what ties it to its pixels on the map.
	private readonly List<ProvinceData> _provinces = new();

	private SubViewportContainer _map;
	private CampaignMap3D _world;
	private int _hovered = -1;
	private Label _infoName;
	private Label _infoMeta;
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
	private ProvinceEconomyPanel _economyPanel;
	private Label _goldLabel;
	private Label _grainLabel;
	private Label _woodLabel;
	private Label _stoneLabel;
	private Label _ironLabel;
	private Label _populationLabel;
	private ProvinceSidebar _sidebar;
	private Control _leaveConfirm;
	private AudioStreamPlayer _leaveFarewell;
	private string _leaveTarget;
	private Control _turnTransition;

	public override void _Ready()
	{
		_map = GetNode<SubViewportContainer>("%Map");
		_world = GetNode<CampaignMap3D>("%World");
		_infoName = GetNode<Label>("%InfoName");
		_infoMeta = GetNode<Label>("%InfoMeta");
		_turnLabel = GetNode<Label>("%TurnValue");
		_seasonLabel = GetNode<Label>("%SeasonValue");
		_goldLabel = GetNode<Label>("%GoldValue");
		_grainLabel = GetNode<Label>("%GrainValue");
		_woodLabel = GetNode<Label>("%WoodValue");
		_stoneLabel = GetNode<Label>("%StoneValue");
		_ironLabel = GetNode<Label>("%IronValue");
		_populationLabel = GetNode<Label>("%PopulationValue");
		_sidebar = GetNode<ProvinceSidebar>("%ProvinceSidebar");
		GoldTitle.Apply(_infoName);
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
		// definition and a province being yours are two different things: the land is described
		// either way — that is what puts its industries on the map — but only what your own realm
		// holds is simulated. Taking an unclaimed province is what will hand its definition over.
		var playerDefinitions = new List<ProvinceDefinition>();
		foreach (ProvinceData province in _provinces)
		{
			if (province.EconomyFile.Length == 0)
			{
				continue; // no economy authored for it yet
			}

			var definition = GD.Load<ProvinceDefinition>(Campaign.Data($"provinces/{province.EconomyFile}.tres"));
			_definitionsByName[definition.ProvinceName] = definition;
			if (province.Realm == _playerRealm)
			{
				playerDefinitions.Add(definition);
			}
		}

		_turnManager = new TurnManager(_balance, playerDefinitions);
		if (SaveGame.Pending != null)
		{
			_turnManager.Restore(SaveGame.Pending.Turn, SaveGame.Pending.Provinces);
			SaveGame.Pending = null; // consumed, so starting a fresh campaign later doesn't reopen it
		}

		UpdateTurnDisplay();

		var infoPanel = GetNode<Control>("InfoPanel");
		infoPanel.OffsetTop = -320f;
		_economyPanel = new ProvinceEconomyPanel { Visible = false };
		_economyPanel.WorkersMoved += _sidebar.Refresh;
		GetNode<VBoxContainer>("InfoPanel/InfoContent").AddChild(_economyPanel);

		_map.GuiInput += OnMapGuiInput;

		Control markers = GetNode<Control>("%Markers");
		foreach (ProvinceData province in _provinces)
		{
			var marker = new ProvinceMarker();
			markers.AddChild(marker);
			marker.Configure(_realms[province.Realm].Accent, province.IsCapital);
			_markers.Add(marker);
		}

		_turnTransition = GetNode<Control>("%TurnTransition");
		GoldTitle.Apply(GetNode<Label>("%TransitionSeason"));
		GetNode<Button>("%EndTurnButton").Pressed += AdvanceTurn;
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
			SaveGame.Write(Campaign.Name, _turnManager.Turn, _turnManager.Provinces);
			gameMenu.Visible = false; // out of the way, so the confirmation lands on the map itself
			ShowSaveToast($"Saved · Turn {_turnManager.Turn}");
		};
		GetNode<Button>("%ResumeButton").Pressed += () => gameMenu.Visible = false;
		GetNode<Button>("%LoadButton").Pressed += () => ConfirmLeave(LoadGameScenePath);
		_leaveConfirm = GetNode<Control>("%LeaveConfirm");
		_leaveFarewell = new AudioStreamPlayer
		{
			Stream = GD.Load<AudioStreamMP3>(LeaveFarewellPath),
			Bus = Settings.SfxBus,
		};
		AddChild(_leaveFarewell);
		GetNode<Button>("%LeaveCancelButton").Pressed += () =>
		{
			_leaveConfirm.Visible = false;
			_leaveFarewell.Stop(); // staying: he doesn't get to finish the farewell
		};
		GetNode<Button>("%LeaveConfirmButton").Pressed += () => SceneRouter.GoTo(this, _leaveTarget);
		GetNode<Button>("%OptionsButton").Pressed += () =>
		{
			// Options is its own scene, so the running campaign rides along in memory rather
			// than through a file, and comes back when Back returns here.
			SaveGame.Pending = SaveGame.Snapshot(Campaign.Name, _turnManager.Turn, _turnManager.Provinces);
			OptionsPage.ReturnScenePath = SelfScenePath;
			SceneRouter.GoTo(this, OptionsScenePath);
		};
		GetNode<Button>("%QuitButton").Pressed += () => ConfirmLeave(MainMenuScenePath);

		// Every described province, not only yours: an unclaimed quarry is still a quarry.
		foreach (ProvinceDefinition definition in _definitionsByName.Values)
		{
			ShowProvinceTrade(definition);
		}

		// After the sites are placed, so a loaded save's crops open in their own season rather than
		// being sown green and repainted a frame later.
		_world.SetSeason(_turnManager.CurrentSeason);

		SelectProvince(0); // Kingsreach, the capital — shows something real before any click.
	}

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
			(NavRailIcon.Glyph.Book, "scroll", "Chronicle", () => ShowSection(NavRail.Section.Chronicle)),
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
		foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> realm in data["realms"].AsGodotDictionary())
		{
			Godot.Collections.Dictionary fields = realm.Value.AsGodotDictionary();
			_realms[realm.Key.AsString()] =
				new RealmData(fields["name"].AsString(), new Color(fields["accent"].AsString()));
		}

		foreach (Variant entry in data["provinces"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			_provinces.Add(new ProvinceData(
				fields["name"].AsString(),
				new Vector2(fields["x"].AsSingle(), fields["y"].AsSingle()),
				fields["realm"].AsString(),
				fields.ContainsKey("capital") && fields["capital"].AsBool(),
				fields.TryGetValue("economy", out Variant economyFile) ? economyFile.AsString() : ""));
		}
	}

	// Both ways out of a campaign drop everything since the last save, so neither goes through
	// on a single click. Options isn't in here: it comes back to this same run.
	private void ConfirmLeave(string scenePath)
	{
		_leaveTarget = scenePath;
		_leaveConfirm.Visible = true;
		// The old man asks it out loud while the panel asks it in writing. He speaks over the
		// campaign he is being left, so the line starts with the panel rather than after it.
		_leaveFarewell.Play();
	}

	/// <summary>A turn passes behind a curtain: the screen fades out, the season turns over while
	/// nothing is visible, and the new season is named before the map comes back. The simulation
	/// runs at the darkest point, so the numbers never visibly jump under the player's eyes.</summary>
	private void AdvanceTurn()
	{
		if (_turnTransition.Visible)
		{
			return; // already mid-turn; a second click must not queue another season
		}

		_turnTransition.Visible = true;

		Tween tween = CreateTween();
		tween.TweenProperty(_turnTransition, "modulate:a", 1.0, TurnFadeInSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			_turnManager.AdvanceTurn();
			UpdateTurnDisplay();
			SelectProvince(_selected != null ? _markers.IndexOf(_selected) : 0);

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
		tween.TweenCallback(Callable.From(() => _turnTransition.Visible = false));
	}

	/// <summary>Draws what a province lives on: its two strongest industries become visible sites
	/// on its ground. Capacity times modifier is the same product the economy pays out on, so a
	/// quarry on the map means quarry income in the ledger, not decoration.</summary>
	private void ShowProvinceTrade(ProvinceDefinition definition)
	{
		Vector2 seat = Vector2.Zero;
		foreach (ProvinceData province in _provinces)
		{
			if (province.Name == definition.ProvinceName)
			{
				seat = province.MapPosition;
			}
		}

		var industries = new (MapDecoration.SiteKind Kind, float Weight)[]
		{
			(MapDecoration.SiteKind.Grain, definition.GrainWorkerCapacity * definition.GrainModifier),
			(MapDecoration.SiteKind.Cattle, definition.CattleWorkerCapacity * definition.CattleModifier),
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
	// naming what belongs there. Replace a case with a real panel as that system gets built.
	private void ShowSection(NavRail.Section section)
	{
		_sectionTitle.Text = section switch
		{
			NavRail.Section.Chronicle => "Chronicle",
			NavRail.Section.Military => "Military",
			NavRail.Section.Buildings => "Buildings",
			NavRail.Section.Court => "Court",
			_ => "Trade",
		};
		_sectionBody.Text = section switch
		{
			NavRail.Section.Chronicle => "A running record of the realm's events, turn by turn. Not built yet.",
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

		if (@event is InputEventMouseMotion motion)
		{
			int hovered = _world.ProvinceAt(motion.Position);
			if (hovered != _hovered)
			{
				_hovered = hovered;
				_world.SetHighlight(_selected != null ? _markers.IndexOf(_selected) : -1, _hovered);
			}

			return;
		}

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click)
		{
			int index = _world.ProvinceAt(click.Position);
			if (index >= 0 && index < _provinces.Count)
			{
				SelectProvince(index);
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
		_infoName.Text = province.Name;
		string realmName = _realms[province.Realm].Name;
		string ownerLine = province.IsCapital ? $"{realmName} · Capital" : realmName;

		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		_infoMeta.Text = economy != null ? $"{ownerLine} · Population {economy.Population:N0}" : ownerLine;

		_economyPanel.Visible = economy != null;
		if (economy != null)
		{
			_economyPanel.Configure(economy, _definitionsByName[province.Name], _balance, _turnManager.CurrentSeason);
			UpdateResourceBar(economy);
		}

		// The sidebar takes the province whether or not anyone runs it: an unclaimed one still has a
		// name, a crest and the land under it, it just has no numbers of its own to show.
		_sidebar.ShowHeader(province.Name, realmName, province.Realm, _realms[province.Realm].Accent);
		_sidebar.ShowEconomy(economy, _definitionsByName.GetValueOrDefault(province.Name),
			_balance, _turnManager.CurrentSeason);
	}

	// Markers are 2D art pinned to 3D ground, so every frame the camera moves they have to be
	// re-projected; one unproject per province is cheaper than tracking whether it moved.
	public override void _Process(double delta)
	{
		for (int i = 0; i < _provinces.Count; i++)
		{
			bool onScreen = _world.TryScreenPosition(_provinces[i].MapPosition, out Vector2 screenPosition);
			_markers[i].Visible = onScreen;
			if (onScreen)
			{
				_markers[i].Position = screenPosition - _markers[i].Size / 2f;
			}
		}
	}
}
