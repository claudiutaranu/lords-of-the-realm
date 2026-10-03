using System.Collections.Generic;
using Godot;

/// <summary>A county's fields drawn as they stand this season: the plots coloured by what they are
/// given to, with the corn (Crops) and the cattle (Herd) set on them.</summary>
public partial class MapDecoration
{
	// A standing crop's colour in Spring/Summer/Autumn/Winter order — the Season enum's own order, so
	// it indexes straight in. (The broadleaves turn through tree-foliage.gdshader instead.) Summer's
	// corn is in full green leaf; it turns gold only in autumn, when it is ready for the sickle.
	private static readonly Color[] GrainBySeason =
		{ new("8fb052"), new("5f8f33"), new("e3bf5a"), new("8f8b84") };

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
	// Ground laid waste — by a flood, a drought or soldiers — churned black, darker than any earth a
	// plough turns, so a lord sees from the map which field he has to send the reclaimers to. It
	// takes no snow: a field that black under a white county is the one thing on it to mend.
	private static readonly Color[] WasteBySeason =
		{ new("221d18"), new("241e18"), new("231c16"), new("2a2520") };

	/// <summary>One node per province's fields, so turning a field over — or a season turning —
	/// redraws that province's land alone.</summary>
	private readonly Dictionary<string, Node3D> _land = new();

	/// <summary>What each province's fields were last drawn from. The page redraws every county's land
	/// whenever anything may have changed — a march, a battle, a season — and most of them have not;
	/// rebuilding a county's plots, corn and herd to draw the same thing again is the one cost here
	/// worth not paying.</summary>
	private readonly Dictionary<string, string> _landDrawn = new();

	public void SetFields(string name, Vector2 seatPixel, ProvinceEconomy province, Season season)
	{
		FieldUse[] uses = province.Fields;
		string drawn = $"{season} {province.StandingCrop} {province.Cattle} {province.Soil} {string.Join(',', uses)}";
		if (_landDrawn.GetValueOrDefault(name) == drawn)
		{
			return;
		}

		_landDrawn[name] = drawn;
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
		int wanted = province.FieldsUnder(FieldUse.Grain) * Husbandry.MostSacksAField * Husbandry.CropPerSack;
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
			AddPlot(surface, plots[field], yaw, GroundOf(use, season, (province.Soil + 100) / 200f),
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

	/// <summary>What a plot's ground looks like: what it is under, what season it stands in, and how
	/// much heart the land has left. The last of those is the whole point of the fallow field — a
	/// lord who crops the same acre four years running should be able to see it going gray from the
	/// map, not only find out in a bad autumn.</summary>
	private static Color GroundOf(FieldUse use, Season season, float heart)
	{
		if (use is FieldUse.Waste or FieldUse.Reclaiming)
		{
			return WasteBySeason[(int)season];
		}

		Color ground = use switch
		{
			FieldUse.Grain => TilledBySeason[(int)season],
			FieldUse.Pasture => PastureBySeason[(int)season],
			_ => FallowBySeason[(int)season],
		};

		return ground.Lerp(Spent, (1f - heart) * 0.75f);
	}
}
