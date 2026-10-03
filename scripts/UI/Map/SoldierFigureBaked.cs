using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

/// <summary>A soldier baked from his clips by tools/bake_figure.py: his own geometry, and every
/// frame of every clip as a row of a texture battlefield-figure.gdshader reads him out of. Each man
/// drawn with him says which row he is at (<see cref="Row"/>).
///
/// His geometry is read from the bake and not imported as a model, because the rows are laid out
/// vertex by vertex in the bake's own order, and an importer is free to reorder them.</summary>
public sealed partial class SoldierFigure
{
	private const string BakedShaderPath = "res://assets/shaders/battlefield-figure.gdshader";

	/// <summary>The angles, in degrees, within which the detail levels merge a surface's normals and
	/// past which they keep an edge sharp.</summary>
	private const float LodNormalMerge = 25f;
	private const float LodNormalSplit = 60f;

	/// <summary>How much of a man's detail a squad far off is drawn with.</summary>
	private const float FarShare = 0.08f;

	/// <summary>How near two of his vertices stand to be welded into one for thinning him, as a share
	/// of his height, and how sharp a fold may be smoothed away while he is.</summary>
	private const float WeldShare = 0.015f;
	private const float FarNormalMerge = 180f;

	/// <summary>One of his clips: its first row, how many frames it has, how long it runs, and
	/// whether it goes round (a loop has its first frame again after its last, to blend into).</summary>
	public readonly record struct Clip(int Start, int Frames, float Seconds, bool IsLoop);

	/// <summary>His clips, by name — idle_loop, walk_loop and death for every man; shoot for the archer,
	/// gallop_loop and attack for the knight — where he was baked.</summary>
	public IReadOnlyDictionary<string, Clip> Clips { get; private set; }

	public bool IsBaked => Clips != null;

	/// <summary>The row of the baked texture he is at <paramref name="seconds"/> into a clip: a loop
	/// goes round, anything else holds on its last frame.</summary>
	public float Row(string clip, float seconds)
	{
		Clip playing = Clips[clip];
		float frame = seconds * playing.Frames / playing.Seconds;
		frame = playing.IsLoop ? Mathf.PosMod(frame, playing.Frames) : Mathf.Clamp(frame, 0f, playing.Frames - 1);
		return playing.Start + frame;
	}

	private static SoldierFigure Baked(string model, float stature)
	{
		if (!Built.TryGetValue(model, out SoldierFigure figure))
		{
			figure = new SoldierFigure(model, ManHip, stature);
			figure.BuildBaked($"res://assets/models/{model}");
			Built[model] = figure;
		}

		return figure;
	}

	private void BuildBaked(string stem)
	{
		Godot.Collections.Dictionary meta = GD.Load<Json>($"{stem}.figure.json").Data.AsGodotDictionary();
		Godot.Collections.Dictionary offsets = meta["offsets"].AsGodotDictionary();
		// ponytail: the .bin is not a resource, so it reaches an exported build only through the
		// "*.figure.bin" include filter every preset in export_presets.cfg carries — a new preset
		// without it ships soldiers with no bodies. A resource wrapper once there are more figures
		// than the archer, the knight and the peasant.
		byte[] bake = FileAccess.GetFileAsBytes($"{stem}.figure.bin");
		int count = meta["vertices"].AsInt32();

		Vector3[] points = Vectors3(bake, offsets["points"].AsInt32(), count);
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Godot.Mesh.ArrayType.Max);
		arrays[(int)Godot.Mesh.ArrayType.Vertex] = points;
		arrays[(int)Godot.Mesh.ArrayType.Normal] = Vectors3(bake, offsets["normals"].AsInt32(), count);
		arrays[(int)Godot.Mesh.ArrayType.TexUV] = Vectors2(bake, offsets["uv"].AsInt32(), count);
		arrays[(int)Godot.Mesh.ArrayType.Index] = Ints(bake, offsets["indices"].AsInt32(), meta["indices"].AsInt32());
		// With levels of detail: a man across the field is drawn with a fraction of his triangles.
		// They only thin out which of his vertices are joined, never move them, so the clips —
		// read by vertex, from a texture — play on every level alike. Without them seventy bowmen
		// of thirty thousand vertices each, lit and shadowed, cost a frame twelve milliseconds.
		var detailed = new ImporterMesh();
		detailed.AddSurface(Godot.Mesh.PrimitiveType.Triangles, arrays);
		detailed.GenerateLods(LodNormalMerge, LodNormalSplit, new Godot.Collections.Array());
		Mesh = detailed.GetMesh();
		Bounds = Mesh.GetAabb();
		Far = Thinned(arrays, Bounds.Size.Y);

