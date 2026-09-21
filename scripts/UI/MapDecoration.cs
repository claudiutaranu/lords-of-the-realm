using System.Collections.Generic;
using Godot;

/// <summary>Everything standing on the terrain: woods, boulders, ploughed fields, the roads
/// between neighbouring seats, and the working sites that show what a province actually produces.
///
/// Scattered props are drawn as MultiMesh instances — thousands of trees cost a handful of draw
/// calls — and placed from the campaign's map-props.png (R woodland, G boulders, B open farmland),
/// generated alongside the terrain so vegetation lands where the ground supports it: not on cliffs,
/// not on the beach, thicker along province borders. The seed is fixed, so the same realm grows
/// the same forest every run.
///
/// Every mesh here is a primitive placeholder. Swapping in real art means changing the mesh a
/// builder returns; nothing else knows or cares what shape a tree is.</summary>
public partial class MapDecoration : Node3D
{
	// Both belong to the played campaign, generated alongside its map images.
	private const string PropsFile = "map-props.png";
	private const string RoadsFile = "map-roads.json";

	// Attempts, not instances: each one rolls against the density at a random pixel, so the count
	// that lands is whatever the mask supports — ~3,800 trees and ~9,300 boulders on this map. The
	// woods have been thinned twice from where they started: a forest that covers the ground reads
	// as a texture, and what the map wants is stands of trees with country between them.
	private const int ScatterAttempts = 345000;
	private const float TreeChance = 0.234f;
	private const float BoulderChance = 0.22f;

	/// <summary>How the march stones are set: how coarsely the frontier is walked looking for them,
	/// and how far apart they are allowed to stand. Close enough that the eye joins them into a
	/// line — a hundred and forty stones spread over every frontier on the island read as two
	/// stones and a coincidence — and still far enough apart to be markers and not masonry.</summary>
	private const int BorderStoneStep = 3;
	private const float BorderStoneSpacing = 9f;
	// How big the two kinds of tree stand, and the largest chance makes one of them. Named rather
	// than written into the scatter, because the fields have to be able to ask: ground cleared to
	// the width of a trunk is still ground with a crown hanging over it.
	private const string ConiferModel = "Resource_PineTree";
	private const string BroadleafModel = "Resource_Tree1";
	private const float ConiferSize = 4.2f;
	private const float BroadleafSize = 2.6f;
	private const float TreeSpread = 1.4f;
	// Above this share of the map's relief conifers take over from broadleaf, the way a real
	// treeline works. A share and not a height: the map can be flattened or raised without the
	// treeline staying behind at an altitude nothing reaches any more.
	private const float ConiferLine = 0.375f;
	// The trunk road between the two capitals is paved; everything else is a dirt track.
	private const string PavedTexturePath = "res://assets/terrain/road-diffuse.jpg";
	private const string PavedNormalPath = "res://assets/terrain/road-normal.jpg";
	// Gravel rather than the woodland mud it used to be: a track worn to the stone reads on grass
	// and on snow alike, where red-brown mud looked painted on above the treeline.
	private const string TrackTexturePath = "res://assets/terrain/sand-diffuse.jpg";
	private const string TrackNormalPath = "res://assets/terrain/sand-normal.jpg";
	private const float PavedWidth = 0.8f;
	private const float DirtWidth = 0.5f;
	private const float RoadTileLength = 3.2f; // world units per repeat of the cobbles
	// Clear of the terrain without floating visibly. It was three tenths when the relief stood at
	// twenty-four; on country a third that height the same lift turned every track into a causeway
	// with a ditch either side of it.
	private const float RoadLift = 0.09f;

	/// <summary>What a province digs out of its ground. Grain and the herd are not in here: those
	/// are worked on fields, and a field is a plot on the map rather than a scattered prop.</summary>
	public enum SiteKind { Wood, Stone, Iron }

	// Broadleaf woods and standing crops are the two things on the ground that a season changes;
	// conifers are evergreen, and stone doesn't care. Each entry is one material and its colour in
	// Spring/Summer/Autumn/Winter order — the Season enum's own order, so it indexes straight in.
	// Winter is snow on the branches, not bare wood. The dark brown that used to sit here was right
	// for a crown built as a bare sphere; on a model with its foliage modelled in, it reads as a
	// black blot on a white hillside.
	private static readonly Color[] BroadleafBySeason =
		{ new("6d8f3c"), new("4a6b34"), new("a86a28"), new("ccd4d8") };
	private static readonly Color[] GrainBySeason =
		{ new("7fa049"), new("dcbb63"), new("e3bf5a"), new("8f8b84") };

	// What a field is drawn as: a plot this many world units on a side, with a hedge band around it,
	// laid on a lattice of cells this far apart. Sized against the cottages beside it — a plot is a
	// strip a family works, not an estate, and two of them side by side should read as two fields
	// rather than as two counties. The gap is the hedge between neighbours, so it is nearly nothing:
	// what makes a patchwork is plots that touch.
	private const float PlotSize = 2.6f;
	private const float PlotGap = 0.25f;
	private const float PlotBorder = 0.07f;  // share of the plot its hedge band takes, each side
	private const float PlotLift = 0.12f;    // clear of the terrain, the way the roads are
	private const int PlotRings = 6;         // lattice cells searched either way of the seat
	private const float PlotMaxDrop = 1.7f;  // fall across a plot before the ground is too steep
	/// <summary>How long a beast is drawn, nose to tail, in world units. Held as a LENGTH and not as
	/// a scale factor because a model brought in from outside arrives in whatever units it was
	/// modelled in — this is the size the map was laid out around, and the scale is worked back from
	/// the model's own footprint so a new animal cannot come in five times too big.</summary>
	/// <summary>The banner that stands for a county's men, and how tall it is drawn in world units.
	/// Taller than life on purpose: an army is the one thing on this map a lord looks for, and at a
	/// man's real height beside a cottage he would have to hunt for it.</summary>
	private const string ArmyModel = "soldier";
	private const float ArmyHeight = 6.5f;

	/// <summary>How near a click has to land to take hold of a banner, in map pixels. Generous: the
	/// figure is tall and thin, and a lord jabbing at his own army should not have to hit the pole.</summary>
	private const float ArmyReach = 34f;

	/// <summary>How long the banner takes to cover one bead of the road it was sent along. Slow
	/// enough to be a march and not a jump, quick enough that a lord who has ordered four of them is
	/// not waiting on the map.</summary>
	private const float StrideSeconds = 0.16f;

	private const float CattleLength = 1.25f;

	/// <summary>Every so many beasts lies down. A field of animals all standing in the same attitude
	/// reads as a row of ornaments; one of them resting is what makes the rest look alive.</summary>
	private const int RestingEveryNth = 3;
	// The herd is drawn, not summarised: one beast stands for this many head, up to what fits inside
	// a hedge. Four beasts is a pasture at its twenty-head capacity, so a field of cattle looks full
	// exactly when it is full, and a lord who has sold his herd looks out on empty grass.
	private const int HeadPerBeast = 5;
	private const int MostBeasts = 4;

	/// <summary>How many patches of corn a field is drawn in, each way. Four quarters in a square.</summary>
	private const int CropQuarters = 2;

	/// <summary>How tall the corn stands, in world units of model height. Read from the map's own
	/// distance rather than from the field's size: at a quarter of a plot the model's own proportions
	/// put the ears about ankle high on a cottage, which from up here is nothing.</summary>
	private const float CropHeight = 2.6f;
	/// <summary>How grey land goes when it has been cropped out. The floor the simulation holds
	/// fertility at is well above zero, so this is what the worst ground a lord can make looks
	/// like, not what bare rock looks like.</summary>
	private static readonly Color Spent = new("8f8878");

