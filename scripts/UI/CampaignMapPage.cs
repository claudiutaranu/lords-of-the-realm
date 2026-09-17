using System.Collections.Generic;
using Godot;

/// <summary>
/// Phase 1 of the campaign: a clickable map of the Royal Crown vs. Northern Watch frontier,
/// an End Turn counter, and a TurnManager-driven economy for the player's 5 provinces.
/// Northern Watch's 3 provinces sit inert until there's AI to run them (Phase 4). No
/// armies or combat yet.
///
/// The map itself is 3D (CampaignMap3D): a displaced terrain mesh the player pans and zooms.
/// This page owns the UI over it and the 2D province markers, which are re-projected from the
/// camera every frame. Province identity still comes from campaign-map-ids.png, an unseen image
/// of the same layout where each pixel's red channel is its province index + 1 (0 = water) — the
/// standard technique this genre uses (Paradox's province bitmaps work the same way), here read
/// at whatever point the click raycast lands on.
/// </summary>
public partial class CampaignMapPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string LoadGameScenePath = "res://scene/load-game/load_game.tscn";
	private const string OptionsScenePath = "res://scene/options/options.tscn";
	private const string SelfScenePath = "res://scene/campaign-map/campaign_map.tscn";
	// The only campaign with a map; a save records it so the load list can name what it opens.
	private const string CampaignName = "The Royal Crown";
	private const float TurnFadeInSeconds = 0.4f;
	private const float TurnHoldSeconds = 1.1f;
	private const float TurnFadeOutSeconds = 0.5f;
	private const float ToastFadeInSeconds = 0.15f;
	private const float ToastHoldSeconds = 1.6f;
	private const float ToastFadeOutSeconds = 0.6f;

	private enum Realm { RoyalCrown, NorthernWatch }

	private record ProvinceData(string Name, Vector2 MapPosition, Realm Owner, bool IsCapital, string[] Neighbors);

	private static readonly Color RoyalCrownColor = new("b23a3a");
	private static readonly Color NorthernWatchColor = new("5f8fc9");

	// Royal Crown's 5 provinces get simulated economies; Northern Watch's 3 sit inert until
	// there's AI to run them (Phase 4) — file names double as ProvinceDefinition.ProvinceName lookups.
	private static readonly string[] PlayerProvinceDataFiles =
		{ "kingsreach", "redmoor-hold", "ashenvale", "thornwatch", "farrowmere" };

	// Pixel coordinates on the map (same 1536x1024 canvas as the ID map), and the adjacency the
	// generator computed from which provinces actually touch — order matters, it's also the ID map's
	// index+1 encoding.
	private static readonly ProvinceData[] Provinces =
	{
		new("Kingsreach", new Vector2(365, 485), Realm.RoyalCrown, true, new[] { "Redmoor Hold", "Ashenvale", "Thornwatch" }),
		new("Redmoor Hold", new Vector2(295, 175), Realm.RoyalCrown, false, new[] { "Kingsreach", "Ashenvale" }),
		new("Ashenvale", new Vector2(565, 385), Realm.RoyalCrown, false, new[] { "Kingsreach", "Redmoor Hold", "Thornwatch", "Valmere", "Icemere Reach" }),
		new("Thornwatch", new Vector2(625, 655), Realm.RoyalCrown, false, new[] { "Kingsreach", "Ashenvale", "Farrowmere", "Icemere Reach" }),
		new("Farrowmere", new Vector2(900, 885), Realm.RoyalCrown, false, new[] { "Thornwatch", "Icemere Reach" }),
		new("Valmere", new Vector2(1095, 105), Realm.NorthernWatch, true, new[] { "Ashenvale", "Frostgate", "Icemere Reach" }),
		new("Frostgate", new Vector2(1260, 290), Realm.NorthernWatch, false, new[] { "Valmere", "Icemere Reach" }),
		new("Icemere Reach", new Vector2(1200, 485), Realm.NorthernWatch, false, new[] { "Ashenvale", "Thornwatch", "Farrowmere", "Valmere", "Frostgate" }),
	};

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
	private ColorRect _sidebarBanner;
	private Label _sidebarName;
	private Label _sidebarPopulation;
	private Label _sidebarLoyalty;
	private Label _sidebarTax;
	private Label _sidebarRation;
	private Control _leaveConfirm;
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
		_sidebarBanner = GetNode<ColorRect>("%SidebarBanner");
		_sidebarName = GetNode<Label>("%SidebarProvinceName");
		_sidebarPopulation = GetNode<Label>("%SidebarPopulationValue");
		_sidebarLoyalty = GetNode<Label>("%SidebarLoyaltyValue");
		_sidebarTax = GetNode<Label>("%SidebarTaxValue");
		_sidebarRation = GetNode<Label>("%SidebarRationValue");
		GoldTitle.Apply(_infoName);
		// Only the date reads gilded; the stockpile numbers stay cream so they carry at a glance
		// against the dark bar.
		foreach (string valueName in new[] { "SeasonValue", "TurnValue" })
		{
			GoldTitle.Apply(GetNode<Label>($"%{valueName}"));
		}

		_balance = GD.Load<GameBalance>("res://data/game-balance.tres");
		var definitions = new List<ProvinceDefinition>();
		foreach (string fileName in PlayerProvinceDataFiles)
		{
			var definition = GD.Load<ProvinceDefinition>($"res://data/provinces/{fileName}.tres");
			definitions.Add(definition);
			_definitionsByName[definition.ProvinceName] = definition;
		}
		_turnManager = new TurnManager(_balance, definitions);
		if (SaveGame.Pending != null)
		{
			_turnManager.Restore(SaveGame.Pending.Turn, SaveGame.Pending.Provinces);
			SaveGame.Pending = null; // consumed, so starting a fresh campaign later doesn't reopen it
		}

		UpdateTurnDisplay();

		var infoPanel = GetNode<Control>("InfoPanel");
		infoPanel.OffsetTop = -320f;
		_economyPanel = new ProvinceEconomyPanel { Visible = false };
		GetNode<VBoxContainer>("InfoPanel/InfoContent").AddChild(_economyPanel);

		_map.GuiInput += OnMapGuiInput;

		Control markers = GetNode<Control>("%Markers");
		foreach (ProvinceData province in Provinces)
		{
			var marker = new ProvinceMarker();
			markers.AddChild(marker);
			marker.Configure(province.Owner == Realm.RoyalCrown ? RoyalCrownColor : NorthernWatchColor, province.IsCapital);
			_markers.Add(marker);
		}

		_turnTransition = GetNode<Control>("%TurnTransition");
		GoldTitle.Apply(GetNode<Label>("%TransitionSeason"));
		GetNode<Button>("%EndTurnButton").Pressed += AdvanceTurn;
		_sectionPanel = GetNode<Control>("%SectionPanel");
		_sectionTitle = GetNode<Label>("%SectionTitle");
		_sectionBody = GetNode<Label>("%SectionBody");
		GoldTitle.Apply(_sectionTitle);
		GetNode<NavRail>("%NavRail").SectionChosen += ShowSection;
		GetNode<Button>("%SectionClose").Pressed += () => _sectionPanel.Visible = false;

		// The crest is the pause menu: save, load, or leave the campaign.
		var gameMenu = GetNode<Control>("%GameMenu");
		GetNode<Button>("%MenuShieldButton").Pressed += () => gameMenu.Visible = !gameMenu.Visible;
		GetNode<Button>("%SaveButton").Pressed += () =>
		{
			SaveGame.Write(CampaignName, _turnManager.Turn, _turnManager.Provinces);
			gameMenu.Visible = false; // out of the way, so the confirmation lands on the map itself
			ShowSaveToast($"Saved · Turn {_turnManager.Turn}");
		};
		GetNode<Button>("%ResumeButton").Pressed += () => gameMenu.Visible = false;
		GetNode<Button>("%LoadButton").Pressed += () => ConfirmLeave(LoadGameScenePath);
		_leaveConfirm = GetNode<Control>("%LeaveConfirm");
		GetNode<Button>("%LeaveCancelButton").Pressed += () => _leaveConfirm.Visible = false;
		GetNode<Button>("%LeaveConfirmButton").Pressed += () => SceneRouter.GoTo(this, _leaveTarget);
		GetNode<Button>("%OptionsButton").Pressed += () =>
		{
			// Options is its own scene, so the running campaign rides along in memory rather
			// than through a file, and comes back when Back returns here.
			SaveGame.Pending = SaveGame.Snapshot(CampaignName, _turnManager.Turn, _turnManager.Provinces);
			OptionsPage.ReturnScenePath = SelfScenePath;
			SceneRouter.GoTo(this, OptionsScenePath);
		};
		GetNode<Button>("%QuitButton").Pressed += () => ConfirmLeave(MainMenuScenePath);

		SelectProvince(0); // Kingsreach, the capital — shows something real before any click.
	}

	// Both ways out of a campaign drop everything since the last save, so neither goes through
	// on a single click. Options isn't in here: it comes back to this same run.
	private void ConfirmLeave(string scenePath)
	{
		_leaveTarget = scenePath;
		_leaveConfirm.Visible = true;
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
			_ => "Battles",
		};
		_sectionBody.Text = section switch
		{
			NavRail.Section.Chronicle => "A running record of the realm's events, turn by turn. Not built yet.",
			NavRail.Section.Military => "Every army you command, where it stands and what it costs. Not built yet.",
			NavRail.Section.Buildings => "What each province has raised, and what it can raise next. Not built yet.",
			NavRail.Section.Court => "Your lords, advisors and heirs. Not built yet.",
			_ => "Sieges and field battles, past and pending. Not built yet.",
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
			if (index >= 0 && index < Provinces.Length)
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

		ProvinceData province = Provinces[index];
		_infoName.Text = province.Name;
		string realmName = province.Owner == Realm.RoyalCrown ? "The Royal Crown" : "The Northern Watch";
		string ownerLine = province.IsCapital ? $"{realmName} · Capital" : realmName;

		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		_infoMeta.Text = economy != null ? $"{ownerLine} · Population {economy.Population:N0}" : ownerLine;

		_economyPanel.Visible = economy != null;
		if (economy != null)
		{
			_economyPanel.Configure(economy, _definitionsByName[province.Name], _balance, _turnManager.CurrentSeason);
			UpdateResourceBar(economy);
		}

		_sidebarBanner.Color = province.Owner == Realm.RoyalCrown ? RoyalCrownColor : NorthernWatchColor;
		_sidebarName.Text = province.Name;
		_sidebarPopulation.Text = economy != null ? economy.Population.ToString("N0") : "-";
		_sidebarLoyalty.Text = economy != null ? Mathf.RoundToInt(economy.Loyalty).ToString() : "-";
		_sidebarTax.Text = economy != null ? $"Tax {economy.Tax}" : "Tax -";
		_sidebarRation.Text = economy != null ? $"Ration {economy.Ration}" : "Ration -";
	}

	// Markers are 2D art pinned to 3D ground, so every frame the camera moves they have to be
	// re-projected; one unproject per province is cheaper than tracking whether it moved.
	public override void _Process(double delta)
	{
		for (int i = 0; i < Provinces.Length; i++)
		{
			bool onScreen = _world.TryScreenPosition(Provinces[i].MapPosition, out Vector2 screenPosition);
			_markers[i].Visible = onScreen;
			if (onScreen)
			{
				_markers[i].Position = screenPosition - _markers[i].Size / 2f;
			}
		}
	}
}
