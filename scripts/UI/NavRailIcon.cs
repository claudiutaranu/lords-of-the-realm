using Godot;

/// <summary>Glyph for one navigation rail button, drawn natively since none of these
/// destinations ship an icon asset. Every shape is laid out in a 0..1 square that is
/// centred and scaled into <see cref="Control.Size"/>, so one control serves any size.</summary>
public partial class NavRailIcon : Control
{
	public enum Glyph
	{
		Book,
		Helmet,
		Tower,
		People,
		Swords,
		Crown,
		Gear,
		Heart,
	}

	private static readonly Color IconColor = new("d8b26b");

	/// <summary>Stroke width as a fraction of the glyph square: thick enough to stay
	/// visible at rail size, thin enough that outlines keep their inner space.</summary>
	private const float StrokeRatio = 0.09f;

	private Glyph _kind;
	private float _side;
	private Vector2 _origin;

	[Export]
	public Glyph Kind
	{
		get => _kind;
		set
		{
			_kind = value;
			QueueRedraw();
		}
	}

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public override void _Draw()
	{
		_side = Mathf.Min(Size.X, Size.Y);
		_origin = (Size - new Vector2(_side, _side)) * 0.5f;

		switch (_kind)
		{
			case Glyph.Book:
				DrawBook();
				break;
			case Glyph.Helmet:
				DrawHelmet();
				break;
			case Glyph.Tower:
				DrawTower();
				break;
			case Glyph.People:
				DrawPeople();
				break;
			case Glyph.Swords:
				DrawSwords();
				break;
			case Glyph.Crown:
				DrawCrown();
				break;
			case Glyph.Gear:
				DrawGear();
				break;
			case Glyph.Heart:
				DrawHeart();
				break;
		}
	}

	private void DrawBook()
	{
		DrawPage(0.10f);
		DrawPage(0.90f);
	}

	/// <summary>One leaf of the open book. Both leaves share the centre edge, so the
	/// doubled stroke there reads as the spine.</summary>
	private void DrawPage(float outerX)
	{
		DrawPolyline(
			new[]
			{
				At(0.5f, 0.28f),
				At(outerX, 0.20f),
				At(outerX, 0.72f),
				At(0.5f, 0.80f),
				At(0.5f, 0.28f),
			},
			IconColor,
			Stroke);
	}

	private void DrawHelmet()
	{
		const int DomeSegments = 16;
		const float DomeRadius = 0.32f;
		const float BrowY = 0.52f;

		var dome = new Vector2[DomeSegments + 1];
		for (int i = 0; i <= DomeSegments; i++)
		{
			float angle = Mathf.Pi + Mathf.Pi * i / DomeSegments;
			dome[i] = At(0.5f + Mathf.Cos(angle) * DomeRadius, BrowY + Mathf.Sin(angle) * DomeRadius);
		}
		DrawColoredPolygon(dome, IconColor);

		DrawColoredPolygon(
			new[]
			{
				At(0.24f, 0.62f),
				At(0.76f, 0.62f),
				At(0.66f, 0.88f),
				At(0.34f, 0.88f),
			},
			IconColor);

		DrawRect(Box(0.45f, 0.50f, 0.10f, 0.16f), IconColor);
	}

	private void DrawTower()
	{
		const float MerlonTop = 0.22f;
		const float MerlonHeight = 0.16f;
		const float MerlonWidth = 0.14f;

		DrawRect(Box(0.26f, 0.34f, 0.48f, 0.58f), IconColor, false, Stroke);
		DrawRect(Box(0.20f, MerlonTop, MerlonWidth, MerlonHeight), IconColor);
		DrawRect(Box(0.43f, MerlonTop, MerlonWidth, MerlonHeight), IconColor);
		DrawRect(Box(0.66f, MerlonTop, MerlonWidth, MerlonHeight), IconColor);
		DrawRect(Box(0.44f, 0.70f, 0.12f, 0.22f), IconColor);
	}

	private void DrawPeople()
	{
		DrawFigure(0.34f);
		DrawFigure(0.66f);
	}

