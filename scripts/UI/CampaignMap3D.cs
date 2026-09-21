using System.Collections.Generic;
using Godot;

/// <summary>The campaign map as real geometry: a plane displaced by the campaign's map-height.png,
/// lit and shadowed, viewed through a fixed-pitch camera the player pans and zooms. It owns
/// the space entirely — the page above it only asks it questions in map-pixel or screen terms.
///
/// Three images describe the same 1536x1024 map and must stay in step (all regenerated together
/// by tools/generate_campaign_map.py, into the played campaign's own asset folder): height drives
/// the mesh AND the click raycast, albedo is
/// what you see, and the ID map says which province a point belongs to. Height and ID are
/// imported as Image, not Texture2D, so the CPU can read their pixels in an exported build;
/// the GPU copies are built from them here.</summary>
public partial class CampaignMap3D : Node3D
{
	// The three images are the played campaign's own (Campaign.Asset); the shader and the ground
	// textures below are the engine's, shared by every campaign.
	private const string HeightFile = "map-height.png";
	private const string AlbedoFile = "map-albedo.png";
	private const string IdFile = "map-ids.png";
	private const string TerrainShaderPath = "res://assets/shaders/terrain.gdshader";
	private const string TerrainTextureDirectory = "res://assets/terrain";

	// The map image's pixels laid out in world units, and how tall a full-white height pixel is.
	// Grown 20% over the original 153.6x102.4: props keep their real size, so the realm reads as
	// bigger ground rather than the same map zoomed. Height goes with it or the relief flattens.
	private const float MapWidth = 184.3f;
	private const float MapDepth = 122.9f;
	// How tall the relief stands. The height map says where the ground rises and by how much
	// relative to itself; this alone says how much of that the player sees, so it is the one number
	// that makes the realm rolling country or a mountain range. Down from 24, and down again: a lord
	// reads his realm off the roads between his counties, and on tall relief the roads spend half
	// their length behind hills. Flatter is not prettier, it is legible — the old game this one is
	// copied from drew almost no relief at all and never lost a road. Lower still would flatten the
	// snow line into a painted stripe, because snow is decided on the height map rather than here.
	private const float HeightScale = 8.0f;

	/// <summary>How coarsely the ID map is read when looking for borders, in pixels.</summary>
	private const int BorderStep = 3;
	// The height map stores the seabed too: this byte value is the waterline, and everything below
	// it is under water (tools/generate_campaign_map.py: SEA_FLOOR_BYTE).
	private const float SeaFloorByte = 46.0f;
	private const float SeaLevel = HeightScale * SeaFloorByte / 255.0f;

	// Fixed pitch: the map reads like the painted original from one angle, and markers stay
	// where the player expects. Free orbit can come later if armies ever need to be seen behind
	// a mountain.
	private const float CameraPitchDegrees = -52.0f;
	private const float MinDistance = 54.0f;
	private const float MaxDistance = 152.0f;
	private const float ZoomStep = 9.5f;
	// Trackpad gestures carry continuous deltas, not the wheel's discrete clicks, so they need their
	// own scale: how many world units one unit of two-finger scroll, and one of pinch, are worth.
	// Tune by feel — these are not comparable to ZoomStep.
	private const float PanGestureZoomStep = 18.0f;
	private const float MagnifyZoomStep = 120.0f;
	private const float PanSpeed = 0.13f;
	private const float KeyPanSpeed = 74.0f; // world units per second, at full zoom-out

	/// <summary>What a season does to the light over the map: the sun's colour and strength, the sky
	/// it comes out of, and the haze on the horizon. The ground and the sea are seasoned by their own
	/// shaders; this is the weather over them.</summary>
	private record SeasonLight(Color Sun, float Energy, Color SkyTop, Color SkyHorizon, Color Fog, float FogDensity);

