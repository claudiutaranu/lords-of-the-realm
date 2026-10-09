using System.Collections.Generic;
using Godot;

/// <summary>Who is fighting whom on the field, man against man, and what each blow and each arrow
/// does.
///
/// A swing lands by the striker's attack against the other man's defence — the two bars on their
/// cards — and bites less into a man in armour, less on the defender's own ground and more from an
/// attacker who has not walked all season. An arrow is loosed at one man
/// of the squad it is aimed at, is less sure the further it flies, and hurts whoever it was aimed at
/// when it comes down, if he is still standing there to be hurt.</summary>
public sealed partial class FieldBattle
{
	/// <summary>An arrow in the air: where from, where it comes down, when, and whom it will hurt
	/// when it does (nobody, if it missed).</summary>
	public readonly record struct Arrow(Vector2 From, Vector2 To, float Loosed, float Flight, FieldSoldier Target,
		float Damage);

	/// <summary>How near an enemy must be for a man to swing at him, from his own place in the ranks:
	/// the man across from him and the two either side of that one.
	/// Nobody leaves his place to go looking: the squad closes with the enemy as a body, until the
	/// two front ranks are this far apart, and then the men in reach fight the men in reach. The
	/// ranks behind wait their turn, and step up when the man in front of them falls.</summary>
	private const float StrikeReach = 1.8f;

	/// <summary>How far a man of a squad at grips goes to find an enemy to fight when there is none
	/// in reach of him, and how many may be at one enemy at once. A company that could only fight
	/// with the men already face to face with the enemy — a column coming at him head first, or the
	/// ranks behind a line — stood in its files and watched a handful do the fighting. Now every man
	/// with nobody to strike goes for the nearest enemy not already pressed by as many as can get at
	/// him, and a company swarms round what it has caught.</summary>
	private const float Swarm = 30f;
	private const int MostOnOne = 3;

	/// <summary>How near an enemy squad must come before a squad standing still turns to face it, and
	/// how quickly it turns, a share a slice: a body of men wheels, it does not spin.</summary>
	private const float FaceWithin = 15f;

	/// <summary>How far either side of the way it faces a squad of bowmen will loose.</summary>
	public const float ShootsWithin = Mathf.Pi / 3f;
	private const float Wheel = 0.06f;

	private const float ArrowSpeed = 45f;

	/// <summary>How much surer a shot is at no range than at its longest.</summary>
	private const float LongShot = 0.5f;

	/// <summary>How far off a missed arrow comes down, in metres.</summary>
	private const float Wide = 4f;

	/// <summary>A man's rhythm is not a metronome's: each swing and each draw takes up to this share
	/// longer than his arm's best.</summary>
	private const float Unsteady = 0.4f;

	public List<Arrow> Arrows { get; } = new();

	/// <summary>Which enemy squad each squad is at grips with, or shooting at, this slice.</summary>
	private void Engage()
	{
		foreach (FieldSquad squad in Squads)
		{
			squad.InMelee = null;
			squad.ShootingAt = null;
			if (!squad.IsStanding)
			{
				continue;
			}

			squad.InMelee = Engaged(squad);

			// A squad standing still turns, as a body, to face the enemy it is fighting or the one
			// bearing down on it, so the two lines meet front to front and not corner through corner.
			FieldSquad threat = squad.InMelee ?? (squad.KeepsFront ? null : Nearest(squad, FaceWithin));
			if (threat != null && !squad.IsMoving)
			{
				squad.Facing = squad.Facing.Lerp((threat.At - squad.At).Normalized(), Wheel).Normalized();
			}
			// Bowmen go on shooting with the enemy at their corner: only the men he is in reach of put
			// down their bows. Stopped as a company the moment anyone was at grips, a whole line of
			// archers stood and watched three swordsmen cut down the end of it.
			if (squad.Shoots && !squad.IsMoving)
			{
				FieldSquad mark = squad.Target is { IsStanding: true } target && InRange(squad, target)
					? target
					: Nearest(squad, squad.Range);
				squad.ShootingAt = mark != null && IsAhead(squad, mark) ? mark : null;
			}
		}
	}

	/// <summary>Whether an enemy stands in the arc a squad faces (ShootsWithin either side): bowmen
	/// loose at what is before them and not at what is behind their backs, and the reach drawn on the
	/// field (BattlefieldGuides) is that same arc.</summary>
	private static bool IsAhead(FieldSquad squad, FieldSquad mark)
	{
		Vector2 toward = mark.At - squad.At;
		return toward.LengthSquared() < 0.01f || squad.Facing.Dot(toward.Normalized()) >= Mathf.Cos(ShootsWithin);
	}

