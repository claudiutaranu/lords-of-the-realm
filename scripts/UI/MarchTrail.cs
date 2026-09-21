using Godot;

/// <summary>The road an army would take, laid out in front of it: a gold-ringed step for each stage
/// of the walk, numbered in the order it would take them, crossed swords where it ends, and dimmed
/// past the point this season's legs give out.
///
/// The dimming is the useful half. A trail that simply stopped at the edge of the allowance would
/// leave a lord unable to tell "there is no way there" from "there is, and it is two seasons off" —
/// and those want completely different decisions out of him.
///
/// Drawn rather than built out of nodes: it changes every time the cursor moves, and a node per bead
/// created and freed would churn the scene tree at whatever rate the mouse reports at.
///
/// It takes screen positions, not map ones. The page re-projects the trail every frame from the same
/// camera it re-projects the province pins with, so the beads stay on the ground while the lord
/// pans — and nothing in this file has to know a camera exists.</summary>
public partial class MarchTrail : Control
{
	private const float BeadRadius = 9f;
	private const float CountyRadius = 12f;
	private const float TargetRadius = 17f;

	private static readonly Color Bead = new("1b2436");
	private static readonly Color Ring = new("e0b95f");
	private static readonly Color Ink = new("f3dfa8");
	private static readonly Color Shade = new(0f, 0f, 0f, 0.4f);

	// Past the season's legs: the same marks with the life gone out of them.
	private static readonly Color FarBead = new("2a2e33");
	private static readonly Color FarRing = new("6f6a5e");
	private static readonly Color FarInk = new("9a958a");

	private (Vector2 At, int Step, bool County, bool Reachable)[] _beads =
		System.Array.Empty<(Vector2, int, bool, bool)>();

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
	public void Lay((Vector2 At, int Step, bool County, bool Reachable)[] beads)
	{
		_beads = beads;
		Visible = beads.Length > 0;
		QueueRedraw();
	}

	public override void _Draw()
	{
		for (int bead = 0; bead < _beads.Length; bead++)
		{
			(Vector2 at, int step, bool county, bool reachable) = _beads[bead];
			bool last = bead == _beads.Length - 1;
			Color ring = reachable ? Ring : FarRing;
			float radius = last ? TargetRadius : county ? CountyRadius : BeadRadius;

			DrawCircle(at + new Vector2(1.5f, 2.5f), radius, Shade);
			DrawCircle(at, radius, reachable ? Bead : FarBead);
			DrawArc(at, radius, 0, Mathf.Tau, 32, ring, last ? 3.5f : 2.5f);

			if (last)
			{
				Swords(at, radius, ring);
				continue;
			}

			int size = county ? 14 : 12;
			string count = step.ToString();
			Vector2 box = _font.GetStringSize(count, HorizontalAlignment.Center, -1f, size);
			DrawString(_font, at + new Vector2(-box.X / 2f, box.Y * 0.36f), count,
				HorizontalAlignment.Center, -1f, size, reachable ? Ink : FarInk);
		}
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
