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
	private const string SurfaceFile = "map-surface.png";
	private const string CoastFile = "map-coast.png";
	private const string IdFile = "map-ids.png";

	// The map image's pixels laid out in world units, and how tall a full-white height pixel is.
	// Grown 20% over the original 153.6x102.4: props keep their real size, so the realm reads as
	// bigger ground rather than the same map zoomed. Height goes with it or the relief flattens.
	//
	// The exact figures are no longer free: the ground is drawn by Terrain3D, which will not lay
	// vertices closer than a quarter of a world unit, so the world is however wide the height map
	// makes it at that spacing. 1536 pixels at one vertex per two of them is 192. Everything placed
	// on the map goes through MapToWorld and follows these two, so this is a four per cent stretch
	// of the old figures and nothing else moves.
	private static readonly float MapWidth = Terrain3DGround.WorldWidth(1536);
	private static readonly float MapDepth = Terrain3DGround.WorldWidth(1024);
	// How tall the relief stands. The height map says where the ground rises and by how much
	// relative to itself; this alone says how much of that the player sees, so it is the one number
	// that makes the realm rolling country or a mountain range. Down from 24, and down again: a lord
	// reads his realm off the roads between his counties, and on tall relief the roads spend half
	// their length behind hills. Flatter is not prettier, it is legible — the old game this one is
	// copied from drew almost no relief at all and never lost a road. Lower still would flatten the
	// snow line into a painted stripe, because snow is decided on the height map rather than here.
	private const float HeightScale = 26.0f;

	// The height map stores the seabed too: this byte value is the waterline, and everything below
	// it is under water (tools/generate_campaign_map.py: SEA_FLOOR_BYTE).
	private const float SeaFloorByte = 46.0f;
	private const float SeaLevel = HeightScale * SeaFloorByte / 255.0f;

	// Fixed pitch: the map reads like the painted original from one angle, and markers stay
	// where the player expects. Free orbit can come later if armies ever need to be seen behind
	// a mountain.
	private const float CameraPitchDegrees = -52.0f;
	private const float CameraFov = 48.0f;
	// A fifth closer than it was (54): near enough to see a field's rows and a village's roofs.
	private const float MinDistance = 45.0f;
	private const float MaxDistance = 152.0f;
	/// <summary>How far the shadows reach, as a multiple of the camera's distance. From this pitch
	/// and field of view the far edge of the screen lies about 1.53 camera distances deep.</summary>
	private const float ShadowReach = 1.6f;
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
	private Terrain3DGround _ground;
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

		_camera = new Camera3D { Fov = CameraFov, Far = 800.0f, Current = true };
		AddChild(_camera);
		// The clipmap is laid out around the camera, so the ground has to be told which one it
		// follows — without it Terrain3D stops processing and never draws.
		_ground?.Watch(_camera);
		ClampFocus();
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
	public void WalkArmy(string army, List<Vector2> road, System.Action arrived) =>
		_decoration.WalkArmy(army, road, arrived);

	/// <summary>Takes down the banners of companies that are not standing any more.</summary>
	public void RetireArmies(System.Collections.Generic.ICollection<string> standing) =>
		_decoration.RetireArmies(standing);

	/// <summary>Puts a county's men on the ground under their lord's colour, or takes them off it.
	/// How many of them there are decides how many figures stand for them.</summary>
	public void SetArmy(string army, Vector2 seatPixel, bool standing, Color lord, int men) =>
		_decoration.SetArmy(army, seatPixel, standing, lord, men);

	/// <summary>Whose village stands under a map pixel, or nothing.</summary>
	public string TownAt(Vector2 mapPixel) => _decoration.TownAt(mapPixel);

	public bool AtCastle(string province, Vector2 mapPixel) => _decoration.AtCastle(province, mapPixel);

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
		_ground?.SetShaderParameter("selected_index", selectedIndex + 1);
		_ground?.SetShaderParameter("hovered_index", hoveredIndex + 1);
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
		_ground?.SetShaderParameter("season", (float)(int)season);
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

	/// <summary>Raises a province's village on its seat — what the map pin points at — flying its
	/// lord's colour, or hands an existing one's banners to a new lord.</summary>
	public void AddSettlement(string province, Vector2 seatPixel, MapDecoration.Settlement kind, Color lord) =>
		_decoration.AddSettlement(province, seatPixel, kind, lord);

	/// <summary>Puts a province's walls on the ground beside its town, taking down whatever stood
	/// there before. Called again whenever a build finishes, so the map keeps up with the ledger.</summary>
	public void SetFortification(string province, Vector2 seatPixel, string fort, string building, Color lord) =>
		_decoration.SetFortification(province, seatPixel, fort, building, lord);

	/// <summary>Lays a province's fields on its ground — one plot per field, under what the province
	/// has it under. Called again whenever the land or the season changes, so the map keeps up with
	/// the ledger the same way the walls do.</summary>
	public void SetFields(string name, Vector2 seatPixel, ProvinceEconomy province, Season season) =>
		_decoration.SetFields(name, seatPixel, province, season);

	/// <summary>Sows the woods, once everything built is standing. Last, so they grow around it.</summary>
	public void Sow() => _decoration.Sow();




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
			// The island reflected in the sea round it. Only reaches opaque surfaces, which is why the
			// water gave up its transparency for it.
			SsrEnabled = true,
			SsrMaxSteps = 64,
			SsrFadeIn = 0.15f,
			SsrFadeOut = 2.0f,
		};
		AddChild(new WorldEnvironment { Environment = _environment });

		// Low sun: long shadows off the ridge are what make the relief read as relief.
		_sun = new DirectionalLight3D
		{
			LightEnergy = 1.55f,
			LightColor = new Color("fff0cf"),
			ShadowEnabled = true,
			// How far the shadows reach is set with the zoom, in UpdateCamera, and the cascades are laid
			// over the stretch of ground the camera can actually see. The default splits spent most of
			// their cascades on the air between the lens and the land, drew every shadow on screen from
			// the coarsest, and let them stop at a fixed distance, which from the default height was
			// two thirds of the way up the screen: a line with shadow on one side and none on the
			// other, dragged across the island by every pan. The cascades are blended into each other
			// for the same reason, and the shadows fade out past the top of the screen, not on it.
			// Two, not four: every cascade over this ground draws the woods into it again, and four
			// cost 2.2 ms a frame more than two (measured at 1920x1080) for sharper shadows nobody can
			// tell apart at map height. Two still give the view twice the old resolution.
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
			DirectionalShadowSplit1 = 0.72f,
			DirectionalShadowBlendSplits = true,
			DirectionalShadowFadeStart = 0.97f,
			ShadowBlur = 1.4f,
			ShadowBias = 0.15f,
			ShadowNormalBias = 1.5f,
		};
		_sun.RotationDegrees = new Vector3(-42, -38, 0);
		AddChild(_sun);
	}

	private void BuildTerrain()
	{
		if (!Terrain3DGround.Available)
		{
			GD.PushError("CampaignMap3D: the Terrain3D plugin is not loaded, so there is no ground to "
				+ "stand on. On macOS its library arrives quarantined: "
				+ "xattr -dr com.apple.quarantine addons/terrain_3d");
			return;
		}

		Image tint = Terrain3DGround.AsTint(GD.Load<Texture2D>(Campaign.Asset(AlbedoFile)).GetImage());
		Image surface = GD.Load<Texture2D>(Campaign.Asset(SurfaceFile)).GetImage();
		_ground = Terrain3DGround.Build(this, _heightImage, tint, surface, _idImage,
			MapWidth, MapDepth, HeightScale);

		// The waterline, in world units, so the shader does not have to know how the height is
		// scaled. Where the snow and the rock and the beach are is the control map's business now.
		_ground.SetShaderParameter("campaign_sea_level", SeaLevel);
		// How hard one surface gives way to the next. The plugin blends each fragment from the four
		// vertices round it by their own height, and at 0.87 that blend was so sharp that each vertex's
		// surface held a hard-edged square of its own: close in, the mountains were a patchwork of
		// cells. Looked at pixel by pixel, the squares went at 0 and were all but gone at 0.25; at 0
		// grass and stone wash into each other, so 0.2 keeps some of the stone standing proud. The
		// shader's warp (campaign_blend_warp) keeps what edges remain off the grid's straight lines.
		_ground.SetShaderParameter("blend_sharpness", 0.2f);
		// Where a face is steep enough that its texture is laid on from the side instead of from
		// above. From above, a cliff gets one row of the texture stretched the full height of the
		// wall — which is the streaking the sea cliffs showed. The plugin's 0.8 left most of this
		// coast just on the wrong side of the line.
		_ground.SetShaderParameter("projection_threshold", 0.92f);
	}

	private void BuildWater()
	{
		_water = new MapWater();
		AddChild(_water);
		_water.Build(_heightImage, GD.Load<Texture2D>(Campaign.Asset(CoastFile)), new Vector2(MapWidth, MapDepth), HeightScale, SeaLevel);
	}

	// --- camera ---------------------------------------------------------------------------

	private void Zoom(float amount)
	{
		_distance = Mathf.Clamp(_distance + amount, MinDistance, MaxDistance);
		ClampFocus(); // a wider view needs more room around it
		UpdateCamera();
	}

	/// <summary>Keeps what the camera SEES on the map, not only the point it looks at. The camera
	/// leans in from the south, so the bottom of the screen is ground a little south of the focus and
	/// the top is ground a long way north of it: stopping the focus at the map's edge let the whole
	/// lower half of the screen hang out over open water. So the view's own reach — near edge, far
	/// edge and half its width at the near edge — is measured off the camera's height and tilt, and
	/// the focus is held where all of it stays over the map. A view bigger than the map is centred.</summary>
	private void ClampFocus()
	{
		float pitch = Mathf.DegToRad(-CameraPitchDegrees);
		float half = Mathf.DegToRad(_camera?.Fov ?? CameraFov) / 2f;
		float height = Mathf.Sin(pitch) * _distance;
		float back = Mathf.Cos(pitch) * _distance;
		float south = back - (height / Mathf.Tan(pitch + half));
		float north = pitch - half > 0.01f ? (height / Mathf.Tan(pitch - half)) - back : MapDepth;
		Vector2 screen = IsInsideTree() ? GetViewport().GetVisibleRect().Size : new Vector2(16, 9);
		float side = height / Mathf.Sin(pitch + half) * Mathf.Tan(half) * (screen.X / Mathf.Max(1f, screen.Y));

		_focus.X = Fit(_focus.X, (-MapWidth / 2) + side, (MapWidth / 2) - side);

		// Taller than the map, the view is laid on its southern coast rather than centred: the near
		// ground fills the bottom half of the screen, so the sea past the south edge was most of what
		// the opening view showed, while what spills past the north edge is far off and small.
		float least = (-MapDepth / 2) + north;
		float most = (MapDepth / 2) - south;
		_focus.Z = least > most ? most : Mathf.Clamp(_focus.Z, least, most);
	}

	private static float Fit(float value, float least, float most) =>
		least > most ? (least + most) / 2f : Mathf.Clamp(value, least, most);

	private void UpdateCamera()
	{
		float pitch = Mathf.DegToRad(CameraPitchDegrees);
		_camera.Position = _focus + new Vector3(0, -Mathf.Sin(pitch) * _distance, Mathf.Cos(pitch) * _distance);
		_camera.RotationDegrees = new Vector3(CameraPitchDegrees, 0, 0);
		_sun.DirectionalShadowMaxDistance = _distance * ShadowReach;
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
