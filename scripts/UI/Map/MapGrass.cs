using System.Collections.Generic;
using Godot;

/// <summary>Grass on the campaign map: tufts over the open country, none on the steep rock, in the
/// deep woods, on the roads or in the sea. Drawn in chunks that fall away with
/// distance, so only the country near the eye carries it — from high up the terrain's own colour
/// says "grass" well enough, and a hundred thousand tufts across the whole island would not.</summary>
public partial class MapGrass : Node3D
{
	private const string ShaderPath = "res://assets/shaders/battlefield-grass.gdshader";

	/// <summary>How far apart the tufts are sown and how big they grow, in world units — the scale
	/// of a map whose soldiers stand 6.5 tall and whose trees a little under 3.</summary>
	private const float Spacing = 0.38f;
	private static readonly Vector2 Tall = new(0.2f, 0.42f);
	private const float Wide = 1.3f;

	/// <summary>Where grass grows: out of the deep woods (the props map's woodland channel under
	/// this), on ground no steeper than this rise across a tuft's spacing, and above the shore.</summary>
	private const float DeepWood = 0.3f;
	private const float Steepest = 0.12f;
	private const float AboveShore = 0.6f;

	/// <summary>How close to a road's centre line grass may grow, in map pixels, give or take a cell.</summary>
	private const float RoadCell = 2.5f;

	/// <summary>How big a chunk is, and how far off one is still drawn, in world units.</summary>
	private const float Chunk = 10f;
	private const float SeenWithin = 65f;

	/// <summary>The grass's colour, root and tip, by season in the Season enum's order: the deep,
	/// lit green of the map's meadow in spring and summer — the battlefield's straw-tipped grass read
	/// as pale specks on it — withered to straw and rust in autumn, and under the snow in winter.</summary>
	private static readonly (Color Root, Color Tip)[] BySeason =
	{
		(new(0.16f, 0.34f, 0.07f), new(0.42f, 0.66f, 0.18f)),
		(new(0.14f, 0.30f, 0.06f), new(0.36f, 0.58f, 0.15f)),
		(new(0.26f, 0.26f, 0.10f), new(0.58f, 0.52f, 0.26f)),
		(new(0.30f, 0.28f, 0.22f), new(0.62f, 0.58f, 0.48f)),
	};

	private ShaderMaterial _look;

	/// <summary>Turns the grass over to a season, from behind the turn curtain.</summary>
	public void SetSeason(float season)
	{
		int from = Mathf.FloorToInt(season) % 4;
		int to = (from + 1) % 4;
		float along = season - Mathf.Floor(season);
		_look?.SetShaderParameter("root_colour", BySeason[from].Root.Lerp(BySeason[to].Root, along));
		_look?.SetShaderParameter("tip_colour", BySeason[from].Tip.Lerp(BySeason[to].Tip, along));

		// Under the snow in winter: it goes as the winter comes, and comes back with the spring.
		Visible = from != (int)Season.Winter && !(from == (int)Season.Autumn && along > 0.5f);
	}

	public void Sow(CampaignMap3D map, Image props, Vector2 mapPixels, ulong seed)
	{
		var dice = new RandomNumberGenerator { Seed = seed };
		var look = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		look.SetShaderParameter("sway", 0.05f);
		_look = look;
		SetSeason((float)(int)Season.Spring);
		ArrayMesh tuft = BattlefieldGrass.Tuft(dice);
		float step = Spacing * map.PixelsPerUnit;
		var chunks = new Dictionary<Vector2I, List<Transform3D>>();

		// The roads, as the cells of a coarse grid they pass through: grass is kept off the track.
		var road = new HashSet<Vector2I>();
		foreach (Vector2 point in MapDecoration.RoadPoints())
		{
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					road.Add(new Vector2I(Mathf.FloorToInt(point.X / RoadCell) + dx, Mathf.FloorToInt(point.Y / RoadCell) + dy));
				}
			}
		}
		for (float x = 0; x < mapPixels.X; x += step)
		{
			for (float y = 0; y < mapPixels.Y; y += step)
			{
				var pixel = new Vector2(x + dice.RandfRange(0f, step), y + dice.RandfRange(0f, step));
				if (pixel.X >= props.GetWidth() || pixel.Y >= props.GetHeight()
					|| props.GetPixel((int)pixel.X, (int)pixel.Y).R > DeepWood)
				{
					continue;
				}

				if (road.Contains(new Vector2I(Mathf.FloorToInt(pixel.X / RoadCell), Mathf.FloorToInt(pixel.Y / RoadCell))))
				{
					continue;
				}

				float height = map.HeightAt(pixel);
				if (height < map.WaterLine + AboveShore
					|| Mathf.Abs(map.HeightAt(pixel + new Vector2(step, 0f)) - height) > Steepest
					|| Mathf.Abs(map.HeightAt(pixel + new Vector2(0f, step)) - height) > Steepest)
				{
					continue;
				}

				Vector3 at = map.WorldAt(pixel);
				float high = dice.RandfRange(Tall.X, Tall.Y);
				float wide = high * Wide * dice.RandfRange(0.8f, 1.2f);
				var basis = new Basis(Vector3.Up, dice.RandfRange(0f, Mathf.Tau)).Scaled(new Vector3(wide, high, wide));
				var key = new Vector2I(Mathf.FloorToInt(at.X / Chunk), Mathf.FloorToInt(at.Z / Chunk));
				if (!chunks.TryGetValue(key, out List<Transform3D> list))
				{
					list = new List<Transform3D>();
					chunks[key] = list;
				}

				list.Add(new Transform3D(basis, at));
			}
		}

		foreach (List<Transform3D> list in chunks.Values)
		{
			var many = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = true,
				Mesh = tuft,
				InstanceCount = list.Count,
			};
			var tints = new List<Color>(list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				float shade = dice.RandfRange(0.85f, 1.1f);
				tints.Add(new Color(shade, shade, shade));
			}

			many.Buffer = BattlefieldGrass.Packed(list, tints);

			AddChild(new MultiMeshInstance3D
			{
				Multimesh = many,
				MaterialOverride = look,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				VisibilityRangeEnd = SeenWithin,
				VisibilityRangeEndMargin = 15f,
				VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self,
			});
		}

	}
}
