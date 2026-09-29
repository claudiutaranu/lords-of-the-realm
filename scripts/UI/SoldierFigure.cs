using System.Collections.Generic;
using Godot;

/// <summary>A man as the game draws him: a model's mesh, with the banner's sway written into its
/// vertex colours where he carries one, and the material all his figures share. Everything that
/// moves on him is soldier.gdshader's.
///
/// <see cref="Standard"/> is the one who carries the banner — every army on the campaign map, and
/// every squad's standard-bearer on the field. <see cref="For"/> is the man a kind of soldier is
/// drawn as on the field: his own model, if one has been made for him (assets/models/units/, named
/// by his key in recruits.json — the bowman is bow.glb), and the standard-bearer until then.
///
/// Built once a model and kept. The battlefield draws a few hundred of them, and working out where
/// a banner is from ten thousand triangles is not a thing to do twice.</summary>
public sealed partial class SoldierFigure
{
	private const string ShaderPath = "res://assets/shaders/soldier.gdshader";
	private const string StandardModel = "soldier";
	private const string UnitDirectory = "units";

	/// <summary>How the banner is found on the standard-bearer, in shares of his height and in his
	/// own units: the cloth is what hangs clear of the pole above his shield, grown from its outer
	/// edge across the mesh so the whole pennant goes with it and the pole does not (see Sway).</summary>
	private const float BannerFloor = 0.45f;
	private const float BannerSeed = 0.52f;
	private const float BannerSeedReach = 0.5f;
	private const float PoleClearance = 0.08f;

	/// <summary>Where his legs meet his body and where his soles are, as shares of the model's
	/// height — the standard-bearer's height runs to the top of his pole, so his hip is lower down
	/// it than a man's who carries nothing over his head.</summary>
	private const float StandardHip = 0.30f;
	private const float ManHip = 0.47f;
	private const float SoleHeight = 0.05f;

	/// <summary>How tall each is drawn, in metres, to the top of the model: the standard-bearer to
	/// the tip of his pole, a man without one to the crown of his head.</summary>
	private const float StandardStature = 1.8f;
	private const float ManStature = 1.45f;

	/// <summary>What a model painted with no metal-and-roughness map is given instead: plain cloth
	/// and leather, neither shiny nor metal. Without it the shader reads an empty map as white —
	/// all metal, polished — and the man comes out chrome.</summary>
	private static readonly Color Matte = new(0f, 0.8f, 0f);

	private static readonly Dictionary<string, SoldierFigure> Built = new();

	private SoldierFigure(string model, float hip, float stature)
	{
		Model = model;
		Stature = stature;
		HipShare = hip;
	}

	private SoldierFigure(string model, bool bannered, float hip, float stature)
	{
		Model = model;
		Stature = stature;
		HipShare = hip;
		Godot.Mesh made = Models.MeshOf(model);
		if (made != null)
		{
			Build(made, bannered);
		}
	}

	/// <summary>The standard-bearer: the map's man, with his banner.</summary>
	public static SoldierFigure Standard => Of(StandardModel, true, StandardHip, StandardStature);

	/// <summary>The man a kind of soldier is drawn as on the field: baked from his clips if he has
	/// been (tools/bake_figure.py), else his model standing still, else the standard-bearer.</summary>
	public static SoldierFigure For(string unit)
	{
		// A hired band wears the look of the kind it fights as.
		string key = Mercenaries.Find(unit)?.Unit ?? unit;
		string model = $"{UnitDirectory}/{key}";
		if (ResourceLoader.Exists($"res://assets/models/{model}.figure.json"))
		{
			return Baked(model);
		}

		return ResourceLoader.Exists($"res://assets/models/{model}.glb")
			? Of(model, false, ManHip, ManStature)
			: Standard;
	}

	public string Model { get; }

	/// <summary>His mesh, or null where the model is missing.</summary>
	public ArrayMesh Mesh { get; private set; }

	/// <summary>His mesh's bounds, kept: asked for every man every frame.</summary>
	public Aabb Bounds { get; private set; }

	/// <summary>The material every figure of him wears; each sets its own colour, march and banner
	/// through instance parameters or its place's custom data.</summary>
	public ShaderMaterial Material { get; private set; }

	/// <summary>How tall he is drawn, in metres.</summary>
	public float Stature { get; }

	/// <summary>How far up him his hip is, as a share of his height.</summary>
	public float HipShare { get; }