	// Season enum order: spring, summer, autumn, winter.
	private static readonly SeasonLight[] LightBySeason =
	{
		new(new("ffeccf"), 1.30f, new("31558a"), new("9db5c4"), new("aec2d2"), 0.00022f),
		new(new("fff2d8"), 1.35f, new("2b4a74"), new("8aa0b4"), new("9fb4c8"), 0.00018f),
		new(new("ffdba8"), 1.18f, new("3a5470"), new("c2a681"), new("bfae95"), 0.00030f),
		new(new("dfeaff"), 0.92f, new("4c5d74"), new("c3ccd4"), new("cbd6df"), 0.00048f),
	};

	private Camera3D _camera;
	private ShaderMaterial _terrainMaterial;
	private Image _heightImage;
	private Image _idImage;
	private MapDecoration _decoration;
	private MapClouds _clouds;
	private MapWater _water;
	private DirectionalLight3D _sun;
	private ProceduralSkyMaterial _sky;
	private Godot.Environment _environment;
	private Vector3 _focus = Vector3.Zero;
	private float _distance = 132.0f;

	public override void _Ready()
	{
		_heightImage = GD.Load<Image>(Campaign.Asset(HeightFile));
		_idImage = GD.Load<Image>(Campaign.Asset(IdFile));

		BuildEnvironment();
		BuildTerrain();
		BuildWater();

		_decoration = new MapDecoration();
		AddChild(_decoration);
		_decoration.Build(this);

		_clouds = new MapClouds();
		AddChild(_clouds);
		// HeightScale is what a fully white height pixel stands for, so it is the tallest ground
		// this map can have — the sky is placed against that.
		_clouds.Build(new Vector2(MapWidth, MapDepth), HeightScale);

		_camera = new Camera3D { Fov = 48.0f, Far = 800.0f, Current = true };
		AddChild(_camera);
		UpdateCamera();
	}

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

