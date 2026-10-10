using Godot;

/// <summary>The castle's flag (the user's call): it stands in the middle of the bailey, and an assault
/// is won not only by killing every man on the walls but by holding the flag — attackers round it with
/// no defender left near it, for <see cref="HoldToTake"/> seconds. Every captain knows it: the
/// castle's falls back on it when the enemy is in (FieldDefence), the attacker's makes for it the
/// moment his men are over the wall.</summary>
public sealed partial class FieldBattle
{
	/// <summary>How near the flag a man has to stand to count as holding it or keeping it; how long it
	/// takes to bring down; how quickly it is raised again when the attackers are driven off it; and
	/// within what of it an enemy is a danger the whole garrison answers.</summary>
	public const float FlagReach = 7f;
	private const float HoldToTake = 12f;
	private const float RaisedAgain = 0.5f;
	private const float FlagDanger = 22f;

	/// <summary>Where the flag stands, a little toward the keep from the middle of the bailey.</summary>
	public Vector2 Flag => Wall.Middle + new Vector2(0f, -Wall.Half * 0.1f);

	/// <summary>How far the flag has come down, 0 standing to 1 taken.</summary>
	public float FlagTaken { get; private set; }

	/// <summary>Whether the attackers have their men round it with nobody to stop them.</summary>
	public bool IsFlagContested { get; private set; }

	private void HoldTheFlag()
	{
		if (Wall == null)
		{
			return;
		}

		int ours = 0;
		int theirs = 0;
		foreach (FieldSquad squad in Squads)
		{
			if (!squad.IsStanding || squad.Kind.IsEngine)
			{
				continue;
			}

			foreach (FieldSoldier man in squad.Soldiers)
			{
				if (man.IsStanding && man.At.DistanceTo(Flag) <= FlagReach)
				{
					if (squad.IsAttacking)
					{
						ours++;
					}
					else
					{
						theirs++;
					}
				}
			}
		}

		IsFlagContested = ours > 0 && theirs == 0;
		FlagTaken = IsFlagContested
			? Mathf.Min(1f, FlagTaken + (Slice / HoldToTake))
			: Mathf.Max(0f, FlagTaken - (Slice * RaisedAgain / HoldToTake));
		if (FlagTaken >= 1f)
		{
			IsFlagTaken = true;
		}
	}

	/// <summary>Whether the flag has been brought down: the castle is taken whoever still stands.</summary>
	public bool IsFlagTaken { get; private set; }

	/// <summary>The nearest attacker to the flag within <see cref="FlagDanger"/> of it, if any.</summary>
	private FieldSquad AtTheFlag()
	{
		FieldSquad best = null;
		float bestFar = FlagDanger;
		foreach (FieldSquad enemy in Squads)
		{
			if (enemy.IsAttacking && enemy.IsStanding && !enemy.Kind.IsEngine && enemy.At.DistanceTo(Flag) < bestFar)
			{
				best = enemy;
				bestFar = enemy.At.DistanceTo(Flag);
			}
		}

		return best;
	}

	/// <summary>An attacker over the wall goes for the flag: for the defenders round it if there are any,
	/// else to stand on it. False where he has no business with it (still outside, or an engine).</summary>
	private bool Storm(FieldSquad squad)
	{
		if (Wall == null || !squad.IsAttacking || squad.Kind.IsEngine || !Wall.IsInside(squad.At))
		{
			return false;
		}

		FieldSquad keeper = null;
		float nearest = FlagReach * 2.5f;
		foreach (FieldSquad foe in Squads)
		{
			if (!foe.IsAttacking && foe.IsStanding && foe.At.DistanceTo(Flag) < nearest)
			{
				keeper = foe;
				nearest = foe.At.DistanceTo(Flag);
			}
		}

		if (keeper != null)
		{
			squad.Target = keeper;
			squad.Goal = null;
		}
		else
		{
			squad.Target = null;
			squad.Goal = Flag;
		}

		return true;
	}
}