	/// <summary>How long his legs are, hip to sole, as a share of his height — what a stride is
	/// measured by, so his feet do not slide over the ground at any pace.</summary>
	public float LegShare => HipShare - SoleHeight;

	/// <summary>Where his feet are on the ground plane, in the model's own units. Not its origin: a
	/// model can stand beside its middle — the standard-bearer's pole is off to one side — so anything
	/// that puts him on a spot puts his feet there and not the origin.</summary>
	public Vector2 Feet { get; private set; }

	private static SoldierFigure Of(string model, bool bannered, float hip, float stature)
	{
		if (!Built.TryGetValue(model, out SoldierFigure figure))
		{
			figure = new SoldierFigure(model, bannered, hip, stature);
			Built[model] = figure;
		}

		return figure;
	}

	private void Build(Godot.Mesh model, bool bannered)
	{
		Godot.Collections.Array arrays = model.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Godot.Mesh.ArrayType.Vertex].AsVector3Array();
		int[] indices = arrays[(int)Godot.Mesh.ArrayType.Index].AsInt32Array();
		Aabb bounds = model.GetAabb();
		float height = bounds.Size.Y;

		Vector3 top = vertices[0];
		foreach (Vector3 vertex in vertices)
		{
			top = vertex.Y > top.Y ? vertex : top;
		}

		var pole = new Vector2(top.X, top.Z);
		Color[] sway = bannered ? Sway(vertices, indices, bounds, pole) : new Color[vertices.Length];
		arrays[(int)Godot.Mesh.ArrayType.Color] = sway;
		Mesh = new ArrayMesh();
		Mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
		Bounds = Mesh.GetAabb();
		Material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		if (model.SurfaceGetMaterial(0) is BaseMaterial3D painted)
		{
			Material.SetShaderParameter("albedo_texture", painted.AlbedoTexture);
			Material.SetShaderParameter("normal_texture", painted.NormalTexture);
			Material.SetShaderParameter("metal_rough_texture", painted.MetallicTexture ?? Plain(Matte));
		}

		Material.SetShaderParameter("wind", MapClouds.PrevailingWind);
		Material.SetShaderParameter("flag_pole", pole);
		Material.SetShaderParameter("pole_radius", bannered ? PoleClearance : 0f);
		Material.SetShaderParameter("flag_rest", bannered ? FlagRest(vertices, sway, pole) : 0f);
		Material.SetShaderParameter("hip", bounds.Position.Y + (HipShare * height));
		Material.SetShaderParameter("sole", bounds.Position.Y + (SoleHeight * height));
		Feet = Stance(vertices, bounds, height);
		Material.SetShaderParameter("body_side", Feet.X);
	}

	/// <summary>The banner's sway, for each vertex — 0 at the pole and 1 at the fly end — which is
	/// what soldier.gdshader waves it by.
	///
	/// Which vertices are cloth is a question about the model, and it is answered here rather than
	/// baked into the file because the model came with neither a skeleton nor a mask. The pennant is
	/// found from its outer edge: everything above the shield and further than BannerSeedReach from
	/// the pole can only be cloth, and from there it is grown across the mesh — over the emblem, into
	/// the tails — stopped from spreading down the pole by PoleClearance and off the man by the floor.
	/// The helmet is never taken: it is not joined to the cloth by a single edge.</summary>
	private static Color[] Sway(Vector3[] vertices, int[] indices, Aabb bounds, Vector2 pole)
	{
		float height = bounds.Size.Y;
		float floor = bounds.Position.Y + (BannerFloor * height);
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

		return sway;
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

	/// <summary>Where his legs stand on the ground plane: side to side, everything left of it swings
	/// against everything right of it. Taken off the legs themselves — a pole carried off to one side
	/// would drag the middle of the man over with it.</summary>
	private Vector2 Stance(IReadOnlyList<Vector3> vertices, Aabb bounds, float height)
	{
		Vector2 sum = Vector2.Zero;
		int counted = 0;
		foreach (Vector3 vertex in vertices)
		{
			if (vertex.Y < bounds.Position.Y + (HipShare * height))
			{
				sum += new Vector2(vertex.X, vertex.Z);
				counted++;
			}
		}

		return counted == 0 ? Vector2.Zero : sum / counted;
	}

	private static ImageTexture Plain(Color colour)
	{
		Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgb8);
		image.Fill(colour);
		return ImageTexture.CreateFromImage(image);
	}
}
