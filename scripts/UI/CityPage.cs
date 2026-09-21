using System;
using System.Collections.Generic;
using Godot;

/// <summary>The province, seen from above: its land, and where its people are on it.
///
/// This is the screen the economy is played on. Every site the province works carries a plaque with
/// its name, the hands on it, and the two buttons that move them — so putting the harvest ahead of
/// the quarry is one press on the thing itself rather than a trip to a menu that lists them. In the
/// middle stands the count nobody wants: the hands with nowhere to be.
///
/// A site that also has a room behind it opens it when its name is pressed. The two do not get in
/// each other's way: the buttons move people, the name opens the door.
///
/// The land is bare until somebody draws it. A site shows its own picture the day
/// assets/city/buildings/&lt;key&gt;.png is dropped in; until then its plaque stands on grass, which
/// is the honest picture of a province nobody has painted yet.</summary>
public partial class CityPage : RoomPage
{
	private const string DataPath = "res://data/buildings.json";

	/// <summary>One thing on the province's land. <paramref name="Works"/> is the industry its
	/// plaque hands people to, and is absent for a site that is only a door — a smithy takes an
	/// order, not a workforce. <paramref name="Needs"/> is what the province must actually have for
	/// the place to exist at all.</summary>
	private record Site(string Key, string Name, string Blurb, string Icon, string Room,
		ResourceType? Works, bool Builds, string Needs, Vector2 Spot, float Height,
		BuildingArt.Rotor Turning);

	/// <summary>How tall a site's picture stands, as a fraction of the land, for a site that has not
	/// been given a size of its own in buildings.json. Every drawing eventually wants one: a windmill
	/// and its field take up rather more of a county than a forge does.</summary>
	private const float TileHeight = 0.31f;

	/// <summary>How far the plaque dips below the top of its building. It hangs above the picture
	/// rather than across the middle of it — a plate over a windmill's sails hides the one thing on
	/// the plot worth looking at — so it is measured from the building's own height.</summary>
	private const float PlaqueInset = 0.025f;

	/// <summary>How far the plaque of the site in hand stands off its building.</summary>
	private const float ChosenLift = 14f;

	/// <summary>Hands move five at a time. One at a time is honest and unusable: an autumn harvest
	/// asks for two hundred and forty of them.</summary>
	private const int Step = 5;

	/// <summary>What a count reads when the site has more hands on it than it can use.</summary>
	private static readonly Color Spare = new("6f93c4");

	/// <summary>Raised with a site's room when the player goes in, so the map decides what opens.
	/// The province knows the smithy is there; it does not know what a smithy is.</summary>
	public event Action<string> RoomChosen;

	private readonly List<Site> _sites = new();
	private readonly Dictionary<string, BuildingArt> _tiles = new();
	private string _chosen;
	private readonly Dictionary<string, Control> _plaques = new();

	private Label _name;
	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;

	protected override string RoomName => "The Province";

	protected override string Tagline => "Its land, and where its people are on it";

	// The land is the screen. A gilded title across the middle of it would be a title across the
	// thing the player came here to read, so the province names itself in the corner instead.
	protected override bool ShowsTitle => false;

