using System.Collections.Generic;
using Godot;

/// <summary>The grass the armies stand in: short turf over the whole of the fighting ground, and
/// stands of taller grass here and there, none of it on the bare earth. Tufts of a few blades each,
/// thousands of them, in chunks so the ones off the screen are not drawn and the far ones fall away.</summary>
public partial class BattlefieldGrass : Node3D
{
	private const string ShaderPath = "res://assets/shaders/battlefield-grass.gdshader";

	/// <summary>How far out from the middle of the field grass grows, in metres — past where the woods
	/// begin it is under the trees and nobody sees it — how far apart its tufts are, and how big a
	/// chunk is.</summary>
	private const float Reach = 160f;

	/// <summary>Where the grass begins to thin toward the woods: past it fewer tufts and fewer, so the
	/// sward runs out into the turf instead of stopping at a line.</summary>
	private const float ThinsFrom = 95f;
	private const float Spacing = 1.9f;
	private const float Chunk = 30f;

	/// <summary>How far off a chunk of grass is still drawn, in metres.</summary>
	private const float SeenWithin = 150f;

	/// <summary>A tuft: how many blades, how wide each is at the root. Short turf and tall grass by
	/// height in metres, and where the tall grows: above this much of the stand noise.</summary>
	private const int Blades = 10;
	private const float BladeWide = 0.08f;
	private static readonly Vector2 ShortHigh = new(0.22f, 0.45f);
	private static readonly Vector2 TallHigh = new(0.5f, 0.9f);
	private const float TallFrom = 0.7f;

	/// <summary>How many tufts are in flower, white and yellow.</summary>
	private const float FlowersWhite = 0.025f;
	private const float FlowersYellow = 0.02f;

