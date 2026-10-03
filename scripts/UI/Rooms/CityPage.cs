using System;
using System.Collections.Generic;
using Godot;

/// <summary>The province, seen from above: its land, and where its people are on it.
///
/// This is the screen the economy is played on. Every site the province works carries a plaque with
/// its name and its people in figures (WorkerFigures), the way Lords of the Realm draws a county:
/// gold for somebody at work, a hollow ring for a place the work still has. Pressing a site takes it
/// in hand and the panel beside it (CitySitePanel) says what it makes and moves its figures. In the
/// middle stands the count nobody wants: the hands with nowhere to be.
///
/// A site that also has a room behind it offers the door in its panel.
///
/// The land is bare until somebody draws it. A site shows its own picture the day
/// assets/city/buildings/&lt;key&gt;.png is dropped in; until then its plaque stands on grass, which
/// is the honest picture of a province nobody has painted yet.</summary>
public partial class CityPage : RoomPage
{
	private const string DataPath = "res://data/buildings.json";

	/// <summary>One thing on the province's land. <paramref name="Works"/> is the industry its
	/// plaque hands people to; the masons (<paramref name="Builds"/>) and the smiths
	/// (<paramref name="Smiths"/>) are trades without one. <paramref name="Needs"/> is what the
	/// province must actually have for the place to exist at all.</summary>
	private record Site(string Key, string Name, string Blurb, string Icon, string Room,
		ResourceType? Works, bool Builds, bool Smiths, string Needs, Vector2 Spot, float Height,
		BuildingArt.Rotor Turning);

	/// <summary>How tall a site's picture stands, as a fraction of the land, for a site that has not
	/// been given a size of its own in buildings.json. Every drawing eventually wants one: a windmill
	/// and its field take up rather more of a county than a forge does.</summary>
	private const float TileHeight = 0.31f;

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
				fields.ContainsKey("smiths"),
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
			// Never so high that the banner runs under the stores along the top of the page.
			Vector2 plaque = site.Spot - new Vector2(0f, site.Height - PlaqueInset);
			Chrome.Anchor(holder, new Vector2(plaque.X, Mathf.Max(plaque.Y, HighestPlaque)), 1, 1);
			_plaques[site.Key] = holder;
		}

		// The panel over the land, not under it: a site's plaque hanging across the panel that is
		// reading that site is the one thing on the page nobody can read past.
		Control chrome = Body.GetParent().GetParent<Control>();
		MoveChild(chrome, GetChildCount() - 1);
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

	/// <summary>The panel in the corner reads the site in hand, and with none in hand the season
	/// they are all working in.</summary>
	protected override void ShowDetail()
	{
		ClearDetail();
		if (_definition == null)
		{
			return;
		}

		// On the side of the page away from the site in hand, so the panel never hides the very
		// building it is talking about.
		Site chosen = _sites.Find(site => site.Key == _chosen);
		Body.Alignment = chosen is { Spot.X: > DockLeftPast } ? BoxContainer.AlignmentMode.Begin : BoxContainer.AlignmentMode.End;
		if (chosen != null)
		{
			ShowSite(chosen);
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

		var spread = new Button { Text = "Set them to work", CustomMinimumSize = new Vector2(0, 46) };
		spread.AddThemeFontSizeOverride("font_size", 18);
		spread.TooltipText = "Enough on the farm for every field and beast this season, the rest to the industry.";
		spread.Pressed += () =>
		{
			Labour.FarmsFirst(Province, _definition, _balance, _season);
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

	/// <summary>Takes one site in hand, or lets it go: its plaque lifts, the building lights, and the
	/// panel turns to it. Pressed again, the panel goes back to the season.</summary>
	private void Choose(string key)
	{
		_chosen = _chosen == key ? null : key;
		foreach ((string at, BuildingArt tile) in _tiles)
		{
			tile.Light(at == _chosen);
		}

		Rebuild();
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
}
