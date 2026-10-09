using System.Collections.Generic;
using Godot;

/// <summary>The engines' work on a castle's wall in an assault (FieldWall): the gate and the stretches
/// of curtain they can bring down, each with the health it has left, and the slice of the day that
/// wears them down. The wall begins every assault whole, and only an engine standing where it can do
/// its work does any (the user's call): a catapult at a throw's length from a stretch, standing still,
/// outside the walls; a ram up against the gate. They are moved on the field like any company.</summary>
public sealed partial class FieldWall
{
	/// <summary>A stretch of the wall the engines can bring down: the gate, or a length of curtain. It
	/// stands until its health is spent, and then it is open.</summary>
	public sealed class Target
	{
		public Opening Gap { get; init; }
		public float Full { get; init; }
		public float Health { get; set; }
		public bool IsDown => Health <= 0f;
	}

	public List<Target> Battered { get; } = new();

	/// <summary>Counts every opening the engines have made, so what is drawn of the wall knows to change.</summary>
	public int Version { get; private set; }

	/// <summary>How many seconds of one engine's work bring a stretch down: a length of stone curtain
	/// under a catapult, the gate under a ram; timber goes in half the time. [I] — the original's
	/// engines did their work over the seasons of a siege, not inside the day.</summary>
	private const float CurtainSeconds = 60f;
	private const float GateSeconds = 40f;
	private const float TimberShare = 0.5f;

	private void Batter(Opening gap, float seconds) =>
		Battered.Add(new Target { Gap = gap, Full = seconds, Health = seconds });

	/// <summary>Where a gap is, on the wall's line.</summary>
	public Vector2 PointOf(Opening gap)
	{
		(Vector2 from, Vector2 to) = Ends(gap.On);
		return from + ((to - from).Normalized() * gap.Middle);
	}

	/// <summary>The stretch a catapult standing here would throw at: the nearest still standing within
	/// its throw, or none.</summary>
	public Target InThrow(Vector2 at)
	{
		Target best = null;
		float bestFar = SiegeEngines.ThrowsTo;
		foreach (Target target in Battered)
		{
			float far = PointOf(target.Gap).DistanceTo(at);
			if (!target.IsDown && target.Gap.Is == Kind.Breach && far >= SiegeEngines.ThrowsFrom && far <= bestFar && !IsInside(at))
			{
				best = target;
				bestFar = far;
			}
		}

		return best;
	}

	/// <summary>A slice of the engines' work: every engine where it can work beats at its stretch, and
	/// a stretch that comes down is open.</summary>
	public void Pound(float seconds, IEnumerable<FieldSquad> squads)
	{
		foreach (FieldSquad engine in squads)
		{
			if (!engine.Kind.IsEngine || !engine.IsStanding || engine.IsMoving)
			{
				continue;
			}

			// Where the engine itself stands, not its company's middle, which a march may carry past it.
			Vector2 at = engine.Soldiers[0].At;
			Target target = engine.Unit == SiegeEngines.Ram ? AtTheGate(at) : InThrow(at);
			if (target == null)
			{
				continue;
			}

			target.Health -= seconds;
			if (target.IsDown)
			{
				Openings.Add(target.Gap);
				Version++;
			}
		}
	}

	private Target AtTheGate(Vector2 at) => Battered.Find(target => !target.IsDown && target.Gap.Is == Kind.Gate
		&& PointOf(target.Gap).DistanceTo(at) <= SiegeEngines.RamsWithin && !IsInside(at));
}