	/// <summary>Sows the field. <paramref name="bare"/> says where the earth shows, 0..1 across the
	/// field of <paramref name="field"/> metres, above <paramref name="bareFrom"/>; <paramref name="stands"/>
	/// where the tall grass grows.</summary>
	public void Sow(Image bare, float bareFrom, float field, FastNoiseLite stands, ulong seed, BattlefieldLand land)
	{
		var dice = new RandomNumberGenerator { Seed = seed };
		var look = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		ArrayMesh tuft = Tuft(dice);
		var chunks = new Dictionary<Vector2I, List<Transform3D>>();
		var tints = new Dictionary<Vector2I, List<Color>>();
		for (float x = -Reach; x < Reach; x += Spacing)
		{
			for (float z = -Reach; z < Reach; z += Spacing)
			{
				// Anywhere in its cell, and every row nudged across by its own amount: a tuft only a
				// little off its grid point left the grass standing in rows a man could count.
				float row = Mathf.PosMod(z * 7.31f, Spacing);
				var at = new Vector2(x + row + dice.RandfRange(-Spacing, Spacing) * 0.5f, z + dice.RandfRange(-Spacing, Spacing) * 0.5f);
				float thin = Mathf.SmoothStep(ThinsFrom, Reach, at.Length());
				if (at.Length() > Reach || Bare(bare, bareFrom, field, at) || dice.Randf() < thin)
				{
					continue;
				}

				bool tall = (stands.GetNoise2D(at.X, at.Y) * 0.5f) + 0.5f > TallFrom;
				Vector2 range = tall ? TallHigh : ShortHigh;
				float high = dice.RandfRange(range.X, range.Y);
				float wide = dice.RandfRange(0.7f, 1.2f);
				var basis = new Basis(Vector3.Up, dice.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(wide, high, wide));
				var key = new Vector2I(Mathf.FloorToInt(at.X / Chunk), Mathf.FloorToInt(at.Y / Chunk));
				if (!chunks.TryGetValue(key, out List<Transform3D> list))
				{
					list = new List<Transform3D>();
					chunks[key] = list;
					tints[key] = new List<Color>();
				}

				list.Add(new Transform3D(basis, land.On(at)));
				// A little warmer or cooler, tuft to tuft, the tall grass going to seed, and here and
				// there a tuft in flower — the white and the yellow a summer meadow is spotted with.
				float shade = dice.RandfRange(0.85f, 1.1f);
				float flower = dice.Randf();
				tints[key].Add(flower < FlowersWhite ? new Color(2.2f, 2.2f, 2.0f)
					: flower < FlowersWhite + FlowersYellow ? new Color(2.4f, 2.0f, 0.5f)
					: tall ? new Color(1.05f * shade, 1.0f * shade, 0.8f * shade) : new Color(shade, shade, shade));
			}
		}

		foreach ((Vector2I key, List<Transform3D> list) in chunks)
		{
			var many = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
				Mesh = tuft,
				InstanceCount = list.Count,
			};
			many.Buffer = Packed(list, tints[key]);

			AddChild(new MultiMeshInstance3D
			{
				Multimesh = many,
				MaterialOverride = look,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				VisibilityRangeEnd = SeenWithin,
				VisibilityRangeEndMargin = 45f,
				VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
			});
		}
	}

	private static bool Bare(Image bare, float bareFrom, float field, Vector2 at)
	{
		int x = Mathf.Clamp(Mathf.FloorToInt(((at.X / field) + 0.5f) * bare.GetWidth()), 0, bare.GetWidth() - 1);
		int y = Mathf.Clamp(Mathf.FloorToInt(((at.Y / field) + 0.5f) * bare.GetHeight()), 0, bare.GetHeight() - 1);
		return bare.GetPixel(x, y).R > bareFrom;
	}

	/// <summary>One tuft, a metre high to be scaled: a few blades, each a thin triangle leaning a
	/// little its own way out from the root. UV.y is how far up the blade (the shader sways by it).</summary>
	/// <summary>A chunk's tufts, laid out as a MultiMesh's buffer takes them — each a 3×4 transform
	/// row by row, then its colour — and handed over in one piece: set one at a time, tens of
	/// thousands of calls into the engine held up the map's loading.</summary>
	internal static float[] Packed(List<Transform3D> placed, List<Color> tints)
	{
		var buffer = new float[placed.Count * 16];
		for (int i = 0; i < placed.Count; i++)
		{
			Transform3D t = placed[i];
			Color c = tints[i];
			int at = i * 16;
			buffer[at] = t.Basis.X.X;
			buffer[at + 1] = t.Basis.Y.X;
			buffer[at + 2] = t.Basis.Z.X;
			buffer[at + 3] = t.Origin.X;
			buffer[at + 4] = t.Basis.X.Y;
			buffer[at + 5] = t.Basis.Y.Y;
			buffer[at + 6] = t.Basis.Z.Y;
			buffer[at + 7] = t.Origin.Y;
			buffer[at + 8] = t.Basis.X.Z;
			buffer[at + 9] = t.Basis.Y.Z;
			buffer[at + 10] = t.Basis.Z.Z;
			buffer[at + 11] = t.Origin.Z;
			buffer[at + 12] = c.R;
			buffer[at + 13] = c.G;
			buffer[at + 14] = c.B;
			buffer[at + 15] = c.A;
		}

		return buffer;
	}

	internal static ArrayMesh Tuft(RandomNumberGenerator dice)
	{
		var points = new List<Vector3>();
		var uvs = new List<Vector2>();
		for (int blade = 0; blade < Blades; blade++)
		{
			float turn = (blade * Mathf.Tau / Blades) + dice.RandfRange(-0.4f, 0.4f);
			var across = new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn)) * BladeWide / 2f;
			var root = new Vector3(dice.RandfRange(-0.08f, 0.08f), 0f, dice.RandfRange(-0.08f, 0.08f));
			var lean = new Vector3(-Mathf.Sin(turn), 0f, Mathf.Cos(turn)) * dice.RandfRange(0.05f, 0.25f);
			float high = dice.RandfRange(0.7f, 1f);
			points.AddRange(new[] { root - across, root + across, root + lean + (Vector3.Up * high) });
			uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 1f) });
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
