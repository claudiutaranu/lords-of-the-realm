using Godot;

/// <summary>The lie of the land on a battlefield: a gentle roll where the lines meet, rising into
/// real hills toward the woods, so a field reads as country and not as a board.
///
/// ponytail: the hills are the eye's, not the battle's — FieldBattle fights on the flat, and a squad
/// on a crest strikes no harder than one in a hollow. When the ground should matter, FieldBattle
/// wants this same height map (the noise is seeded, so both would read one field) and a slope term
/// in FieldBlows.
///
/// Worked out once, on a grid, and read back between its points: every man, mark and arrow asks
/// where the ground is every frame, and a noise asked five hundred times a frame is time spent on
/// the same hills.</summary>
public sealed class BattlefieldLand
{
	/// <summary>Metres between the grid's points, and how many squares the ground's mesh is cut into
	/// along a side of the field.</summary>
	private const float Step = 2f;

	/// <summary>How the land rises: a roll of this many metres over the middle, where the fighting
	/// is and a slope would only hide the men behind it; hills of this many where the wood begins,
	/// between the one and the other as the ground runs out from the centre.</summary>
	private const float Roll = 7f;
	private const float Hills = 26f;
	private const float RollWithin = 60f;
	private const float HillsFrom = 190f;
	private const float Wavelength = 0.0055f;

	private readonly float _field;
	private readonly int _points;
	private readonly float[] _heights;

	public BattlefieldLand(float field, int seed)
	{
		_field = field;
		_points = Mathf.CeilToInt(field / Step) + 1;
		_heights = new float[_points * _points];
		var noise = new FastNoiseLite { Frequency = Wavelength, Seed = seed, FractalOctaves = 3 };
		for (int z = 0; z < _points; z++)
		{
			for (int x = 0; x < _points; x++)
			{
				var at = new Vector2((x * Step) - (field / 2f), (z * Step) - (field / 2f));
				float swell = Mathf.Lerp(Roll, Hills, Mathf.SmoothStep(RollWithin, HillsFrom, at.Length()));
				_heights[(z * _points) + x] = noise.GetNoise2D(at.X, at.Y) * swell;
			}
		}
	}

	/// <summary>How high the ground stands at a point on the field, in metres.</summary>
	public float Rise(Vector2 at)
	{
		float gx = Mathf.Clamp((at.X + (_field / 2f)) / Step, 0f, _points - 1.001f);
		float gz = Mathf.Clamp((at.Y + (_field / 2f)) / Step, 0f, _points - 1.001f);
		int x = (int)gx;
		int z = (int)gz;
		float fx = gx - x;
		float fz = gz - z;
		float near = Mathf.Lerp(Height(x, z), Height(x + 1, z), fx);
		float far = Mathf.Lerp(Height(x, z + 1), Height(x + 1, z + 1), fx);
		return Mathf.Lerp(near, far, fz);
	}

	/// <summary>A point on the field, standing on the ground.</summary>
	public Vector3 On(Vector2 at) => new(at.X, Rise(at), at.Y);

	/// <summary>Where a ray from the eye first meets the ground, if it does: walked out in steps and
	/// closed in on by halves, which on hills this gentle cannot step over a crest.</summary>
	public Vector2? Hit(Vector3 origin, Vector3 ray, float reach)
	{
		const float stride = 1f;
		float before = 0f;
		for (float t = stride; t <= reach; t += stride)
		{
			Vector3 at = origin + (ray * t);
			if (at.Y > Rise(new Vector2(at.X, at.Z)))
			{
				before = t;
				continue;
			}

			float after = t;
			for (int i = 0; i < 12; i++)
			{
				float middle = (before + after) / 2f;
				Vector3 m = origin + (ray * middle);
				if (m.Y > Rise(new Vector2(m.X, m.Z)))
				{
					before = middle;
				}
				else
				{
					after = middle;
				}
			}

			Vector3 hit = origin + (ray * after);
			return new Vector2(hit.X, hit.Z);
		}

		return null;
	}

	/// <summary>The ground as a mesh: the grid, lit by its own slopes, with the field's UVs running
	/// 0..1 across it the way a PlaneMesh's do, so the ground shader and the bare-earth map read it
	/// unchanged.</summary>
	public ArrayMesh Mesh()
	{
		var vertices = new Vector3[_points * _points];
		var normals = new Vector3[vertices.Length];
		var uvs = new Vector2[vertices.Length];
		for (int z = 0; z < _points; z++)
		{
			for (int x = 0; x < _points; x++)
			{
				int i = (z * _points) + x;
				vertices[i] = new Vector3((x * Step) - (_field / 2f), _heights[i], (z * Step) - (_field / 2f));
				float dx = Height(Mathf.Min(x + 1, _points - 1), z) - Height(Mathf.Max(x - 1, 0), z);
				float dz = Height(x, Mathf.Min(z + 1, _points - 1)) - Height(x, Mathf.Max(z - 1, 0));
				normals[i] = new Vector3(-dx, 2f * Step, -dz).Normalized();
				uvs[i] = new Vector2((float)x / (_points - 1), (float)z / (_points - 1));
			}
		}

		var indices = new int[(_points - 1) * (_points - 1) * 6];
		int n = 0;
		for (int z = 0; z < _points - 1; z++)
		{
			for (int x = 0; x < _points - 1; x++)
			{
				int a = (z * _points) + x;
				int b = a + 1;
				int c = a + _points;
				int d = c + 1;
				indices[n++] = a; indices[n++] = b; indices[n++] = c;
				indices[n++] = b; indices[n++] = d; indices[n++] = c;
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Godot.Mesh.ArrayType.Max);
		arrays[(int)Godot.Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Godot.Mesh.ArrayType.Normal] = normals;
		arrays[(int)Godot.Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Godot.Mesh.ArrayType.Index] = indices;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	private float Height(int x, int z) => _heights[(z * _points) + x];
}
