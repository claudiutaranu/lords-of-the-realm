using Godot;

/// <summary>A county's people, a column a season: how healthy they were along the top, and under it
/// the births standing up from a line and the deaths hanging down from it, so a season that buried
/// more than it christened reads as one at a glance.
///
/// Drawn in the same boxes as <see cref="HappinessChart"/> and over the same tavern (<see cref="ChartArt"/>),
/// so the county's two charts read as one set.</summary>
public partial class PeopleChart : Control
{
	/// <summary>How much of the height the health line has; the births and deaths have the rest.</summary>
	private const float HealthShare = 0.36f;

	/// <summary>How much of a season's column its bars fill, and the room kept between the bands.</summary>
	private const float BarShare = 0.56f;
	private const float BandGap = 14f;

	/// <summary>How far a box leans back, as a share of its width — the happiness chart's own.</summary>
	private const float LeanShare = 0.3f;

	private static readonly Color BornColour = new(0.50f, 0.66f, 0.39f);
	private static readonly Color DiedColour = new(0.78f, 0.35f, 0.28f);
	private static readonly Color MiddlingColour = new(0.72f, 0.58f, 0.30f);
	private static readonly Color LineColour = new(0.93f, 0.83f, 0.58f);
	private static readonly Color RuleColour = new(1f, 1f, 1f, 0.18f);
	private static readonly Color Shadow = new(0f, 0f, 0f, 0.55f);

	private PeopleSeason[] _seasons = System.Array.Empty<PeopleSeason>();

	public void Draws(PeopleSeason[] seasons)
	{
		_seasons = seasons;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_seasons.Length == 0)
		{
			return;
		}

		float column = Size.X / _seasons.Length;
		float healthHigh = (Size.Y * HealthShare) - (BandGap / 2f);
		float barsTop = (Size.Y * HealthShare) + (BandGap / 2f);
		float barsMiddle = barsTop + ((Size.Y - barsTop) / 2f);
		float barsHalf = (Size.Y - barsTop) / 2f;

		// Health: fifty marked, and the line through each season's figure.
		DrawLine(new Vector2(0f, healthHigh / 2f), new Vector2(Size.X, healthHigh / 2f), RuleColour, 1f);
		var points = new Vector2[_seasons.Length];
		for (int i = 0; i < _seasons.Length; i++)
		{
			float health = Mathf.Clamp(_seasons[i].Health, 0, 100);
			points[i] = new Vector2((i + 0.5f) * column, healthHigh * (1f - (health / 100f)));
		}

		// A dark line under the bright one, and a dark ring under each dot: drawn over the tavern, a
		// thin gold line on its own was lost in the lamplight.
		if (points.Length > 1)
		{
			DrawPolyline(points, Shadow, 5f, true);
			DrawPolyline(points, LineColour, 2f, true);
		}

		for (int i = 0; i < points.Length; i++)
		{
			DrawCircle(points[i], 6f, Shadow);
			DrawCircle(points[i], 4.5f, Band(_seasons[i].Health));
		}

		// Births up, deaths down, both against the season that had the most of either: a scale of its
		// own per chart, so a county of two hundred and one of two thousand both fill the frame.
		DrawLine(new Vector2(0f, barsMiddle), new Vector2(Size.X, barsMiddle), RuleColour, 1f);
		int most = 1;
		foreach (PeopleSeason season in _seasons)
		{
			most = Mathf.Max(most, Mathf.Max(season.Born, season.Died));
		}

		// The deaths first and the births over them: a box hanging from the line has its lid on the
		// line, and the season's births stand on that lid rather than behind it.
		float wide = column * BarShare;
		float lean = wide * LeanShare;
		for (int i = 0; i < _seasons.Length; i++)
		{
			float died = (barsHalf - lean) * _seasons[i].Died / most;
			if (died >= 1f)
			{
				ChartArt.Box(this, new Rect2(Left(i, column, wide), barsMiddle, wide, died), lean, DiedColour);
			}
		}

		for (int i = 0; i < _seasons.Length; i++)
		{
			float born = (barsHalf - lean) * _seasons[i].Born / most;
			if (born >= 1f)
			{
				ChartArt.Box(this, new Rect2(Left(i, column, wide), barsMiddle - born, wide, born), lean, BornColour);
			}
		}
	}

	/// <summary>Where a season's box starts across the chart: in the middle of its column, pushed left by
	/// its own lean so the box as drawn, side and all, sits centred.</summary>
	private static float Left(int season, float column, float wide) =>
		(season * column) + ((column - wide - (wide * LeanShare)) / 2f);

	/// <summary>A season's dot in the colour of its health band, the simulation's own: sick or worse
	/// red, average amber, good or better green.</summary>
	private static Color Band(int health) => Livelihood.BandOf(health) switch
	{
		Livelihood.Band.Diseased or Livelihood.Band.Sick => DiedColour,
		Livelihood.Band.Average => MiddlingColour,
		_ => BornColour,
	};
}
