using System.Collections.Generic;
using Godot;

/// <summary>The roads and what the ground is already given to: the road network read once, the
/// clearings kept round what is built, and where a plough may and may not go.</summary>
public partial class MapDecoration
{
	/// <summary>How far a field keeps from the middle of a road, in map pixels: the track and its
	/// verge as the generator paints them (ROAD_HALF_WIDTH and ROAD_FEATHER), and a step more.</summary>
	private const float RoadClear = 6f;
	private const string RoadsFile = "map-roads.json";
	private const float RoadStep = 2f;

	/// <summary>The road points, by the cell of a coarse grid they fall in, so a field asks only the
	/// cells near it and not every road on the map.</summary>
	private readonly Dictionary<Vector2I, List<Vector2>> _roads = new();
	private const float RoadCell = 8f;

	/// <summary>Ground somebody has built on, which the woods are sown around.</summary>
	private readonly List<(Vector2 Centre, float Radius)> _clearings = new();

	/// <summary>Points along every road the generator laid (map-roads.json), in map pixels, no more
	/// than <see cref="RoadStep"/> apart — close enough that keeping clear of the points keeps clear
	/// of the road.</summary>
	internal static List<Vector2> RoadPoints()
	{
		var points = new List<Vector2>();
		var file = GD.Load<Json>(Campaign.Data(RoadsFile));
		if (file?.Data.VariantType != Variant.Type.Array)
		{
			return points;
		}

		foreach (Variant road in file.Data.AsGodotArray())
		{
			Vector2? last = null;
			foreach (Variant point in road.AsGodotDictionary()["points"].AsGodotArray())
			{
				Godot.Collections.Array xy = point.AsGodotArray();
				var here = new Vector2(xy[0].AsSingle(), xy[1].AsSingle());

				// Filled in between the generator's points, which lie up to eleven pixels apart: kept
				// clear of the points alone, grass and fields crept onto the road between them.
				if (last is Vector2 from)
				{
					int steps = Mathf.CeilToInt(from.DistanceTo(here) / RoadStep);
					for (int step = 1; step < steps; step++)
					{
						points.Add(from.Lerp(here, step / (float)steps));
					}
				}

				points.Add(here);
				last = here;
			}
		}

		return points;
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

		// Nor across a road: a field ploughed over the track the carts and the armies use.
		float clear = (half * Mathf.Sqrt2) + RoadClear;
		int reach = Mathf.CeilToInt(clear / RoadCell);
		var home = new Vector2I(Mathf.FloorToInt(centre.X / RoadCell), Mathf.FloorToInt(centre.Y / RoadCell));
		for (int x = -reach; x <= reach; x++)
		{
			for (int y = -reach; y <= reach; y++)
			{
				if (_roads.TryGetValue(home + new Vector2I(x, y), out List<Vector2> points)
					&& points.Exists(point => point.DistanceSquaredTo(centre) < clear * clear))
				{
					return false;
				}
			}
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
}
