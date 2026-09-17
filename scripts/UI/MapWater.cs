using Godot;

/// <summary>The sea the campaign map sits in. Its own node and its own shader
/// (assets/shaders/water.gdshader), so the surface can be worked on without touching the terrain:
/// the shader gets the same height map the terrain is displaced by, which is what lets it know
/// where the coast is.</summary>
public partial class MapWater : Node3D
{
	private const string WaterShaderPath = "res://assets/shaders/water.gdshader";

	public void Build(Image heightImage, Vector2 mapSize, float heightScale, float seaLevel)
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>(WaterShaderPath) };
		material.SetShaderParameter("height_map", ImageTexture.CreateFromImage(heightImage));
		material.SetShaderParameter("height_scale", heightScale);
		material.SetShaderParameter("sea_level", seaLevel);
		material.SetShaderParameter("map_size", mapSize);

		AddChild(new MeshInstance3D
		{
			// Far wider than the land: at 3.5x the plane's own edge came into frame and the island
			// looked cut out of paper. Subdivided because the swell moves vertices.
			Mesh = new PlaneMesh
			{
				Size = mapSize * 14f,
				SubdivideWidth = 220,
				SubdivideDepth = 160,
				Material = material,
			},
			Position = new Vector3(0, seaLevel, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});
	}
}
