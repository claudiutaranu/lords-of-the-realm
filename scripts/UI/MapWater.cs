using Godot;

/// <summary>The sea the campaign map sits in. Its own node and its own shader
/// (assets/shaders/water.gdshader), so the surface can be worked on without touching the terrain:
/// the shader gets the same height map the terrain is displaced by, which is what lets it know
/// where the coast is.</summary>
public partial class MapWater : Node3D
{
	private const string WaterShaderPath = "res://assets/shaders/water.gdshader";

	private ShaderMaterial _material;

	public void Build(Image heightImage, Texture2D coast, Vector2 mapSize, float heightScale, float seaLevel)
	{
		_material = new ShaderMaterial { Shader = GD.Load<Shader>(WaterShaderPath) };
		_material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(heightImage));
		_material.SetShaderParameter("height_scale", heightScale);
		_material.SetShaderParameter("sea_level", seaLevel);
		_material.SetShaderParameter("map_size", mapSize);
		_material.SetShaderParameter("coast_map", coast);

		// The surface detail, made here rather than shipped: two layers of waves at different sizes and
		// a noise field for the surf. Seamless so the sea has no seam every few hundred units, and
		// mipmapped so it does not shimmer out towards the horizon.
		_material.SetShaderParameter("wave_normal_a", Waves(seed: 11, frequency: 0.012f, bump: 7.0f));
		_material.SetShaderParameter("wave_normal_b", Waves(seed: 29, frequency: 0.024f, bump: 5.0f));
		_material.SetShaderParameter("foam_noise", Noise(seed: 47, frequency: 0.02f));

		AddChild(new MeshInstance3D
		{
			// Far wider than the land: at 3.5x the plane's own edge came into frame and the island
			// looked cut out of paper. Subdivided because the swell moves vertices.
			Mesh = new PlaneMesh
			{
				Size = mapSize * 14f,
				SubdivideWidth = 110,
				SubdivideDepth = 80,
				Material = _material,
			},
			Position = new Vector3(0, seaLevel, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	/// <summary>A tileable wave normal map. Fractal noise read as a bump map: a few octaves give long
	/// swells with chop riding on them, which one octave of anything never looks like.</summary>
	private static NoiseTexture2D Waves(int seed, float frequency, float bump) => new()
	{
		Width = 512,
		Height = 512,
		Seamless = true,
		AsNormalMap = true,
		BumpStrength = bump,
		GenerateMipmaps = true,
		Noise = new FastNoiseLite
		{
			Seed = seed,
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
			Frequency = frequency,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 4,
		},
	};

	/// <summary>Plain tileable noise, for breaking up the surf and the shelf colour.</summary>
	private static NoiseTexture2D Noise(int seed, float frequency) => new()
	{
		Width = 512,
		Height = 512,
		Seamless = true,
		GenerateMipmaps = true,
		Noise = new FastNoiseLite
		{
			Seed = seed,
			NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
			Frequency = frequency,
			FractalType = FastNoiseLite.FractalTypeEnum.Fbm,
			FractalOctaves = 3,
		},
	};

	/// <summary>Only winter shows on the sea, and the shader decides what that looks like — this
	/// just hands the season over.</summary>
	public void SetSeason(Season season) => _material.SetShaderParameter("season", (float)(int)season);
}
