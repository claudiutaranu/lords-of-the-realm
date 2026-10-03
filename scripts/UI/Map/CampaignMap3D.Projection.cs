using Godot;

/// <summary>Between the screen, the map's pixels and the world: which county is under a point,
/// where a pixel stands in the land and on the screen, and the ray cast onto the terrain to find
/// out.</summary>
public partial class CampaignMap3D
{
	// --- what the page above needs -----------------------------------------------------

	/// <summary>Province index under a point in viewport coordinates, or -1 for sea and sky.</summary>
	public int ProvinceAt(Vector2 viewportPosition)
	{
		Vector3 origin = _camera.ProjectRayOrigin(viewportPosition);
		Vector3 direction = _camera.ProjectRayNormal(viewportPosition);
		return RayHitsTerrain(origin, direction, out Vector3 hit) ? ProvinceAtWorld(hit) : -1;
	}

	/// <summary>Where on the map a point in the viewport lands, in map pixels. False for sea and sky,
	/// the same as <see cref="ProvinceAt"/> — the two answer the same ray.</summary>
	public bool TryMapPixel(Vector2 viewportPosition, out Vector2 mapPixel)
	{
		Vector3 origin = _camera.ProjectRayOrigin(viewportPosition);
		Vector3 direction = _camera.ProjectRayNormal(viewportPosition);
		if (!RayHitsTerrain(origin, direction, out Vector3 hit) || !IsInsideMap(hit))
		{
			mapPixel = Vector2.Zero;
			return false;
		}

		Vector2I pixel = WorldToPixel(hit);
		mapPixel = new Vector2(pixel.X, pixel.Y);
		return true;
	}

	private int IdAt(int x, int y) => Mathf.RoundToInt(_idImage.GetPixel(x, y).R * 255f) - 1;

	/// <summary>Which county a map pixel belongs to, or -1 for water and the edge of the world.</summary>
	public int CountyAt(Vector2 mapPixel)
	{
		int x = Mathf.Clamp(Mathf.RoundToInt(mapPixel.X), 0, _idImage.GetWidth() - 1);
		int y = Mathf.Clamp(Mathf.RoundToInt(mapPixel.Y), 0, _idImage.GetHeight() - 1);
		return IdAt(x, y);
	}

	/// <summary>How big the map is in pixels, for anything that wants to lay a grid over it.</summary>
	public Vector2I MapPixels => new(_heightImage.GetWidth(), _heightImage.GetHeight());

	/// <summary>Where a map pixel currently sits on screen, for the 2D markers drawn over the
	/// viewport. Returns false when it is behind the camera.</summary>
	public bool TryScreenPosition(Vector2 mapPixel, out Vector2 screenPosition)
	{
		Vector3 world = MapToWorld(mapPixel);
		if (_camera == null || _camera.IsPositionBehind(world))
		{
			screenPosition = Vector2.Zero;
			return false;
		}

		screenPosition = _camera.UnprojectPosition(world);
		return true;
	}

	/// <summary>Map pixels to a world unit — anything that has to measure a width on the ground
	/// needs this to convert before sampling.</summary>
	public float PixelsPerUnit => _heightImage.GetWidth() / MapWidth;

	/// <summary>The map pixel under a point in the world.</summary>
	public Vector2 MapPixelOf(Vector3 world) => new(
		(world.X / MapWidth + 0.5f) * _heightImage.GetWidth(),
		(world.Z / MapDepth + 0.5f) * _heightImage.GetHeight());

	/// <summary>World position of a map pixel, sitting on the terrain surface.</summary>
	public Vector3 WorldAt(Vector2 mapPixel) => MapToWorld(mapPixel);

	/// <summary>Terrain height in world units at a map pixel — where a marker or a future army
	/// has to stand so it isn't buried in a hillside.</summary>
	public float HeightAt(Vector2 mapPixel) => SampleHeight(MapToWorld(mapPixel));

	/// <summary>The waterline in world units — anything below it is sea.</summary>
	public float WaterLine => SeaLevel;

	/// <summary>How tall this map's relief stands, in world units: the height of ground the map's
	/// whitest pixel would carry. Anything that means "high up" has to be a share of this rather
	/// than a height of its own, or flattening the map leaves it behind at the old altitude.</summary>
	public float Relief => HeightScale;

	// --- sampling the same images the shader draws from --------------------------------------

	private Vector3 MapToWorld(Vector2 mapPixel)
	{
		var world = new Vector3(
			(mapPixel.X / _heightImage.GetWidth() - 0.5f) * MapWidth,
			0,
			(mapPixel.Y / _heightImage.GetHeight() - 0.5f) * MapDepth);
		world.Y = SampleHeight(world);
		return world;
	}

	private Vector2I WorldToPixel(Vector3 world)
	{
		float u = world.X / MapWidth + 0.5f;
		float v = world.Z / MapDepth + 0.5f;
		return new Vector2I(
			Mathf.Clamp((int)(u * _heightImage.GetWidth()), 0, _heightImage.GetWidth() - 1),
			Mathf.Clamp((int)(v * _heightImage.GetHeight()), 0, _heightImage.GetHeight() - 1));
	}

	private bool IsInsideMap(Vector3 world) =>
		Mathf.Abs(world.X) <= MapWidth / 2 && Mathf.Abs(world.Z) <= MapDepth / 2;

	private float SampleHeight(Vector3 world)
	{
		if (!IsInsideMap(world))
		{
			return 0f;
		}

		Vector2I pixel = WorldToPixel(world);
		return _heightImage.GetPixel(pixel.X, pixel.Y).R * HeightScale;
	}

	private int ProvinceAtWorld(Vector3 world)
	{
		if (!IsInsideMap(world))
		{
			return -1;
		}

		Vector2I pixel = WorldToPixel(world);
		return Mathf.RoundToInt(_idImage.GetPixel(pixel.X, pixel.Y).R * 255f) - 1;
	}

	/// <summary>Walks the ray forward until it passes under the terrain, then bisects to land on
	/// the surface. Marching the same heightmap the shader displaces by keeps clicks honest on
	/// the mountains, where a flat ground plane would be off by a province.</summary>
	private bool RayHitsTerrain(Vector3 origin, Vector3 direction, out Vector3 hit)
	{
		const float step = 0.6f;
		const float maxDistance = 600f;
		const int refineSteps = 12;

		hit = Vector3.Zero;
		if (direction.Y >= 0f)
		{
			return false; // looking up: nothing below to hit
		}

		// Skip the empty air above the highest possible peak instead of marching through it.
		float travelled = origin.Y > HeightScale ? (origin.Y - HeightScale) / -direction.Y : 0f;
		float previous = travelled;

		while (travelled < maxDistance)
		{
			Vector3 point = origin + direction * travelled;
			if (point.Y < 0f)
			{
				return false; // under the sea floor: the ray passed the map by, don't march the rest
			}

			if (point.Y <= SampleHeight(point))
			{
				float low = previous;
				float high = travelled;
				for (int i = 0; i < refineSteps; i++)
				{
					float middle = (low + high) / 2f;
					Vector3 probe = origin + direction * middle;
					if (probe.Y <= SampleHeight(probe))
					{
						high = middle;
					}
					else
					{
						low = middle;
					}
				}

				hit = origin + direction * high;
				return true;
			}

			previous = travelled;
			travelled += step;
		}

		return false;
	}
}
