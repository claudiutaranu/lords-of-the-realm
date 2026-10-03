using Godot;

/// <summary>An arrow in flight as the eye catches it: not a shaft but a long pale streak, bright at its
/// head and gone to nothing at its tail, the way a flight of arrows reads across a field. Two thin
/// strips crossed along its length, so it is the same from above and from the side; the head is
/// where the arrow is, and the streak trails behind it (the model's +Z, since it is turned to look
/// down its flight along −Z).</summary>
public static class BattlefieldStreak
{
	/// <summary>How long the streak is behind its head and how wide, in metres.</summary>
	private const float Long = 1.6f;
	private const float Wide = 0.05f;

	private static readonly Color Head = new(0.95f, 0.93f, 0.86f, 0.6f);

	public static Mesh Mesh()
	{
		Color tail = new(Head, 0f);
		var tool = new SurfaceTool();
		tool.Begin(Godot.Mesh.PrimitiveType.Triangles);
		foreach (Vector3 across in new[] { Vector3.Right * (Wide / 2f), Vector3.Up * (Wide / 2f) })
		{
			Vector3 back = Vector3.Back * Long;
			(Vector3 At, Color Shade)[] corners =
			{
				(-across, Head), (across, Head), (across + back, tail),
				(-across, Head), (across + back, tail), (-across + back, tail),
			};
			foreach ((Vector3 at, Color shade) in corners)
			{
				tool.SetColor(shade);
				tool.AddVertex(at);
			}
		}

		return tool.Commit();
	}

	/// <summary>Lit by nothing: a streak is the light catching the arrow, not the arrow in the light.</summary>
	public static Material Look() => new StandardMaterial3D
	{
		VertexColorUseAsAlbedo = true,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};
}
