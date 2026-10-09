using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The campaign read off disk: its realms and counties from provinces.json, where each
/// village stands, the roads, and the ground an army may cross cut once from the map's own
/// images.</summary>
public partial class CampaignMapPage
{
	private const string ProvincesDataFile = "provinces.json";
	private const string RoadsDataFile = "map-roads.json";
	// Where each village stands, written by the map generator: its seat, unless the level ground its
	// village needs would not fit there — see flatten_yards in tools/generate_campaign_map.py.
	private const string YardsDataFile = "map-yards.json";

	/// <summary>How far above the waterline the ground has to stand before an army will set foot on
	/// it, and how steeply it may climb across one cell before it is a wall rather than a hill.
	///
	/// The rise is in world units, so it follows the map's own height scale: flattening the relief
	/// to make the roads legible also flattened every mountain out of an army's way, and this came
	/// down with it. A lord should still have to go round the spine of the island.</summary>
	private const float ShoreClearance = 0.35f;
	private const string DitchFile = "map-ditch.png";
	private const float MarchableRise = 0.9f;

	/// <summary>A realm as the campaign describes it: what it is called, and the colour everything
	/// belonging to it is drawn in.</summary>
	private record RealmData(string Name, Color Accent);

	/// <summary>Whose colour is whose: every lord wears his own (lords.json "colour") on every map,
	/// whoever plays him — the Crown blue, the Margrave red (the user's call). A realm the campaign
	/// seats nobody in that is not the unclaimed country takes these in turn; the unclaimed country
	/// keeps the colour the campaign gives it.</summary>
	private static readonly Color[] UnseatedColours =
	{
		new("b23a3a"), new("d1a34f"), new("4f8f4a"), new("7a4fa8"),
	};

	/// <summary>One province of the played campaign. <paramref name="Realm"/> is who holds it when
	/// the campaign opens, which for most of them is nobody. <paramref name="EconomyFile"/> names the
	/// ProvinceDefinition describing its land, and is empty where none is authored yet.
	/// <paramref name="MapPosition"/> is its seat — the pin, the end of its roads, where its army
	/// stands; <paramref name="TownPosition"/> is where its village, fields and castle are laid out,
	/// which is the seat itself unless the generator had to move the village clear of a cliff.</summary>
	/// <summary>Which counties border which (provinces.json "neighbours"), for moving house.</summary>
	private readonly Dictionary<string, List<string>> _neighbours = new();

	private record ProvinceData(string Name, Vector2 MapPosition, string Realm, bool IsCapital, string EconomyFile,
		Vector2 TownPosition);

	private readonly Dictionary<string, RealmData> _realms = new();
	// Which of the lords (data/lords.json) holds each realm on this map.
	private readonly Dictionary<string, string> _lordOf = new();
	// Which realm is yours. The others' provinces, and the unclaimed ones, run on nobody's orders yet.
	private string _playerRealm = "";
	// And which realm means nobody. A province of this realm is not simulated at all — it is not
	// handed to the TurnManager — so it keeps its authored numbers until somebody takes it.
	private string _unclaimedRealm = "";

	/// <summary>The highest rung the other lords may build on this map (provinces.json).</summary>
	private string _rivalWallsUpTo = "";

	/// <summary>What the narrator says over the map on the first turn, and what stays on screen after
	/// he has stopped (provinces.json "opening"). Kept to what a first turn can actually do, because
	/// a briefing nobody can act on is a cutscene with a Close button.</summary>
	private string _opening = "";
	// Authored order is load-bearing: it is the ID map's index + 1 encoding, so a province's place
	// in provinces.json is what ties it to its pixels on the map.
	private readonly List<ProvinceData> _provinces = new();

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
	/// Everybody's ground is open, a rival's as much as the player's: the men cross his border, and
	/// what happens when they reach his town happens at his town. His ditches still stop them
	/// everywhere but at a ford or a road, which is what a ditch is for.</summary>
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
	}

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
		_unclaimedRealm = data["unclaimed"].AsString();
		_rivalWallsUpTo = data.TryGetValue("rivalWallsUpTo", out Variant walls) ? walls.AsString() : "";
		_opening = data.TryGetValue("opening", out Variant opening) ? opening.AsString() : "";
		int unseated = 0;
		var renamed = new Dictionary<string, string>();
		Heraldry.Clear();
		foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> realm in Campaign.Seated(data["realms"].AsGodotDictionary(), _playerRealm))
		{
			Godot.Collections.Dictionary fields = realm.Value.AsGodotDictionary();
			string key = realm.Key.AsString();
			string wearer = key == _playerRealm ? Campaign.Player
				: fields.TryGetValue("lord", out Variant lord) ? lord.AsString() : "";
			Color colour = key == _unclaimedRealm ? new Color(fields["accent"].AsString())
				: Lords.Find(wearer)?.Colour ?? UnseatedColours[unseated++ % UnseatedColours.Length];
			_realms[key] = new RealmData(fields["name"].AsString(), colour);
			string drawnAs = data["realms"].AsGodotDictionary()[key].AsGodotDictionary()["name"].AsString();
			if (drawnAs != fields["name"].AsString())
			{
				renamed[drawnAs] = fields["name"].AsString();
			}
			if (wearer.Length > 0)
			{
				Heraldry.Seat(key, wearer);
			}

			// The player's own realm is played, not seated: the lords' table is for the others.
			if (key != _playerRealm && wearer.Length > 0)
			{
				_lordOf[key] = wearer;
			}
		}

		// The speech names the realms as the map drew them; a realm seated under another name is spoken
		// of under that one. In one pass, since two realms may have traded names.
		if (renamed.Count > 0)
		{
			string any = string.Join("|", renamed.Keys.Select(System.Text.RegularExpressions.Regex.Escape));
			_opening = System.Text.RegularExpressions.Regex.Replace(_opening, any, said => renamed[said.Value]);
		}

		// The great crest over the map is the played lord's: the scene draws the Crown's.
		if (ResourceLoader.Exists(Heraldry.CrestPath(_playerRealm)) && GetNode<TextureRect>("Crest").Texture is AtlasTexture crest)
		{
			crest.Atlas = GD.Load<Texture2D>(Heraldry.CrestPath(_playerRealm));
		}

		Dictionary<string, Vector2> yards = LoadYards();
		foreach (Variant entry in data["provinces"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			string name = fields["name"].AsString();
			var seat = new Vector2(fields["x"].AsSingle(), fields["y"].AsSingle());
			var bordering = new List<string>();
			if (fields.TryGetValue("neighbours", out Variant neighbours))
			{
				foreach (Variant other in neighbours.AsGodotArray())
				{
					bordering.Add(other.AsString());
				}
			}

			_neighbours[name] = bordering;
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
}
