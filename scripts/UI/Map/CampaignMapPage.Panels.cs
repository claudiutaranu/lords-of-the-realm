using System.Collections.Generic;
using Godot;

/// <summary>The panels raised over the map when the page opens, and wired to what opens them: the
/// county's own, the army's, the march's furniture, and the order the season's reports are read
/// in.</summary>
public partial class CampaignMapPage
{
	private const string DisbandVoicePath = "res://assets/audio/disband-troops.mp3";

	/// <summary>The advisor, the fallen reign's last word and the county's own panels, each wired to the
	/// sidebar button that opens it.</summary>
	private void RaiseCountyPanels()
	{
		// The advisor stands over everything, so he goes in last and stays: a message about a county
		// must not be arrived at through the panel of the room the player happened to leave open.
		_advisor = new AdvisorPanel();
		AddChild(_advisor);

		// And over the advisor, the end of it all, when there is nothing left to be advised about.
		_fallen = new FallenPanel();
		AddChild(_fallen);
		_victory = new VictoryPanel();
		AddChild(_victory);
		// On to the next map's briefing; after a campaign's last, back to the campaigns. Nothing is
		// carried over (the user's call): the next map opens fresh, as Lords of the Realm's did.
		_victory.Next += () => SceneRouter.GoTo(this, Campaign.Advance() ? BriefingScenePath : CampaignSelectionScenePath);
		_fallen.Chosen += load => SceneRouter.GoTo(this, load ? LoadGameScenePath : MainMenuScenePath);

		_taxes = new TaxPanel();
		AddChild(_taxes);
		// The rate is the county's the moment the arrow is pressed, so the readout beside it is out
		// of date the moment after.
		_taxes.Changed += _sidebar.Refresh;
		_sidebar.TaxPressed += OpenTaxes;

		_happiness = new HappinessPanel();
		AddChild(_happiness);
		_people = new PeoplePanel();
		AddChild(_people);
		_sidebar.LoyaltyPressed += OpenHappiness;
		_sidebar.PeoplePressed += OpenPeople;

		_rations = new RationPanel();
		AddChild(_rations);
		_rations.Changed += _sidebar.Refresh;
		_sidebar.RationPressed += OpenRations;

		// A cart sent is a county's barn lighter and a mark on the road.
		_supply = new SupplyPanel();
		AddChild(_supply);
		_supply.Dispatched += cart =>
		{
			_sidebar.Refresh();
			int seasons = _turnManager.SupplySeasons(cart.From, cart.To);
			ShowSaveToast($"The carts leave {cart.From} for {cart.To}: {seasons} season{(seasons == 1 ? "" : "s")} on the road");
		};

		_letters = new LetterPanel();
		AddChild(_letters);
		_diplomacy = new DiplomacyPanel();
		AddChild(_diplomacy);
		_diplomacy.Wrote += () => _goldLabel.Text = _turnManager.PlayerGold.ToString("N0");

		_stores = new StorePanel();
		AddChild(_stores);
		_sidebar.StorePressed += OpenStore;
		_stores.SupplyPressed += OpenSupply;
	}

	/// <summary>The trail an army in hand would walk, the reading beside the pointer, and the carts on the road.</summary>
	private void LayMarchLayer()
	{
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

		// The carts on the road, among the pins for the same reason as the trail.
		var carts = new CartMarkers();
		markerLayer.AddChild(carts);
		carts.Watch(_world, () => _turnManager.Shipments);

		AddChild(_marchLabel);
	}