	// Nothing hangs on signs here; every site carries its own plaque.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override void Load()
	{
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"{RoomName}: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["buildings"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			Godot.Collections.Array spot = fields["spot"].AsGodotArray();

			_sites.Add(new Site(
				fields["key"].AsString(),
				fields["name"].AsString(),
				fields["blurb"].AsString(),
				fields["icon"].AsString(),
				fields.TryGetValue("room", out Variant room) ? room.AsString() : null,
				fields.TryGetValue("works", out Variant works) ? Industry(works.AsString()) : null,
				fields.ContainsKey("masons"),
				fields.TryGetValue("site", out Variant needs) ? needs.AsString() : null,
				new Vector2((float)spot[0], (float)spot[1]),
				fields.TryGetValue("scale", out Variant scale) ? (float)scale : TileHeight,
				fields.TryGetValue("rotor", out Variant rotor) ? Turning(rotor.AsGodotDictionary()) : null));
		}
	}

	protected override void BuildChoosers()
	{
		BuildNamePlate();
		foreach (Site site in _sites)
		{
			if (CityArt.HasTile(site.Key))
			{
				_tiles[site.Key] = HangTile(site);
			}

			// A bare holder anchored on the spot; the plaque inside it is rebuilt whenever a count
			// moves, because one site gaining hands changes what every other one can still be given.
			var holder = new Control { MouseFilter = MouseFilterEnum.Ignore };
			AddChild(holder);
			Chrome.Anchor(holder, site.Spot - new Vector2(0f, site.Height - PlaqueInset), 1, 1);
			_plaques[site.Key] = holder;
		}
	}

	/// <summary>The province's name in the top corner, under its lord's colours: what a lord would
	/// have written over the door, rather than a heading over a page.</summary>
	private void BuildNamePlate()
	{
		var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		stack.AddThemeConstantOverride("separation", 0);

		_name = new Label { ThemeTypeVariation = "GildedTitle" };
		_name.AddThemeFontSizeOverride("font_size", 34);
		GoldTitle.Apply(_name);
		stack.AddChild(_name);
		stack.AddChild(Line("Province of the Crown", 15, Soft));

		AddChild(stack);
		stack.SetAnchorsPreset(LayoutPreset.TopLeft);
		stack.OffsetLeft = 34;
		stack.OffsetTop = 18;
		stack.OffsetRight = 560;
		stack.OffsetBottom = 100;
	}

	/// <summary>What the province has to work with: the land it owns and the season it stands in,
	/// neither of which a room is normally handed. A site the province has no capacity for is not
	/// drawn at all — there is no quarry where there is no stone.</summary>
	public void Brief(ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_definition = definition;
		_balance = balance;
		_season = season;
		_name.Text = Province.ProvinceName.ToUpperInvariant();

		foreach (Site site in _sites)
		{
			bool stands = site.Needs == null || Capacity(site.Needs) > 0;
			_plaques[site.Key].Visible = stands;
			if (_tiles.TryGetValue(site.Key, out BuildingArt tile))
			{
				tile.Visible = stands;

				// The field is the one plot whose men are not simply there: it lies turned over
				// whether or not anybody is on it, and the hands only walk onto it in a year that
				// has something to put right.
				tile.Crew(CrewShare(site));
			}
		}

		Rebuild();
	}

	protected override void Opened()
	{
	}

	/// <summary>The panel in the corner reads the season rather than a chosen site. On a screen
	/// where everything says its own name and carries its own count there is nothing left to add
	/// about one of them, and a great deal to say about the season they are all working in.</summary>
	protected override void ShowDetail()
	{
		ClearDetail();
		if (_definition == null)
		{
			return;
		}

		Label heading = Line(_season.ToString().ToUpperInvariant(), 24, Cream);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(heading);
		Detail.AddChild(Chrome.Rule(360));

		int hands = Province.Workers;
		int idle = EconomySimulation.Idle(Province, _definition, _balance, _season);
		Detail.AddChild(Reading("Hands", hands.ToString("N0"), Bright));
		Detail.AddChild(Reading("At work", (hands - idle).ToString("N0"), Bright));
		Detail.AddChild(Reading("Idle", idle.ToString("N0"), idle > 0 ? Short : Soft));
		Detail.AddChild(Chrome.Rule(360));

		var labour = new LabourBar();
		Detail.AddChild(labour);
		labour.Show(Province, _definition, _balance, _season);

		// While the grip moves only the plaques follow it: rebuilding this panel here would free the
		// bar under the hand that is dragging it. The panel catches up when the hand lets go.
		labour.Moved += Replaque;
		labour.Settled += Rebuild;

		// Three lines' worth of room whether the note needs them or not, so the button under it does
		// not walk up and down the panel as the season turns.
		Label note = Line(Note(), 15, Soft);
		note.AutowrapMode = TextServer.AutowrapMode.Word;
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.VerticalAlignment = VerticalAlignment.Center;
		note.CustomMinimumSize = new Vector2(0, 62);
		Detail.AddChild(note);

		Site chosen = _sites.Find(site => site.Key == _chosen);
		if (chosen?.Room != null)
		{
			string room = chosen.Room;
			var door = new Button { Text = $"{chosen.Name}  ›", CustomMinimumSize = new Vector2(0, 46) };
			door.AddThemeFontSizeOverride("font_size", 18);
			door.TooltipText = chosen.Blurb;
			door.Pressed += () => RoomChosen?.Invoke(room);
			Detail.AddChild(door);
		}

		var spread = new Button { Text = "Set them to work", CustomMinimumSize = new Vector2(0, 46) };
		spread.AddThemeFontSizeOverride("font_size", 18);
		spread.TooltipText = "Fills every site to what it can use, bread first.";
		spread.Pressed += () =>
		{
			EconomySimulation.Deploy(Province, _definition, _balance, _season);
			Rebuild();
		};

		Detail.AddChild(spread);
	}

	/// <summary>What this season is asking of the province, in one line. Autumn gets its own warning
	/// because it is the one season where the right answer is "everybody".</summary>
	private string Note() => _season switch
	{
		Season.Spring => "Seed goes into the ground now, out of your own granary. What is not sown is not reaped.",
		Season.Summer => "A few hands keep the weeds down. The rest are yours to send anywhere.",
		Season.Autumn => "The whole year comes in over one turn. Every hand you leave elsewhere is grain left standing in the field.",
		_ => "Nothing grows. The woods and the diggings are all there is to work.",
	};

	// --- the plaques -------------------------------------------------------------------------------

	private void Rebuild()
	{
		Replaque();
		ShowDetail();
	}

	/// <summary>Redraws the plaques alone. Split out from <see cref="Rebuild"/> because the labour
	/// bar lives in the panel that a full rebuild tears down, and a bar that frees itself halfway
	/// through being dragged takes the drag with it.</summary>
	/// <summary>How much of a site's gang is out on its ground: all of them where it is manned to
	/// what the season asks, none where nobody is on it. It is the same number the plaque carries,
	/// said in men rather than in figures.</summary>
	private float CrewShare(Site site)
	{
		if (_definition == null)
		{
			return 0f;
		}

		if (site.Key == "field")
		{
			return EconomySimulation.SeasonsToMend(Province, _balance, _season) > 0 ? 1f : 0f;
		}

		if (site.Key == "idle")
		{
			return (float)EconomySimulation.Idle(Province, _definition, _balance, _season)
				/ Mathf.Max(1, Province.Workers);
		}

		if (site.Builds)
		{
			return (float)Province.BuildWorkers / Mathf.Max(1, _balance.MasonsPerBuildSeason);
		}

		if (site.Works == null)
		{
			return 1f; // a place with no hands on it at all is drawn as it was painted
		}

		int demand = EconomySimulation.Demand(site.Works.Value, Province, _definition, _balance, _season);
		return demand <= 0 ? 0f : (float)Allocated(site.Works.Value) / demand;
	}

	private void Replaque()
	{
		foreach (Site site in _sites)
		{
			Control holder = _plaques[site.Key];
			foreach (Node old in holder.GetChildren())
			{
				old.QueueFree();
			}

			Control row = Row(site);
			holder.AddChild(row);

			// Centred on the spot by hand: the holder is a bare Control with no layout of its own,
			// which is the point — a container here would stretch the plaque to the page.
			// A plaque taken in hand lifts off its building. The gold border says which one is
			// chosen, but on a page where nine plates are already gold-edged it says it quietly;
			// the one that moved is the one the eye goes to.
			Vector2 size = row.GetCombinedMinimumSize();
			float lift = site.Key == _chosen ? ChosenLift : 0f;
			row.Position = new Vector2(-size.X / 2f, -size.Y / 2f - lift);
			row.Size = size;
		}
	}

	/// <summary>One site's plaque: its name, the hands on it, and the two buttons that move them.
	///
	/// The count sits inside the plate with the name rather than beside it on the grass. A number
	/// painted straight onto a sunlit field is a number nobody can read, and this screen is nothing
	/// but numbers on a sunlit field.</summary>
	private Control Row(Site site)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);

		var named = new Button
		{
			TooltipText = site.Room == null ? site.Blurb : $"{site.Blurb}\n\nPress to go in.",
			FocusMode = FocusModeEnum.None,
		};

		// The name is the door. A site with nothing behind it is still drawn as a plate, so the two
		// kinds stand together without one of them looking broken — it simply has nothing wired to
		// it, rather than being disabled, which Godot would grey out.
		// Every plaque takes its site in hand. Where there is a room behind it, the panel in the
		// corner offers the door — rather than the plaque being a door and a handle at once, which
		// would mean pressing the thing you want to give people to walks you out of the screen you
		// are giving them on.
		string key = site.Key;
		named.Pressed += () => Choose(key);

		var plate = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		plate.AddThemeConstantOverride("separation", 10);
		named.AddChild(plate);
		Chrome.Fill(plate);

		TextureRect glyph = Icon(site.Icon, 26);
		glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		plate.AddChild(glyph);

		// Ground being put right says so, and says how many more seasons it has at the strength the
		// lord has on it. A dash where the figure goes is a field nobody can be spared for.
		int mending = site.Key == "field" && _definition != null
			? EconomySimulation.SeasonsToMend(Province, _balance, _season)
			: 0;

		Label name = Line(mending == 0 ? site.Name : "Mending", 18, Cream);
		name.VerticalAlignment = VerticalAlignment.Center;
		plate.AddChild(name);

		// The masons are a trade like the others as far as this plate is concerned: they have men on
		// them and an appetite, and the appetite happens to be whatever is left of the wall.
		int on = site.Builds ? Province.BuildWorkers : site.Works == null ? 0 : Allocated(site.Works.Value);
		int demand = 0;
		int spare = 0;
		if (_definition != null)
		{
			spare = Province.Workers - Province.AllocatedWorkers;
			if (site.Builds)
			{
				demand = EconomySimulation.Masons(Province);
			}
			else if (site.Works != null)
			{
				demand = EconomySimulation.Demand(site.Works.Value, Province, _definition, _balance, _season);
			}
		}

		// The idle count is the one number here that is read rather than set.
		int shown = site.Key == "idle" && _definition != null
			? EconomySimulation.Idle(Province, _definition, _balance, _season)
			: on;

		// Short-handed reads red and over-manned reads cool, the way Lords of the Realm marks its own
		// production: the first costs you the season's yield, the second only costs you hands you
		// could have had somewhere else.
		// A site nobody is on is not short-handed, it is shut: a province that has put every hand
		// into the harvest should not read as five things going wrong at once.
		string reads = null;
		Color tint = Soft;
		if (mending != 0)
		{
			reads = mending > 0 ? mending.ToString() : "\u2014";
			tint = mending > 0 ? Cream : Short;
		}
		else if (site.Works != null || site.Builds || site.Key == "idle")
		{
			reads = shown.ToString("N0");
			tint = site.Key == "idle" ? (shown > 0 ? Short : Soft)
				: on == 0 ? Soft : on < demand ? Short : on > demand ? Spare : Bright;
		}

		if (reads != null)
		{
			Label count = Line(reads, 19, tint);
			count.HorizontalAlignment = HorizontalAlignment.Right;
			count.VerticalAlignment = VerticalAlignment.Center;
			count.CustomMinimumSize = new Vector2(44, 0);
			plate.AddChild(count);
		}

		Chrome.DressPlaque(named, lit: site.Key == _chosen);
		named.CustomMinimumSize = new Vector2(plate.GetCombinedMinimumSize().X + 34, 46);
		row.AddChild(named);

		if (site.Builds)
		{
			row.AddChild(Chrome.Plate("−", 38, () => Mason(key, -Step, demand, spare)));
			row.AddChild(Chrome.Plate("+", 38, () => Mason(key, Step, demand, spare)));
			return row;
		}

		if (site.Works == null)
		{
			return row;
		}

		ResourceType works = site.Works.Value;
		row.AddChild(Chrome.Plate("−", 38, () => Move(key, works, -Step, demand, spare)));
		row.AddChild(Chrome.Plate("+", 38, () => Move(key, works, Step, demand, spare)));
		return row;
	}

	/// <summary>Takes one site in hand: its plaque lights, and so does the building itself. Nothing
	/// else follows from it — this screen has no detail to show about one site that the site is not
	/// already saying — but a player moving hands about wants to see which plot he is moving them
	/// on to, and on a picture this busy the name alone is not enough.</summary>
	private void Choose(string key)
	{
		_chosen = _chosen == key ? null : key;
		foreach ((string at, BuildingArt tile) in _tiles)
		{
			tile.Light(at == _chosen);
		}

		Rebuild();
	}

	/// <summary>Moves hands on or off a site. It is never given more than it can use, nor more than
	/// the province has left standing about.</summary>
	/// <summary>Moves hands on or off the scaffolding. Same rule as any other trade: never more than
	/// the work left on the wall, never more than the county has standing about.</summary>
	private void Mason(string key, int by, int demand, int spare)
	{
		int on = Province.BuildWorkers;
		Province.BuildWorkers = Mathf.Clamp(on + by, 0, Mathf.Min(demand, on + spare));
		if (_chosen != key)
		{
			Choose(key);
			return;
		}

		Rebuild();
	}

	private void Move(string key, ResourceType works, int by, int demand, int spare)
	{
		int on = Allocated(works);
		Allocate(works, Mathf.Clamp(on + by, 0, Mathf.Min(demand, on + spare)));
		if (_chosen != key)
		{
			Choose(key);
			return;
		}

		Rebuild();
	}

	private Control Reading(string what, string value, Color colour)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		Label name = Line(what, 16, Soft);
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(name);
		row.AddChild(Line(value, 18, colour));
		return row;
	}

	// --- the land ----------------------------------------------------------------------------------

	/// <summary>The site's own picture, standing on the spot its plaque hangs over. Bottom-centred
	/// on the spot rather than centred on it: what has to line up with the ground is the ground it
	/// stands on, not the middle of its roof.</summary>
	private BuildingArt HangTile(Site site)
	{
		var art = GD.Load<Texture2D>(CityArt.Tile(site.Key));
		var tile = new BuildingArt { MouseFilter = MouseFilterEnum.Ignore };
		AddChild(tile);
		// In the order they were read, so a plot in front of another is drawn over it. The land is
		// seen on the diagonal: the back row's far corner belongs behind the front row's near one.
		MoveChild(tile, _tiles.Count + 1);
		tile.Show(
			art,
			CityArt.HasRotor(site.Key) ? GD.Load<Texture2D>(CityArt.Rotor(site.Key)) : null,
			site.Turning,
			CityArt.HasLife(site.Key) ? GD.Load<PackedScene>(CityArt.Life(site.Key)) : null);

		string key = site.Key;
		tile.Pressed += () => Choose(key);

		// Anchored by fractions of the land rather than by a pixel count, so a building is the same
		// share of the province whatever the window is doing. Its width follows from its height,
		// which BuildingArt works out for itself.
		tile.AnchorLeft = tile.AnchorRight = site.Spot.X;
		tile.AnchorTop = site.Spot.Y - site.Height;
		tile.AnchorBottom = site.Spot.Y;
		tile.OffsetTop = 0;
		tile.OffsetBottom = 0;
		return tile;
	}

	// --- reading the province ----------------------------------------------------------------------

	private static BuildingArt.Rotor Turning(Godot.Collections.Dictionary rotor)
	{
		Godot.Collections.Array at = rotor["at"].AsGodotArray();
		Godot.Collections.Array frame = rotor["frame"].AsGodotArray();
		return new BuildingArt.Rotor(
			new Vector2((float)at[0], (float)at[1]),
			new Vector2((float)frame[0], (float)frame[1]),
			rotor["columns"].AsInt32(),
			rotor["frames"].AsInt32(),
			(float)rotor["fps"]);
	}

	private static ResourceType? Industry(string works) => works switch
	{
		"grain" => ResourceType.Grain,
		"cattle" => ResourceType.Cattle,
		"wood" => ResourceType.Wood,
		"stone" => ResourceType.Stone,
		"iron" => ResourceType.Iron,
		_ => null,
	};

	private int Capacity(string needs) => needs switch
	{
		"grain" => _definition.GrainWorkerCapacity,
		"cattle" => _definition.CattleWorkerCapacity,
		"wood" => _definition.WoodWorkerCapacity,
		"stone" => _definition.StoneWorkerCapacity,
		_ => _definition.IronWorkerCapacity,
	};

	private int Allocated(ResourceType works) => works switch
	{
		ResourceType.Grain => Province.GrainWorkers,
		ResourceType.Cattle => Province.CattleWorkers,
		ResourceType.Wood => Province.WoodWorkers,
		ResourceType.Stone => Province.StoneWorkers,
		_ => Province.IronWorkers,
	};

	private void Allocate(ResourceType works, int hands)
	{
		switch (works)
		{
			case ResourceType.Grain: Province.GrainWorkers = hands; break;
			case ResourceType.Cattle: Province.CattleWorkers = hands; break;
			case ResourceType.Wood: Province.WoodWorkers = hands; break;
			case ResourceType.Stone: Province.StoneWorkers = hands; break;
			default: Province.IronWorkers = hands; break;
		}
	}
}
