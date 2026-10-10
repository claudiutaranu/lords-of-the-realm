using Godot;

/// <summary>The besiegers' engines on the field of an assault: each ram and catapult its own company
/// of one, drawn up behind the attacker's line, moved by the lord like any other — or, left to the
/// captain, taken where it can work: a catapult to a throw's length off the nearest stretch of curtain
/// still standing, a ram up to the gate (FieldWall.Engines does the work). An engine strikes nobody,
/// and is struck like a man.</summary>
public sealed partial class FieldBattle
{
	/// <summary>How far behind the attacker's foot the engines are drawn up, and how far apart.</summary>
	private const float EnginesBack = 14f;
	private const float EnginesApart = 12f;

	/// <summary>Where the captain stands a catapult: this share of its longest throw off its stretch.</summary>
	private const float ThrowShare = 0.6f;

	/// <summary>How far out from the gate a ram stands to beat at it.</summary>
	private const float RamStandsOff = 3f;

	/// <summary>How near its spot a catapult in reach settles down to throw.</summary>
	private const float SettlesWithin = 6f;

	private void Engines(Defenders against)
	{
		int count = against.Rams + against.Catapults;
		for (int i = 0; i < count; i++)
		{
			string kind = i < against.Rams ? SiegeEngines.Ram : SiegeEngines.Catapult;
			var engine = new FieldSquad(kind, true, 1, 1, SiegeEngines.EngineHealth * _balance.FieldManHealth)
			{
				At = new Vector2((i - ((count - 1) / 2f)) * EnginesApart, (Gap / 2f) + EnginesBack),
				Facing = Vector2.Up,
			};
			engine.Soldiers[0].At = engine.At;
			engine.Soldiers[0].Was = engine.At;
			engine.Soldiers[0].Facing = engine.Facing;
			engine.Was = engine.At;
			Squads.Add(engine);
		}
	}

	/// <summary>Where an engine the lord sends somewhere goes: anywhere he likes in the open, but sent
	/// at the walls — or into them — a catapult stops a throw off the nearest stretch, never at the
	/// stone where it could throw at nothing, and a ram goes to the gate.</summary>
	private Vector2 Workable(FieldSquad squad, Vector2 to)
	{
		if (Wall == null || !squad.Kind.IsEngine)
		{
			return to;
		}

		bool atTheWalls = Wall.IsInside(to) || Wall.UnderIt(to);
		if (squad.Unit == SiegeEngines.Ram)
		{
			return atTheWalls && RamSpot() is Vector2 gate ? gate : to;
		}

		bool tooNear = NearestStretch(to, squad.At) is FieldWall.Target near && Wall.PointOf(near.Gap).DistanceTo(to) < SiegeEngines.ThrowsFrom;
		return (atTheWalls || tooNear) && ThrowSpot(to, squad.At) is Vector2 spot ? spot : to;
	}

	/// <summary>The stretch still standing nearest a spot, of the faces the engine is outside of: sent
	/// into the castle, a catapult was taken round to the far side of it, to the stretch nearest the
	/// middle.</summary>
	private FieldWall.Target NearestStretch(Vector2 from, Vector2 engine)
	{
		FieldWall.Target best = null;
		foreach (FieldWall.Target target in Wall.Battered)
		{
			bool isFacing = FieldWall.Outward(target.Gap.On).Dot(engine - Wall.PointOf(target.Gap)) > 0f;
			if (!target.IsDown && target.Gap.Is == FieldWall.Kind.Breach && isFacing
				&& (best == null || Wall.PointOf(target.Gap).DistanceTo(from) < Wall.PointOf(best.Gap).DistanceTo(from)))
			{
				best = target;
			}
		}

		return best;
	}

	/// <summary>The captain's order to an engine for this slice.</summary>
	private void Engineer(FieldSquad engine)
	{
		// Where the engine itself stands, which is where it works from (FieldWall.Pound).
		Vector2 at = engine.Soldiers[0].At;
		Vector2? spot = engine.Unit == SiegeEngines.Ram ? RamSpot() : ThrowSpot(at, at);
		if (spot is not Vector2 there)
		{
			engine.Goal = null;
			return;
		}

		// A catapult already throwing stays put; one only just in reach goes on to its spot, or it
		// stopped at the very edge of its throw and a step to either side took it out again.
		if (engine.Unit == SiegeEngines.Catapult && Wall.InThrow(at) != null && at.DistanceTo(there) < SettlesWithin)
		{
			engine.Goal = null;
			return;
		}

		if (at.DistanceTo(there) > 1f)
		{
			engine.Goal = there;
			engine.FaceGoal = (Wall.Middle - there).Normalized();
		}
	}

	private Vector2? RamSpot()
	{
		FieldWall.Target gate = Wall.Battered.Find(target => !target.IsDown && target.Gap.Is == FieldWall.Kind.Gate);
		return gate == null ? null : Wall.PointOf(gate.Gap) + (Vector2.Down * RamStandsOff);
	}

	/// <summary>A throw's length out from the nearest stretch still standing, outside the face it is on.</summary>
	private Vector2? ThrowSpot(Vector2 from, Vector2 engine)
	{
		FieldWall.Target best = NearestStretch(from, engine);
		return best == null ? null : Wall.PointOf(best.Gap) + (FieldWall.Outward(best.Gap.On) * SiegeEngines.ThrowsTo * ThrowShare);
	}
}
