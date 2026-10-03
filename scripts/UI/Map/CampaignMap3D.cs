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
	//
	// Read off the campaign's own height map, so a campaign drawn on a larger canvas is larger ground
	// (England's is twice the first map's either way) and not the same island squeezed smaller.
	private float MapWidth => Terrain3DGround.WorldWidth(_heightImage.GetWidth());
	private float MapDepth => Terrain3DGround.WorldWidth(_heightImage.GetHeight());
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

	private Camera3D _camera;
	private Terrain3DGround _ground;
	private Image _heightImage;
	private Image _idImage;
	private MapDecoration _decoration;
	private MapClouds _clouds;
	private MapRain _rain;
	private MapWater _water;
	private DirectionalLight3D _sun;
	private ProceduralSkyMaterial _sky;
	private Godot.Environment _environment;
	private Vector3 _focus = Vector3.Zero;
	private float _distance = 132.0f;
	private int _lookingAt = -1;

	/// <summary>Raised when the county in the middle of the screen changes under the camera: the lord
	/// is looking at whatever he has panned to, without pointing at it. -1 is the sea.</summary>
	public event System.Action<int> LookedAt;

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

		_grass = new MapGrass();
		AddChild(_grass);
		_grass.Sow(this, GD.Load<Image>(Campaign.Asset("map-props.png")),
			new Vector2(_heightImage.GetWidth(), _heightImage.GetHeight()), GrassSeed);

		_clouds = new MapClouds();
		AddChild(_clouds);
		// HeightScale is what a fully white height pixel stands for, so it is the tallest ground
		// this map can have — the sky is placed against that.
		_clouds.Build(new Vector2(MapWidth, MapDepth), HeightScale);
		_rain = new MapRain();
		AddChild(_rain);

		_camera = new Camera3D { Fov = CameraFov, Far = 800.0f, Current = true };
		AddChild(_camera);
		// The clipmap is laid out around the camera, so the ground has to be told which one it
		// follows — without it Terrain3D stops processing and never draws.
		_ground?.Watch(_camera);
		ClampFocus();
		UpdateCamera();
	}

	/// <summary>Raised whenever the chosen province changes. The minimap listens to this rather than
	/// being wired up by the page, so it keeps working however that page is rearranged.</summary>
	public static event System.Action<int> ProvinceHighlighted;

	/// <summary>Raised with the holders strip whenever a county may have changed hands, for the
	/// minimap, which washes its counties from the same strip the ground does.</summary>
	public static event System.Action<Image> HoldersChanged;

	private ImageTexture _holders;

	/// <summary>Who holds every province now, one texel each in province order: the holder's colour,
	/// and his realm's number + 1 in alpha. The ground draws the frontiers between them.</summary>
	public void ShowHolders(Image strip)
	{
		// One texture for the whole reign, written over in place: the strip is a texel a province
		// and the provinces do not change in number, only in hands.
		if (_holders == null || _holders.GetSize() != (Vector2)strip.GetSize())
		{
			_holders = ImageTexture.CreateFromImage(strip);
		}
		else
		{
			_holders.Update(strip);
		}

		_ground?.SetShaderParameter("campaign_owners", _holders);
		HoldersChanged?.Invoke(strip);
	}

	public void SetHighlight(int selectedIndex, int hoveredIndex)
	{
		ProvinceHighlighted?.Invoke(selectedIndex);

		// The shader compares against the ID map's raw red channel, which is index + 1 so that
		// 0 can mean water.
		_ground?.SetShaderParameter("selected_index", selectedIndex + 1);
		_ground?.SetShaderParameter("hovered_index", hoveredIndex + 1);
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
}
