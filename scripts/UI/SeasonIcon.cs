using Godot;

/// <summary>Small snowflake-style season glyph, drawn natively since no season icon assets
/// exist yet. Six spokes, each with a pair of short barbs.</summary>
public partial class SeasonIcon : Control
{
	private const int SpokeCount = 6;
	private const float SpokeWidth = 1.6f;
	private const float BarbLengthRatio = 0.3f;
	private const float BarbPositionRatio = 0.6f;
	private const float BarbAngle = Mathf.Pi / 4f;
	private static readonly Color IconColor = new("d8b26b");

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Draw()
	{
		Vector2 center = Size / 2f;
		float radius = Mathf.Min(Size.X, Size.Y) / 2f;

		for (int i = 0; i < SpokeCount; i++)
		{
			float angle = Mathf.Tau * i / SpokeCount;
			Vector2 direction = Vector2.Right.Rotated(angle);
			Vector2 tip = center + direction * radius;
			DrawLine(center, tip, IconColor, SpokeWidth);

			Vector2 barbRoot = center + direction * (radius * BarbPositionRatio);
			float barbLength = radius * BarbLengthRatio;
			DrawLine(barbRoot, barbRoot + direction.Rotated(BarbAngle) * barbLength, IconColor, SpokeWidth);
			DrawLine(barbRoot, barbRoot + direction.Rotated(-BarbAngle) * barbLength, IconColor, SpokeWidth);
		}
	}
}
