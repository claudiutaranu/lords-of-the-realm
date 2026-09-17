using System.Collections.Generic;
using Godot;

/// <summary>Everything standing on the terrain: woods, boulders, ploughed fields, the roads
/// between neighbouring seats, and the working sites that show what a province actually produces.
///
/// Scattered props are drawn as MultiMesh instances — thousands of trees cost a handful of draw
/// calls — and placed from campaign-map-props.png (R woodland, G boulders, B open farmland),
/// generated alongside the terrain so vegetation lands where the ground supports it: not on cliffs,
/// not on the beach, thicker along province borders. The seed is fixed, so the same realm grows
/// the same forest every run.
///
/// Every mesh here is a primitive placeholder. Swapping in real art means changing the mesh a
/// builder returns; nothing else knows or cares what shape a tree is.</summary>
public partial class MapDecoration : Node3D
{
	private const string PropsPath = "res://assets/ui/campaign-map-props.png";
	private const string RoadsPath = "res://data/map-roads.json";

	private const int ScatterAttempts = 90000;
	private const float TreeChance = 0.55f;
	private const float BoulderChance = 0.35f;
	private const float FieldChance = 0.02f;
	private const float RoadWidth = 0.55f;
	private const float RoadLift = 0.12f;   // clear of the terrain without floating visibly

	/// <summary>What a province works, drawn on its ground. Mirrors the resources
	/// ProvinceDefinition gives capacities for.</summary>
	public enum SiteKind { Grain, Cattle, Wood, Stone, Iron }

	private CampaignMap3D _map;
	private Image _props;
	private RandomNumberGenerator _rng;

	public void Build(CampaignMap3D map)
	{
		_map = map;
		_props = GD.Load<Image>(PropsPath);
		_rng = new RandomNumberGenerator();
		_rng.Seed = 20260917; // fixed: the map must look the same every time it loads

		var trees = new List<Transform3D>();
		var boulders = new List<Transform3D>();
		var fields = new List<Transform3D>();

		for (int i = 0; i < ScatterAttempts; i++)
		{
			var pixel = new Vector2(_rng.RandfRange(0, _props.GetWidth() - 1), _rng.RandfRange(0, _props.GetHeight() - 1));
			Color density = _props.GetPixel((int)pixel.X, (int)pixel.Y);
			if (density.R > 0.05f && _rng.Randf() < density.R * TreeChance)
			{
				trees.Add(PropTransform(pixel, _rng.RandfRange(0.75f, 1.35f)));
			}
			else if (density.G > 0.05f && _rng.Randf() < density.G * BoulderChance)
			{
				boulders.Add(PropTransform(pixel, _rng.RandfRange(0.6f, 1.6f)));
			}
			else if (density.B > 0.35f && _rng.Randf() < density.B * FieldChance)
			{
				fields.Add(PropTransform(pixel, _rng.RandfRange(0.8f, 1.2f)));
			}
		}

		// Trunk and crown share one transform list, so a tree is two instanced meshes rather than
		// one merged mesh that would have to be rebuilt to change either.
		AddScatter(TrunkMesh(), new Color("4a3524"), trees);
		AddScatter(CrownMesh(), new Color("2f4a2a"), trees);
		AddScatter(BoulderMesh(), new Color("77736e"), boulders);
		AddScatter(FieldMesh(), new Color("9a7b45"), fields);

		BuildRoads();
	}

	/// <summary>Drops a working site — a quarry, a pasture, a lumber camp — on the ground around a
	/// province's seat. The page calls this from each province's authored capacities, so what the
	/// map shows and what the economy simulates are the same fact.</summary>
	public void AddSite(Vector2 seatPixel, SiteKind kind, int count)
	{
		var transforms = new List<Transform3D>();
		for (int i = 0; i < count; i++)
		{
			// Ringed around the seat, not on top of it: the stronghold marker has to stay readable.
			float angle = _rng.RandfRange(0, Mathf.Tau);
			float radius = _rng.RandfRange(38f, 88f);
			var pixel = new Vector2(
				Mathf.Clamp(seatPixel.X + Mathf.Cos(angle) * radius, 0, _props.GetWidth() - 1),
				Mathf.Clamp(seatPixel.Y + Mathf.Sin(angle) * radius, 0, _props.GetHeight() - 1));

			if (_map.HeightAt(pixel) <= 0.6f)
			{
				continue; // out at sea or below the waterline
			}

			transforms.Add(PropTransform(pixel, _rng.RandfRange(0.9f, 1.15f)));
		}

		(Mesh mesh, Color color) = kind switch
		{
			SiteKind.Grain => (FieldMesh(), new Color("c9a martin")),
			SiteKind.Cattle => (CattleMesh(), new Color("d8d2c6")),
			SiteKind.Wood => (LogPileMesh(), new Color("6b4c2f")),
			SiteKind.Stone => (QuarryMesh(), new Color("9d9891")),
			_ => (MineMesh(), new Color("4a4440")),
		};
		AddScatter(mesh, color, transforms);
	}

