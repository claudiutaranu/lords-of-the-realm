using Godot;

/// <summary>What is standing on a piece of ground: a ring of gold round the men's feet and a shield
/// under them with the count on it.
///
/// The count is the point. A banner says an army is there; a lord deciding whether to walk into it
/// needs to know whether it is forty men or four hundred, and having to select the county to find
/// out turns a glance into an errand. Every army on the map carries its own number, a rival's
/// included — what a lord can see from a hilltop is how many they are, and that is the one thing
/// about another man's army that is honestly visible.
///
/// Drawn over the map rather than modelled on it, for the same reason the province pins are: it has
/// to stay the same size whatever the camera does, or the number stops being readable exactly when
/// the lord pulls back to look at his whole realm.</summary>
public partial class ArmyBadges : Control
{
	private const float RingRadius = 21f;
	private const float RingSquash = 0.42f;	  // a circle on the ground, seen from the map's angle
	private const float ShieldWidth = 30f;
	private const float ShieldHeight = 32f;
	private const float ShieldDrop = 14f;	  // below the ring, where a man's feet are

	private static readonly Color Ring = new("e0b95f");
	private static readonly Color RingShade = new(0f, 0f, 0f, 0.35f);
	private static readonly Color Face = new("1b2436");
	private static readonly Color Ink = new("f3dfa8");

	private (Vector2 At, int Men, Color Colour)[] _armies = System.Array.Empty<(Vector2, int, Color)>();
	private Font _font;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		_font = ThemeDB.FallbackFont;
	}

	/// <summary>Where the armies are on screen and how many men each one has. An empty list takes
	/// them all down.</summary>
	public void Show((Vector2 At, int Men, Color Colour)[] armies)
	{
		_armies = armies;
		Visible = armies.Length > 0;
		QueueRedraw();
	}

	public override void _Draw()
	{
		foreach ((Vector2 at, int men, Color colour) in _armies)
		{
			// The ring is drawn flattened, so it lies on the ground rather than standing up through
			// the man like a hoop.
			DrawSetTransform(at, 0f, new Vector2(1f, RingSquash));
			DrawArc(Vector2.Zero + new Vector2(0f, 4f), RingRadius, 0, Mathf.Tau, 40, RingShade, 5f);
			DrawArc(Vector2.Zero, RingRadius, 0, Mathf.Tau, 40, Ring, 3f);
			DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

			Vector2 hangs = at + new Vector2(0f, (RingRadius * RingSquash) + ShieldDrop);
			Shield(hangs, colour);

			string count = men.ToString("N0");
			Vector2 box = _font.GetStringSize(count, HorizontalAlignment.Center, -1f, 15);
			DrawString(_font, hangs + new Vector2(-box.X / 2f, box.Y * 0.30f), count,
				HorizontalAlignment.Center, -1f, 15, Ink);
		}
	}

	/// <summary>A shield hanging under the ring: square at the shoulders, pointed at the foot. In the
	/// realm's own colour, so whose men they are is the first thing read and the number the
	/// second.</summary>
	private void Shield(Vector2 at, Color colour)
	{
		float half = ShieldWidth * 0.5f;
		float top = at.Y - (ShieldHeight * 0.5f);
		float shoulder = top + (ShieldHeight * 0.55f);
		float foot = top + ShieldHeight;

		var outline = new[]
		{
			new Vector2(at.X - half, top),
			new Vector2(at.X + half, top),
			new Vector2(at.X + half, shoulder),
			new Vector2(at.X, foot),
			new Vector2(at.X - half, shoulder),
		};

		DrawColoredPolygon(outline, Face);
		DrawColoredPolygon(Inset(outline, at, 0.74f), colour);
		for (int point = 0; point < outline.Length; point++)
		{
			DrawLine(outline[point], outline[(point + 1) % outline.Length], Ring, 2f);
		}
	}

	private static Vector2[] Inset(Vector2[] outline, Vector2 middle, float share)
	{
		var inner = new Vector2[outline.Length];
		for (int point = 0; point < outline.Length; point++)
		{
			inner[point] = middle + ((outline[point] - middle) * share);
		}

		return inner;
	}
}
