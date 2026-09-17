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

	// The map image's pixels laid out in world units, and how tall a full-white height pixel is.
	private const float MapWidth = 153.6f;
	private const float MapDepth = 102.4f;
	private const float HeightScale = 20.0f;
	private const float SeaLevel = 0.55f;

	// Fixed pitch: the map reads like the painted original from one angle, and markers stay
	// where the player expects. Free orbit can come later if armies ever need to be seen behind
	// a mountain.
	private const float CameraPitchDegrees = -52.0f;
	private const float MinDistance = 45.0f;
	private const float MaxDistance = 170.0f;
	private const float ZoomStep = 8.0f;
	private const float PanSpeed = 0.13f;

	private Camera3D _camera;
	private ShaderMaterial _terrainMaterial;
	private Image _heightImage;
	private Image _idImage;
	private Vector3 _focus = Vector3.Zero;
	private float _distance = 120.0f;

	public override void _Ready()
	{
		_heightImage = GD.Load<Image>(HeightPath);
		_idImage = GD.Load<Image>(IdPath);

		BuildEnvironment();
		BuildTerrain();
		BuildWater();

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

	/// <summary>Terrain height in world units at a map pixel — where a marker or a future army
	/// has to stand so it isn't buried in a hillside.</summary>
	public float HeightAt(Vector2 mapPixel) => SampleHeight(MapToWorld(mapPixel));

	// --- world building ------------------------------------------------------------------

	private void BuildEnvironment()
	{
		var sky = new ProceduralSkyMaterial
		{
			SkyTopColor = new Color("2b4a74"),
			SkyHorizonColor = new Color("8aa0b4"),
			GroundHorizonColor = new Color("6b7280"),
			SunAngleMax = 24.0f,
		};
		var environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = sky },
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightEnergy = 0.65f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			FogEnabled = true,
			FogLightColor = new Color("9fb4c8"),
			FogDensity = 0.0022f,
		};
		AddChild(new WorldEnvironment { Environment = environment });

		// Low sun: long shadows off the ridge are what make the relief read as relief.
		var light = new DirectionalLight3D
		{
			LightEnergy = 1.35f,
			LightColor = new Color("fff2d8"),
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 320.0f,
		};
		light.RotationDegrees = new Vector3(-42, -38, 0);
		AddChild(light);
	}

	private void BuildTerrain()
	{
		_terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>(TerrainShaderPath) };
		_terrainMaterial.SetShaderParameter("height_map", ImageTexture.CreateFromImage(_heightImage));
		_terrainMaterial.SetShaderParameter("albedo_map", GD.Load<Texture2D>(AlbedoPath));
		_terrainMaterial.SetShaderParameter("id_map", ImageTexture.CreateFromImage(_idImage));
		_terrainMaterial.SetShaderParameter("height_scale", HeightScale);
		_terrainMaterial.SetShaderParameter("world_texel",
			new Vector2(MapWidth / _heightImage.GetWidth(), MapDepth / _heightImage.GetHeight()));

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
		var material = new StandardMaterial3D
		{
			AlbedoColor = new Color("14304f"),
			Metallic = 0.35f,
			Roughness = 0.09f,
		};
		AddChild(new MeshInstance3D
		{
			// Wider than the land so the sea runs past the coast to the horizon.
			Mesh = new PlaneMesh { Size = new Vector2(MapWidth * 3.5f, MapDepth * 3.5f), Material = material },
			Position = new Vector3(0, SeaLevel, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
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
