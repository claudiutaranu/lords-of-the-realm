using Godot;

/// <summary>People counted the way Lords of the Realm counts them on its county screen: in little
/// figures, each one a twenty-fifth of the county. A village of two hundred and a town of two
/// thousand both come out at about twenty-five figures, so the screen reads the same at any size,
/// and moving a figure moves a twenty-fifth of the county whatever that is in heads.
///
/// A gold figure is somebody at work; a hollow ring is a place the work still has for somebody.</summary>
public static class WorkerFigures
{
	/// <summary>How many figures a whole county comes to.</summary>
	private const int PerCounty = 25;

	private static readonly Color Ring = new(0.72f, 0.58f, 0.30f, 0.9f);
	private static readonly Color Hollow = new(0.05f, 0.05f, 0.08f, 0.85f);
	private static readonly Color Filled = new(0.13f, 0.11f, 0.08f, 0.95f);

	/// <summary>How many people one figure stands for: never less than one.</summary>
	public static int Size(int population) => Mathf.Max(1, Mathf.CeilToInt(population / (float)PerCounty));

	/// <summary>How many figures a number of people fills, a part-filled one counting whole: ten men
	/// on a site where a figure is twenty are still somebody on it.</summary>
	public static int Of(int people, int population) =>
		people <= 0 ? 0 : Mathf.CeilToInt(people / (float)Size(population));

	/// <summary>One figure in its ring.</summary>
	public static Control Token(bool isFilled, int side)
	{
		var ring = new Panel
		{
			CustomMinimumSize = new Vector2(side, side),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		int round = side / 2;
		ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = isFilled ? Filled : Hollow,
			BorderColor = Ring,
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = round,
			CornerRadiusTopRight = round,
			CornerRadiusBottomLeft = round,
			CornerRadiusBottomRight = round,
			AntiAliasing = true,
		});

		if (isFilled)
		{
			TextureRect bust = Chrome.Icon("worker", side - 8);
			ring.AddChild(bust);
			bust.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			bust.OffsetLeft = bust.OffsetTop = 4;
			bust.OffsetRight = bust.OffsetBottom = -4;
		}

		return ring;
	}

	/// <summary>A run of figures: <paramref name="on"/> gold, then hollow up to
	/// <paramref name="wanted"/>. Wraps where the space it is given runs out.</summary>
	public static Control Row(int on, int wanted, int side)
	{
		var row = new HFlowContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
		row.AddThemeConstantOverride("h_separation", 4);
		row.AddThemeConstantOverride("v_separation", 4);
		for (int figure = 0; figure < Mathf.Max(on, wanted); figure++)
		{
			row.AddChild(Token(figure < on, side));
		}

		return row;
	}
}