		int positionsAt = offsets["positions"].AsInt32();
		int turnedAt = offsets["turned"].AsInt32();
		Material = new ShaderMaterial { Shader = GD.Load<Shader>(BakedShaderPath) };
		Material.SetShaderParameter("albedo_texture", GD.Load<Texture2D>($"{stem}.png"));
		if (ResourceLoader.Exists($"{stem}.material.png"))
		{
			Material.SetShaderParameter("material_texture", GD.Load<Texture2D>($"{stem}.material.png"));
			Material.SetShaderParameter("has_material", true);
		}
		int wide = meta["texture_width"].AsInt32();
		int high = meta["texture_height"].AsInt32();
		Material.SetShaderParameter("positions", ImageTexture.CreateFromImage(Image.CreateFromData(wide, high, false,
			Image.Format.Rgbah, bake[positionsAt..(positionsAt + (wide * high * 8))])));
		Material.SetShaderParameter("turned", ImageTexture.CreateFromImage(Image.CreateFromData(wide, high, false,
			Image.Format.Rgba8, bake[turnedAt..(turnedAt + (wide * high * 4))])));
		Material.SetShaderParameter("vertex_count", count);
		Material.SetShaderParameter("cloth_value", meta["cloth_value"].AsSingle());
		Material.SetShaderParameter("row_width", wide);

		var clips = new Dictionary<string, Clip>();
		foreach ((Variant name, Variant clip) in meta["clips"].AsGodotDictionary())
		{
			Godot.Collections.Dictionary made = clip.AsGodotDictionary();
			clips[name.AsString()] = new Clip(made["start"].AsInt32(), made["frames"].AsInt32(),
				made["seconds"].AsSingle(), made["loop"].AsBool());
		}

