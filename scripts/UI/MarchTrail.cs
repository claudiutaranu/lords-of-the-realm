using Godot;

/// <summary>The road an army would take, laid out in front of it: small chevrons at an even pace
/// along it, pointing the way the men would walk; a numbered medallion where each season's march
/// would end — 1 where they halt this season, 2 the next — and crossed swords where it ends. Past
/// the point this season's legs give out it is dimmed.
///
/// The dimming is the useful half. A trail that simply stopped at the edge of the allowance would
/// leave a lord unable to tell "there is no way there" from "there is, and it is two seasons off" —
/// and those want completely different decisions out of him.
///
/// Drawn rather than built out of nodes: it changes every time the cursor moves, and a node per mark
/// created and freed would churn the scene tree at whatever rate the mouse reports at.
///
/// It takes screen positions, not map ones. The page re-projects the trail every frame from the same
/// camera it re-projects the province pins with, so the marks stay on the ground while the lord
/// pans — and nothing in this file has to know a camera exists.</summary>
public partial class MarchTrail : Control
{
	/// <summary>What a mark on the trail is: a footstep along it, the end of a season's march, or
	/// where the men are being sent.</summary>
	public enum Mark { Step, Season, Target }

	private const float ChevronLength = 5f;
	private const float ChevronWide = 4.5f;
	private const float SeasonRadius = 11f;
	private const float TargetRadius = 16f;

	private static readonly Color Bead = new("1b2436");
	private static readonly Color Ring = new("e0b95f");
	private static readonly Color Ink = new("f3dfa8");
	private static readonly Color Shade = new(0f, 0f, 0f, 0.45f);

	// Past the season's legs: the same marks with the life gone out of them.
	private static readonly Color FarBead = new("2a2e33");
	private static readonly Color FarRing = new("8a857a");
	private static readonly Color FarInk = new("b0ab9f");

	private (Vector2 At, Mark Mark, int Season, bool Reachable)[] _marks =
		System.Array.Empty<(Vector2, Mark, int, bool)>();

	private Font _font;

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Ignore;
		_font = ThemeDB.FallbackFont;
		Visible = false;
	}

	/// <summary>Lays the trail out, in screen coordinates and walking order. An empty one takes it
	/// down.</summary>
	public void Lay((Vector2 At, Mark Mark, int Season, bool Reachable)[] marks)
	{
		_marks = marks;
		Visible = marks.Length > 0;
		QueueRedraw();
	}

	public override void _Draw()
	{
		// The footsteps first, then the medallions over them.
		for (int i = 0; i < _marks.Length; i++)
		{
			if (_marks[i].Mark == Mark.Step)
			{
				Chevron(i);
			}
		}

		foreach ((Vector2 at, Mark mark, int season, bool reachable) in _marks)
		{
			if (mark == Mark.Season)
			{
				Medallion(at, SeasonRadius, reachable, 2.5f);
				Number(at, season, reachable);
			}
			else if (mark == Mark.Target)
			{
				Medallion(at, TargetRadius, reachable, 3.5f);
				Swords(at, TargetRadius, reachable ? Ring : FarRing);
			}
		}
	}

	/// <summary>A footstep: a chevron pointing along the road, from the step before it to the one
	/// after, with a dark edge so it reads on grass and on road alike.</summary>
	private void Chevron(int i)
	{
		(Vector2 at, _, _, bool reachable) = _marks[i];
		Vector2 before = i > 0 ? _marks[i - 1].At : at;
		Vector2 after = i < _marks.Length - 1 ? _marks[i + 1].At : at;
		Vector2 way = after - before;
		if (way.LengthSquared() < 0.01f)
		{
			return;
		}

		way = way.Normalized();
		var aside = new Vector2(-way.Y, way.X);
		Vector2 tip = at + (way * ChevronLength * 0.5f);
		Vector2[] arms = { tip - (way * ChevronLength) + (aside * ChevronWide), tip, tip - (way * ChevronLength) - (aside * ChevronWide) };
		DrawPolyline(arms, reachable ? Bead : new Color(0f, 0f, 0f, 0.5f), 4.5f, true);
		DrawPolyline(arms, reachable ? Ring : FarRing, 2.2f, true);
	}

	private void Medallion(Vector2 at, float radius, bool reachable, float rim)
	{
		DrawCircle(at + new Vector2(1.5f, 2.5f), radius, Shade);
		DrawCircle(at, radius, reachable ? Bead : FarBead);
		DrawArc(at, radius, 0, Mathf.Tau, 40, reachable ? Ring : FarRing, rim, true);
	}

	private void Number(Vector2 at, int season, bool reachable)
	{
		const int size = 13;
		string count = season.ToString();
		Vector2 box = _font.GetStringSize(count, HorizontalAlignment.Center, -1f, size);
		DrawString(_font, at + new Vector2(-box.X / 2f, box.Y * 0.36f), count,
			HorizontalAlignment.Center, -1f, size, reachable ? Ink : FarInk);
	}

	/// <summary>The crossed swords on the last step: this is where the men are being sent, and it is
	/// the one mark on the trail that is not a number, because it is not a count of anything.</summary>
	private void Swords(Vector2 at, float radius, Color colour)
	{
		float reach = radius * 0.62f;
		foreach (Vector2 lean in new[] { new Vector2(1f, 1f), new Vector2(1f, -1f) })
		{
			Vector2 blade = lean.Normalized() * reach;
			DrawLine(at - blade, at + blade, colour, 3f);

			// A crosspiece near the hilt end, which is what makes two lines read as two swords.
			Vector2 across = new Vector2(-lean.Y, lean.X).Normalized() * (reach * 0.34f);
			Vector2 hilt = at - (blade * 0.55f);
			DrawLine(hilt - across, hilt + across, colour, 2.5f);
		}
	}
}