	/// <summary>One man's slice: keep his place in the ranks, and swing at whoever is in reach of
	/// it — or, with nobody in reach, loose if his squad is shooting.</summary>
	private void Act(FieldSoldier man)
	{
		FieldSquad squad = man.Squad;
		man.Ready -= Slice;
		(Vector2 place, Vector2 facing) = squad.Posture(man.Slot);
		place += man.Loose;

		// An engine strikes nobody: it goes where it is sent and works there (FieldWall.Engines).
		if (squad.Kind.IsEngine)
		{
			Walk(man, place, 0.05f);
			man.Facing = man.IsMoving ? man.Facing : facing;
			return;
		}

		// Ordered to march, he goes: he turns from whoever he was fighting and keeps his place in
		// the ranks. Left to stand and trade blows, the men of a squad called out of a fight stayed
		// in it while their standard walked away without them.
		if (squad.Goal != null)
		{
			man.Foe = null;
			Walk(man, place, 0.05f);
			man.Facing = man.IsMoving ? man.Facing : facing;
			return;
		}

		if (man.Foe is not { IsStanding: true } || man.Foe.At.DistanceTo(man.At) > StrikeReach)
		{
			man.Foe = NearestMan(man.At, squad.IsAttacking, StrikeReach, man);
		}

		if (man.Foe is FieldSoldier foe)
		{
			// A man with an enemy in reach stands and fights him where he is. Walked back to his
			// place in the ranks every slice, he stepped out of reach, then in again to meet the
			// next — back and forth for as long as the fight lasted. He goes back when it is over.
			man.IsMoving = false;
			man.Facing = (foe.At - man.At).Normalized();
			if (man.Ready <= 0f)
			{
				Swing(man, foe);
			}

			return;
		}

		// Only once blows are being struck by his squad: going out while it was still coming on, the
		// men of two advancing squads met in the gap between them and fought it out in ones and twos.
		// The captain goes in with the rest — he does not stand at the back and outlive them all.
		// Not a bowman, though: with nobody in reach he looses from where he stands, and leaves the
		// swarming to the men whose trade it is.
		FieldSoldier going = _meeting.GetValueOrDefault(man);
		FieldSoldier meet = squad.IsFighting && !squad.Shoots ? Quarry(man) : null;
		_meeting[man] = meet;
		if (going != meet)
		{
			// He was counted at the one he was going for as the slice began; move him to the new one.
			if (going != null)
			{
				_pressing[going] = _pressing.GetValueOrDefault(going) - 1;
			}

			if (meet != null)
			{
				_pressing[meet] = _pressing.GetValueOrDefault(meet) + 1;
			}
		}

		Walk(man, meet?.At ?? place, meet == null ? 0.05f : StrikeReach * 0.8f);
		// A bowman shooting keeps facing what he shoots at between arrows too: turned back to his
		// squad's front after every one, he twitched round and back with each shot.
		if (!man.IsMoving)
		{
			man.Facing = squad.ShootingAt is { IsStanding: true } aimed ? (aimed.At - man.At).Normalized() : facing;
		}

		if (squad.ShootingAt is { IsStanding: true } mark && man.Ready <= 0f)
		{
			Loose(man, mark);
		}
	}

	/// <summary>The enemy a man with nobody in reach goes for: the one he was already going for, while
	/// that one stands and is not crowded, or else the nearest within <see cref="Swarm"/> that is not.</summary>
	private FieldSoldier Quarry(FieldSoldier man)
	{
		// Already counted among those at him, so he keeps him while no more than the most are.
		if (_meeting.GetValueOrDefault(man) is { IsStanding: true } going && going.At.DistanceTo(man.At) <= Swarm
			&& _pressing.GetValueOrDefault(going) <= MostOnOne)
		{
			return going;
		}

		FieldSoldier best = null;
		float bestFar = Swarm;
		foreach (FieldSquad squad in Squads)
		{
			// A whole squad further off than the best so far is passed over without looking at its men.
			if (squad.IsAttacking == man.Squad.IsAttacking
				|| man.At.DistanceTo(squad.At) - squad.Reach - squad.Depth > bestFar)
			{
				continue;
			}

			foreach (FieldSoldier enemy in squad.Soldiers)
			{
				float far = enemy.At.DistanceTo(man.At);
				if (enemy.IsStanding && far < bestFar && _pressing.GetValueOrDefault(enemy) < MostOnOne && CanReach(man, enemy))
				{
					best = enemy;
					bestFar = far;
				}
			}
		}

		return best;
	}

