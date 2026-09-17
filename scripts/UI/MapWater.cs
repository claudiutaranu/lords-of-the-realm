using Godot;

/// <summary>The sea the campaign map sits in. Its own node and its own shader
/// (assets/shaders/water.gdshader), so the surface can be worked on without touching the terrain:
/// the shader gets the same height map the terrain is displaced by, which is what lets it know
/// where the coast is.</summary>
public partial class MapWater : Node3D
{
	private const string WaterShaderPath = "res://assets/shaders/water.gdshader";

	private ShaderMaterial _material;

	public void Build(Image heightImage, Vector2 mapSize, float heightScale, float seaLevel)
	{
		_material = new ShaderMaterial { Shader = GD.Load<Shader>(WaterShaderPath) };
		_material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(heightImage));
		_material.SetShaderParameter("height_scale", heightScale);
		_material.SetShaderParameter("sea_level", seaLevel);
		_material.SetShaderParameter("map_size", mapSize);

		AddChild(new MeshInstance3D
		{
			// Far wider than the land: at 3.5x the plane's own edge came into frame and the island
			// looked cut out of paper. Subdivided because the swell moves vertices.
			Mesh = new PlaneMesh
			{
				Size = mapSize * 14f,
				SubdivideWidth = 220,
				SubdivideDepth = 160,
				Material = _material,
			},
			Position = new Vector3(0, seaLevel, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}

	/// <summary>Only winter shows on the sea, and the shader decides what that looks like — this
	/// just hands the season over.</summary>
	public void SetSeason(Season season) => _material.SetShaderParameter("season", (float)(int)season);
}
