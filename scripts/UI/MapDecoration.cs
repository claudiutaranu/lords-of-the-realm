using System.Collections.Generic;
using Godot;

/// <summary>Everything standing on the terrain: woods, ploughed fields, and the working sites that
/// show what a province actually produces. (The roads are part of the ground itself — the
/// generator paints them into the surface map.)
///
/// Scattered props are drawn as MultiMesh instances — thousands of trees cost a handful of draw
/// calls — and placed from the campaign's map-props.png (R woodland, B open farmland),
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

	// Attempts, not instances: each one rolls against the density at a random pixel, so the count
	// that lands is whatever the mask supports — ~3,800 trees on this map. The
	// woods have been thinned twice from where they started: a forest that covers the ground reads
	// as a texture, and what the map wants is stands of trees with country between them.
	private const int ScatterAttempts = 345000;
	private const float TreeChance = 0.150f;

	// How big the two kinds of tree stand, and the largest chance makes one of them. Named rather
	// than written into the scatter, because the fields have to be able to ask: ground cleared to
	// the width of a trunk is still ground with a crown hanging over it.
	// The pack ships five trees and this map used two of them, which is how a wood comes out as one
	// trunk printed four thousand times. The "Group" models are a whole stand on a single mesh —
	// five or six trees for the price of one instance — so they carry the bulk of the canopy and
	// the single trees fill in around them.
	// The baked trees (tools/decimate_meshy.py): each a few thousand triangles cut down from a
	// multi-million-triangle original, with its look baked into its own texture. Evergreens keep
	// their own material; the broadleaves are drawn through tree-foliage.gdshader so their leaves
	// turn with the year.
	private static readonly string[] Conifers = { "trees/evergreen-pine", "trees/evergreen-pine", "trees/cypress-tree" };
	private static readonly string[] Broadleaves = { "trees/oak-tree", "trees/whispering-oak", "trees/verdant-guardian" };
	private const string FoliageShaderPath = "res://assets/shaders/tree-foliage.gdshader";
	/// <summary>What the file holding a model's baked normals is called, beside the model itself.</summary>
	private const string ReliefSuffix = "-normal.png";
	// How many of the trees that land are a whole stand rather than one trunk.
	private const float StandShare = 0.24f;
	// The baked trees stand about 1.9 units tall in their own space; these bring them to the height
	// the old pack's trees stood at on this map. A "stand" is an old, big tree rather than a clump.
	private const float ConiferSize = 1.45f;
	private const float BroadleafSize = 1.35f;
	private const float StandSize = 1.3f;

	// The scatter is drawn in square chunks of this many world units rather than as one piece per
	// kind of thing. One MultiMesh over the whole island is culled and given its level of detail as
	// a single object — by its nearest point, so every tree on the map was drawn at full detail even
	// when the camera was across the sea from it. In chunks, the far woods drop to their coarse LODs
	// and the woods off the edge of the screen are not drawn at all.
	private const float ChunkSize = 24f;
	private const float TreeSpread = 1.4f;
	// Above this share of the map's relief conifers take over from broadleaf, the way a real
	// treeline works. A share and not a height: the map can be flattened or raised without the
	// treeline staying behind at an altitude nothing reaches any more.
	private const float ConiferLine = 0.375f;

	/// <summary>What a province digs out of its ground. Grain and the herd are not in here: those
	/// are worked on fields, and a field is a plot on the map rather than a scattered prop.</summary>
	public enum SiteKind { Wood, Stone, Iron }

	// A standing crop's colour in Spring/Summer/Autumn/Winter order — the Season enum's own order, so
	// it indexes straight in. (The broadleaves turn through tree-foliage.gdshader instead.)
	private static readonly Color[] GrainBySeason =
		{ new("7fa049"), new("dcbb63"), new("e3bf5a"), new("8f8b84") };

	// What a field is drawn as: a plot this many world units on a side, with a hedge band around it,
	// laid on a lattice of cells this far apart. Sized against the cottages beside it — a plot is a
	// strip a family works, not an estate, and two of them side by side should read as two fields
	// rather than as two counties. The gap is the hedge between neighbours, so it is nearly nothing:
	// what makes a patchwork is plots that touch.
	// A field's side, in world units. Smaller than it was: at 2.6 a county's ten fields were a
	// patchwork a fifth of the way across the island, and the plots read as tiles laid on the map
	// rather than as fields in it.
	private const float PlotSize = 2.0f;
	private const float PlotGap = 0.25f;
	private const float PlotBorder = 0.07f;  // share of the plot its hedge band takes, each side
	private const float PlotLift = 0.12f;    // clear of the terrain
	private const int PlotRings = 7;         // lattice cells searched either way of the seat

	/// <summary>The ground a seat keeps for itself, in map pixels. The town's houses stand in a
	/// ring out to TownRing (AddSettlement), and the castle is raised CastleDistance off the seat at
	/// CastleBearing with CastleReach of ground round it (SetFortification). The fields are laid out
	/// from the seat outwards and used to start half a cell from the pin — in the middle of the town
	/// — because the only thing keeping them off it was the clearing the town registers when it is
	/// built, and that is built after the fields and may not be built at all. So the fields read the
	/// same figures and keep off that ground themselves, whatever else has run.</summary>
	public const float TownRing = 38f;
	private const float TownGap = 6f;        // a lane between the last house and the first furrow
	private const float CastleBearing = Mathf.Pi * 0.25f;
	private const float CastleDistance = 62f;
	private const float CastleReach = 26f;
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
	/// <summary>How many figures a county's men are drawn as, and where the second and third stand
	/// beside the first, in world units. A banner used to carry its number on a shield; the size of
	/// the thing on the ground says it now, which is what a lord on a hilltop actually reads — one
	/// man, a pair, or a body of them.</summary>
	private static readonly int[] ArmyAbreast = { 60, 160 };
	private static readonly Vector3[] ArmyRanks =
	{
		Vector3.Zero,
		new(1.7f, 0f, 0.7f),
		new(-1.5f, 0f, 0.9f),
	};
	private const string SoldierShaderPath = "res://assets/shaders/soldier.gdshader";
	/// <summary>How the banner is found on the figure, in shares of its height and in its own units:
	/// the cloth is what hangs clear of the pole above his shield, grown from its outer edge across
	/// the mesh so the whole pennant goes with it and the pole does not (see ArmyMesh). The rest is
	/// where his legs meet his body, which is where the marching swings them from.</summary>
	private const float BannerFloor = 0.45f;
	private const float BannerSeed = 0.52f;
	private const float BannerSeedReach = 0.5f;
	private const float PoleClearance = 0.08f;
	private const float HipHeight = 0.30f;
	private const float SoleHeight = 0.05f;

	/// <summary>How near a click has to land to take hold of a banner, in map pixels. Generous: the
	/// figure is tall and thin, and a lord jabbing at his own army should not have to hit the pole.</summary>
	private const float ArmyReach = 34f;

	/// <summary>How long the banner takes to cover one bead of the road it was sent along. Slow
	/// enough to be a march and not a jump, quick enough that a lord who has ordered four of them is
	/// not waiting on the map.</summary>
	public const float StrideSeconds = 0.16f;

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
	// The broadleaves' leaves, which take the season as a shader parameter rather than a colour.
	private readonly List<ShaderMaterial> _seasonalLeaves = new();
	/// <summary>One node per province's walls, so a finished build can replace them on their own.</summary>
	private readonly Dictionary<string, Node3D> _forts = new();
	/// <summary>Each seat's village, by province, so a conquest can hand its banners over, and where
	/// its yard lies on the map, which is the ground a lord walks into the town from.</summary>
	private readonly Dictionary<string, GeometryInstance3D> _settlements = new();
	private readonly Dictionary<string, Vector2> _settlementSites = new();
	private ShaderMaterial _settlementMaterial;
	/// <summary>Which way the village model's flag flies as it was made, on its own ground plane.</summary>
	private float _flagRest;
	private readonly Dictionary<string, Node3D> _armies = new();
	/// <summary>The figure every army is drawn with: the model's own mesh with the banner's sway
	/// written into its vertex colours, and the one material they all share.</summary>
	private ArrayMesh _armyMesh;
	private ShaderMaterial _armyMaterial;
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

	/// <summary>Opens the map. The roads are not laid here: they are painted into the ground itself,
	/// by the generator's surface map. The woods are not sown here either: a settlement has to
	/// be standing before them, or the trees grow through its roofs. The page calls
	/// <see cref="Sow"/> once it has put every province on the ground.</summary>
	public void Build(CampaignMap3D map)
	{
		_map = map;
		_props = GD.Load<Image>(Campaign.Asset(PropsFile));
		_rng = new RandomNumberGenerator();
		_rng.Seed = 20260917; // fixed: the map must look the same every time it loads
	}

	/// <summary>Sows every wood on the map, keeping clear of whatever has already been built. Called
	/// after the settlements, which is the whole point of it being its own call — a forest sown first
	/// grows straight through the villages.</summary>
	public void Sow()
	{
		var conifers = new List<Transform3D>[Conifers.Length];
		var broadleaves = new List<Transform3D>[Broadleaves.Length];
		for (int kind = 0; kind < Conifers.Length; kind++)
		{
			conifers[kind] = new List<Transform3D>();
		}

		for (int kind = 0; kind < Broadleaves.Length; kind++)
		{
			broadleaves[kind] = new List<Transform3D>();
		}

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
				bool conifer = isHigh ? _rng.Randf() < 0.92f : _rng.Randf() < 0.55f;
				// Now and then an old tree, bigger than the ones round it: a wood of one size of tree
				// reads as a plantation.
				if (_rng.Randf() < StandShare)
				{
					tree = tree.ScaledLocal(Vector3.One * StandSize);
				}

				// Three silhouettes of each, so no two neighbouring trees are the same drawing.
				if (conifer)
				{
					conifers[_rng.RandiRange(0, Conifers.Length - 1)].Add(tree);
				}
				else
				{
					broadleaves[_rng.RandiRange(0, Broadleaves.Length - 1)].Add(tree);
				}
			}
		}

		// One mesh per tree: the model carries its trunk and its crown as one drawing, so a wood costs
		// a third of the draw calls it did when a tree was three meshes over one transform list.
		//
		// Every tree goes through the foliage shader — the evergreens for their relief, the broadleaves
		// for that and for the year: it picks the leaves out of the baked texture by their colour and
		// gives those the season, and leaves the bark alone.
		for (int kind = 0; kind < Conifers.Length; kind++)
		{
			AddScatter(Models.MeshOf(Conifers[kind]), null, Grown(conifers[kind], ConiferSize),
				overrideMaterial: Foliage(Conifers[kind], evergreen: true));
		}

		for (int kind = 0; kind < Broadleaves.Length; kind++)
		{
			AddScatter(Models.MeshOf(Broadleaves[kind]), null, Grown(broadleaves[kind], BroadleafSize),
				overrideMaterial: Foliage(Broadleaves[kind], evergreen: false));
		}
	}

	/// <summary>How a tree is drawn: its baked colour, the relief baked out of the original beside it
	/// (tools/decimate_meshy.py writes both), and whether it keeps its leaves through the year.</summary>
	private ShaderMaterial Foliage(string model, bool evergreen)
	{
		var leaves = new ShaderMaterial { Shader = GD.Load<Shader>(FoliageShaderPath) };
		if (Models.MeshOf(model)?.SurfaceGetMaterial(0) is BaseMaterial3D baked)
		{
			leaves.SetShaderParameter("albedo_texture", baked.AlbedoTexture);
		}

		leaves.SetShaderParameter("relief_texture", GD.Load<Texture2D>($"res://assets/models/{model}{ReliefSuffix}"));
		leaves.SetShaderParameter("evergreen", evergreen ? 1f : 0f);
		_seasonalLeaves.Add(leaves);
		return leaves;
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
						|| OnSeatGround(seat, Centre(square))
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

	/// <summary>Whether a plot would touch the ground the seat keeps for its town or its castle.
	/// Judged on the plot's reach, half its diagonal, so no corner of a field clips a roof.</summary>
	private bool OnSeatGround(Vector2 seat, Vector2 plot)
	{
		float reach = PlotSize * 0.71f * _map.PixelsPerUnit;
		if (plot.DistanceTo(seat) < TownRing + TownGap + reach)
		{
			return true;
		}

		return plot.DistanceTo(Clamped(seat, CastleBearing, CastleDistance)) < CastleReach + reach;
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
	private float CrownReach
	{
		get
		{
			float widest = 0f;
			foreach (string tree in Conifers)
			{
				widest = Mathf.Max(widest, Models.FootprintOf(tree) * ConiferSize);
			}

			foreach (string tree in Broadleaves)
			{
				widest = Mathf.Max(widest, Models.FootprintOf(tree) * BroadleafSize);
			}

			// An old tree is grown past the rest; its crown is the one that reaches furthest.
			return 0.5f * _map.PixelsPerUnit * TreeSpread * widest * StandSize;
		}
	}

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

	/// <summary>The village every seat stands in: a Meshy hamlet cut down by tools/decimate_meshy.py,
	/// drawn through settlement.gdshader so its banners fly the owner's colour and it takes the season.
	/// Its yard is sized to fill the town's ring — the fields start a lane beyond it (TownGap) and the
	/// castle stands off to one side (CastleBearing) — and a hamlet stands a size down from a town.
	/// It is sunk a hair into the ground, so the thin sheet it stands on reads as the ground itself and
	/// not as a tray laid on it.</summary>
	private const string HamletModel = "settlements/blue-banner-hamlet";
	private const string SettlementShaderPath = "res://assets/shaders/settlement.gdshader";
	private const float HamletShare = 0.85f;

	/// <summary>How much of the town's ring the village itself fills. The ring is the town for the rules
	/// (where a march halts on its gate, where its fields start); the village drawn in it stands a
	/// fifth smaller than that, so it sits on the map as a place and not as a stamp over the county.</summary>
	private const float VillageSize = 0.8f;
	private const float SettlementSink = 0.04f;
	/// <summary>How far a village may stand off the wind. Every banner flies with the prevailing
	/// wind, so the village is turned to put its flag in it — but eight villages all turned the one
	/// way read as one stamp, so each is set up to this far off, and the shader turns the cloth the
	/// rest of the way round its pole. Kept small because the flag, turned far enough, flies through
	/// the church tower.</summary>
	private static readonly float FlagJitter = Mathf.DegToRad(20f);

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

	/// <summary>Raises the village on a province's seat, flying its lord's colour — or, for a seat
	/// that already has one, hands the banners to whoever holds it now. One node per seat rather than
	/// a scatter, since each flies its own colour; they share one material, and the colour is a
	/// per-instance parameter on it.</summary>
	public void AddSettlement(string province, Vector2 seatPixel, Settlement kind, Color lord)
	{
		if (_settlements.TryGetValue(province, out GeometryInstance3D standing))
		{
			standing.SetInstanceShaderParameter("lord_color", lord);
			return;
		}

		Mesh mesh = Models.MeshOf(HamletModel);
		if (mesh == null)
		{
			return;
		}

		if (_settlementMaterial == null)
		{
			_settlementMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(SettlementShaderPath) };
			if (mesh.SurfaceGetMaterial(0) is BaseMaterial3D baked)
			{
				_settlementMaterial.SetShaderParameter("albedo_texture", baked.AlbedoTexture);
			}

			_settlementMaterial.SetShaderParameter("relief_texture",
				GD.Load<Texture2D>($"res://assets/models/{HamletModel}{ReliefSuffix}"));

			// The flag flies from the pole, and the pole is the model's highest point — its finial.
			Vector3 pole = Vector3.Down * float.MaxValue;
			foreach (Vector3 vertex in mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				pole = vertex.Y > pole.Y ? vertex : pole;
			}

			_settlementMaterial.SetShaderParameter("flag_pole", new Vector2(pole.X, pole.Z));
			_flagRest = FlagRest(mesh, pole);
		}

		float yard = 2f * TownRing / _map.PixelsPerUnit * VillageSize * (kind == Settlement.Hamlet ? HamletShare : 1f);
		float scale = yard / Models.FootprintOf(HamletModel);
		// Turned so its flag flies with the wind, give or take its own few degrees (FlagJitter). A turn
		// about y takes a direction at angle a on the ground (atan2 of z over x) to a - yaw.
		Vector2 wind = MapClouds.PrevailingWind;
		float jitter = (PlotYaw(province) / Mathf.Tau - 0.5f) * 2f * FlagJitter;
		float yaw = _flagRest - Mathf.Atan2(wind.Y, wind.X) + jitter;
		var village = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = _settlementMaterial,
			Transform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, yaw).Scaled(Vector3.One * scale),
				_map.WorldAt(seatPixel) + (Vector3.Down * SettlementSink)),
		};
		AddChild(village);
		village.SetInstanceShaderParameter("lord_color", lord);
		village.SetInstanceShaderParameter("flag_turn", jitter);
		_settlements[province] = village;
		_settlementSites[province] = seatPixel;

		// Wide of the yard, so a village sits in a clearing rather than in a thicket.
		_clearings.Add((seatPixel, TownRing + 14f));
	}

	/// <summary>Which way a village model's flag flies as it was made: the angle on its ground plane
	/// (atan2 of z over x) from the pole to the middle of the cloth. The cloth is read off the texture's
	/// alpha, where tools/decimate_meshy.py baked how far along the flag each vertex lies, and each
	/// vertex counts for as much as it sways, so the fly end decides it more than the hoist.</summary>
	private static float FlagRest(Mesh mesh, Vector3 pole)
	{
		if (mesh.SurfaceGetMaterial(0) is not BaseMaterial3D { AlbedoTexture: Texture2D texture })
		{
			return 0f;
		}

		Image picture = texture.GetImage();
		if (picture.IsCompressed())
		{
			picture.Decompress();
		}

		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
		Vector2 fly = Vector2.Zero;
		for (int i = 0; i < vertices.Length; i++)
		{
			float sway = picture.GetPixel(
				Mathf.Clamp((int)(uvs[i].X * picture.GetWidth()), 0, picture.GetWidth() - 1),
				Mathf.Clamp((int)(uvs[i].Y * picture.GetHeight()), 0, picture.GetHeight() - 1)).A;
			fly += new Vector2(vertices[i].X - pole.X, vertices[i].Z - pole.Z) * sway;
		}

		return Mathf.Atan2(fly.Y, fly.X);
	}

	/// <summary>Walks a county's banner along a road to where it was sent, and says when it has got
	/// there. The piece is not moved and then the map redrawn — it is the same figure, carried along
	/// the same line of beads the lord was shown, so what he ordered and what he watches are plainly
	/// the same thing.
	///
	/// The men have already moved in the ledger by the time this runs. This is the walk, not the
	/// march: if it were the other way round, a lord could close the game mid-stride and find his
	/// army had never left.</summary>
	public void WalkArmy(string army, List<Vector2> road, System.Action arrived)
	{
		if (!_armies.TryGetValue(army, out Node3D piece) || piece.GetChildCount() == 0
			|| road.Count == 0)
		{
			arrived();
			return;
		}

		float stands = piece.Position.Y - _map.WorldAt(_armySites.GetValueOrDefault(army)).Y;
		foreach (Node rank in piece.GetChildren())
		{
			// soldier.gdshader swings their legs while this stands.
			((GeometryInstance3D)rank).SetInstanceShaderParameter("walking", 1f);
		}

		Tween walk = CreateTween();
		foreach (Vector2 step in road)
		{
			Vector3 ground = _map.WorldAt(step) + (Vector3.Up * stands);
			// Turned to face the next bead before he walks to it: a man marching sideways up a road is
			// worse than one who does not move his legs at all. The model is made facing +z.
			walk.TweenCallback(Callable.From(() =>
			{
				Vector3 ahead = ground - piece.Position;
				if (ahead.LengthSquared() > 0.0001f)
				{
					piece.Rotation = new Vector3(0f, Mathf.Atan2(ahead.X, ahead.Z), 0f);
				}
			}));
			walk.TweenProperty(piece, "position", ground, StrideSeconds);
		}

		walk.TweenCallback(Callable.From(() =>
		{
			foreach (Node rank in piece.GetChildren())
			{
				((GeometryInstance3D)rank).SetInstanceShaderParameter("walking", 0f);
			}

			arrived();
		}));
	}

	/// <summary>Whose village stands under this map pixel, or nothing. The town is walked into from
	/// its own streets: a county is a great deal of ground, and a click on the far side of its woods
	/// used to open its market square.</summary>
	public string TownAt(Vector2 pixel)
	{
		foreach ((string province, Vector2 yard) in _settlementSites)
		{
			if (pixel.DistanceTo(yard) <= TownRing)
			{
				return province;
			}
		}

		return "";
	}

	/// <summary>Which company is standing under this map pixel, by the key the page named it with, or
	/// nothing. The banner is picked off its own position rather than off a collision body, the same
	/// way the fields are: one distance against a handful of armies costs nothing, and a body on
	/// every figure would have to be built and thrown away every time a county raised or lost a man.
	///
	/// The nearest one wins, so two companies mustered beside the same town are told apart by which
	/// of them the lord actually pointed at.</summary>
	public string ArmyAt(Vector2 pixel)
	{
		string nearest = "";
		float reach = ArmyReach;
		foreach ((string army, Vector2 site) in _armySites)
		{
			float off = pixel.DistanceTo(site);
			if (_armies.ContainsKey(army) && off <= reach)
			{
				nearest = army;
				reach = off;
			}
		}

		return nearest;
	}

	/// <summary>Takes down every banner that is not in the list — a company merged into another,
	/// disbanded, or killed to the last man. Called with everything that IS standing, so a banner
	/// nobody claims cannot be left on the map.</summary>
	public void RetireArmies(System.Collections.Generic.ICollection<string> standing)
	{
		foreach (string army in new List<string>(_armies.Keys))
		{
			if (!standing.Contains(army))
			{
				SetArmy(army, Vector2.Zero, false, Colors.White, 0);
			}
		}
	}

	/// <summary>One company, standing where it is. A banner to an army and not to a county: a lord
	/// who raises a second company sees a second banner, and can take hold of either of them.
	///
	/// Not a figure per hundred men — the ranks say roughly how big it is, and the panel behind the
	/// right button says exactly.</summary>
	public void SetArmy(string army, Vector2 seatPixel, bool standing, Color lord, int men)
	{
		if (_armies.TryGetValue(army, out Node3D piece))
		{
			RemoveChild(piece);
			piece.QueueFree();
			_armies.Remove(army);
		}

		_armySites.Remove(army);
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

		if (ArmyMesh() == null)
		{
			return;
		}

		// Scaled by his own height to the figure this map draws a man at, and stood on his feet
		// rather than sunk to the waist — the same rule the cattle are placed by.
		float scale = ArmyHeight / Mathf.Max(0.01f, Models.HeightOf(ArmyModel));
		Aabb bounds = Models.MeshOf(ArmyModel).GetAabb();

		// The company stands on one node: it is one army, it marches as one, and the map moves it as
		// one. The figures are its ranks, set out beside each other.
		var held = new Node3D { Position = _map.WorldAt(site) + (Vector3.Up * (-bounds.Position.Y * scale)) };
		AddChild(held);
		_armies[army] = held;
		_armySites[army] = site;

		for (int rank = 0; rank < Ranks(men); rank++)
		{
			var man = new MeshInstance3D { Mesh = _armyMesh, MaterialOverride = _armyMaterial };
			held.AddChild(man);
			man.SetInstanceShaderParameter("lord_color", lord);
			man.SetInstanceShaderParameter("walking", 0f);
			// One standard to a company: the men behind it carry their shafts and nothing on them.
			man.SetInstanceShaderParameter("banner", rank == 0 ? 1f : 0f);
			// A shade apart in size and bearing: three of the same figure on the same angle is one
			// figure drawn three times.
			man.Transform = new Transform3D(
				Basis.Identity.Rotated(Vector3.Up, (Mathf.Pi * 0.25f) + (rank * 0.22f))
					.Scaled(Vector3.One * scale * (1f - (rank * 0.06f))),
				ArmyRanks[rank]);
		}
	}

	/// <summary>How many figures a body of men is drawn as.</summary>
	private static int Ranks(int men) => men >= ArmyAbreast[1] ? 3 : men >= ArmyAbreast[0] ? 2 : 1;

	/// <summary>The figure, built once: the model's mesh with the banner's sway in its vertex colours
	/// — 0 at the pole and 1 at the fly end — which is what soldier.gdshader waves it by.
	///
	/// Which vertices are cloth is a question about the model, and it is answered here rather than
	/// baked into the file because this model came with neither a skeleton nor a mask. The pennant is
	/// found from its outer edge: everything above the shield and further than BannerSeedReach from
	/// the pole can only be cloth, and from there it is grown across the mesh — over the emblem, into
	/// the tails — stopped from spreading down the pole by PoleClearance and off the man by the floor.
	/// The helmet is never taken: it is not joined to the cloth by a single edge.</summary>
	private ArrayMesh ArmyMesh()
	{
		if (_armyMesh != null)
		{
			return _armyMesh;
		}

		Mesh model = Models.MeshOf(ArmyModel);
		if (model == null)
		{
			return null;
		}

		Godot.Collections.Array arrays = model.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
		Aabb bounds = model.GetAabb();
		float height = bounds.Size.Y;
		float floor = bounds.Position.Y + (BannerFloor * height);

		Vector3 top = vertices[0];
		foreach (Vector3 vertex in vertices)
		{
			top = vertex.Y > top.Y ? vertex : top;
		}

		var pole = new Vector2(top.X, top.Z);
		float[] reach = new float[vertices.Length];
		bool[] allowed = new bool[vertices.Length];
		var cloth = new bool[vertices.Length];
		for (int i = 0; i < vertices.Length; i++)
		{
			reach[i] = new Vector2(vertices[i].X, vertices[i].Z).DistanceTo(pole);
			allowed[i] = vertices[i].Y > floor && reach[i] > PoleClearance;
			cloth[i] = vertices[i].Y > bounds.Position.Y + (BannerSeed * height) && reach[i] > BannerSeedReach;
		}

		// The mesh is split at its UV seams, so it is joined back up by position to be walked.
		var joined = new int[vertices.Length];
		var seen = new Dictionary<Vector3I, int>();
		for (int i = 0; i < vertices.Length; i++)
		{
			var key = new Vector3I(Mathf.RoundToInt(vertices[i].X * 10000f), Mathf.RoundToInt(vertices[i].Y * 10000f),
				Mathf.RoundToInt(vertices[i].Z * 10000f));
			if (!seen.TryGetValue(key, out int at))
			{
				at = seen.Count;
				seen[key] = at;
			}

			joined[i] = at;
		}

		var flag = new bool[seen.Count];
		var may = new bool[seen.Count];
		for (int i = 0; i < vertices.Length; i++)
		{
			flag[joined[i]] |= cloth[i];
			may[joined[i]] |= allowed[i];
		}

		for (bool spreading = true; spreading;)
		{
			spreading = false;
			for (int triangle = 0; triangle < indices.Length; triangle += 3)
			{
				if (!flag[joined[indices[triangle]]] && !flag[joined[indices[triangle + 1]]]
					&& !flag[joined[indices[triangle + 2]]])
				{
					continue;
				}

				for (int corner = 0; corner < 3; corner++)
				{
					int at = joined[indices[triangle + corner]];
					if (may[at] && !flag[at])
					{
						flag[at] = true;
						spreading = true;
					}
				}
			}
		}

		float furthest = PoleClearance;
		for (int i = 0; i < vertices.Length; i++)
		{
			furthest = flag[joined[i]] && allowed[i] ? Mathf.Max(furthest, reach[i]) : furthest;
		}

		var sway = new Color[vertices.Length];
		for (int i = 0; i < vertices.Length; i++)
		{
			float along = flag[joined[i]] && allowed[i]
				? Mathf.Clamp((reach[i] - PoleClearance) / Mathf.Max(0.001f, furthest - PoleClearance), 0f, 1f)
				: 0f;
			sway[i] = new Color(along, 0f, 0f);
		}

		arrays[(int)Mesh.ArrayType.Color] = sway;
		_armyMesh = new ArrayMesh();
		_armyMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_armyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(SoldierShaderPath) };
		if (model.SurfaceGetMaterial(0) is BaseMaterial3D painted)
		{
			_armyMaterial.SetShaderParameter("albedo_texture", painted.AlbedoTexture);
			_armyMaterial.SetShaderParameter("normal_texture", painted.NormalTexture);
			_armyMaterial.SetShaderParameter("metal_rough_texture", painted.MetallicTexture);
		}

		_armyMaterial.SetShaderParameter("wind", MapClouds.PrevailingWind);
		_armyMaterial.SetShaderParameter("flag_pole", pole);
		_armyMaterial.SetShaderParameter("flag_rest", FlagRest(vertices, sway, pole));
		_armyMaterial.SetShaderParameter("hip", bounds.Position.Y + (HipHeight * height));
		_armyMaterial.SetShaderParameter("sole", bounds.Position.Y + (SoleHeight * height));
		_armyMaterial.SetShaderParameter("body_side", BodySide(vertices, bounds, height));
		return _armyMesh;
	}

	/// <summary>Which way the banner hangs as the model was made, on its ground plane: the angle from
	/// the pole to the middle of the cloth, each vertex counting for as much as it sways.</summary>
	private static float FlagRest(Vector3[] vertices, Color[] sway, Vector2 pole)
	{
		Vector2 fly = Vector2.Zero;
		for (int i = 0; i < vertices.Length; i++)
		{
			fly += (new Vector2(vertices[i].X, vertices[i].Z) - pole) * sway[i].R;
		}

		return Mathf.Atan2(fly.Y, fly.X);
	}

	/// <summary>Where his legs stand, side to side: everything left of this swings against everything
	/// right of it. Taken off the legs themselves — the pole he carries is well off to one side and
	/// would drag the middle of the man over with it.</summary>
	private static float BodySide(Vector3[] vertices, Aabb bounds, float height)
	{
		float sum = 0f;
		int counted = 0;
		foreach (Vector3 vertex in vertices)
		{
			if (vertex.Y < bounds.Position.Y + (HipHeight * height))
			{
				sum += vertex.X;
				counted++;
			}
		}

		return counted == 0 ? 0f : sum / counted;
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
		Vector2 site = Clamped(seatPixel, CastleBearing, CastleDistance);
		if (_map.HeightAt(site) <= _map.WaterLine + 0.3f)
		{
			return; // nowhere to stand
		}

		_clearings.Add((site, CastleReach));

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

	/// <summary>A point that many pixels from the seat, kept inside the map.</summary>
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

		foreach (ShaderMaterial leaves in _seasonalLeaves)
		{
			leaves.SetShaderParameter("season", (float)(int)season);
		}

		_settlementMaterial?.SetShaderParameter("season", (float)(int)season);
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
		Node3D parent = null, Material overrideMaterial = null)
	{
		if (transforms.Count == 0 || mesh == null)
		{
			return;
		}

		// A model brought in from the pack paints itself — its walls and its roof are different
		// materials on the one mesh, and an override would flatten both to one colour. Only the
		// shapes built here in code need telling what colour to be, and the few models whose
		// material is swapped for a shader of their own.
		Material material = overrideMaterial;
		if (material == null && color.HasValue)
		{
			var painted = new StandardMaterial3D
			{
				AlbedoColor = color.Value,
				Roughness = 0.95f,
				VertexColorUseAsAlbedo = colorJitter > 0f,
			};
			if (seasonColors != null)
			{
				_seasonal.Add((painted, seasonColors));
			}

			material = painted;
		}

		// Sorted into chunks of the map, each its own MultiMesh — see ChunkSize.
		var chunks = new Dictionary<Vector2I, List<Transform3D>>();
		foreach (Transform3D one in transforms)
		{
			var key = new Vector2I(Mathf.FloorToInt(one.Origin.X / ChunkSize), Mathf.FloorToInt(one.Origin.Z / ChunkSize));
			if (!chunks.TryGetValue(key, out List<Transform3D> chunk))
			{
				chunk = new List<Transform3D>();
				chunks[key] = chunk;
			}

			chunk.Add(one);
		}

		foreach (List<Transform3D> chunk in chunks.Values)
		{
			var multiMesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = mesh,
				UseColors = colorJitter > 0f,
				InstanceCount = chunk.Count,
			};
			for (int i = 0; i < chunk.Count; i++)
			{
				multiMesh.SetInstanceTransform(i, chunk[i].TranslatedLocal(Vector3.Up * liftY));
				if (colorJitter > 0f)
				{
					float shade = _rng.RandfRange(1f - colorJitter, 1f + colorJitter);
					multiMesh.SetInstanceColor(i, new Color(shade, shade, shade));
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
