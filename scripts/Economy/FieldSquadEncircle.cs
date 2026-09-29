using Godot;

/// <summary>How the files of a squad that overhang a narrower enemy curl round his flank and rear.</summary>
public sealed partial class FieldSquad
{
	/// <summary>A man of a file that overhangs the enemy, in his place round him, and which way is
	/// away from the enemy there. Null for a man of a file square in front of him.
	///
	/// The places round the enemy are a path: a quarter turn at the corner of his line, along his
	/// flank the depth of his ranks, a quarter turn at his rear corner, and across his back to his
	/// middle, where the other side's overhang comes round to meet it. The men of the overhanging
	/// files are set along it one after another, the front rank's first; past its end they start a
	/// second ring, a man's width further out.</summary>
	private (Vector2 At, Vector2 Outward)? Encircle(FieldSquad foe, int file, int rank, Vector2 right, Vector2 frontLine)
	{
		float centre = (foe.At - At).Dot(right);
		float halfWide = foe.Extent(right);
		float Beyond(int f) => Mathf.Abs(Across(f) - centre) - halfWide - (Gap / 2f);
		if (Beyond(file) <= 0f)
		{
			return null;
		}

		float side = Mathf.Sign(Across(file) - centre);
		int overhang = 0;
		int order = 0;
		for (int f = 0; f < _files; f++)
		{
			if (Beyond(f) > 0f && Mathf.Sign(Across(f) - centre) == side)
			{
				overhang++;
				order += Beyond(f) < Beyond(file) ? 1 : 0;
			}
		}

		Vector2 outward = right * side;
		float arc = Mathf.Pi / 2f * Turn;
		float flank = Mathf.Max(0f, (2f * foe.Extent(Facing)) + LineGap - (2f * Turn));
		float path = arc + flank + arc + Mathf.Max(Gap, halfWide - Turn);
		float along = ((rank * overhang) + order + 0.5f) * Gap;
		int ring = Mathf.FloorToInt(along / path);
		along -= ring * path;

		Vector2 corner = frontLine + (right * (centre + (side * (halfWide + LineGap - Turn))));
		(Vector2 at, Vector2 away) = Round(corner, outward, along, arc, flank);
		return (at + (away * ring * Gap), away);
	}

	/// <summary>A point on the path round the enemy, this far along it from the corner of his line,
	/// and which way is away from him there.</summary>
	private (Vector2 At, Vector2 Away) Round(Vector2 corner, Vector2 outward, float along, float arc, float flank)
	{
		if (along <= arc)
		{
			float angle = along / Turn;
			return (corner + (outward * (Turn * Mathf.Sin(angle))) + (Facing * (Turn * (1f - Mathf.Cos(angle)))),
				(-Facing * Mathf.Cos(angle)) + (outward * Mathf.Sin(angle)));
		}

		Vector2 alongFlank = corner + (outward * Turn) + (Facing * Turn);
		if (along <= arc + flank)
		{
			return (alongFlank + (Facing * (along - arc)), outward);
		}

		Vector2 rearCorner = alongFlank + (Facing * flank) - (outward * Turn);
		float turned = along - arc - flank;
		if (turned <= arc)
		{
			float angle = turned / Turn;
			return (rearCorner + (outward * (Turn * Mathf.Cos(angle))) + (Facing * (Turn * Mathf.Sin(angle))),
				(outward * Mathf.Cos(angle)) + (Facing * Mathf.Sin(angle)));
		}

		return (rearCorner + (Facing * Turn) - (outward * (turned - arc)), Facing);
	}
}