	// The ground a field shows through its year: ploughed, growing, cropped, under snow. Season
	// order, like every other table here. Read against the map's own grass, which is a dry
	// yellow-green: turned earth has to be earth, pasture has to be greener and kept, and stubble
	// has to be the straw colour of ground nobody is working — three answers to "what is this
	// square", not three shades of the same one.
	private static readonly Color[] TilledBySeason =
		{ new("5d3f26"), new("6b4a2c"), new("74573a"), new("aba49b") };
	private static readonly Color[] PastureBySeason =
		{ new("6d9a3c"), new("5f8c33"), new("6b8438"), new("c4ccc6") };
	private static readonly Color[] FallowBySeason =
		{ new("9d9a5a"), new("a89f58"), new("9a8d4f"), new("bcbcb2") };

	private CampaignMap3D _map;
	private Image _props;
	private RandomNumberGenerator _rng;
	private readonly List<(StandardMaterial3D Material, Color[] BySeason)> _seasonal = new();
	/// <summary>One node per province's walls, so a finished build can replace them on their own.</summary>
	private readonly Dictionary<string, Node3D> _forts = new();
	private readonly Dictionary<string, Node3D> _armies = new();
	private readonly Dictionary<string, Vector2> _armySites = new();
	/// <summary>One node per province's fields, so turning a field over — or a season turning —
	/// redraws that province's land alone.</summary>
	private readonly Dictionary<string, Node3D> _land = new();
	/// <summary>Where each province's plots were laid, kept so a redraw puts them back exactly where
	/// they were. Searching again would not: the plots themselves are ground taken, so the second
	/// search would refuse the very cells the first one chose and walk the fields out of the county.</summary>
	private readonly Dictionary<string, List<Vector2>> _plots = new();
	/// <summary>Ground somebody has built on, which the woods are sown around.</summary>
	private readonly List<(Vector2 Centre, float Radius)> _clearings = new();

	/// <summary>Opens the map and lays its roads. The woods are not sown here: a settlement has to
	/// be standing before them, or the trees grow through its roofs. The page calls
	/// <see cref="SowWoods"/> once it has put every province on the ground.</summary>
	public void Build(CampaignMap3D map)
	{
		_map = map;
		_props = GD.Load<Image>(Campaign.Asset(PropsFile));
		_rng = new RandomNumberGenerator();
		_rng.Seed = 20260917; // fixed: the map must look the same every time it loads

		BuildRoads();
	}

	/// <summary>Sets a line of stones along every frontier between two counties.
	///
	/// A border drawn only as a line on the ground is a line on a map, not a thing standing in the
	/// country. Marches were marked with stones, and a lord riding out should be able to see where
	/// his land stops without the map having to tell him in ink.
	///
	/// Walked at a stride and thinned to a spacing, because a stone on every border pixel is a wall
	/// — and a wall is a different thing from a boundary.</summary>
	public void SowBorderStones()
	{
		var stones = new List<Transform3D>();
		var placed = new List<Vector2>();
		Vector2I size = _map.MapPixels;

		for (int y = BorderStoneStep; y < size.Y - BorderStoneStep; y += BorderStoneStep)
		{
			for (int x = BorderStoneStep; x < size.X - BorderStoneStep; x += BorderStoneStep)
			{
				var here = new Vector2(x, y);
				int county = _map.CountyAt(here);
				if (county < 0)
				{
					continue; // the sea is nobody's, and a coast is not a frontier
				}

				bool frontier = false;
				foreach (Vector2 side in new[]
					{ Vector2.Right, Vector2.Down, Vector2.Left, Vector2.Up })
				{
					int there = _map.CountyAt(here + side * BorderStoneStep);
					if (there >= 0 && there != county)
					{
						frontier = true;
						break;
					}
				}

				if (!frontier)
				{
					continue;
				}

				// Far enough apart to read as markers rather than as masonry, and off the line itself
				// by a pace or two: stones were set beside a boundary, not balanced on it.
				Vector2 at = here + new Vector2(_rng.RandfRange(-2.5f, 2.5f), _rng.RandfRange(-2.5f, 2.5f));
				bool crowded = false;
				foreach (Vector2 already in placed)
				{
					if (already.DistanceSquaredTo(at) < BorderStoneSpacing * BorderStoneSpacing)
					{
						crowded = true;
						break;
					}
				}

				if (crowded || IsBuiltOn(at, 4f))
				{
					continue;
				}

				placed.Add(at);
				stones.Add(PropTransform(at, _rng.RandfRange(0.9f, 1.7f), tiltDegrees: 7f));
			}
		}

		AddScatter(Models.MeshOf("Rock"), null, stones, castsShadow: false);
	}

	/// <summary>Sows every wood and boulder field on the map, keeping clear of whatever has already
	/// been built. Called after the settlements, which is the whole point of it being its own
	/// call — a forest sown first grows straight through the villages.</summary>
	public void SowWoods()
	{
		var conifers = new List<Transform3D>();
		var broadleaves = new List<Transform3D>();
		var boulders = new List<Transform3D>();

		for (int i = 0; i < ScatterAttempts; i++)
		{
			var pixel = new Vector2(_rng.RandfRange(0, _props.GetWidth() - 1), _rng.RandfRange(0, _props.GetHeight() - 1));
			if (IsBuiltOn(pixel))
			{
				continue; // somebody's village, or the ground their castle stands on
			}

			Color density = _props.GetPixel((int)pixel.X, (int)pixel.Y);
			if (density.R > 0.05f && _rng.Randf() < density.R * TreeChance)
			{
				Transform3D tree = PropTransform(pixel, _rng.RandfRange(TreeSpread * 0.5f, TreeSpread), tiltDegrees: 4f);
				bool isHigh = _map.HeightAt(pixel) > _map.Relief * ConiferLine;
				// Mixed woods, weighted by altitude, rather than one species per region.
				if (isHigh ? _rng.Randf() < 0.92f : _rng.Randf() < 0.55f)
				{
					conifers.Add(tree);
				}
				else
				{
					broadleaves.Add(tree);
				}
			}
			else if (density.G > 0.05f && _rng.Randf() < density.G * BoulderChance)
			{
				boulders.Add(PropTransform(pixel, _rng.RandfRange(0.5f, 1.3f)));
			}
		}

		// A tree is several instanced meshes over one transform list: each piece carries its own
		// local offset so the trunk stands ON the ground instead of being centred in it, and its
		// own colour, which one merged mesh could not have.
		// One mesh per tree now, where each used to take three: the model carries its own trunk and
		// crown as two surfaces of one drawing, so a wood costs a third of the draw calls it did.
		AddScatter(Models.MeshOf(ConiferModel), null, Grown(conifers, ConiferSize));

		// A broadleaf still turns with the year, so its crown surface is repainted on the way in and
		// that material is handed to SetSeason like the hand-built one was.
		var crown = new StandardMaterial3D
		{
			AlbedoColor = BroadleafBySeason[(int)Season.Summer],
			Roughness = 0.95f,
		};
		_seasonal.Add((crown, BroadleafBySeason));
		AddScatter(Models.Repainted(BroadleafModel, "Green", crown), null, Grown(broadleaves, BroadleafSize));

		AddScatter(Models.MeshOf("Rock"), null, Grown(boulders, 3.4f), castsShadow: false);
	}

