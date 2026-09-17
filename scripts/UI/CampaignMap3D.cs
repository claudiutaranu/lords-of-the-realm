using Godot;

/// <summary>The campaign map as real geometry: a plane displaced by campaign-map-height.png,
/// lit and shadowed, viewed through a fixed-pitch camera the player pans and zooms. It owns
/// the space entirely — the page above it only asks it questions in map-pixel or screen terms.
///
/// Three images describe the same 1536x1024 map and must stay in step (all regenerated together
/// by tools/generate_campaign_map.py): height drives the mesh AND the click raycast, albedo is
/// what you see, and the ID map says which province a point belongs to. Height and ID are
/// imported as Image, not Texture2D, so the CPU can read their pixels in an exported build;
/// the GPU copies are built from them here.</summary>
public partial class CampaignMap3D : Node3D
{
	private const string HeightPath = "res://assets/ui/campaign-map-height.png";
	private const string AlbedoPath = "res://assets/ui/campaign-map-albedo.png";
	private const string IdPath = "res://assets/ui/campaign-map-ids.png";
	private const string TerrainShaderPath = "res://assets/shaders/terrain.gdshader";
	private const string TerrainTextureDirectory = "res://assets/terrain";

	// The map image's pixels laid out in world units, and how tall a full-white height pixel is.
	private const float MapWidth = 153.6f;
	private const float MapDepth = 102.4f;
	private const float HeightScale = 20.0f;
	// The height map stores the seabed too: this byte value is the waterline, and everything below
	// it is under water (tools/generate_campaign_map.py: SEA_FLOOR_BYTE).
	private const float SeaFloorByte = 46.0f;
	private const float SeaLevel = HeightScale * SeaFloorByte / 255.0f;

	// Fixed pitch: the map reads like the painted original from one angle, and markers stay
	// where the player expects. Free orbit can come later if armies ever need to be seen behind
	// a mountain.
	private const float CameraPitchDegrees = -52.0f;
	private const float MinDistance = 45.0f;
	private const float MaxDistance = 170.0f;
	private const float ZoomStep = 8.0f;
	private const float PanSpeed = 0.13f;
	private const float KeyPanSpeed = 62.0f; // world units per second, at full zoom-out

	/// <summary>What a season does to the light over the map: the sun's colour and strength, the sky
	/// it comes out of, and the haze on the horizon. The ground and the sea are seasoned by their own
	/// shaders; this is the weather over them.</summary>
	private record SeasonLight(Color Sun, float Energy, Color SkyTop, Color SkyHorizon, Color Fog, float FogDensity);

	// Season enum order: spring, summer, autumn, winter.
	private static readonly SeasonLight[] LightBySeason =
	{
		new(new("ffeccf"), 1.30f, new("31558a"), new("9db5c4"), new("aec2d2"), 0.00055f),
		new(new("fff2d8"), 1.35f, new("2b4a74"), new("8aa0b4"), new("9fb4c8"), 0.00050f),
		new(new("ffdba8"), 1.18f, new("3a5470"), new("c2a681"), new("bfae95"), 0.00075f),
		new(new("dfeaff"), 0.92f, new("4c5d74"), new("c3ccd4"), new("cbd6df"), 0.00110f),
	};

	private Camera3D _camera;
	private ShaderMaterial _terrainMaterial;
	private Image _heightImage;
	private Image _idImage;
	private MapDecoration _decoration;
	private MapWater _water;
	private DirectionalLight3D _sun;
	private ProceduralSkyMaterial _sky;
	private Godot.Environment _environment;
	private Vector3 _focus = Vector3.Zero;
	private float _distance = 120.0f;

	public override void _Ready()
	{
		_heightImage = GD.Load<Image>(HeightPath);
		_idImage = GD.Load<Image>(IdPath);

		BuildEnvironment();
		BuildTerrain();
		BuildWater();

		_decoration = new MapDecoration();
		AddChild(_decoration);
		_decoration.Build(this);

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

	public void SetHighlight(int selectedIndex, int hoveredIndex)
	{
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

	/// <summary>Pan with a right/middle drag, zoom on the wheel. Left clicks are the page's,
	/// for selecting provinces.</summary>
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

		SeasonLight light = LightBySeason[(int)season];
		_sun.LightColor = light.Sun;
		_sun.LightEnergy = light.Energy;
		_sky.SkyTopColor = light.SkyTop;
		_sky.SkyHorizonColor = light.SkyHorizon;
		_environment.FogLightColor = light.Fog;
		_environment.FogDensity = light.FogDensity;
	}

	/// <summary>World position of a map pixel, sitting on the terrain surface.</summary>
	public Vector3 WorldAt(Vector2 mapPixel) => MapToWorld(mapPixel);

	/// <summary>Puts a working site (quarry, pasture, lumber camp) on a province's ground.</summary>
	public void AddSite(Vector2 seatPixel, MapDecoration.SiteKind kind, float weight) =>
		_decoration.AddSite(seatPixel, kind, weight);

	/// <summary>Terrain height in world units at a map pixel — where a marker or a future army
	/// has to stand so it isn't buried in a hillside.</summary>
	public float HeightAt(Vector2 mapPixel) => SampleHeight(MapToWorld(mapPixel));

	/// <summary>The waterline in world units — anything below it is sea.</summary>
	public float WaterLine => SeaLevel;

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
			AmbientLightEnergy = 0.65f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			FogEnabled = true,
			FogLightColor = new Color("9fb4c8"),
			FogDensity = 0.0005f,
			FogSkyAffect = 0.15f,
			FogAerialPerspective = 0.25f,
			SsaoEnabled = true,
			SsaoRadius = 3.0f,
			SsaoIntensity = 1.4f,
		};
		AddChild(new WorldEnvironment { Environment = _environment });

		// Low sun: long shadows off the ridge are what make the relief read as relief.
		_sun = new DirectionalLight3D
		{
			LightEnergy = 1.35f,
			LightColor = new Color("fff2d8"),
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 320.0f,
			ShadowBlur = 1.4f,
		};
		_sun.RotationDegrees = new Vector3(-42, -38, 0);
		AddChild(_sun);
	}

	private void BuildTerrain()
	{
		_terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(TerrainShaderPath) };
		_terrainMaterial.SetShaderParameter("height_map", ImageTexture.CreateFromImage(_heightImage));
		_terrainMaterial.SetShaderParameter("albedo_map", GD.Load<Texture2D>(AlbedoPath));
		_terrainMaterial.SetShaderParameter("id_map", ImageTexture.CreateFromImage(_idImage));
		_terrainMaterial.SetShaderParameter("height_scale", HeightScale);
		_terrainMaterial.SetShaderParameter("sea_level_normalized", SeaFloorByte / 255.0f);
		_terrainMaterial.SetShaderParameter("map_size_x", MapWidth);
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

		// One vertex per 4 height pixels: finer than that and the mesh resolves noise the
		// heightmap doesn't actually carry.
		var mesh = new PlaneMesh
		{
			Size = new Vector2(MapWidth, MapDepth),
			SubdivideWidth = _heightImage.GetWidth() / 4,
			SubdivideDepth = _heightImage.GetHeight() / 4,
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