		Clips = clips;
		Feet = Stance(points, Bounds, Bounds.Size.Y);
	}

	/// <summary>The man as a squad far off is drawn: FarShare of his triangles over the same vertices,
	/// so the same clips. Godot will not choose a level of detail for each man of a MultiMesh, only for
	/// the squad's whole box, and so drew every man across the field at his full forty thousand
	/// (BattlefieldSquads.Follow chooses instead).
	///
	/// Thinned welded: a Meshy man is hundreds of loose patches, and simplified as he is his levels
	/// stop at a third, every patch's edge held. Welded by where his vertices stand, he thins as far as
	/// asked, and each corner of a thinned triangle is put back as one of the vertices it stood for,
	/// all three from the same patch of paint where they can be: taken from any patch, one triangle
	/// was painted across three, and the peasant came out brown.</summary>
	private static ArrayMesh Thinned(Godot.Collections.Array arrays, float tall)
	{
		float weldWithin = tall * WeldShare;
		var points = (Vector3[])arrays[(int)Godot.Mesh.ArrayType.Vertex];
		var normals = (Vector3[])arrays[(int)Godot.Mesh.ArrayType.Normal];
		int[] indices = (int[])arrays[(int)Godot.Mesh.ArrayType.Index];

		int[] patch = Patches(points.Length, indices);
		var weldedAt = new Dictionary<Vector3I, int>();
		var standsFor = new List<Dictionary<int, int>>();
		var welded = new int[points.Length];
		for (int i = 0; i < points.Length; i++)
		{
			Vector3I cell = (Vector3I)(points[i] / weldWithin).Round();
			if (!weldedAt.TryGetValue(cell, out int at))
			{
				at = standsFor.Count;
				weldedAt[cell] = at;
				standsFor.Add(new Dictionary<int, int>());
			}

			standsFor[at].TryAdd(patch[i], i);
			welded[i] = at;
		}

		var joined = new List<int>(indices.Length);
		for (int t = 0; t < indices.Length; t += 3)
		{
			int a = welded[indices[t]], b = welded[indices[t + 1]], c = welded[indices[t + 2]];
			if (a != b && b != c && a != c)
			{
				joined.AddRange(new[] { a, b, c });
			}
		}

		var one = new Godot.Collections.Array();
		one.Resize((int)Godot.Mesh.ArrayType.Max);
		one[(int)Godot.Mesh.ArrayType.Vertex] = standsFor.ConvertAll(by => points[First(by)]).ToArray();
		one[(int)Godot.Mesh.ArrayType.Normal] = standsFor.ConvertAll(by => normals[First(by)]).ToArray();
		one[(int)Godot.Mesh.ArrayType.Index] = joined.ToArray();
		var whole = new ImporterMesh();
		whole.AddSurface(Godot.Mesh.PrimitiveType.Triangles, one);
		whole.GenerateLods(FarNormalMerge, FarNormalMerge, new Godot.Collections.Array());

		int levels = whole.GetSurfaceLodCount(0);
		if (levels == 0)
		{
			return null;
		}

		int[] thinnest = whole.GetSurfaceLodIndices(0, levels - 1);
		for (int level = 0; level < levels; level++)
		{
			int[] lod = whole.GetSurfaceLodIndices(0, level);
			if (lod.Length <= indices.Length * FarShare)
			{
				thinnest = lod;
				break;
			}
		}

		var far = (Godot.Collections.Array)arrays.Duplicate();
		var corners = new int[thinnest.Length];
		for (int t = 0; t < thinnest.Length; t += 3)
		{
			Dictionary<int, int> a = standsFor[thinnest[t]], b = standsFor[thinnest[t + 1]], c = standsFor[thinnest[t + 2]];
			int shared = -1;
			foreach (int each in a.Keys)
			{
				if (b.ContainsKey(each) && c.ContainsKey(each))
				{
					shared = each;
					break;
				}
			}

			corners[t] = shared >= 0 ? a[shared] : First(a);
			corners[t + 1] = shared >= 0 ? b[shared] : b.GetValueOrDefault(patch[corners[t]], First(b));
			corners[t + 2] = shared >= 0 ? c[shared] : c.GetValueOrDefault(patch[corners[t]], First(c));
		}

		far[(int)Godot.Mesh.ArrayType.Index] = corners;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, far);
		return mesh;
	}

	private static int First(Dictionary<int, int> byPatch)
	{
		foreach (int vertex in byPatch.Values)
		{
			return vertex;
		}

		return 0;
	}

	/// <summary>Which patch of paint each vertex belongs to: the triangles joined through the vertices
	/// they share, as the unwrap left them.</summary>
	private static int[] Patches(int count, int[] indices)
	{
		var root = new int[count];
		for (int i = 0; i < count; i++)
		{
			root[i] = i;
		}

		int Find(int v)
		{
			while (root[v] != v)
			{
				root[v] = root[root[v]];
				v = root[v];
			}

			return v;
		}

		for (int t = 0; t < indices.Length; t += 3)
		{
			int a = Find(indices[t]);
			root[Find(indices[t + 1])] = a;
			root[Find(indices[t + 2])] = a;
		}

		var patch = new int[count];
		for (int i = 0; i < count; i++)
		{
			patch[i] = Find(i);
		}

		return patch;
	}

	private static Vector3[] Vectors3(byte[] bake, int at, int count)
	{
		ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bake.AsSpan(at, count * 12));
		var vectors = new Vector3[count];
		for (int i = 0; i < count; i++)
		{
			vectors[i] = new Vector3(floats[i * 3], floats[(i * 3) + 1], floats[(i * 3) + 2]);
		}

		return vectors;
	}

	private static Vector2[] Vectors2(byte[] bake, int at, int count)
	{
		ReadOnlySpan<float> floats = MemoryMarshal.Cast<byte, float>(bake.AsSpan(at, count * 8));
		var vectors = new Vector2[count];
		for (int i = 0; i < count; i++)
		{
			vectors[i] = new Vector2(floats[i * 2], floats[(i * 2) + 1]);
		}

		return vectors;
	}

	private static int[] Ints(byte[] bake, int at, int count) =>
		MemoryMarshal.Cast<byte, int>(bake.AsSpan(at, count * 4)).ToArray();
}