	/// <summary>The panels a company is looked at and ordered through: the army card, the questions it
	/// raises, the walls, the battle, the split and the men sent home.</summary>
	private void RaiseArmyPanels()
	{
		// Over the map and over the rail: a company is looked at where it stands.
		_army = new ArmyPanel();
		AddChild(_army);
		_army.MarchPressed += TakeUpArmy;

		// Two of the lord's companies standing in the same field raise one question, and this is
		// where it is asked: one army now, or two?
		_ask = new QuestionPanel();
		AddChild(_ask);

		// And what happens when the men standing there are somebody else's.
		// The masons' news, in the frame every modal wears.
		_construction = new ConstructionPanel();
		AddChild(_construction);

		// And a county taken, over everything.
		_conquest = new ConquestPanel();
		AddChild(_conquest);

		// And the walls themselves, asked about with the right button.
		_garrison = new GarrisonPanel();
		AddChild(_garrison);

		_battle = new BattlePanel();
		AddChild(_battle);
		_battle.Fielded += fielding =>
		{
			// The lord is down on the field: the map under it is neither drawn nor steered.
			_map.Visible = !fielding;
			_world.ProcessMode = fielding ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
		};
		_battle.Settled += () =>
		{
			// A day's fighting moves men, walls and sometimes a border: everything drawn out there
			// is stale, and so is the county in the sidebar.
			ShowArmies();
			ShowFortifications();
			ShowSettlements();
			if (_selected != null)
			{
				SelectProvince(_markers.IndexOf(_selected));
			}

			Conquered();
			Won();
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
		_army.StormPressed += Storm;
		_army.DisbandPressed += army =>
		{
			ProvinceEconomy home = _turnManager.GetProvince(army.Home);
			if (home == null)
			{
				return;
			}

			// Sent home is sent HOME. They were taken out of the county's people the day they were
			// raised, so a lord who lets an army go gets his hands back into the fields — otherwise
			// disbanding would quietly be the most expensive thing on the panel. Hired men were never
			// the county's: paid off, they walk away down the road.
			int ours = 0;
			foreach ((string unit, int men) in army.Men)
			{
				ours += Units.IsHired(unit) ? 0 : men;
			}

			home.Population += ours;
			home.Disband(army);
			Narrator.Say(DisbandVoicePath);
			ShowArmies();
			_sidebar.Refresh();
			ShowSaveToast(ours == army.Strength
				? $"{ours:N0} men of {army.Home} have gone back to the fields"
				: $"{ours:N0} men of {army.Home} have gone back to the fields, and {army.Strength - ours:N0} hired men have gone their way");
		};
	}

	/// <summary>The fields panel, and the order the season's reports are read in once the advisor is done.</summary>
	private void RaiseFieldsAndLetters()
	{
		_fields = new FieldPanel();
		AddChild(_fields);
		_fields.Changed += county =>
		{
			// The ground itself changed, so the county out there is wrong until it is drawn again.
			_sidebar.Refresh();
			ShowFields(_provinces.Find(province => province.Name == county));
		};
		// The lords' letters come after the advisor has finished, and anything else the turn has to
		// report after them, or it is shown behind them and read by nobody.
		_advisor.Emptied += () => _letters.Read(_turnManager, _turnManager.Diplomacy.TakeLetters(), RealmName);
		_letters.Emptied += () =>
		{
			if (_turnNote.Length > 0)
			{
				ShowSaveToast(_turnNote);
				_turnNote = "";
			}

			// A gate that opened to a siege over the season is a county taken too.
			Conquered();
			StormWhenReady();

			if (_wallsRaised.Count > 0)
			{
				_construction.Announce(new List<(string, string, int)>(_wallsRaised));
				_wallsRaised.Clear();
			}
		};
	}

	/// <summary>The assault on the walls a company is sitting before, put to the lord.</summary>
	private void Storm(FieldArmy army)
	{
		string county = _turnManager.Besieging(army);
		int index = _provinces.FindIndex(province => province.Name == county);
		if (index >= 0)
		{
			_battle.Open(_turnManager, _balance, army, county, ArmyPixel(army),
				ColoursOf(_turnManager.RealmOf(army)), ColoursOf(HolderOf(_provinces[index])));
		}
	}

	/// <summary>The season a siege's engines are built, the battle for the walls opens without the lord
	/// having to ask for it (the user's call): the turns he was told it would take are up. One siege a
	/// season is put to him this way; any other ready with it waits on its company's Storm.</summary>
	private void StormWhenReady()
	{
		FieldArmy ready = _turnManager.Armies().Find(army => _turnManager.RealmOf(army) == _playerRealm
			&& _turnManager.Besieging(army) is { Length: > 0 } county && _turnManager.IsSiegeJustReady(county));
		if (ready != null)
		{
			Storm(ready);
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
}