	// --- placement ---------------------------------------------------------------------------

	private Transform3D PropTransform(Vector2 mapPixel, float scale)
	{
		Vector3 position = _map.WorldAt(mapPixel);
		var basis = Basis.Identity.Rotated(Vector3.Up, _rng.RandfRange(0, Mathf.Tau)).Scaled(Vector3.One * scale);
		return new Transform3D(basis, position);
	}

	private void AddScatter(Mesh mesh, Color color, List<Transform3D> transforms)
	{
		if (transforms.Count == 0)
		{
			return;
		}

		var multiMesh = new MultiMesh
		{
			TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
			Mesh = mesh,
			InstanceCount = transforms.Count,
		};
		for (int i = 0; i < transforms.Count; i++)
		{
			multiMesh.SetInstanceTransform(i, transforms[i]);
		}

		AddChild(new MultiMeshInstance3D
		{
			Multimesh = multiMesh,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.95f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		});
	}

	// --- roads -------------------------------------------------------------------------------

	private void BuildRoads()
	{
		var file = GD.Load<Json>(RoadsPath);
		if (file?.Data.VariantType != Variant.Type.Array)
		{
			GD.PushWarning("MapDecoration: no roads data; run tools/generate_campaign_map.py");
			return;
		}

		var material = new StandardMaterial3D { AlbedoColor = new Color("6f5b3e"), Roughness = 1.0f };
		foreach (Variant entry in file.Data.AsGodotArray())
		{
			Godot.Collections.Array points = entry.AsGodotDictionary()["points"].AsGodotArray();
			var line = new List<Vector2>();
			foreach (Variant point in points)
			{
				Godot.Collections.Array pair = point.AsGodotArray();
				line.Add(new Vector2((float)pair[0], (float)pair[1]));
			}

			MeshInstance3D road = BuildRoadRibbon(line, material);
			if (road != null)
			{
				AddChild(road);
			}
		}
	}

	/// <summary>Turns a road's centre line into a ribbon that follows the ground. Each point is
	/// re-sampled against the heightmap, because a road drawn between two sampled ends would sink
	/// into every valley it crosses.</summary>
	private MeshInstance3D BuildRoadRibbon(List<Vector2> line, Material material)
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

		var surface = new SurfaceTool();
		surface.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < dense.Count - 1; i++)
		{
			Vector3 a = _map.WorldAt(dense[i]) + Vector3.Up * RoadLift;
			Vector3 b = _map.WorldAt(dense[i + 1]) + Vector3.Up * RoadLift;
			Vector3 side = (b - a) with { Y = 0 };
			if (side.LengthSquared() < 0.0001f)
			{
				continue;
			}

			side = side.Normalized().Cross(Vector3.Up) * RoadWidth;
			AddQuad(surface, a - side, a + side, b + side, b - side);
		}

		surface.GenerateNormals();
		return new MeshInstance3D
		{
			Mesh = surface.Commit(),
			MaterialOverride = material,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}

	private static void AddQuad(SurfaceTool surface, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
	{
		surface.AddVertex(a);
		surface.AddVertex(b);
		surface.AddVertex(c);
		surface.AddVertex(a);
		surface.AddVertex(c);
		surface.AddVertex(d);
	}

	// --- placeholder meshes -------------------------------------------------------------------

	private static Mesh TrunkMesh() =>
		new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.11f, Height = 0.7f, RadialSegments = 5, Rings = 1 };

	private static Mesh CrownMesh() =>
		new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.5f, Height = 1.7f, RadialSegments = 6, Rings = 1 };

	private static Mesh BoulderMesh() =>
		new SphereMesh { Radius = 0.4f, Height = 0.55f, RadialSegments = 6, Rings = 3 };

	private static Mesh FieldMesh() =>
		new BoxMesh { Size = new Vector3(2.6f, 0.08f, 1.9f) };

	private static Mesh CattleMesh() =>
		new BoxMesh { Size = new Vector3(0.6f, 0.4f, 0.3f) };

	private static Mesh LogPileMesh() =>
		new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.1f, RadialSegments = 6, Rings = 1 };

	private static Mesh QuarryMesh() =>
		new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.4f) };

	private static Mesh MineMesh() =>
		new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f) };
}
