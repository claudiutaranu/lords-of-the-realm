/// <summary>Who can reach whom: which enemy squad a squad is at grips with, which it can shoot at, and
/// which is nearest.</summary>
public sealed partial class FieldBattle
{
	/// <summary>Whom a squad is at grips with: the one it was sent at if that one is in reach, or
	/// else the nearest enemy that is.</summary>
	private FieldSquad Engaged(FieldSquad squad)
	{
		if (squad.Target is { IsStanding: true } target && Touching(squad, target))
		{
			return target;
		}

		FieldSquad nearest = Nearest(squad, float.MaxValue);
		return nearest != null && Touching(squad, nearest) ? nearest : null;
	}

	private static bool Touching(FieldSquad a, FieldSquad b) =>
		a.At.DistanceTo(b.At) <= Closed(a, b) - FieldSquad.LineGap + StrikeReach + Contact;

	private static bool InRange(FieldSquad a, FieldSquad b) => a.At.DistanceTo(b.At) - b.Reach <= a.Range;

	/// <summary>The nearest enemy squad still standing, measured to the edge of its ranks, within
	/// <paramref name="within"/> metres.</summary>
	private FieldSquad Nearest(FieldSquad from, float within)
	{
		FieldSquad best = null;
		float bestFar = within;
		foreach (FieldSquad squad in Squads)
		{
			if (squad.IsAttacking == from.IsAttacking || !squad.IsStanding)
			{
				continue;
			}

			float far = from.At.DistanceTo(squad.At) - squad.Reach;
			if (far <= bestFar)
			{
				best = squad;
				bestFar = far;
			}
		}

		return best;
	}
}
