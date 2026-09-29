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

	/// <summary>One of his clips: its first row, how many frames it has, how long it runs, and
	/// whether it goes round (a loop has its first frame again after its last, to blend into).</summary>
	public readonly record struct Clip(int Start, int Frames, float Seconds, bool IsLoop);

	/// <summary>His clips, by name — walk_loop, idle_loop, shoot, death — where he was baked.</summary>
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

	private static SoldierFigure Baked(string model)
	{
		if (!Built.TryGetValue(model, out SoldierFigure figure))
		{
			figure = new SoldierFigure(model, ManHip, ManStature);
			figure.BuildBaked($"res://assets/models/{model}");
			Built[model] = figure;
		}

		return figure;
	}

	private void BuildBaked(string stem)
	{
		Godot.Collections.Dictionary meta = GD.Load<Json>($"{stem}.figure.json").Data.AsGodotDictionary();
		Godot.Collections.Dictionary offsets = meta["offsets"].AsGodotDictionary();
		// ponytail: the .bin is not a resource, so an exported build needs "*.figure.bin" in the export
		// preset's non-resource filter; a resource wrapper if more figures than the archer are baked.
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

		int positionsAt = offsets["positions"].AsInt32();
		int turnedAt = offsets["turned"].AsInt32();
		Material = new ShaderMaterial { Shader = GD.Load<Shader>(BakedShaderPath) };
		Material.SetShaderParameter("albedo_texture", GD.Load<Texture2D>($"{stem}.png"));
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
