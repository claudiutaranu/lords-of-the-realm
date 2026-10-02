using System.Collections.Generic;
using Godot;

/// <summary>How squads move over the field: walking to where they were sent, closing with an enemy,
/// and keeping out of each other's way.</summary>
public sealed partial class FieldBattle
{
	private void Walk(FieldSquad squad)
	{
		squad.IsMoving = false;
		if (!squad.IsStanding)
		{
			return;
		}

		if (squad.Target is { IsStanding: false })
		{
			squad.Target = null;
		}

		// Left without orders, a squad the enemy comes up to goes for him rather than standing in
		// its line and watching.
		if (squad.Target == null && squad.Goal == null && !squad.Shoots
			&& NearestEnemyMan(squad, Challenge) is FieldSoldier comer)
		{
			squad.Target = comer.Squad;
		}

		// A squad fighting some other enemy than the one it was sent at holds where it is: walked on,
		// it would drag its ranks sideways through the men it is fighting. Fighting the one it was
		// sent at, it goes on closing until its front rank is on him — it used to stop the moment two
		// or three of its men were at blows, and the rest of the battalion stood and watched them.
		if (squad.IsFighting && squad.Goal == null && squad.InMelee != null && squad.Target != squad.InMelee)
		{
			return;
		}

		if (squad.Target != null && !squad.Shoots)
		{
			Close(squad, squad.Target);
			return;
		}

		Vector2 to;
		float stopAt;
		if (squad.Target != null)
		{
			to = squad.Target.At;
			stopAt = squad.Range * 0.9f;
		}
		else if (squad.Goal is Vector2 goal)
		{
			to = goal;
			// All the way there: the lord sent him to that spot, and nothing shoves a squad he placed
			// (Spread), so nothing holds him off it. Stopped a pace and a half short, a company brought
			// up beside another stood that far from where it was told.
			stopAt = 0f;
		}
		else
		{
			return;
		}

		Vector2 way = to - squad.At;
		float far = way.Length();
		// A hair's breadth past stopping counts as stopped: walked exactly up to the line he stops
		// at, a squad could come to rest a rounding error short of it and walk on the spot for ever.
		if (far <= stopAt + 0.01f)
		{
			// There: he turns to the front the lord drew for him, if he drew one.
			squad.Goal = null;
			squad.CameToRest = Clock;
			if (squad.FaceGoal is Vector2 face)
			{
				squad.Facing = face;
				squad.FaceGoal = null;
				squad.KeepsFront = true;
			}

			return;
		}

		// On his way to a front the lord drew, he already faces the way he will stand there, and
		// walks to it in his ranks: turned to the road instead, a long thin line walked edgeways and
		// swung round when it arrived, its men running to their new places in curves.
		// Faced the way he will stand there, he still walks toward the spot and not the way he faces:
		// walking the way he faced, a company sent to a front drawn beside it marched straight past it.
		squad.Facing = squad.FaceGoal ?? way / far;
		squad.At += way / far * Mathf.Min(squad.Pace * Slice, far - stopAt);
		squad.IsMoving = true;
	}

	/// <summary>A squad sent at an enemy: it faces his standard — steadily, and only until it is up
	/// against him, when it keeps the front it has — and walks straight ahead until its front rank
	/// is a weapon's length from the first of his men in front of it.
	///
	/// Measured to his men and not to his standard: a squad cut up, or curled round somebody else's
	/// flank, is not where its standard says, and a squad sent at the standard stopped the right
	/// distance from nobody. And straight ahead, not at whichever of his men was nearest: turned
	/// after that man every slice, a squad swung round and round the enemy and never reached him.</summary>
	private void Close(FieldSquad squad, FieldSquad foe)
	{
		Vector2 toward = foe.At - squad.At;
		if (squad.InMelee == null && toward.LengthSquared() > 0.01f)
		{
			squad.Facing = toward.Normalized();
		}

		var right = new Vector2(-squad.Facing.Y, squad.Facing.X);
		float halfWide = squad.Reach;
		float ahead = float.MaxValue;
		float anywhere = float.MaxValue;
		foreach (FieldSoldier man in foe.Soldiers)
		{
			Vector2 from = man.At - squad.At;
			float along = from.Dot(squad.Facing);
			anywhere = Mathf.Min(anywhere, from.Length());
			if (along > 0f && Mathf.Abs(from.Dot(right)) <= halfWide)
			{
				ahead = Mathf.Min(ahead, along);
			}
		}

		// Nobody of his in front of us — he is off to one side: make for the nearest of him.
		float gap = (ahead < float.MaxValue ? ahead : anywhere) - squad.Front - Reached;
		if (gap > 0.05f)
		{
			squad.At += squad.Facing * Mathf.Min(squad.Pace * Slice, gap);
			squad.IsMoving = true;
		}
	}

