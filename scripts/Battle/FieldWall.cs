using System.Collections.Generic;
using Godot;

/// <summary>A castle's wall on the field, for an assault fought by hand: a closed square round the
/// defenders, as big as the rung (a palisade small, the royal castle large), crossed only where it is
/// open — the gate once the rams have beaten it down, a breach once a catapult has (both in the face
/// toward the attacker, and whole when the day begins: <see cref="Pound"/>), and ladders on that face
/// and the two beside it, which are the frontage the captain's
/// reckoning counts made into places to climb. A horse goes up no ladder. Anyone else who would cross
/// it goes round its corners to the nearest gap he can use; no step passes through stone.
///
/// It holds the rules the captain's terms hold (Battle.Walls): a defender inside it is harder to kill
/// by its stone, an attacker under it — at its foot, in a gap, on a ladder — easier by AssaultExposure.
/// The castle's men hold it: sent at anything outside, they go to the wall facing it (Within).
///
/// ponytail: fought by both captains, the hand assault still goes easier on the attacker than the
/// captain's reckoning of the same walls (a large castle the captain holds falls by hand); a balance
/// pass, and a BattleCheck holding the two together as ByHand holds the field, is what it wants. The field's metres: the attacker to the
/// south (y larger), the castle to the north.</summary>
public sealed partial class FieldWall
{
	/// <summary>What a gap in the wall is: the gate rammed open, a breach, or a ladder.</summary>
	public enum Kind { Gate, Breach, Ladder }

	/// <summary>The four sides, by the way they face.</summary>
	public enum Face { South, East, North, West }

	/// <summary>A gap, on one face, from one distance along it to another (from its west or south end).</summary>
	public readonly record struct Opening(Face On, float From, float To, Kind Is)
	{
		public float Middle => (From + To) / 2f;
	}

	/// <summary>How big the square is for the lowest rung, and how much each rung above adds to half its side.</summary>
	private const float SmallestHalf = 16f;
	private const float HalfPerRung = 4f;

	/// <summary>How near the wall, either side of it, an attacker is under it; how far out its foot is;
	/// and how much slower a man goes up a ladder than he walks.</summary>
	private const float Sheltered = 3.5f;
	private const float Foot = 1.5f;
	public const float Climb = 0.35f;

	private const float GateWide = 5f;
	private const float BreachWide = 7f;
	private const float LadderWide = 1.6f;
	private const int MenToALadder = 10;
	private const int FewestLadders = 2;
	private const int MostLadders = 8;

	public Vector2 Middle { get; }

	/// <summary>Half the square's side, in metres.</summary>
	public float Half { get; }

	public float Stone { get; }

	public float Exposure { get; }

	public bool IsTimber { get; }

	public List<Opening> Openings { get; } = new();


	public FieldWall(Vector2 middle, Defenders against, GameBalance b)
	{
		Middle = middle;
		Fortifications.Wall wall = Fortifications.Of(against.Fortification);
		Half = SmallestHalf + (Mathf.Max(0, wall.Rung) * HalfPerRung);
		Stone = Mathf.Max(1f, wall.Defence * (1f - (against.Catapults * b.CatapultDefenceCut)));
		Exposure = b.AssaultExposure;
		IsTimber = Fortifications.IsTimber(against.Fortification);
		float side = Half * 2f;

		float soft = IsTimber ? TimberShare : 1f;
		if (against.Rams > 0)
		{
			Batter(new Opening(Face.South, Half - (GateWide / 2f), Half + (GateWide / 2f), Kind.Gate), GateSeconds * soft);
		}

		for (int i = 0; i < against.Catapults; i++)
		{
			float at = Half + ((i % 2 == 0 ? -1f : 1f) * side * (0.22f + (0.1f * (i / 2))));
			Batter(new Opening(Face.South, at - (BreachWide / 2f), at + (BreachWide / 2f), Kind.Breach), CurtainSeconds * soft);
		}

		// Ladders: half on the south face, the rest shared by the east and west, spread along each.
		int ladders = Mathf.Clamp(Mathf.RoundToInt(wall.Frontage / (float)MenToALadder), FewestLadders, MostLadders);
		int south = (ladders + 1) / 2;
		int flank = ladders - south;
		Spread(Face.South, south, side);
		Spread(Face.East, (flank + 1) / 2, side);
		Spread(Face.West, flank / 2, side);
	}

	/// <summary>Puts so many ladders along a face, clear of the gaps already in it and of the stretches
	/// the engines will open.</summary>
	private void Spread(Face face, int count, float side)
	{
		for (int i = 0; i < count; i++)
		{
			float at = side * (i + 1) / (count + 1);
			while (Gap(face, at, LadderWide * 2f) != null || Battered.Exists(t => t.Gap.On == face
				&& at >= t.Gap.From - (LadderWide * 2f) && at <= t.Gap.To + (LadderWide * 2f)))
			{
				at += LadderWide * 3f;
			}

			Openings.Add(new Opening(face, at - (LadderWide / 2f), at + (LadderWide / 2f), Kind.Ladder));
		}
	}

	// --- where the stone is ------------------------------------------------------------------

	/// <summary>A face's two ends, west to east or south to north.</summary>
	public (Vector2 From, Vector2 To) Ends(Face face)
	{
		float w = Middle.X - Half, e = Middle.X + Half, n = Middle.Y - Half, s = Middle.Y + Half;
		return face switch
		{
			Face.South => (new Vector2(w, s), new Vector2(e, s)),
			Face.North => (new Vector2(w, n), new Vector2(e, n)),
			Face.West => (new Vector2(w, s), new Vector2(w, n)),
			_ => (new Vector2(e, s), new Vector2(e, n)),
		};
	}