	private void Swing(FieldSoldier man, FieldSoldier foe)
	{
		man.Struck = Clock;
		man.Ready = _balance.FieldSwingSeconds * (1f + _dice.Randf() * Unsteady);
		float arm = man.Squad.Kind.Attack * (man.Squad.Shoots ? ShooterInMelee : 1f);
		if (_dice.Randf() < Sure(arm, foe))
		{
			Hurt(foe, Blow(man.Squad, foe.Squad, arm));
		}
	}

	private void Loose(FieldSoldier man, FieldSquad mark)
	{
		FieldSoldier aim = mark.Soldiers[_dice.RandiRange(0, mark.Soldiers.Count - 1)];
		float far = man.At.DistanceTo(aim.At);
		man.Struck = Clock;
		man.Ready = _balance.FieldReloadSeconds * (1f + _dice.Randf() * Unsteady);
		// He faces the body of men he is shooting into, not the one man in it this arrow is loosed at:
		// turned to each in turn, a line of bowmen swung left and right with every volley.
		man.Facing = (mark.At - man.At).Normalized();

		float sure = Sure(man.Squad.Kind.Attack, aim) * (1f - (LongShot * Mathf.Min(1f, far / man.Squad.Range)));
		bool hit = _dice.Randf() < sure;
		Vector2 down = hit
			? aim.At
			: aim.At + new Vector2(_dice.RandfRange(-Wide, Wide), _dice.RandfRange(-Wide, Wide));
		Arrows.Add(new Arrow(man.At, down, Clock, Mathf.Max(0.3f, far / ArrowSpeed), hit ? aim : null,
			Blow(man.Squad, mark, man.Squad.Kind.Attack) * _balance.FieldShotRate));
	}

	/// <summary>Brings down every arrow whose time has come, on whoever it was loosed at.</summary>
	private void Land()
	{
		for (int i = Arrows.Count - 1; i >= 0; i--)
		{
			Arrow arrow = Arrows[i];
			if (Clock < arrow.Loosed + arrow.Flight)
			{
				continue;
			}

			if (arrow.Target is { IsStanding: true } struck)
			{
				Hurt(struck, arrow.Damage);
			}

			Arrows.RemoveAt(i);
		}
	}

	/// <summary>The chance a blow at this arm lands on this man: his attack against the other's
	/// defence, so a swordsman lands on a peasant most of the time and a peasant on a spearman
	/// seldom.</summary>
	private static float Sure(float arm, FieldSoldier on) =>
		arm / Mathf.Max(0.1f, arm + on.Squad.Kind.Defence);

	/// <summary>What one blow that lands takes off a man: armour turns the rest of it, so a blow
	/// takes less off the better-defended — by exactly as much as makes the chance of landing it and
	/// the harm it does, together, the captain's attack over defence (<see cref="Battle"/>). A man is
	/// worth to another here what he is worth to him in the reckoning. The attacker has his legs to
	/// answer for; the defender his own ground to stand on.</summary>
	private float Blow(FieldSquad from, FieldSquad on, float arm)
	{
		float guard = Mathf.Max(1, on.Kind.Defence);
		return _balance.FieldHitDamage * (arm + guard) / (2f * guard)
			* (from.IsAttacking ? _vigour : 1f) / (on.IsAttacking ? 1f : _ground);
	}

	/// <summary>Takes health off a man, and men off his file as it runs out.</summary>
	private void Hurt(FieldSoldier man, float damage)
	{
		// On the walls a defender is that much harder to kill, behind the parapet as the captain's
		// reckoning has it — and only there: down in the bailey he is a man like any other (the user's
		// call). An attacker under the walls, in a breach or on a ladder, is that much easier.
		if (Wall != null)
		{
			damage = !man.Squad.IsAttacking && Wall.OnWalls(man.At) ? damage / Wall.Stone
				: man.Squad.IsAttacking && Wall.UnderIt(man.At) ? damage / Mathf.Max(0.05f, Wall.Exposure)
				: damage;
		}

		man.Health = Mathf.Max(0f, man.Health - damage);
		// A man's health is his file's, a man's worth to each of them — but never more men than the
		// file has: the captain is one man with more than one man's health, and he falls only when
		// all of it is gone.
		int left = Mathf.Min(man.Men, Mathf.CeilToInt(man.Health / _balance.FieldManHealth - 0.0001f));
		man.Squad.Fallen += man.Men - left;
		man.Men = left;
	}

}
