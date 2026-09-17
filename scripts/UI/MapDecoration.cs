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
	// that lands is whatever the mask supports (~8,700 trees, ~2,500 boulders on this map).
	private const int ScatterAttempts = 345000;
	private const float TreeChance = 0.55f;
	private const float BoulderChance = 0.22f;
	// Above this height conifers take over from broadleaf, the way a real treeline works.
	private const float ConiferHeight = 9.0f;
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
	private const float RoadLift = 0.3f;    // clear of the terrain without floating visibly

	/// <summary>What a province works, drawn on its ground. Mirrors the resources
	/// ProvinceDefinition gives capacities for.</summary>
	public enum SiteKind { Grain, Cattle, Wood, Stone, Iron }

	// Broadleaf woods and standing crops are the two things on the ground that a season changes;
	// conifers are evergreen, and stone doesn't care. Each entry is one material and its colour in
	// Spring/Summer/Autumn/Winter order — the Season enum's own order, so it indexes straight in.
	private static readonly Color[] BroadleafBySeason =
		{ new("6d8f3c"), new("4a6b34"), new("a86a28"), new("5a4a37") };
	private static readonly Color[] GrainBySeason =
		{ new("6f8a3f"), new("d4b264"), new("9c8552"), new("8f8b84") };

	private CampaignMap3D _map;
	private Image _props;
	private RandomNumberGenerator _rng;
	private readonly List<(StandardMaterial3D Material, Color[] BySeason)> _seasonal = new();

	public void Build(CampaignMap3D map)
	{
		_map = map;
		_props = GD.Load<Image>(Campaign.Asset(PropsFile));
		_rng = new RandomNumberGenerator();
		_rng.Seed = 20260917; // fixed: the map must look the same every time it loads

		var conifers = new List<Transform3D>();
		var broadleaves = new List<Transform3D>();
		var boulders = new List<Transform3D>();

		for (int i = 0; i < ScatterAttempts; i++)
		{
			var pixel = new Vector2(_rng.RandfRange(0, _props.GetWidth() - 1), _rng.RandfRange(0, _props.GetHeight() - 1));
			Color density = _props.GetPixel((int)pixel.X, (int)pixel.Y);
			if (density.R > 0.05f && _rng.Randf() < density.R * TreeChance)
			{
				Transform3D tree = PropTransform(pixel, _rng.RandfRange(0.7f, 1.4f), tiltDegrees: 4f);
				bool isHigh = _map.HeightAt(pixel) > ConiferHeight;
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
		AddScatter(TrunkMesh(), new Color("4a3524"), conifers, liftY: 0.35f, castsShadow: false);
		AddScatter(LowerConeMesh(), new Color("293b2a"), conifers, liftY: 1.25f, colorJitter: 0.24f);
		AddScatter(UpperConeMesh(), new Color("314a33"), conifers, liftY: 2.1f, colorJitter: 0.24f,
			castsShadow: false);

		AddScatter(TrunkMesh(), new Color("53402c"), broadleaves, liftY: 0.4f, castsShadow: false);
		AddScatter(BroadleafCrownMesh(), BroadleafBySeason[(int)Season.Summer], broadleaves,
			liftY: 1.35f, colorJitter: 0.2f, seasonColors: BroadleafBySeason);

		AddScatter(BoulderMesh(), new Color("77736e"), boulders, colorJitter: 0.12f, castsShadow: false);

		BuildRoads();
	}

	/// <summary>Drops a working site — a quarry, a pasture, a lumber camp — on the ground around a
	/// province's seat. The page calls this from each province's authored capacities, so what the
	/// map shows and what the economy simulates are the same fact.</summary>
	public void AddSite(Vector2 seatPixel, SiteKind kind, float weight)
	{
		// A handful, never a field of them: these mark what a province works, they are not the
		// industry itself. Fields and pastures read as clutter long before quarries do.
		bool isFarmed = kind is SiteKind.Grain or SiteKind.Cattle;
		int count = (isFarmed ? 2 : 3) + (weight > (isFarmed ? 80f : 70f) ? 2 : 0);

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

			// Crops and cattle need the open, level ground the props mask already marks out;
			// quarries and mines can sit anywhere.
			if (isFarmed && _props.GetPixel((int)pixel.X, (int)pixel.Y).B < 0.25f)
			{
				continue;
			}

			transforms.Add(PropTransform(pixel, _rng.RandfRange(0.9f, 1.15f)));
		}

		(Mesh mesh, Color color) = kind switch
		{
			SiteKind.Grain => (FieldMesh(), GrainBySeason[(int)Season.Summer]),
			SiteKind.Cattle => (CattleMesh(), new Color("d8d2c6")),
			SiteKind.Wood => (LogPileMesh(), new Color("6b4c2f")),
			SiteKind.Stone => (QuarryMesh(), new Color("9d9891")),
			_ => (MineMesh(), new Color("4a4440")),
		};
		AddScatter(mesh, color, transforms, colorJitter: 0.1f,
			seasonColors: kind == SiteKind.Grain ? GrainBySeason : null);
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
	private void AddScatter(Mesh mesh, Color color, List<Transform3D> transforms, float liftY = 0f,
		float colorJitter = 0f, Color[] seasonColors = null, bool castsShadow = true)
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

		var material = new StandardMaterial3D
		{
			AlbedoColor = color,
			Roughness = 0.95f,
			VertexColorUseAsAlbedo = colorJitter > 0f,
		};
		if (seasonColors != null)
		{
			_seasonal.Add((material, seasonColors));
		}

		AddChild(new MultiMeshInstance3D
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
		var paved = new StandardMaterial3D
		{
			AlbedoTexture = GD.Load<Texture2D>(PavedTexturePath),
			NormalEnabled = true,
			NormalTexture = GD.Load<Texture2D>(PavedNormalPath),
			Roughness = 0.85f,
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

			bool isPaved = entry.AsGodotDictionary()["kind"].AsString() == "paved";
			MeshInstance3D road = BuildRoadRibbon(line, isPaved ? paved : dirt, isPaved ? PavedWidth : DirtWidth);
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

	// --- placeholder meshes -------------------------------------------------------------------

	private static Mesh TrunkMesh() =>
		new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.12f, Height = 0.8f, RadialSegments = 5, Rings = 1 };

	// Two stacked cones read as a conifer; one cone reads as a traffic cone.
	private static Mesh LowerConeMesh() =>
		new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.55f, Height = 1.5f, RadialSegments = 7, Rings = 1 };

	private static Mesh UpperConeMesh() =>
		new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.34f, Height = 1.2f, RadialSegments = 7, Rings = 1 };

	private static Mesh BroadleafCrownMesh() =>
		new SphereMesh { Radius = 0.66f, Height = 0.78f, RadialSegments = 7, Rings = 3 };

	private static Mesh BoulderMesh() =>
		new SphereMesh { Radius = 0.4f, Height = 0.55f, RadialSegments = 6, Rings = 3 };

	private static Mesh FieldMesh() =>
		new BoxMesh { Size = new Vector3(1.7f, 0.08f, 1.2f) };

	private static Mesh CattleMesh() =>
		new BoxMesh { Size = new Vector3(0.6f, 0.4f, 0.3f) };

	private static Mesh LogPileMesh() =>
		new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.1f, RadialSegments = 6, Rings = 1 };

	private static Mesh QuarryMesh() =>
		new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.4f) };

	private static Mesh MineMesh() =>
		new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f) };
}
