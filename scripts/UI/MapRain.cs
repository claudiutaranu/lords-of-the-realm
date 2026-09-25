using Godot;

/// <summary>Rain over a flooded county for the season the river rose in: from the turn the flood
/// comes until the next one. It is weather for the eye only — the flood has already been rolled
/// and paid for by the time anybody sees a drop, as in Lords of the Realm, where the river rose
/// without warning.
///
/// The storm's dark cloud hangs low, just over the rain, and not up with the drifting sky: from the
/// map's camera anything as high as the other clouds sits over the next county north of the rain it
/// is supposed to be dropping.</summary>
public partial class MapRain : Node3D
{
	private const string CloudShaderPath = "res://assets/shaders/cloud.gdshader";
	// The cloud shader leaves most of a sheet see-through, which is right for fair weather and
	// wrong for a storm: pushed past one, the opacity fills the sheet in to a solid body with soft
	// edges. It is clamped to one in the shader, so this cannot overdraw.
	private const float CloudOpacity = 1.7f;
	private const float CloudAboveRain = 1.5f;
	private static readonly Color CloudTint = new("5f6570");

	// About the width of a county's fields and village, in world units.
	private const float StormRadius = 9f;
	private const float FallSpeed = 30f;
	// How far the wind pushes the rain off the vertical: a slant, not a gale.
	private const float WindSlant = 0.25f;
	private const int Drops = 450;
	// How far above the ground a drop is first drawn. Rain drawn from the clouds down is, from
	// above, a column standing half across the screen; drawn over the last stretch it is weather
	// over a village.
	private const float RainHeight = 12f;
	// A drop is drawn about a pixel and a half wide whatever the zoom: the camera's distance times
	// this is that width in world units. A drop of fixed world width is a hair from far off and a
	// fence post up close, and neither reads as rain.
	private const float DropWidthPerDistance = 0.0012f;
	private const float DropLength = 0.5f;

	private static readonly Color DropColour = new(0.82f, 0.86f, 0.92f, 0.4f);

	// One drop shape shared by every storm, so a change of zoom re-sizes the rain everywhere at once.
	private readonly QuadMesh _drop = new()
	{
		Material = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			AlbedoColor = DropColour,
			// Streaks stay upright and turn to face the camera, which is what rain looks like.
			BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
			BillboardKeepScale = true,
		},
	};

	/// <summary>Told by the camera how far off it stands, so the drops keep their width on screen.</summary>
	public void SetZoom(float distance) =>
		_drop.Size = new Vector2(DropWidthPerDistance * distance, DropLength);

	/// <summary>Rains on <paramref name="ground"/> until <see cref="StopAll"/>. Started behind the turn
	/// curtain, so it is already falling at full strength when the map is seen again.</summary>
	public void Shower(Vector3 ground)
	{
		// A drop lives as long as it takes to reach the ground, and a little over: the county is not
		// flat, and rain that stops short of a valley hangs in the air. What falls on past a
		// hillside is hidden by the hill.
		Vector2 wind = MapClouds.PrevailingWind * WindSlant;
		// Upwind of the county by as far as the wind carries a drop on its way down, so the rain
		// lands on the village it is about rather than on the next county over.
		var storm = new Node3D
		{
			Position = ground + new Vector3(-wind.X * RainHeight, RainHeight, -wind.Y * RainHeight),
		};
		AddChild(storm);

		var cloud = new ShaderMaterial { Shader = GD.Load<Shader>(CloudShaderPath) };
		cloud.SetShaderParameter("tint", CloudTint);
		cloud.SetShaderParameter("opacity", CloudOpacity);
		cloud.SetShaderParameter("seed", GD.Randf() * 100f);
		storm.AddChild(new MeshInstance3D
		{
			Mesh = new PlaneMesh { Size = Vector2.One * StormRadius * 5f, Material = cloud },
			Position = Vector3.Up * CloudAboveRain,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		storm.AddChild(new GpuParticles3D
		{
			Amount = Drops,
			Lifetime = RainHeight * 1.15f / FallSpeed,
			VisibilityAabb = new Aabb(new Vector3(-StormRadius * 2f, -RainHeight - 2f, -StormRadius * 2f),
				new Vector3(StormRadius * 4f, RainHeight + 4f, StormRadius * 4f)),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			ProcessMaterial = new ParticleProcessMaterial
			{
				// A flat disc: every drop starts at the same height, so every drop has the same way
				// to fall.
				EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring,
				EmissionRingAxis = Vector3.Up,
				EmissionRingRadius = StormRadius,
				EmissionRingInnerRadius = 0f,
				EmissionRingHeight = 0.5f,
				Direction = new Vector3(wind.X, -1f, wind.Y),
				Spread = 1f,
				InitialVelocityMin = FallSpeed,
				InitialVelocityMax = FallSpeed,
				Gravity = Vector3.Zero,
			},
			DrawPass1 = _drop,
		});
	}

	/// <summary>The season is over and so is its weather. Called behind the turn curtain, so the
	/// rain is simply gone rather than fading.</summary>
	public void StopAll()
	{
		foreach (Node storm in GetChildren())
		{
			storm.QueueFree();
		}
	}
}
