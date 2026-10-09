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

	/// <summary>The captain's order to an engine for this slice.</summary>
	private void Engineer(FieldSquad engine)
	{
		Vector2? spot = engine.Unit == SiegeEngines.Ram ? RamSpot() : ThrowSpot(engine.At);
		if (spot is not Vector2 there || (engine.Unit == SiegeEngines.Catapult && Wall.InThrow(engine.At) != null))
		{
			engine.Goal = null;
			return;
		}

		if (engine.At.DistanceTo(there) > 1f)
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

	/// <summary>A throw's length off the nearest stretch still standing, on the attacker's side of it.</summary>
	private Vector2? ThrowSpot(Vector2 from)
	{
		FieldWall.Target best = null;
		foreach (FieldWall.Target target in Wall.Battered)
		{
			if (!target.IsDown && target.Gap.Is == FieldWall.Kind.Breach
				&& (best == null || Wall.PointOf(target.Gap).DistanceTo(from) < Wall.PointOf(best.Gap).DistanceTo(from)))
			{
				best = target;
			}
		}

		return best == null ? null : Wall.PointOf(best.Gap) + new Vector2(0f, SiegeEngines.ThrowsTo * ThrowShare);
	}
}