	/// <summary>Which counties share a border, by province index. Read off the ID map itself, which
	/// is the only thing that actually knows: seats a short way apart can be separated by a bay, and
	/// two counties that look far apart on the pins can run a hundred miles of frontier together.
	///
	/// Sampled rather than walked pixel by pixel. A million and a half GetPixel calls to answer a
	/// question about where borders are is a second of loading for an answer a third of the pixels
	/// gives exactly as well — a border long enough for an army to cross is many pixels wide.</summary>
	public List<(int A, int B)> Borders()
	{
		var found = new HashSet<(int, int)>();
		int width = _idImage.GetWidth();
		int height = _idImage.GetHeight();
		for (int y = 0; y < height - BorderStep; y += BorderStep)
		{
			for (int x = 0; x < width - BorderStep; x += BorderStep)
			{
				int here = IdAt(x, y);
				if (here < 0)
				{
					continue;
				}

				foreach (int there in new[] { IdAt(x + BorderStep, y), IdAt(x, y + BorderStep) })
				{
					if (there >= 0 && there != here)
					{
						found.Add((Mathf.Min(here, there), Mathf.Max(here, there)));
					}
				}
			}
		}

		return new List<(int, int)>(found);
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

	/// <summary>Whose banner stands under a map pixel, or nothing.</summary>
	public string ArmyAt(Vector2 mapPixel) => _decoration.ArmyAt(mapPixel);

	/// <summary>Walks a county's banner along a road, and says when it has arrived.</summary>
	public void WalkArmy(string province, List<Vector2> road, System.Action arrived) =>
		_decoration.WalkArmy(province, road, arrived);

	/// <summary>Puts a county's men on the ground, or takes them off it.</summary>
	public void SetArmy(string province, Vector2 seatPixel, bool standing) =>
		_decoration.SetArmy(province, seatPixel, standing);

	/// <summary>Which of a county's fields sits under a map pixel, or -1.</summary>
	public int PlotAt(string province, Vector2 mapPixel) => _decoration.PlotAt(province, mapPixel);

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

	/// <summary>Raised whenever the chosen province changes. The minimap listens to this rather than
	/// being wired up by the page, so it keeps working however that page is rearranged.</summary>
	public static event System.Action<int> ProvinceHighlighted;

	public void SetHighlight(int selectedIndex, int hoveredIndex)
	{
		ProvinceHighlighted?.Invoke(selectedIndex);

		// The shader compares against the ID map's raw red channel, which is index + 1 so that
		// 0 can mean water.
		_terrainMaterial.SetShaderParameter("selected_index", selectedIndex + 1);
		_terrainMaterial.SetShaderParameter("hovered_index", hoveredIndex + 1);
	}

	// Physical key positions, not letters, so WASD stays under the same fingers on a non-QWERTY
	// layout. Polled rather than handled as events: holding a key has to pan every frame, and the
	// UI over the map would otherwise eat the repeats.
	public override void _Process(double delta)
	{
		var move = new Vector3(
			(IsHeld(Key.D) || IsHeld(Key.Right) ? 1 : 0) - (IsHeld(Key.A) || IsHeld(Key.Left) ? 1 : 0),
			0,
			(IsHeld(Key.S) || IsHeld(Key.Down) ? 1 : 0) - (IsHeld(Key.W) || IsHeld(Key.Up) ? 1 : 0));
		if (move == Vector3.Zero)
		{
			return;
		}

		// Close in, the same key press should cover less ground, or the map bolts away from you.
		float speed = KeyPanSpeed * Mathf.Max(_distance / MaxDistance, 0.35f);
		_focus += move.Normalized() * speed * (float)delta;
		ClampFocus();
		UpdateCamera();
	}

	private static bool IsHeld(Key key) => Input.IsPhysicalKeyPressed(key);

	/// <summary>Pan with a right/middle drag, zoom on the wheel, a two-finger scroll or a pinch.
	/// Left clicks are the page's, for selecting provinces.</summary>
	public void HandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton button && button.Pressed)
		{
			if (button.ButtonIndex == MouseButton.WheelUp)
			{
				Zoom(-ZoomStep);
			}
			else if (button.ButtonIndex == MouseButton.WheelDown)
			{
				Zoom(ZoomStep);
			}
		}
		// A trackpad sends no wheel buttons at all: macOS gives anything with a gesture phase to
		// Godot as a pan or magnify event instead, which is why the wheel branch above never fires
		// on a laptop. Both are already the inverse of the finger movement, so scrolling up zooms in
		// exactly as the wheel does.
		else if (@event is InputEventPanGesture pan)
		{
			Zoom(pan.Delta.Y * PanGestureZoomStep);
		}
		else if (@event is InputEventMagnifyGesture magnify)
		{
			Zoom((1.0f - magnify.Factor) * MagnifyZoomStep);
		}
		else if (@event is InputEventMouseMotion motion &&
			(motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
		{
			// Drag distance scales with zoom so the ground keeps up with the cursor at any height.
			float scale = PanSpeed * (_distance / MaxDistance) * 2.0f;
			_focus += new Vector3(-motion.Relative.X, 0, -motion.Relative.Y) * scale;
			ClampFocus();
			UpdateCamera();
		}
	}

	/// <summary>Turns the whole map over to a season: the ground, the sea, what grows on it and the
	/// light it all stands in. Called on every turn change, from behind the turn curtain, so the
	/// change is never seen happening.</summary>
	public void SetSeason(Season season)
	{
		_terrainMaterial.SetShaderParameter("season", (float)(int)season);
		_water.SetSeason(season);
		_decoration.SetSeason(season);
		_clouds.SetSeason(season);

		SeasonLight light = LightBySeason[(int)season];
		_sun.LightColor = light.Sun;
		_sun.LightEnergy = light.Energy;
		_sky.SkyTopColor = light.SkyTop;
		_sky.SkyHorizonColor = light.SkyHorizon;
		_environment.FogLightColor = light.Fog;
		_environment.FogDensity = light.FogDensity;
	}

	/// <summary>Map pixels to a world unit — anything that has to measure a width on the ground
	/// needs this to convert before sampling.</summary>
	public float PixelsPerUnit => _heightImage.GetWidth() / MapWidth;

	/// <summary>World position of a map pixel, sitting on the terrain surface.</summary>
	public Vector3 WorldAt(Vector2 mapPixel) => MapToWorld(mapPixel);

	/// <summary>Puts a working site (quarry, pasture, lumber camp) on a province's ground.</summary>
	public void AddSite(Vector2 seatPixel, MapDecoration.SiteKind kind, float weight) =>
		_decoration.AddSite(seatPixel, kind, weight);

	/// <summary>Raises a province's settlement on its seat — what the map pin points at.</summary>
	public void AddSettlement(Vector2 seatPixel, MapDecoration.Settlement kind) =>
		_decoration.AddSettlement(seatPixel, kind);

	/// <summary>Puts a province's walls on the ground beside its town, taking down whatever stood
	/// there before. Called again whenever a build finishes, so the map keeps up with the ledger.</summary>
	public void SetFortification(string province, Vector2 seatPixel, string fort) =>
		_decoration.SetFortification(province, seatPixel, fort);

	/// <summary>Lays a province's fields on its ground — one plot per field, under what the province
	/// has it under. Called again whenever the land or the season changes, so the map keeps up with
	/// the ledger the same way the walls do.</summary>
	public void SetFields(string name, Vector2 seatPixel, ProvinceEconomy province, Season season) =>
		_decoration.SetFields(name, seatPixel, province, season);

	/// <summary>Sows the woods, once everything built is standing. Last, so they grow around it.</summary>
	public void SowWoods() => _decoration.SowWoods();

	/// <summary>Sets the march stones along every frontier between two counties.</summary>
	public void SowBorderStones() => _decoration.SowBorderStones();

	/// <summary>How close the camera is standing, as a multiple of how close it stands when pulled
	/// all the way out: 1 at the far end and near three at the near one. What is pinned to the
	/// ground rather than painted on it grows by this, so a mark over a county reads at the same
	/// size against the land whatever the lord has done with the wheel.</summary>
	public float Closeness => MaxDistance / _distance;

	/// <summary>Terrain height in world units at a map pixel — where a marker or a future army
	/// has to stand so it isn't buried in a hillside.</summary>
	public float HeightAt(Vector2 mapPixel) => SampleHeight(MapToWorld(mapPixel));

	/// <summary>The waterline in world units — anything below it is sea.</summary>
	public float WaterLine => SeaLevel;

	/// <summary>How tall this map's relief stands, in world units: the height of ground the map's
	/// whitest pixel would carry. Anything that means "high up" has to be a share of this rather
	/// than a height of its own, or flattening the map leaves it behind at the old altitude.</summary>
	public float Relief => HeightScale;

	// --- world building ------------------------------------------------------------------

	private void BuildEnvironment()
	{
		_sky = new ProceduralSkyMaterial
		{
			SkyTopColor = new Color("2b4a74"),
			SkyHorizonColor = new Color("8aa0b4"),
			GroundHorizonColor = new Color("6b7280"),
			SunAngleMax = 24.0f,
		};
		_environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = _sky },
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightEnergy = 0.45f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			FogEnabled = true,
			FogLightColor = new Color("9fb4c8"),
			FogDensity = 0.00018f,
			FogSkyAffect = 0.1f,
			FogAerialPerspective = 0.18f,
			SsaoEnabled = true,
			SsaoRadius = 1.8f,
			SsaoIntensity = 1.2f,
		};
		AddChild(new WorldEnvironment { Environment = _environment });

