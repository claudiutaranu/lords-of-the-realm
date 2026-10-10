using Godot;

/// <summary>The top of a castle's wall in an assault (FieldWall): the walkway behind its parapet, where
/// a defender stands above the fight and behind stone, and the posts the castle's captain gives his
/// companies on it.</summary>
public sealed partial class FieldWall
{
	/// <summary>How far in from the wall's line its walkway runs: a defender within it is on the walls.</summary>
	public const float Walkway = 3.2f;

	/// <summary>How far in from the line a company is posted on the walkway.</summary>
	private const float Posted = 1.6f;

	/// <summary>Whether a spot is on the walkway: within it of the line, and not in the gate or a
	/// breach, where there is no wall left to stand on.</summary>
	public bool OnWalls(Vector2 at)
	{
		Vector2 off = at - Middle;
		float inside = Half - Mathf.Max(Mathf.Abs(off.X), Mathf.Abs(off.Y));
		if (inside < 0f || inside > Walkway)
		{
			return false;
		}

		Face face = FaceOf(at);
		(Vector2 from, Vector2 to) = Ends(face);
		float along = Geometry2D.GetClosestPointToSegment(at, from, to).DistanceTo(from);
		return Gap(face, along) is not { Is: Kind.Gate or Kind.Breach };
	}

	/// <summary>The face a spot is nearest.</summary>
	public Face FaceOf(Vector2 at)
	{
		Vector2 off = at - Middle;
		return Mathf.Abs(off.Y) >= Mathf.Abs(off.X)
			? (off.Y > 0f ? Face.South : Face.North)
			: (off.X > 0f ? Face.East : Face.West);
	}

	/// <summary>A post on the walkway: so far along a face, standing on it and facing out over it.</summary>
	public (Vector2 At, Vector2 Facing) Post(Face face, float along)
	{
		(Vector2 from, Vector2 to) = Ends(face);
		Vector2 on = from + ((to - from).Normalized() * along);
		return (on - (Outward(face) * Posted), Outward(face));
	}

	/// <summary>How far in from the wall's line a corner tower's top reaches, either way: a man that
	/// near both faces stands on the tower, above the walkway.</summary>
	public const float TowerReach = 2.4f;

	/// <summary>Whether a spot is on the top of one of the corner towers.</summary>
	public bool OnTower(Vector2 at)
	{
		Vector2 off = at - Middle;
		return OnWalls(at) && Mathf.Abs(off.X) > Half - TowerReach && Mathf.Abs(off.Y) > Half - TowerReach;
	}

	/// <summary>A post on the top of the tower at one end of a face, facing out across that face's
	/// corner.</summary>
	public (Vector2 At, Vector2 Facing) TowerPost(Face face, bool atItsStart, float wide)
	{
		// Along the walkway from the corner, as far as the company is wide, so its end files stand on
		// the tower and the rest on the wall beside it — never off the stone.
		float along = (wide / 2f) + 0.5f;
		return Post(face, atItsStart ? along : (Half * 2f) - along);
	}

	/// <summary>Inside a gap, so far in from it: where a company stands to hold it.</summary>
	public Vector2 Behind(Opening gap, float depth)
	{
		(Vector2 from, Vector2 to) = Ends(gap.On);
		return from + ((to - from).Normalized() * gap.Middle) - (Outward(gap.On) * depth);
	}
}
