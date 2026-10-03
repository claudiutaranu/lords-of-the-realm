using Godot;

/// <summary>Applies the gilded look (vertical gold gradient,
/// assets/shaders/gold_gradient.gdshader) to a title Label. Reusable across any page's heading.
/// Weight comes from the theme's title font, which is a real variable font — synthesizing bold
/// here on top of that fattens the strokes until the letter counters close up.</summary>
public static class GoldTitle
{
	private const string ShaderPath = "res://assets/shaders/gold_gradient.gdshader";

	public static void Apply(Label label)
	{
		var material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
		label.Material = material;

		// Rect isn't finalized on the same frame; recompute now, next frame, and on any
		// later resize (container layout, window resize) so the bounds can't go stale.
		UpdateBounds(label, material);
		Callable.From(() => UpdateBounds(label, material)).CallDeferred();
		label.Resized += () => UpdateBounds(label, material);
	}

	private static void UpdateBounds(Label label, ShaderMaterial material)
	{
		Rect2 rect = label.GetGlobalRect();
		material.SetShaderParameter("top_y", rect.Position.Y);
		material.SetShaderParameter("px_height", rect.Size.Y);
	}
}