		// Low sun: long shadows off the ridge are what make the relief read as relief.
		_sun = new DirectionalLight3D
		{
			LightEnergy = 1.55f,
			LightColor = new Color("fff0cf"),
			ShadowEnabled = true,
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
			DirectionalShadowMaxDistance = 190.0f,
			ShadowBlur = 1.4f,
			// The seabed is a single near-flat sheet the size of the map, seen almost edge-on: at the
			// default bias it shadows itself, and the streaks that come off every islet run halfway to
			// the horizon across the water. Nothing on land needs a bias this generous; the flat sea
			// does, and it is the same light over both.
			ShadowBias = 0.6f,
			ShadowNormalBias = 4.0f,
		};
		_sun.RotationDegrees = new Vector3(-42, -38, 0);
		AddChild(_sun);
	}

	private void BuildTerrain()
	{
		_terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(TerrainShaderPath) };
		_terrainMaterial.SetShaderParameter("height_map", ImageTexture.CreateFromImage(_heightImage));
		_terrainMaterial.SetShaderParameter("albedo_map", GD.Load<Texture2D>(Campaign.Asset(AlbedoFile)));
		_terrainMaterial.SetShaderParameter("id_map", ImageTexture.CreateFromImage(_idImage));
		_terrainMaterial.SetShaderParameter("height_scale", HeightScale);
		_terrainMaterial.SetShaderParameter("sea_level_normalized", SeaFloorByte / 255.0f);
		_terrainMaterial.SetShaderParameter("map_size_x", MapWidth);
		// Ground texture density stays fixed to world units, so growing the map does not stretch it.
		_terrainMaterial.SetShaderParameter("detail_tiling", MapWidth * 0.586f);
		_terrainMaterial.SetShaderParameter("world_texel",
			new Vector2(MapWidth / _heightImage.GetWidth(), MapDepth / _heightImage.GetHeight()));

		// CC0 ground textures (assets/terrain/LICENSE.txt), tiled far tighter than the map so the
		// surface has grain of its own instead of reading as painted clay.
		foreach (string surface in new[] { "grass", "rock", "snow", "sand" })
		{
			_terrainMaterial.SetShaderParameter($"{surface}_texture",
				GD.Load<Texture2D>($"{TerrainTextureDirectory}/{surface}-diffuse.jpg"));
			_terrainMaterial.SetShaderParameter($"{surface}_normal",
				GD.Load<Texture2D>($"{TerrainTextureDirectory}/{surface}-normal.jpg"));
		}

		var mesh = new PlaneMesh
		{
			Size = new Vector2(MapWidth, MapDepth),
			// One vertex per 3 height pixels: enough that an islet is a shape rather than a few flat
			// facets, without the 790k triangles that one vertex per 2 pixels cost.
			SubdivideWidth = _heightImage.GetWidth() / 3,
			SubdivideDepth = _heightImage.GetHeight() / 3,
			Material = _terrainMaterial,
		};
		AddChild(new MeshInstance3D
		{
			Mesh = mesh,
			// The plane's own bounds are flat, so displaced peaks get culled at the screen edge
			// unless the AABB is grown to cover where the vertex shader actually puts them.
			CustomAabb = new Aabb(new Vector3(-MapWidth / 2, 0, -MapDepth / 2),
				new Vector3(MapWidth, HeightScale, MapDepth)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
		});
	}

	private void BuildWater()
	{
		_water = new MapWater();
		AddChild(_water);
		_water.Build(_heightImage, new Vector2(MapWidth, MapDepth), HeightScale, SeaLevel);
	}

	// --- camera ---------------------------------------------------------------------------

	private void Zoom(float amount)
	{
		_distance = Mathf.Clamp(_distance + amount, MinDistance, MaxDistance);
		UpdateCamera();
	}

	private void ClampFocus()
	{
		_focus.X = Mathf.Clamp(_focus.X, -MapWidth / 2, MapWidth / 2);
		_focus.Z = Mathf.Clamp(_focus.Z, -MapDepth / 2, MapDepth / 2);
	}

	private void UpdateCamera()
	{
		float pitch = Mathf.DegToRad(CameraPitchDegrees);
		_camera.Position = _focus + new Vector3(0, -Mathf.Sin(pitch) * _distance, Mathf.Cos(pitch) * _distance);
		_camera.RotationDegrees = new Vector3(CameraPitchDegrees, 0, 0);
		_clouds?.SetFocus(_focus);
	}

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