	/// <summary>Whether a spot has been taken by something built — a village, a castle, a working
	/// site, a field. Kept as circles rather than as a mask because there are a hundred of them
	/// against a third of a million scatter attempts, and a hundred distance checks still beat an
	/// image lookup. <paramref name="margin"/> is how wide the thing being placed is, so a plot is
	/// kept off a village by its own edge rather than by its middle.</summary>
	private bool IsBuiltOn(Vector2 pixel, float margin = 0f)
	{
		foreach ((Vector2 centre, float radius) in _clearings)
		{
			if (pixel.DistanceSquaredTo(centre) < (radius + margin) * (radius + margin))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Drops a working site — a quarry, a mine, a lumber camp — on the ground around a
	/// province's seat. The page calls this from each province's authored capacities, so what the
	/// map shows and what the economy simulates are the same fact.
	///
	/// Each one takes its own footprint of ground with it, which is what keeps the fields laid after
	/// it from being ploughed straight through the diggings. A bite the size of the prop and no
	/// more: pushing the sites out past the farmland instead would put half of them in the next
	/// county, which on a map of eight provinces is a lie about who owns the ore.</summary>
	public void AddSite(Vector2 seatPixel, SiteKind kind, float weight)
	{
		// A handful, never a field of them: these mark what a province works, they are not the
		// industry itself.
		int count = 3 + (weight > 70f ? 2 : 0);

		var transforms = new List<Transform3D>();
		for (int attempt = 0; attempt < count * 14 && transforms.Count < count; attempt++)
		{
			// Ringed around the seat, not on top of it: the stronghold marker has to stay readable.
			float angle = _rng.RandfRange(0, Mathf.Tau);
			float radius = _rng.RandfRange(38f, 88f);
			var pixel = new Vector2(
				Mathf.Clamp(seatPixel.X + Mathf.Cos(angle) * radius, 0, _props.GetWidth() - 1),
				Mathf.Clamp(seatPixel.Y + Mathf.Sin(angle) * radius, 0, _props.GetHeight() - 1));

			if (_map.HeightAt(pixel) <= _map.WaterLine + 0.2f)
			{
				continue; // out at sea or below the waterline
			}

			transforms.Add(PropTransform(pixel, _rng.RandfRange(0.9f, 1.15f)));
			_clearings.Add((pixel, 9f)); // its own footprint: a plot may abut a quarry, never cover it
		}

		(Mesh mesh, Color color) = kind switch
		{
			SiteKind.Wood => (LogPileMesh(), new Color("6b4c2f")),
			SiteKind.Stone => (QuarryMesh(), new Color("9d9891")),
			_ => (MineMesh(), new Color("4a4440")),
		};
		AddScatter(mesh, color, transforms, colorJitter: 0.1f);
	}

	// --- the land ------------------------------------------------------------------------------

	/// <summary>Lays a province's fields on the ground around its seat: one enclosed plot per field,
	/// under grain, under the herd, or resting, in the season it stands in.
	///
	/// This is the province's own <see cref="ProvinceEconomy.Fields"/> drawn at map scale, not a
	/// decoration of it — turn a field over in the labour room and the plot out here changes with
	/// it, which is the whole point: what the land is under is a decision, and a decision the player
	/// cannot see from the map is a decision he makes in a menu with his eyes shut.
	///
	/// Rebuilt whole rather than patched, because the cheapest thing that can be right is one that
	/// only draws the present state: ten plots is nothing to build, and a patched one would have to
	/// know which field changed and what season it changed in.</summary>
	/// <summary>Which of a county's fields a map pixel fell on, or -1 for the ground between them.
	/// Answered off the plot centres this class already keeps rather than by giving every plot a
	/// collision body: the plots are one mesh on purpose — a hundred and sixty little bodies to
	/// answer a question a distance check answers is how a map starts costing frames.
	///
	/// The reach is half a plot, which is the circle that fits inside the square. A click on the very
	/// corner of a field misses; a click that would have been ambiguous between two of them cannot
	/// happen, and the second is worth more than the first.</summary>
	public int PlotAt(string province, Vector2 pixel)
	{
		if (!_plots.TryGetValue(province, out List<Vector2> plots))
		{
			return -1;
		}

		float reach = PlotSize * 0.5f * _map.PixelsPerUnit;
		for (int field = 0; field < plots.Count; field++)
		{
			if (pixel.DistanceTo(plots[field]) <= reach)
			{
				return field;
			}
		}

		return -1;
	}

	public void SetFields(string name, Vector2 seatPixel, ProvinceEconomy province, Season season)
	{
		FieldUse[] uses = province.Fields;
		if (_land.TryGetValue(name, out Node3D standing))
		{
			// Out of the tree first and freed after: QueueFree alone runs at the end of the frame, so
			// the old fields would spend that frame sitting inside the new ones — same ground, two
			// sets of colours, which on the turn a field is changed is a visible flicker.
			RemoveChild(standing);
			standing.QueueFree();
		}

		var land = new Node3D();
		AddChild(land);
		_land[name] = land;

		float yaw = PlotYaw(name);
		if (!_plots.TryGetValue(name, out List<Vector2> plots))
		{
			plots = PlotSites(seatPixel, yaw, uses.Length);
			_plots[name] = plots;
			foreach (Vector2 plot in plots)
			{
				// Half the plot's diagonal, and then the reach of a branch: a wood cleared to the
				// trunk line still hangs over the corn, which is what the fields under the trees
				// looked like. Taken once, when the ground is first claimed — a plot is not new
				// ground every redraw.
				_clearings.Add((plot, (PlotSize * 0.71f * _map.PixelsPerUnit) + CrownReach));
			}

			if (plots.Count < uses.Length)
			{
				// Not fatal — the province works the land it has — but it means the ledger is counting
				// fields the map has nowhere to put, which is worth hearing while the map is young.
				GD.PushWarning($"MapDecoration: {name} has ground for {plots.Count} of its {uses.Length} fields");
			}
		}

		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);

		string model = CropModel(season);
		var crop = new List<Transform3D>();
		var herd = new List<Transform3D>();
		Beast afoot = BeastOf("cow");
		Beast lying = BeastOf("cow-resting");
		var resting = new List<Transform3D>();

		// How much of a full crop is standing: what was sown against what the county's grain fields
		// would have taken, after the summer has had whatever the weeders did not stop it having.
		int wanted = province.FieldsUnder(FieldUse.Grain) * GameBalance.Engine.SeedPerField;
		float fullness = wanted <= 0 ? 0f : Mathf.Clamp((float)province.StandingCrop / wanted, 0f, 1f);

		// Whichever ran out first: the ground the province was found, or the fields it holds.
		// The herd is spread over the pastures it actually has: two fields of cattle share the
		// province's beasts between them rather than each showing a full field of them.
		int pastures = province.FieldsUnder(FieldUse.Pasture);
		int perPasture = pastures == 0 ? 0 : province.Cattle / pastures;

		for (int field = 0; field < Mathf.Min(plots.Count, uses.Length); field++)
		{
			FieldUse use = uses[field];
			bool sowable = use == FieldUse.Grain && model != null && province.StandingCrop > 0;
			int quarters = sowable ? Quarters(fullness) : 0;

			// The soil of a sown quarter is the crop's own colour, a shade down from the ears
			// standing on it, so the rows have something to be rows of.
			AddPlot(surface, plots[field], yaw, GroundOf(use, season, province.Fertility[field]),
				GrainBySeason[(int)season].Darkened(0.32f), quarters);

			// Corn only where corn was actually sown. The simulation buys its seed out of the
			// province's own granary in spring and only as far as the hands reach, so a lord who ate
			// his seed or left the fields unmanned has bare earth — and the map has to say so, or it
			// is telling him a harvest is coming that the ledger will not pay.
			if (quarters > 0)
			{
				AddCrop(crop, plots[field], yaw, model, quarters);
			}
			else if (use == FieldUse.Pasture)
			{
				AddHerd(herd, resting, plots[field], yaw, afoot, lying, perPasture);
			}
		}

		if (plots.Count > 0)
		{
			surface.GenerateNormals();
			land.AddChild(new MeshInstance3D
			{
				Mesh = surface.Commit(),
				MaterialOverride = new StandardMaterial3D
				{
					VertexColorUseAsAlbedo = true,
					// The plot colours are written as sRGB hex like every other colour in this file.
					// Unsaid, the renderer reads a vertex colour as linear and every field comes out
					// washed pale — which is exactly what the first map showed.
					VertexColorIsSrgb = true,
					Roughness = 1.0f,
					// The plots are draped sheets with nothing behind them; culling one is a hole in
					// the ground seen from the wrong side of a hill.
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}

		if (crop.Count > 0)
		{
			// The corn model carries no material of its own, so the season's colour is the only one it
			// has: green in spring, gold by autumn, off the same table the ground under it is painted
			// from. Nothing seasonal is registered — the next season rebuilds this node, so a material
			// left on the list would outlive the field it belongs to.
			AddScatter(Models.MeshOf(model), GrainBySeason[(int)season], crop,
				castsShadow: false, parent: land);
		}

		// No colour on either: the beasts paint themselves now. An override would flatten a Holstein
		// to one shade, which is the difference between a cow and a bean bag at this distance.
		AddScatter(afoot.Mesh, null, herd, parent: land);
		AddScatter(lying.Mesh, null, resting, parent: land);
	}

	/// <summary>One kind of beast, ready to be set out: its mesh, the scale that makes it the length
	/// this map draws cattle at, and how far off the ground its own feet sit.</summary>
	private readonly record struct Beast(Mesh Mesh, float Scale, float Stands);

	private static Beast BeastOf(string name)
	{
		Mesh mesh = Models.MeshOf(name);
		float scale = CattleLength / Mathf.Max(0.01f, Models.FootprintOf(name));

		// Its own underside, not half its height: a model built around its middle and one built on
		// its feet both land on the grass this way.
		return new Beast(mesh, scale, (-mesh.GetAabb().Position.Y * scale) + PlotLift);
	}

	/// <summary>Where a province's fields lie: blocks of plots that touch, grown outwards from the
	/// nearest workable cell of a lattice laid over the seat. Grown rather than picked, because
	/// picking the ten best cells in a ring scatters ten lonely squares across the county, and what
	/// makes ground read as farmed is fields sharing hedges with their neighbours.
	///
	/// A block stops when it runs into woods, water, a hillside or the village, and the next one
	/// starts at the nearest cell still free — so a province whose land is broken up gets two or
	/// three small patchworks rather than one impossible square.
	///
	/// The order is total — distance first, then the cell's own coordinates — so two cells equally
	/// far out never swap places between one rebuild and the next. A field that jumps across the
	/// county because the player changed what it is under reads as a bug, and would be one.
	///
	/// Two passes: the first asks for the open farmland the props mask marks out, the second takes
	/// whatever is level and dry. A county of rock still has to eat.</summary>
	private List<Vector2> PlotSites(Vector2 seat, float yaw, int wanted)
	{
		float cell = (PlotSize + PlotGap) * _map.PixelsPerUnit;
		int county = _map.CountyAt(seat);

		var cells = new List<Vector2I>();
		for (int x = -PlotRings; x <= PlotRings; x++)
		{
			for (int z = -PlotRings; z <= PlotRings; z++)
			{
				cells.Add(new Vector2I(x, z));
			}
		}

		cells.Sort((a, b) =>
		{
			int byDistance = (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y);
			return byDistance != 0 ? byDistance
				: a.X != b.X ? a.X.CompareTo(b.X)
				: a.Y.CompareTo(b.Y);
		});

		// Half a cell off the lattice's own corners, so no plot lands on the seat itself.
		Vector2 Centre(Vector2I square) => seat + new Vector2(
			(square.X + 0.5f) * cell, (square.Y + 0.5f) * cell).Rotated(yaw);

		var taken = new List<Vector2I>();
		for (int pass = 0; pass < 2 && taken.Count < wanted; pass++)
		{
			var refused = new HashSet<Vector2I>();
			foreach (Vector2I first in cells)
			{
				if (taken.Count >= wanted)
				{
					break;
				}

				// Each sweep of this loop starts a new block at the nearest cell nothing has claimed,
				// and the walk below grows it as far as the ground allows.
				var frontier = new Queue<Vector2I>();
				frontier.Enqueue(first);
				while (frontier.Count > 0 && taken.Count < wanted)
				{
					Vector2I square = frontier.Dequeue();
					if (refused.Contains(square) || taken.Contains(square))
					{
						continue;
					}

					// The open-farmland mask is asked of the cell a block STARTS on and of no other.
					// Asked of every cell it fragments the block into single squares dotted across
					// the county — which is what the first map showed — because the mask thins out
					// wherever the woods begin. Where a man puts his first field, he ploughs the
					// next one beside it whatever the map thinks of the ground.
					bool seeding = square == first;
					if (Mathf.Abs(square.X) > PlotRings || Mathf.Abs(square.Y) > PlotRings
						|| !CanPlough(Centre(square), yaw, openGroundOnly: pass == 0 && seeding, county))
					{
						refused.Add(square);
						continue;
					}

					taken.Add(square);
					foreach (Vector2I side in new[]
						{ Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up })
					{
						frontier.Enqueue(square + side);
					}
				}
			}
		}

		var sites = new List<Vector2>();
		foreach (Vector2I square in taken)
		{
			sites.Add(Centre(square));
		}

		return sites;
	}

	/// <summary>Whether a plot can be laid here: on the map, off everything already built, out of
	/// the water, and level enough that a field does not hang off a cliff. The four corners are what
	/// is sampled, not the middle — a plot straddling a ridge has a perfectly good middle.</summary>
	private bool CanPlough(Vector2 centre, float yaw, bool openGroundOnly, int county)
	{
		if (centre.X < 0 || centre.Y < 0 || centre.X > _props.GetWidth() - 1 || centre.Y > _props.GetHeight() - 1)
		{
			return false;
		}

		float half = PlotSize * 0.5f * _map.PixelsPerUnit;
		if (IsBuiltOn(centre, half))
		{
			return false; // the village, its castle, or one of its diggings
		}

		if (openGroundOnly && _props.GetPixel((int)centre.X, (int)centre.Y).B < 0.2f)
		{
			return false;
		}

		// A field belongs to a county, whole. Judged on the corners rather than the middle, because a
		// plot whose centre is inside is still half in the neighbour's ground — which is what the
		// fields lying across the frontier were.
		if (_map.CountyAt(centre) != county)
		{
			return false;
		}

		float lowest = float.MaxValue;
		float highest = float.MinValue;
		for (int corner = 0; corner < 4; corner++)
		{
			Vector2 offset = new Vector2(corner < 2 ? -half : half, corner % 2 == 0 ? -half : half).Rotated(yaw);
			Vector2 at = centre + offset;
			if (_map.CountyAt(at) != county)
			{
				return false;
			}

			float height = _map.HeightAt(at);
			lowest = Mathf.Min(lowest, height);
			highest = Mathf.Max(highest, height);
		}

		return lowest > _map.WaterLine + 0.4f && highest - lowest <= PlotMaxDrop;
	}

	/// <summary>One plot: a grid draped over the ground, its outer ring painted dark so the field
	/// reads as enclosed. Hedges as geometry would be thousands of instances for a line two pixels
	/// wide at map height; a darker band on the same mesh says the same thing for nothing.
	///
	/// The interior is cut into cells rather than left as one quad, because one quad is a flat sheet
	/// bridging whatever the ground does between its corners — and a plot bridging a swell of ground
	/// has that swell coming up through the middle of it. The terrain carries a vertex every three
	/// map pixels; this samples at about the same rate, so the field lies on the hillside instead of
	/// across it.</summary>
	/// <summary>The ground of one field. <paramref name="sown"/> and <paramref name="quarters"/> paint
	/// the part of it that is under crop in the crop's own colour rather than in bare earth.
	///
	/// That colouring is what makes a sown field read as sown from any height. The corn itself is a
	/// model of separate rows, and from close up the eye goes straight to the soil between them — so
	/// a field with corn standing on bare brown looked like a scatter of straw laid on a ploughed
	/// field, which is exactly what it was. Under crop-coloured ground the rows read as rows.</summary>
	private void AddPlot(SurfaceTool surface, Vector2 centre, float yaw, Color colour,
		Color sown = default, int quarters = 0)
	{
		// Four across, so the quarters the crop is counted in fall on cell boundaries rather than
		// through the middle of one.
		const int Inner = 4; // interior cells across a plot, either way

		float half = PlotSize * 0.5f * _map.PixelsPerUnit;
		// Edges of every cell, as fractions of the plot's half-width: the hedge band, then the
		// interior cut evenly, then the hedge band again.
		float band = PlotBorder * 2f;
		var edge = new float[Inner + 3];
		edge[0] = -1f;
		edge[^1] = 1f;
		for (int i = 0; i <= Inner; i++)
		{
			edge[i + 1] = -1f + band + ((2f - (2f * band)) * i / Inner);
		}

		var grid = new Vector3[edge.Length, edge.Length];
		for (int x = 0; x < edge.Length; x++)
		{
			for (int z = 0; z < edge.Length; z++)
			{
				Vector2 offset = new Vector2(edge[x] * half, edge[z] * half).Rotated(yaw);
				grid[x, z] = _map.WorldAt(centre + offset) + (Vector3.Up * PlotLift);
			}
		}

		int last = edge.Length - 2;
		Color hedge = colour.Darkened(0.62f);
		for (int x = 0; x <= last; x++)
		{
			for (int z = 0; z <= last; z++)
			{
				bool edgeCell = x == 0 || z == 0 || x == last || z == last;
				Color shade = edgeCell ? hedge
					: UnderCrop(x, z, Inner, quarters) ? sown
					: colour;
				foreach (Vector3 corner in new[]
				{
					grid[x, z], grid[x + 1, z], grid[x + 1, z + 1],
					grid[x, z], grid[x + 1, z + 1], grid[x, z + 1],
				})
				{
					surface.SetColor(shade);
					surface.AddVertex(corner);
				}
			}
		}
	}

	/// <summary>Two rows of standing crop inside a grain plot, each drawn where it actually stands
	/// so a field on a slope has its far row up the hill. Both the size of the crop and the space
	/// between the rows are measured off the model's own bounds against the plot's — a spacing
	/// guessed in world units fills one plot and overhangs the next the day either is retuned.</summary>
	/// <summary>Whether an interior cell falls in one of the quarters the crop was laid on. Counted
	/// the same way round as <see cref="AddCrop"/> lays its patches out, or the ground would be
	/// painted in one corner and the corn stood in another.</summary>
	private static bool UnderCrop(int x, int z, int inner, int quarters)
	{
		if (quarters <= 0)
		{
			return false;
		}

		int across = ((x - 1) >= inner / 2 ? 1 : 0) + ((z - 1) >= inner / 2 ? CropQuarters : 0);
		return across < quarters;
	}

	/// <summary>A field under grain, in quarters. Not because quarters are tidy — because the amount
	/// of a field that is actually under crop is a number the lord has to be able to read off his own
	/// land. A county that sowed half of what its fields would take, or let the weeds have half of
	/// what it sowed, has half a field of corn standing there, and the map says so before the ledger
	/// does.
	///
	/// Quarters are enough resolution and not too much: four steps a player can count at a glance
	/// beats a smooth fade he has to squint at, and it is what the game this one is copied from
	/// did.</summary>
	private void AddCrop(List<Transform3D> crop, Vector2 centre, float yaw, string model, int quarters)
	{
		Aabb bounds = Models.MeshOf(model).GetAabb();
		float inside = PlotSize * (1f - (2f * PlotBorder));
		float patch = inside / CropQuarters;
		Vector3 middle = bounds.GetCenter();

		// Stretched onto its quarter on BOTH sides rather than scaled by its longest one. The model
		// is a strip of rows half again as long as it is deep, so fitting it evenly left a third of
		// every quarter as bare earth between the rows — which is what made a sown field read as a
		// thin scatter instead of a crop.
		var spread = new Vector3(patch / bounds.Size.X, CropHeight, patch / bounds.Size.Z);

		// The height is its own number and not the ground scale. Corn stands the same height in a
		// small field as in a large one, and a field the player has to squint at is a field he does
		// not count — this is the one place where being a little taller than life is the honest
		// choice, because the map is read from further away than a man ever saw his own land.
		float roots = (-bounds.Position.Y * spread.Y) + PlotLift;

		// Laid row by row from one corner, so a quarter-full field is a quarter of the ground covered
		// and not a quarter of it thinned — a field sown thin and a field sown small are different
		// things, and the one this game has is the second.
		for (int quarter = 0; quarter < quarters; quarter++)
		{
			var step = new Vector2(
				((quarter % CropQuarters) + 0.5f - (CropQuarters * 0.5f)) * patch,
				((quarter / CropQuarters) + 0.5f - (CropQuarters * 0.5f)) * patch);

			Vector2 spot = centre + (step * _map.PixelsPerUnit).Rotated(yaw);
			// A map rotation turns the other way round the vertical than a world one does, so what
			// squares a model to its plot is the lattice's angle negated.
			// A map rotation turns the other way round the vertical than a world one does, so what
			// squares a model to its plot is the lattice's angle negated.
			var standing = new Transform3D(
				Basis.Identity.Rotated(Vector3.Up, -yaw).Scaled(spread), _map.WorldAt(spot));
			crop.Add(standing
				.TranslatedLocal(new Vector3(-middle.X, 0f, -middle.Z))
				.Translated(Vector3.Up * roots));
		}
	}

	/// <summary>How many quarters of a field are under crop, from how full the county's crop is.
	/// Rounded up, so a field with anything at all standing in it shows something standing.</summary>
	private static int Quarters(float fullness) =>
		Mathf.Clamp(Mathf.CeilToInt(CropQuarters * CropQuarters * fullness), 0, CropQuarters * CropQuarters);

	/// <summary>Beasts on a pasture — as many as the province keeps there, one drawn to every few
	/// head, up to what stands inside a hedge. Counted rather than decorative: in the game this is
	/// copied from, a lord reads his herd off the map by looking at it, and a fixed two cows on
	/// every green square would make selling half the herd invisible.
	///
	/// A beast is a body and a head over one transform, the way a tree used to be a trunk and a
	/// crown: one box at this size is a crate, and the knob on the front of it is the whole
	/// difference between a crate and an animal.</summary>
	private void AddHerd(List<Transform3D> herd, List<Transform3D> resting, Vector2 centre, float yaw,
		Beast afoot, Beast lying, int head)
	{
		int beasts = Mathf.Min(head / HeadPerBeast, MostBeasts);
		if (head > 0 && beasts == 0)
		{
			beasts = 1; // a handful of cattle is still cattle standing in the field
		}

		// Far enough apart to read as four animals, near enough that the outermost one does not put
		// its head through the hedge: a beast standing half outside its own field is the one thing on
		// this map that looks like a mistake rather than like husbandry.
		float reach = PlotSize * (0.5f - PlotBorder) * 0.52f * _map.PixelsPerUnit;
		var corners = new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1) };
		for (int beast = 0; beast < beasts; beast++)
		{
			// Four corners of the square, in the same order and at the same spots on every pasture in
			// the realm, with no jitter on top. A pasture is a fenced field and not a wilderness: the
			// eye reads a repeated arrangement as husbandry and a scattered one as an accident, and
			// the count is the thing the player is meant to read off the ground anyway.
			//
			// Opposite corners first, so a herd of two stands as a diagonal rather than as a pair.
			Vector2 spot = corners[beast % corners.Length] * reach;
			Beast kind = beast % RestingEveryNth == RestingEveryNth - 1 ? lying : afoot;

			// Squared to the plot, the way the corn rows are, and every beast the same way. Left to
			// itself the transform picks a random heading, which is what had them facing four
			// different ways in the same field.
			(kind.Mesh == lying.Mesh ? resting : herd).Add(
				BuildingTransform(centre + spot.Rotated(yaw), kind.Scale, -yaw)
					.Translated(Vector3.Up * kind.Stands));
		}
	}

	/// <summary>What a plot's ground looks like: what it is under, what season it stands in, and how
	/// much heart the land has left. The last of those is the whole point of the fallow field — a
	/// lord who crops the same acre four years running should be able to see it going gray from the
	/// map, not only find out in a bad autumn.</summary>
	private static Color GroundOf(FieldUse use, Season season, float heart)
	{
		Color ground = use switch
		{
			FieldUse.Grain => TilledBySeason[(int)season],
			FieldUse.Pasture => PastureBySeason[(int)season],
			_ => FallowBySeason[(int)season],
		};

		return ground.Lerp(Spent, (1f - heart) * 0.75f);
	}

	/// <summary>What stands in a grain field this season: a full crop from the sowing to the
	/// reaping, coloured green, gold or stubble by the year, and bare ploughed earth all winter —
	/// the same year the labour room charges hands for. The pack's thinner sown model was honest
	/// about spring and unreadable from map height, which on a map is the same as being wrong.</summary>
	private static string CropModel(Season season) =>
		season == Season.Winter ? null : "Farm_FirstAge_Level2_Wheat";

	/// <summary>How far the crown of the widest tree this map sows reaches from its trunk, in map
	/// pixels. Measured off the models and the scatter's own sizes rather than guessed, so retuning
	/// how big the woods stand cannot quietly leave the fields under them again.</summary>
	private float CrownReach => 0.5f * _map.PixelsPerUnit * TreeSpread * Mathf.Max(
		Models.FootprintOf(ConiferModel) * ConiferSize,
		Models.FootprintOf(BroadleafModel) * BroadleafSize);

	/// <summary>A fixed angle per province, so its fields lie square to each other but not to the
	/// map — and the same angle every run, which a string's own hash code does not promise.</summary>
	private static float PlotYaw(string province)
	{
		int hash = 0;
		foreach (char letter in province)
		{
			hash = (hash * 31) + letter;
		}

		return Mathf.Tau * ((hash & 0xFF) / 256f);
	}

	/// <summary>How big a place stands on a province's seat. Two rungs, not three: a walled seat is
	/// no longer a kind of town but a fortification standing beside one, because the player builds
	/// that and cannot build the town.</summary>
	public enum Settlement { Hamlet, Town }

	/// <summary>Which model a rung wears, and how big it stands. The ladder runs a timber watchtower
	/// up to a walled castle, which is the progression the fortifications page charges for — so what
	/// the map shows and what the ledger holds are the same fact.</summary>
	private record Works(string Model, float Size);

	/// <summary>The three cottages a village is dealt from, and how far the pack's own units have to
	/// be stretched to stand beside this map's trees — its buildings are modelled about a metre
	/// tall, where a conifer here is nearly three.</summary>
	private static readonly string[] Cottages =
		{ "Houses_SecondAge_1_Level1", "Houses_SecondAge_2_Level1", "Houses_SecondAge_3_Level1" };

	private const float HouseScale = 2.1f;

	// A town is more cottages than a hamlet and nothing else. The pack's hall is a walled compound
	// with ponds and gardens in it, which from map height reads as a blue smear rather than as a
	// building, and its church stood taller than anything a village of this size would raise.

	private static Works PlanFor(string fort) => fort switch
	{
		"small-palisade" => new Works("WatchTower_FirstAge_Level1", 3.2f),
		"medium-fort" => new Works("WatchTower_FirstAge_Level2", 3.4f),
		"large-fort" => new Works("TowerHouse_FirstAge", 3.2f),
		"small-castle" => new Works("WatchTower_SecondAge_Level1", 3.6f),
		"medium-castle" => new Works("WatchTower_SecondAge_Level3", 3.8f),
		"large-castle" => new Works("Wonder_SecondAge_Level2", 3.4f),
		"grand-castle" => new Works("Wonder_SecondAge_Level3", 3.8f),
		_ => null,
	};

	/// <summary>Raises a settlement on a province's seat — the thing the map pin points at, built
	/// out of the same primitives as the woods and the quarries rather than an imported model.
	///
	/// Houses are laid in a loose ring around the seat with a clear middle, because a cluster with
	/// a square in it reads as a place people live and an even scatter reads as debris. The ring
	/// starts wide of the seat, not on it: a province's pin is drawn over that spot, and a
	/// settlement tucked underneath it is a settlement nobody ever sees.
	///
	/// Everything here stands plumb and unstretched: a leaning tree is character, a leaning house
	/// is a bug, and a roof only sits on its walls if both took the same transform.</summary>
	public void AddSettlement(Vector2 seatPixel, Settlement kind)
	{
		int houses = kind == Settlement.Hamlet ? 5 : 8;
		float inner = kind == Settlement.Hamlet ? 16f : 18f;
		float outer = kind == Settlement.Hamlet ? 30f : 38f;

		// Measured off the widest cottage rather than guessed: its own footprint, grown by the scale
		// it is placed at, converted into map pixels, and given a third again so the walls have
		// daylight between them. Guessed at thirteen pixels, this was narrower than a house.
		float clearance = 1.34f * HouseScale * _map.PixelsPerUnit
			* Mathf.Max(Models.FootprintOf(Cottages[0]),
				Mathf.Max(Models.FootprintOf(Cottages[1]), Models.FootprintOf(Cottages[2])));

		// Wide of the last roof, so a village sits in a clearing rather than in a thicket.
		_clearings.Add((seatPixel, outer + 14f));

		var taken = new List<Vector2>();
		var homes = new List<Transform3D>();
		for (int attempt = 0; attempt < houses * 40 && homes.Count < houses; attempt++)
		{
			float angle = _rng.RandfRange(0, Mathf.Tau);
			float radius = _rng.RandfRange(inner, outer);
			Vector2 pixel = Clamped(seatPixel, angle, radius);
			if (_map.HeightAt(pixel) <= _map.WaterLine + 0.3f || Crowds(taken, clearance, pixel))
			{
				continue; // below the tideline, or on top of somebody's roof
			}

			taken.Add(pixel);
			homes.Add(BuildingTransform(pixel, HouseScale * _rng.RandfRange(0.92f, 1.12f)));
		}

		// Dealt round-robin rather than at random: three kinds shuffled by chance leaves a village
		// with four of one and none of another often enough to look wrong.
		for (int cottage = 0; cottage < Cottages.Length; cottage++)
		{
			var mine = new List<Transform3D>();
			for (int i = cottage; i < homes.Count; i += Cottages.Length)
			{
				mine.Add(homes[i]);
			}

			AddScatter(Models.MeshOf(Cottages[cottage]), null, mine);
		}

	}
	/// <summary>Puts a province's castle on the ground beside its town, and takes down whatever
	/// stood there before. Its whole job is to say, from the map and without a click, that this
	/// place has a castle and roughly how much of one — so it is the building itself and no curtain
	/// wall around it: a ring of blocks at map scale reads as a smudge, and the keep is what carries
	/// the fact.
	///
	/// A castle is the one thing on this map the player builds, so it is the one thing that has to
	/// change without the map being rebuilt around it — hence a node of its own per province rather
	/// than another scatter folded in with the trees.
	///
	/// An empty key takes it down and leaves an open village, which is what a province that has
	/// never built looks like.</summary>
	/// <summary>Walks a county's banner along a road to where it was sent, and says when it has got
	/// there. The piece is not moved and then the map redrawn — it is the same figure, carried along
	/// the same line of beads the lord was shown, so what he ordered and what he watches are plainly
	/// the same thing.
	///
	/// The men have already moved in the ledger by the time this runs. This is the walk, not the
	/// march: if it were the other way round, a lord could close the game mid-stride and find his
	/// army had never left.</summary>
	public void WalkArmy(string province, List<Vector2> road, System.Action arrived)
	{
		if (!_armies.TryGetValue(province, out Node3D piece) || piece.GetChildCount() == 0
			|| road.Count == 0)
		{
			arrived();
			return;
		}

		var man = (Node3D)piece.GetChild(0);
		float stands = man.Position.Y - _map.WorldAt(_armySites.GetValueOrDefault(province)).Y;

		Tween walk = CreateTween();
		foreach (Vector2 step in road)
		{
			Vector3 ground = _map.WorldAt(step);
			walk.TweenProperty(man, "position", ground + (Vector3.Up * stands), StrideSeconds);
		}

		walk.TweenCallback(Callable.From(arrived));
	}

	/// <summary>Whose men are standing under this map pixel, or nothing. The banner is picked off its
	/// own position rather than off a collision body, the same way the fields are: one distance
	/// against a handful of armies costs nothing, and a body on every figure would have to be built
	/// and thrown away every time a county raised or lost a man.</summary>
	public string ArmyAt(Vector2 pixel)
	{
		foreach ((string province, Vector2 site) in _armySites)
		{
			if (_armies.ContainsKey(province) && pixel.DistanceTo(site) <= ArmyReach)
			{
				return province;
			}
		}

		return "";
	}

	/// <summary>The men of a county, standing on it as one figure. Not a figure per company and not a
	/// figure per hundred: what the player needs off the map is WHERE his army is, and the sidebar
	/// tells him what is in it the moment he selects the county. A field of little men would tell him
	/// neither thing any better and would cost a draw call every time somebody recruited.</summary>
	public void SetArmy(string province, Vector2 seatPixel, bool standing)
	{
		if (_armies.TryGetValue(province, out Node3D piece))
		{
			RemoveChild(piece);
			piece.QueueFree();
			_armies.Remove(province);
		}

		_armySites.Remove(province);
		if (!standing)
		{
			return;
		}

		// Exactly where the men are, and nowhere near it. This used to stand them a little north-west
		// of the town, back when a banner meant "this county has men in it" — now it means "the men
		// are HERE", and an army drawn fifty paces from where it was sent is a map that argues with
		// the order the lord just gave.
		Vector2 site = seatPixel;
		if (_map.HeightAt(site) <= _map.WaterLine + 0.3f)
		{
			return;
		}

		Node3D man = Models.Instance(ArmyModel);
		if (man == null)
		{
			return;
		}

		var held = new Node3D();
		AddChild(held);
		_armies[province] = held;
		_armySites[province] = site;
		held.AddChild(man);

		// Scaled by his own height to the figure this map draws a man at, and stood on his feet
		// rather than sunk to the waist — the same rule the cattle are placed by.
		float scale = ArmyHeight / Mathf.Max(0.01f, Models.HeightOf(ArmyModel));
		Aabb bounds = Models.MeshOf(ArmyModel).GetAabb();
		man.Transform = BuildingTransform(site, scale, yaw: Mathf.Pi * 0.25f)
			.Translated(Vector3.Up * (-bounds.Position.Y * scale));
	}

	public void SetFortification(string province, Vector2 seatPixel, string fort)
	{
		if (_forts.TryGetValue(province, out Node3D standing))
		{
			RemoveChild(standing); // as above: gone this frame, not at the end of it
			standing.QueueFree();
			_forts.Remove(province);
		}

		Works plan = PlanFor(fort);
		if (plan == null)
		{
			return; // an open village, or a key this map has no shapes for
		}

		var works = new Node3D();
		AddChild(works);
		_forts[province] = works;

		// South-east of the town and clear of its houses: near enough to read as this place's
		// castle, far enough that the two are not one heap under the province's pin.
		Vector2 site = Clamped(seatPixel, Mathf.Pi * 0.25f, 62f);
		if (_map.HeightAt(site) <= _map.WaterLine + 0.3f)
		{
			return; // nowhere to stand
		}

		_clearings.Add((site, 26f));

		Node3D castle = Models.Instance(plan.Model);
		if (castle == null)
		{
			return;
		}

		works.AddChild(castle);
		castle.Transform = BuildingTransform(site, plan.Size, yaw: Mathf.Pi * 0.25f);
	}

	/// <summary>The pack models about a metre tall where this map's props are two or three, so every
	/// scatter of them is grown on the way in. The transforms keep their own random spread and tilt;
	/// this only changes how big one of them is.</summary>
	private static List<Transform3D> Grown(List<Transform3D> transforms, float by)
	{
		var grown = new List<Transform3D>(transforms.Count);
		foreach (Transform3D one in transforms)
		{
			grown.Add(one.ScaledLocal(Vector3.One * by));
		}

		return grown;
	}

	/// <summary>Whether a spot is too near a house already standing.</summary>
	private static bool Crowds(List<Vector2> taken, float clearance, Vector2 pixel)
	{
		foreach (Vector2 built in taken)
		{
			if (pixel.DistanceSquaredTo(built) < clearance * clearance)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>A point that many pixels from the seat, kept inside the map.</summary>	/// <summary>A point that many pixels from the seat, kept inside the map.</summary>
	private Vector2 Clamped(Vector2 seat, float angle, float radius) => new(
		Mathf.Clamp(seat.X + Mathf.Cos(angle) * radius, 0, _props.GetWidth() - 1),
		Mathf.Clamp(seat.Y + Mathf.Sin(angle) * radius, 0, _props.GetHeight() - 1));

	/// <summary>Like PropTransform, but for things people built: no tilt, no vertical stretch, and
	/// a yaw the caller can pin down so a roof lands on its own walls.</summary>
	private Transform3D BuildingTransform(Vector2 mapPixel, float scale, float? yaw = null)
	{
		Basis basis = Basis.Identity.Rotated(Vector3.Up, yaw ?? _rng.RandfRange(0, Mathf.Tau));
		return new Transform3D(basis.Scaled(Vector3.One * scale), _map.WorldAt(mapPixel));
	}

	/// <summary>Repaints everything that turns with the year — sown, standing, harvested, bare.
	/// The props themselves never move: a wood is the same wood in December as in June.</summary>
	public void SetSeason(Season season)
	{
		foreach ((StandardMaterial3D material, Color[] bySeason) in _seasonal)
		{
			material.AlbedoColor = bySeason[(int)season];
		}
	}

	// --- placement ---------------------------------------------------------------------------

	private Transform3D PropTransform(Vector2 mapPixel, float scale, float tiltDegrees = 0f)
	{
		Vector3 position = _map.WorldAt(mapPixel);
		Basis basis = Basis.Identity.Rotated(Vector3.Up, _rng.RandfRange(0, Mathf.Tau));
		if (tiltDegrees > 0f)
		{
			// Nothing in a wood grows perfectly plumb; a few degrees kills the plantation look.
			basis = basis.Rotated(Vector3.Right, Mathf.DegToRad(_rng.RandfRange(-tiltDegrees, tiltDegrees)))
				.Rotated(Vector3.Forward, Mathf.DegToRad(_rng.RandfRange(-tiltDegrees, tiltDegrees)));
		}

		// Trees are never all the same height: stretch the vertical separately from the spread.
		var size = new Vector3(scale, scale * _rng.RandfRange(0.85f, 1.25f), scale);
		return new Transform3D(basis.Scaled(size), position);
	}

	/// <summary>One instanced draw per piece. <paramref name="liftY"/> raises the piece in the
	/// prop's own space (primitive meshes are centred on their origin, so without it half of every
	/// tree sits underground), and the jitter gives each instance its own shade — a plain
	/// brightness the shader multiplies the material's colour by, which is what lets
	/// <see cref="SetSeason"/> repaint a whole scatter by setting that one colour.
	/// <paramref name="seasonColors"/>, when given, is the four seasons' colours for this piece.</summary>
	private void AddScatter(Mesh mesh, Color? color, List<Transform3D> transforms, float liftY = 0f,
		float colorJitter = 0f, Color[] seasonColors = null, bool castsShadow = true,
		Node3D parent = null)
	{
		if (transforms.Count == 0)
		{
			return;
		}

		var multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = mesh,
			UseColors = colorJitter > 0f,
			InstanceCount = transforms.Count,
		};
		for (int i = 0; i < transforms.Count; i++)
		{
			multiMesh.SetInstanceTransform(i, transforms[i].TranslatedLocal(Vector3.Up * liftY));
			if (colorJitter > 0f)
			{
				float shade = _rng.RandfRange(1f - colorJitter, 1f + colorJitter);
				multiMesh.SetInstanceColor(i, new Color(shade, shade, shade));
			}
		}

		// A model brought in from the pack paints itself — its walls and its roof are different
		// materials on the one mesh, and an override would flatten both to one colour. Only the
		// shapes built here in code need telling what colour to be.
		StandardMaterial3D material = null;
		if (color.HasValue)
		{
			material = new StandardMaterial3D
			{
				AlbedoColor = color.Value,
				Roughness = 0.95f,
				VertexColorUseAsAlbedo = colorJitter > 0f,
			};
			if (seasonColors != null)
			{
				_seasonal.Add((material, seasonColors));
			}
		}

		(parent ?? this).AddChild(new MultiMeshInstance3D
		{
			Multimesh = multiMesh,
			MaterialOverride = material,
			CastShadow = castsShadow
				? GeometryInstance3D.ShadowCastingSetting.On
				: GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	// --- roads -------------------------------------------------------------------------------

	private void BuildRoads()
	{
		var file = GD.Load<Json>(Campaign.Data(RoadsFile));
		if (file?.Data.VariantType != Variant.Type.Array)
		{
			GD.PushWarning("MapDecoration: no roads data; run tools/generate_campaign_map.py");
			return;
		}

		var dirt = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>(TrackTexturePath),
			NormalEnabled = true,
			NormalTexture = GD.Load<Texture2D>(TrackNormalPath),
			AlbedoColor = new Color("9d968a"), // dusty grey-tan, a shade under whatever it crosses
			Roughness = 1.0f,
		};
		foreach (Variant entry in file.Data.AsGodotArray())
		{
			Godot.Collections.Array points = entry.AsGodotDictionary()["points"].AsGodotArray();
			var line = new List<Vector2>();
			foreach (Variant point in points)
			{
				Godot.Collections.Array pair = point.AsGodotArray();
				line.Add(new Vector2((float)pair[0], (float)pair[1]));
			}

			// Every road is a dirt track while the map is being stripped back to the ground it stands
			// on. The paved trunk between the capitals reads as a motorway across an empty island.
			MeshInstance3D road = BuildRoadRibbon(line, dirt, DirtWidth);
			if (road != null)
			{
				AddChild(road);
			}
		}
	}

	/// <summary>Turns a road's centre line into a ribbon that follows the ground. Each point is
	/// re-sampled against the heightmap, because a road drawn between two sampled ends would sink
	/// into every valley it crosses.</summary>
	private MeshInstance3D BuildRoadRibbon(List<Vector2> line, Material material, float width)
	{
		if (line.Count < 2)
		{
			return null;
		}

		var dense = new List<Vector2>();
		for (int i = 0; i < line.Count - 1; i++)
		{
			int steps = Mathf.Max(1, (int)(line[i].DistanceTo(line[i + 1]) / 4f));
			for (int s = 0; s < steps; s++)
			{
				dense.Add(line[i].Lerp(line[i + 1], (float)s / steps));
			}
		}

		dense.Add(line[^1]);

		// Half the road's width, in map pixels: the edges are draped separately, so the offset has to
		// be taken in the map's own coordinates before the ground is sampled.
		float halfWidthPixels = width * 0.5f * _map.PixelsPerUnit;

		// One pair of edge points per station, shared by the quads on both sides of it. Building each
		// segment its own four corners left a seam wherever the road turned, which is what broke the
		// ribbon into patches.
		var left = new Vector3[dense.Count];
		var right = new Vector3[dense.Count];
		for (int i = 0; i < dense.Count; i++)
		{
			// The tangent comes from both neighbours, so the width stays square to the road through
			// a bend instead of pivoting at each station.
			Vector2 ahead = dense[Mathf.Min(i + 1, dense.Count - 1)];
			Vector2 behind = dense[Mathf.Max(i - 1, 0)];
			Vector2 tangent = ahead - behind;
			Vector2 side = (tangent.LengthSquared() < 0.0001f ? Vector2.Right : tangent.Normalized().Orthogonal()) * halfWidthPixels;
			left[i] = _map.WorldAt(dense[i] - side);
			right[i] = _map.WorldAt(dense[i] + side);
		}

		// A road is graded: it cuts the small humps rather than riding over every one of them.
		// Smoothing the draped heights is what keeps the surface from sinking into each wrinkle.
		SmoothHeights(left);
		SmoothHeights(right);

		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);
		float travelled = 0f;
		for (int i = 0; i < dense.Count - 1; i++)
		{
			Vector3 leftA = left[i] + Vector3.Up * RoadLift;
			Vector3 rightA = right[i] + Vector3.Up * RoadLift;
			Vector3 leftB = left[i + 1] + Vector3.Up * RoadLift;
			Vector3 rightB = right[i + 1] + Vector3.Up * RoadLift;

			// UVs run along the road, so the paving repeats down its length instead of the whole
			// texture being stretched over one ribbon.
			float nextTravelled = travelled + leftA.DistanceTo(leftB) / RoadTileLength;
			AddQuad(surface,
				(leftA, new Vector2(0f, travelled)), (rightA, new Vector2(1f, travelled)),
				(rightB, new Vector2(1f, nextTravelled)), (leftB, new Vector2(0f, nextTravelled)));
			travelled = nextTravelled;
		}

		surface.GenerateNormals();
		return new MeshInstance3D
		{
			Mesh = surface.Commit(),
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	/// <summary>Runs a short moving average over a draped edge's heights, so the road reads as a made
	/// thing crossing the ground rather than a sheet shrink-wrapped onto it.</summary>
	private static void SmoothHeights(Vector3[] points)
	{
		var heights = new float[points.Length];
		for (int pass = 0; pass < 2; pass++)
		{
			for (int i = 0; i < points.Length; i++)
			{
				int from = Mathf.Max(i - 2, 0);
				int to = Mathf.Min(i + 2, points.Length - 1);
				float total = 0f;
				for (int j = from; j <= to; j++)
				{
					total += points[j].Y;
				}

				heights[i] = total / (to - from + 1);
			}

			for (int i = 0; i < points.Length; i++)
			{
				points[i].Y = heights[i];
			}
		}
	}

	private static void AddQuad(SurfaceTool surface,
		(Vector3 Position, Vector2 Uv) a, (Vector3 Position, Vector2 Uv) b,
		(Vector3 Position, Vector2 Uv) c, (Vector3 Position, Vector2 Uv) d)
	{
		foreach ((Vector3 position, Vector2 uv) in new[] { a, b, c, a, c, d })
		{
			surface.SetUV(uv);
			surface.AddVertex(position);
		}
	}

	// --- the shapes still built here -------------------------------------------------------------
	// Everything a lord built — cottages, halls, towers, and the trees around them — is a model out
	// of the pack now. What is left in here is the handful of things too small to be worth a model:
	// a beast on a pasture, a heap of cut logs, a working face of stone. The box-and-cone kit the
	// map was first built out of went with the models that replaced it.

	private static Mesh LogPileMesh() =>
		new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.1f, RadialSegments = 6, Rings = 1 };

	private static Mesh QuarryMesh() =>
		new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.4f) };

	private static Mesh MineMesh() =>
		new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f) };
}
