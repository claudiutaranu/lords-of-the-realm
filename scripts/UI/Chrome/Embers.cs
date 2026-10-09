using Godot;

/// <summary>Embers rising behind a card: two layers, a slow far haze and a quick near spark, laid
/// over the card and a little past it, behind it, so its gilt stays on top and the fire reads as
/// the hall's, not the card's. The campaign cards light theirs under the pointer; the lords' cards
/// on the chosen one, in his colour.</summary>
public partial class Embers : Node2D
{
	private const float FadeSeconds = 0.1f;
	private const float NearAlpha = 0.85f;
	private const float FarAlpha = 0.55f;

	/// <summary>How far past the frame the fire is laid: a card's face is opaque, so behind it only
	/// what rises round its rim and over its top is ever seen.</summary>
	private static readonly Vector2 Overhang = new(36f, 10f);

	private CpuParticles2D _near;
	private CpuParticles2D _far;

	private float _width = 1f;

	/// <summary>Laid behind <paramref name="host"/>, unlit until told to glow; <paramref name="width"/>
	/// is how much of the card's breadth the fire is gathered into.</summary>
	public static Embers Behind(Control host, Color colour, float width = 1f)
	{
		var embers = new Embers { ShowBehindParent = true, Modulate = new Color(1, 1, 1, 0), _width = width };
		embers._far = Layer(170, 2.0f, 0.6f, 28f, 12f, -25f, 35f, 80f, 0.9f, 1.7f, new Color(colour, FarAlpha));
		embers._near = Layer(230, 1.1f, 0.5f, 18f, 8f, -40f, 90f, 190f, 0.5f, 1.1f, new Color(colour, NearAlpha));
		embers.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
		embers.AddChild(embers._far);
		embers.AddChild(embers._near);
		host.AddChild(embers);
		host.Resized += () => embers.Spread(host.Size);
		embers.Spread(host.Size);
		return embers;
	}

	public void Glow(bool isOn)
	{
		_near.Emitting = isOn;
		_far.Emitting = isOn;
		CreateTween().TweenProperty(this, "modulate:a", isOn ? 1.0 : 0.0, FadeSeconds);
	}

	/// <summary>Over the whole card and a little past it, whatever size it is given.</summary>
	private void Spread(Vector2 size)
	{
		Position = size / 2f;
		Vector2 reach = (size / 2f) + Overhang;
		reach.X *= _width;
		_near.EmissionRectExtents = reach;
		_far.EmissionRectExtents = reach + new Vector2(5f, 0f);
	}

	private static CpuParticles2D Layer(int amount, float lifetime, float randomness, float spread, float tilt,
		float rise, float slowest, float fastest, float smallest, float largest, Color colour)
	{
		var spark = new Gradient();
		spark.SetColor(0, Colors.White);
		spark.SetColor(1, new Color(1, 1, 1, 0));
		var fade = new Gradient { Offsets = new[] { 0f, 0.6f, 1f }, Colors = new[] { Colors.White, new Color(1, 1, 1, 0.85f), new Color(1, 1, 1, 0) } };
		return new CpuParticles2D
		{
			Emitting = false,
			Amount = amount,
			Lifetime = lifetime,
			Randomness = randomness,
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
			Direction = Vector2.Up,
			Spread = spread,
			AngleMin = -tilt,
			AngleMax = tilt,
			Gravity = new Vector2(0, rise),
			InitialVelocityMin = slowest,
			InitialVelocityMax = fastest,
			ScaleAmountMin = smallest,
			ScaleAmountMax = largest,
			HueVariationMin = -0.03f,
			HueVariationMax = 0.05f,
			Color = colour,
			ColorRamp = fade,
			UseParentMaterial = true,
			Texture = new GradientTexture2D
			{
				Gradient = spark,
				Width = 10,
				Height = 26,
				Fill = GradientTexture2D.FillEnum.Radial,
				FillFrom = new Vector2(0.5f, 0.5f),
				FillTo = new Vector2(0.5f, 1f),
			},
		};
	}
}
