using Godot;

/// <summary>What the county's charts are drawn with, so the goodwill and the people read as one
/// set: a bar is a box with a lid and a side to it, and the chart stands on a picture of the people
/// it counts — the tavern — cropped to its frame and darkened under the bars.</summary>
public static class ChartArt
{
	private const string TavernArtPath = "res://assets/ui/happiness-tavern.png";

	/// <summary>One box: the face the lord is looking at, the lid, and the side it throws away from
	/// the light. The two slanted faces are what make it a box rather than a bar.</summary>
	public static void Box(CanvasItem canvas, Rect2 face, float lean, Color colour)
	{
		canvas.DrawRect(face, colour);

		Vector2 back = new(lean, -lean);
		canvas.DrawColoredPolygon(new[]
		{
			face.Position,
			face.Position + back,
			face.Position + back + new Vector2(face.Size.X, 0f),
			face.Position + new Vector2(face.Size.X, 0f),
		}, colour.Lightened(0.28f));

		canvas.DrawColoredPolygon(new[]
		{
			face.Position + new Vector2(face.Size.X, 0f),
			face.Position + new Vector2(face.Size.X, 0f) + back,
			face.Position + face.Size + back,
			face.Position + face.Size,
		}, colour.Darkened(0.35f));
	}

	/// <summary>The tavern behind a chart: the picture, darkened, in a dark frame of <paramref name="height"/>
	/// with the chart laid over it inside <paramref name="margins"/> (left, top, right, bottom).
	///
	/// The frame holds its contents off its own edge by the width of its border unless it is told not
	/// to, and that gap is what left the happiness years hovering above the floor of the picture they
	/// are drawn on. The border still draws; it simply stops pushing.</summary>
	public static PanelContainer Scene(Control chart, int height, (int Left, int Top, int Right, int Bottom) margins)
	{
		var scene = new PanelContainer { CustomMinimumSize = new Vector2(0, height), ClipContents = true };
		StyleBoxFlat frame = Chrome.CardStyle(new Color(0.05f, 0.045f, 0.04f, 1f));
		frame.ContentMarginLeft = frame.ContentMarginRight = 0;
		frame.ContentMarginTop = frame.ContentMarginBottom = 0;
		scene.AddThemeStyleboxOverride("panel", frame);

		scene.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(TavernArtPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		// Darkened under the bars: a chart drawn over a lit room is a chart nobody can read.
		scene.AddChild(new ColorRect
		{
			Color = new Color(0.04f, 0.035f, 0.03f, 0.45f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		var over = new MarginContainer();
		over.AddThemeConstantOverride("margin_left", margins.Left);
		over.AddThemeConstantOverride("margin_top", margins.Top);
		over.AddThemeConstantOverride("margin_right", margins.Right);
		over.AddThemeConstantOverride("margin_bottom", margins.Bottom);
		scene.AddChild(over);
		chart.MouseFilter = Control.MouseFilterEnum.Ignore;
		over.AddChild(chart);
		return scene;
	}
}
