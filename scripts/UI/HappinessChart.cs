using Godot;

/// <summary>The county's goodwill, a box a year, drawn rather than assembled out of rectangles.
///
/// Drawn because a bar with a top and a side to it is three shapes, two of them slanted, and Godot
/// has no rectangle that leans. Thirty lines of <see cref="_Draw"/> against three nodes a year, a
/// container to hold them in and a set of anchors to keep them standing on the floor — and the
/// slanted faces would still be missing.
///
/// The colour is the reading. A lord looking at ten years of his reign should be able to find the
/// bad ones without going along the row with his finger, so a year spent under the unrest line is
/// drawn red and a year the county was glad of him is drawn green.</summary>
public partial class HappinessChart : Control
{
	/// <summary>How far the box leans back, as a share of a bar's width, and the least and most a
	/// bar may be drawn at. A reign of one year should not be a single sliver in the middle of the
	/// panel, and a reign of twenty should not be twenty threads.</summary>
	private const float LeanShare = 0.3f;
	private const int NarrowestBar = 14;
	private const int WidestBar = 34;
	private const int Gap = 6;

	private float[] _years = System.Array.Empty<float>();
	private GameBalance _balance;

	public void Draws(float[] years, GameBalance balance)
	{
		_years = years;
		_balance = balance;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_years.Length == 0 || _balance == null)
		{
			return;
		}

		Vector2 room = Size;
		float width = Mathf.Clamp((room.X - Gap * (_years.Length - 1)) / _years.Length,
			NarrowestBar, WidestBar);
		float lean = width * LeanShare;

		// The boxes stand on the bottom edge itself — the room they are drawn in is the picture, and
		// a chart floating above the floor of its own frame reads as a mistake. The lean is taken off
		// the TOP only, which is where it is actually drawn: a county at a hundred still has to fit
		// its own lid inside the frame, but nothing of a box is ever drawn below its face.
		float floor = room.Y;
		float tallest = room.Y - lean;

		for (int year = 0; year < _years.Length; year++)
		{
			float hearts = Mathf.Clamp(_years[year], 0f, 100f);
			float high = tallest * hearts / 100f;
			if (high < 1f)
			{
				continue; // a county at nothing has nothing to draw, and a hairline would be a lie
			}

			float left = year * (width + Gap);
			Box(new Rect2(left, floor - high, width, high), lean, Colour(hearts));
		}
	}

	/// <summary>One box: the face the lord is looking at, the lid, and the side it throws away from
	/// the light. The two slanted faces are what make it a box rather than a bar.</summary>
	private void Box(Rect2 face, float lean, Color colour)
	{
		DrawRect(face, colour);

		Vector2 back = new(lean, -lean);
		DrawColoredPolygon(new[]
		{
			face.Position,
			face.Position + back,
			face.Position + back + new Vector2(face.Size.X, 0f),
			face.Position + new Vector2(face.Size.X, 0f),
		}, colour.Lightened(0.28f));

		DrawColoredPolygon(new[]
		{
			face.Position + new Vector2(face.Size.X, 0f),
			face.Position + new Vector2(face.Size.X, 0f) + back,
			face.Position + face.Size + back,
			face.Position + face.Size,
		}, colour.Darkened(0.35f));
	}

	private Color Colour(float hearts) =>
		hearts < _balance.UnrestBelow ? new Color(0.78f, 0.35f, 0.28f)
		: hearts >= _balance.ImmigrationAbove ? new Color(0.50f, 0.66f, 0.39f)
		: new Color(0.72f, 0.58f, 0.30f);
}