	/// <summary>Squads of one side that have come to rest stand side by side, not one on another. Enemies
	/// are not pushed apart here — they stop of their own accord, front rank to front rank (Close);
	/// pushed apart as well, a wide squad was rolled round a narrow one and never met it.
	///
	/// A squad the lord placed is not moved at all: the other makes all the room, and two he placed
	/// stand where he put them, overlapping or not — pushed apart evenly, a company he had placed was
	/// shoved off its spot every time he brought another up beside it, and made the later give way,
	/// the one he brought up would not stay where he told it. Between two he did not place, the one
	/// that came to rest last makes the room.</summary>
	private void Spread()
	{
		for (int i = 0; i < Squads.Count; i++)
		{
			for (int j = i + 1; j < Squads.Count; j++)
			{
				FieldSquad a = Squads[i];
				FieldSquad b = Squads[j];
				// A company on the march goes through a friendly one, its men between theirs: shoved
				// aside instead, a company standing where it was put was pushed off its ground by
				// every other that walked past it. Only two that have both come to rest are kept apart.
				if (!a.IsStanding || !b.IsStanding || a.IsAttacking != b.IsAttacking
					|| a.IsMoving || b.IsMoving || a.Goal != null || b.Goal != null)
				{
					continue;
				}

				Vector2 apart = b.At - a.At;
				float far = apart.Length();
				// By the ground each actually covers that way — deep ahead, wide to the side — and not a
				// circle round each: kept a circle apart, a second company could not come in on the
				// flank of the enemy beside the first, and a crowd came at a few in ones and twos.
				Vector2 way = far < 0.01f ? Vector2.Right : apart / far;
				float room = a.Extent(way) + b.Extent(-way) + a.Gap;
				if (far >= room)
				{
					continue;
				}

				if (a.IsPlaced && b.IsPlaced)
				{
					continue;
				}

				Vector2 away = far < 0.01f ? Vector2.Right : apart / far;
				bool aGives = !a.IsPlaced && (b.IsPlaced || a.CameToRest >= b.CameToRest);
				bool bGives = !b.IsPlaced && (a.IsPlaced || b.CameToRest >= a.CameToRest);
				float push = (room - far) * (aGives && bGives ? 0.5f : 1f);
				if (aGives)
				{
					a.At -= away * push;
				}

				if (bGives)
				{
					b.At += away * push;
				}
			}
		}
	}

	/// <summary>The enemy man nearest a squad's ranks, within <paramref name="within"/> metres of them.</summary>
	private FieldSoldier NearestEnemyMan(FieldSquad squad, float within)
	{
		FieldSoldier best = null;
		float bestFar = within;
		foreach (FieldSquad other in Squads)
		{
			if (other.IsAttacking == squad.IsAttacking)
			{
				continue;
			}

			foreach (FieldSoldier man in other.Soldiers)
			{
				float far = man.At.DistanceTo(squad.At) - squad.Reach;
				if (far < bestFar)
				{
					best = man;
					bestFar = far;
				}
			}
		}

		return best;
	}

	/// <summary>How far apart two enemy squads' standards stand once their ranks have met, a
	/// weapon's length between them.</summary>
	private static float Closed(FieldSquad a, FieldSquad b)
	{
		Vector2 between = (b.At - a.At).Normalized();
		return a.Extent(between) + b.Extent(-between) + FieldSquad.LineGap;
	}
}
