using Godot;

/// <summary>Opening what the lord asks to look at: the county's panels from the sidebar, a field,
/// the rooms of the town and the hall, the nav rail's sections, and the first turn's
/// briefing.</summary>
public partial class CampaignMapPage
{
	private const string OpeningVoicePath = "res://assets/audio/campaign-opening-briefing.mp3";

	private const string RecruitsScenePath = "res://scene/campaign-map/recruits.tscn";
	private const string BlacksmithScenePath = "res://scene/campaign-map/blacksmith.tscn";
	private const string MarketScenePath = "res://scene/campaign-map/market.tscn";
	private const string HallScenePath = "res://scene/campaign-map/hall.tscn";
	private const string FortificationsScenePath = "res://scene/campaign-map/fortifications.tscn";
	private const string CityScenePath = "res://scene/campaign-map/city.tscn";


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
	private void OpenStore(ResourceType type)
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy != null && _definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition))
		{
			_stores.Open(type, economy, definition, _balance, _turnManager.CurrentSeason);
		}
	}

	/// <summary>The carts, loaded out of the county in hand for another of the lord's.</summary>
	private void OpenSupply()
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null)
		{
			ShowSaveToast($"{province.Name} is not yours to send from");
			return;
		}

		if (!_supply.Open(_turnManager, economy))
		{
			ShowSaveToast("You hold no other county to send to");
		}
	}

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

	/// <summary>How the county's people have fared, season by season. Read-only, like the goodwill.</summary>
	private void OpenPeople()
	{
		ProvinceData province = Selected();
		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy == null)
		{
			ShowSaveToast($"{province.Name} keeps its own counsel");
			return;
		}

		_people.Open(economy, TurnManager.StartYear);
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

		// A company raised is a company to march: every room is put away and the lord is back on the
		// map, with the new banner standing at its seat.
		if (room is RecruitsPage yard)
		{
			yard.ArmyRaised += () =>
			{
				foreach (RoomPage open in _rooms.ToArray())
				{
					open.Close();
				}
			};
		}

		room.Open(economy);
		room.Closed += () =>
		{
			_rooms.Remove(room);
			room.QueueFree();
			// An order spends the province's stores and its people, so everything that reads them is
			// stale by now — the map behind, and any room this one was opened from.
			_sidebar.Refresh();
			UpdateResourceBar(economy);
			// A room may have changed what the land is worked by, so it is redrawn whichever room was
			// closed: ten plots cost nothing to lay, and asking which rooms can change the land is how
			// a room added later quietly stops updating it.
			ShowFields(province);
			ShowArmies();
			// A band hired in the barracks is off the offer, and its mark comes down with it.
			ShowMercenaries();
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
	/// stores; the smithy needs the land the county is working and the season it is working it in —
	/// it deals the hands again when it is lit — and neither is handed to a room, so it is told
	/// separately.</summary>
	private void Enter(string room)
	{
		RoomPage opened = OpenRoom($"res://scene/campaign-map/{room}.tscn", "use");
		if (!_definitionsByName.TryGetValue(Selected().Name, out ProvinceDefinition definition))
		{
			return;
		}

		if (opened is BlacksmithPage smithy)
		{
			smithy.Brief(definition, _balance, _turnManager.CurrentSeason);
		}
	}

	/// <summary>The briefing a campaign opens on: what you hold, and what to do before you end your
	/// first turn, in the royal frame over the king's view; Begin puts it away.
	///
	/// The voice is optional. Until it is recorded the briefing simply reads itself.</summary>
	private void OpenBriefing()
	{
		string crestPath = Heraldry.CrestPath(_playerRealm);
		Texture2D crest = ResourceLoader.Exists(crestPath)
			? new AtlasTexture { Atlas = GD.Load<Texture2D>(crestPath), Region = ProvinceSidebar.CrestRegion }
			: null;
		var opening = new OpeningPanel();
		AddChild(opening);
		opening.Open($"{_turnManager.CurrentSeason}, {_turnManager.CurrentYear}",
			$"Welcome to {_provinces[0].Name}", _opening, crest, GetNode<Control>("Sidebar").Size.X);

		Narrator.Say(OpeningVoicePath);
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
				case "diplomacy": _diplomacy.Open(_turnManager, RealmName, RealmAccent); break;
				// The same way out as the crest menu's, so the campaign rides along to the options and back.
				case "settings": GetNode<Button>("%OptionsButton").EmitSignal(BaseButton.SignalName.Pressed); break;
				default: ShowSaveToast($"{door} is not built yet"); break;
			}
		};

		_hall.TurnEnded += () =>
		{
			AdvanceTurn();
			_hall?.Refresh();
		};
	}

	/// <summary>Where each button on the nav rail leads. Four open a room of the province in hand —
	/// the hammer is the smithy, not the town, which is reached the way you would reach it, by
	/// walking into it off the map with a second press on the seat already in hand.</summary>
	private void ShowSection(NavRail.Section section)
	{
		switch (section)
		{
			case NavRail.Section.Buildings: OpenRoom(BlacksmithScenePath, "forge"); break;
			case NavRail.Section.Military: OpenRoom(RecruitsScenePath, "raise men"); break;
			case NavRail.Section.Fortifications: OpenRoom(FortificationsScenePath, "build"); break;
			case NavRail.Section.Trade: OpenRoom(MarketScenePath, "trade"); break;
			case NavRail.Section.Court: OpenHall(); break;
			case NavRail.Section.Diplomacy: _diplomacy.Open(_turnManager, RealmName, RealmAccent); break;
		}
	}
}