	/// <summary>The way out of the castle across a face.</summary>
	private static Vector2 Outward(Face face) => face switch
	{
		Face.South => Vector2.Down,
		Face.North => Vector2.Up,
		Face.West => Vector2.Left,
		_ => Vector2.Right,
	};

	public static readonly Face[] Faces = { Face.South, Face.East, Face.North, Face.West };

	public bool IsInside(Vector2 at) =>
		Mathf.Abs(at.X - Middle.X) < Half && Mathf.Abs(at.Y - Middle.Y) < Half;

	private Opening? Gap(Face face, float along, float slack = 0f)
	{
		foreach (Opening gap in Openings)
		{
			if (gap.On == face && along >= gap.From - slack && along <= gap.To + slack)
			{
				return gap;
			}
		}

		return null;
	}

	private static bool Passes(Opening gap, bool mounted) => gap.Is != Kind.Ladder || !mounted;

	/// <summary>Whether a straight step crosses stone: through a face anywhere but a gap he can use.</summary>
	public bool Blocked(Vector2 from, Vector2 to, bool mounted)
	{
		foreach (Face face in Faces)
		{
			(Vector2 a, Vector2 b) = Ends(face);
			Variant crossing = Geometry2D.SegmentIntersectsSegment(from, to, a, b);
			if (crossing.VariantType == Variant.Type.Vector2)
			{
				Vector2 hit = crossing.AsVector2();
				float along = hit.DistanceTo(a);
				if (Gap(face, along) is not Opening gap || !Passes(gap, mounted))
				{
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>Where a man going from one spot to another walks next: straight there if no stone
	/// stands between; else to the nearest gap he can use — round a corner first if the castle is in
	/// the way of that — to its foot on his own side, and once there, through it.</summary>
	public Vector2 Detour(Vector2 from, Vector2 to, bool mounted)
	{
		if (!Blocked(from, to, mounted))
		{
			return to;
		}

		bool inside = IsInside(from);
		Opening? best = null;
		float bestFar = float.MaxValue;
		foreach (Opening gap in Openings)
		{
			if (!Passes(gap, mounted))
			{
				continue;
			}

			float far = from.DistanceTo(At(gap, inside)) + At(gap, !inside).DistanceTo(to);
			if (far < bestFar)
			{
				best = gap;
				bestFar = far;
			}
		}

		if (best is not Opening way)
		{
			return from;
		}

		Vector2 foot = At(way, inside);
		if (from.DistanceTo(foot) < 0.8f)
		{
			return At(way, !inside);
		}

		return Blocked(from, foot, mounted) ? Corner(from, foot, inside, mounted) : foot;
	}

	/// <summary>A gap's foot on one side of it.</summary>
	private Vector2 At(Opening gap, bool inside)
	{
		(Vector2 a, Vector2 b) = Ends(gap.On);
		Vector2 on = a + ((b - a).Normalized() * gap.Middle);
		return on + (Outward(gap.On) * (inside ? -Foot : Foot));
	}

	/// <summary>The corner of the square to go round, a little off it, that leads best to a spot.</summary>
	private Vector2 Corner(Vector2 from, Vector2 to, bool inside, bool mounted)
	{
		float off = inside ? -Foot : Foot * 2f;
		Vector2 best = from;
		float bestFar = float.MaxValue;
		foreach (Vector2 corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
		{
			Vector2 at = Middle + (corner * (Half + off));
			if (Blocked(from, at, mounted))
			{
				continue;
			}

			float far = from.DistanceTo(at) + at.DistanceTo(to);
			if (far < bestFar)
			{
				best = at;
				bestFar = far;
			}
		}

		return best;
	}

	/// <summary>The nearest spot to this one inside the walls, a little way in from them: where a
	/// defender goes who is sent at something outside. He mans the wall facing it; he does not leave.</summary>
	public Vector2 Within(Vector2 at)
	{
		float room = Half - InsideWall;
		return new Vector2(Mathf.Clamp(at.X, Middle.X - room, Middle.X + room), Mathf.Clamp(at.Y, Middle.Y - room, Middle.Y + room));
	}

	/// <summary>How far in from the wall a defender stands to fight off it.</summary>
	private const float InsideWall = 1.1f;

	/// <summary>A step that would pass through stone is not taken.</summary>
	public Vector2 Stop(Vector2 from, Vector2 to, bool mounted) => Blocked(from, to, mounted) ? from : to;

	/// <summary>Whether a man is going up a ladder: within a ladder's gap, at the wall.</summary>
	public bool Climbing(Vector2 at) => OnWall(at, Foot * 1.5f) is { Is: Kind.Ladder };

	/// <summary>Whether an attacker is under the walls: at their foot outside, or in one of their gaps.</summary>
	public bool UnderIt(Vector2 at) =>
		Mathf.Abs(Mathf.Max(Mathf.Abs(at.X - Middle.X), Mathf.Abs(at.Y - Middle.Y)) - Half) <= Sheltered;

	/// <summary>The gap a spot near the wall stands in, or null.</summary>
	private Opening? OnWall(Vector2 at, float within)
	{
		foreach (Face face in new[] { Face.South, Face.East, Face.North, Face.West })
		{
			(Vector2 a, Vector2 b) = Ends(face);
			Vector2 near = Geometry2D.GetClosestPointToSegment(at, a, b);
			if (near.DistanceTo(at) <= within && Gap(face, near.DistanceTo(a)) is Opening gap)
			{
				return gap;
			}
		}

		return null;
	}
}