	private void DrawFigure(float centerX)
	{
		const float HeadRadius = 0.13f;
		const float HeadY = 0.30f;
		const float ShoulderHalfWidth = 0.09f;
		const float HipHalfWidth = 0.13f;
		const float BodyBottom = 0.82f;

		DrawCircle(At(centerX, HeadY), HeadRadius * _side, IconColor);

		float bodyTop = HeadY + HeadRadius * 0.9f;
		DrawColoredPolygon(
			new[]
			{
				At(centerX - ShoulderHalfWidth, bodyTop),
				At(centerX + ShoulderHalfWidth, bodyTop),
				At(centerX + HipHalfWidth, BodyBottom),
				At(centerX - HipHalfWidth, BodyBottom),
			},
			IconColor);
	}

	private void DrawSwords()
	{
		DrawSword(new Vector2(0.14f, 0.88f), new Vector2(0.86f, 0.14f));
		DrawSword(new Vector2(0.86f, 0.88f), new Vector2(0.14f, 0.14f));
	}

	private void DrawSword(Vector2 hilt, Vector2 tip)
	{
		const float GuardDistance = 0.22f;
		const float GuardHalfWidth = 0.13f;
		const float PommelRadius = 0.055f;

		Vector2 blade = (tip - hilt).Normalized();
		var across = new Vector2(-blade.Y, blade.X) * GuardHalfWidth;
		Vector2 guard = hilt + blade * GuardDistance;

		DrawLine(At(hilt), At(tip), IconColor, Stroke);
		DrawLine(At(guard - across), At(guard + across), IconColor, Stroke);
		DrawCircle(At(hilt), PommelRadius * _side, IconColor);
	}

	private void DrawCrown()
	{
		const float BandY = 0.55f;
		const float BandHeight = 0.18f;

		DrawRect(Box(0.20f, BandY, 0.60f, BandHeight), IconColor);
		DrawColoredPolygon(new[] { At(0.20f, BandY), At(0.32f, 0.22f), At(0.44f, BandY) }, IconColor);
		DrawColoredPolygon(new[] { At(0.38f, BandY), At(0.50f, 0.14f), At(0.62f, BandY) }, IconColor);
		DrawColoredPolygon(new[] { At(0.56f, BandY), At(0.68f, 0.22f), At(0.80f, BandY) }, IconColor);
	}

	private void DrawGear()
	{
		const int Teeth = 8;
		const float InnerRadius = 0.28f;
		const float OuterRadius = 0.42f;
		const float HubRadius = 0.14f;

		Vector2 center = At(0.5f, 0.5f);
		DrawArc(center, InnerRadius * _side, 0f, Mathf.Tau, 32, IconColor, Stroke);
		for (int i = 0; i < Teeth; i++)
		{
			float angle = Mathf.Tau * i / Teeth;
			var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			DrawLine(center + direction * InnerRadius * _side, center + direction * OuterRadius * _side, IconColor, Stroke * 1.4f);
		}
		DrawCircle(center, HubRadius * _side, IconColor);
	}

	private void DrawHeart()
	{
		const float LobeRadius = 0.20f;
		const float LobeY = 0.34f;

		DrawCircle(At(0.5f - LobeRadius * 0.85f, LobeY), LobeRadius * _side, IconColor);
		DrawCircle(At(0.5f + LobeRadius * 0.85f, LobeY), LobeRadius * _side, IconColor);
		DrawColoredPolygon(
			new[]
			{
				At(0.5f - 0.38f, LobeY + 0.04f),
				At(0.5f + 0.38f, LobeY + 0.04f),
				At(0.5f, 0.86f),
			},
			IconColor);
	}

	private float Stroke => _side * StrokeRatio;

	private Vector2 At(Vector2 point) => _origin + point * _side;

	private Vector2 At(float x, float y) => At(new Vector2(x, y));

	private Rect2 Box(float x, float y, float width, float height) =>
		new(At(x, y), new Vector2(width, height) * _side);
}
