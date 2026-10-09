using System.Collections.Generic;
using Godot;

/// <summary>The men on their feet: where each is, whom he can reach, and how they keep out of each
/// other's way. Every man is looked up through a grid of the field rather than against every other
/// man, which is what lets a few hundred of them fight ten times a second.</summary>
public sealed partial class FieldBattle
{
	/// <summary>A man who fell: who, where, facing which way, and of which squad. The screen lays him
	/// down there, lets him go a few seconds later, and forgets how he walked.</summary>
	public readonly record struct Fall(FieldSoldier Man, Vector2 At, Vector2 Facing, FieldSquad Squad);

	private const float Cell = 4f;

	/// <summary>How much faster than his squad's pace a man hurries to his place.</summary>
	private const float Hurry = 1.3f;

	/// <summary>The men of each side by where they stood as the slice began, the attackers' and the
	/// defenders' apart: a man looking for an enemy has no business reading through his own side,
	/// which in a press is half of everybody near him.</summary>
	private readonly Dictionary<Vector2I, List<FieldSoldier>> _attackers = new();
	private readonly Dictionary<Vector2I, List<FieldSoldier>> _defenders = new();

	/// <summary>How far a man can have walked from the cell he was put in since the slice began, with
	/// room to spare: a cell is passed over only when nobody in it could be nearer than the best found.</summary>
	private const float MovedSince = 1f;

	/// <summary>How much over the best's square a man's may be and still be measured: far more than a
	/// square and a root can round apart.</summary>
	private const float Rounding = 1e-4f;

	/// <summary>How many men are at each man this slice, and whom each man with nobody in reach is
	/// going for (<see cref="Quarry"/>).</summary>
	private readonly Dictionary<FieldSoldier, int> _pressing = new();
	private readonly Dictionary<FieldSoldier, FieldSoldier> _meeting = new();

	/// <summary>Everyone who has fallen since the screen last asked.</summary>
	public List<Fall> Fell { get; } = new();

	/// <summary>Every man's slice, then the arrows that come down, then the dead.</summary>
	private void Fight()
	{
		Grid();
		foreach (FieldSquad squad in Squads)
		{
			squad.IsFighting = false;
			foreach (FieldSoldier man in squad.Soldiers)
			{
				if (man.IsStanding)
				{
					Act(man);
					squad.IsFighting |= man.Foe != null;
				}
			}
		}

		Land();
		Bury();
	}

	private void Grid()
	{
		foreach (Dictionary<Vector2I, List<FieldSoldier>> side in new[] { _attackers, _defenders })
		{
			foreach (List<FieldSoldier> cell in side.Values)
			{
				cell.Clear();
			}
		}

		// How many are at each man as the slice begins: those fighting him, and those going for him.
		_pressing.Clear();
		foreach (FieldSquad squad in Squads)
		{
			foreach (FieldSoldier man in squad.Soldiers)
			{
				FieldSoldier at = man.Foe ?? _meeting.GetValueOrDefault(man);
				if (at is { IsStanding: true })
				{
					_pressing[at] = _pressing.GetValueOrDefault(at) + 1;
				}
			}
		}

		foreach (FieldSquad squad in Squads)
		{
			foreach (FieldSoldier man in squad.Soldiers)
			{
				man.Was = man.At;
				Vector2I key = CellOf(man.At);
				Dictionary<Vector2I, List<FieldSoldier>> side = squad.IsAttacking ? _attackers : _defenders;
				if (!side.TryGetValue(key, out List<FieldSoldier> cell))
				{
					cell = new List<FieldSoldier>();
					side[key] = cell;
				}

				cell.Add(man);
			}
		}
	}

	private static Vector2I CellOf(Vector2 at) => new(Mathf.FloorToInt(at.X / Cell), Mathf.FloorToInt(at.Y / Cell));

	/// <summary>The nearest man of the other side still standing within <paramref name="within"/>
	/// metres of a spot. Only the other side's grid is read, cell by cell in the same order as ever
	/// so two men equally near are settled the same way, and a cell wholly further off than the best
	/// already found is not opened.</summary>
	private FieldSoldier NearestMan(Vector2 from, bool attacking, float within, FieldSoldier striker = null)
	{
		Dictionary<Vector2I, List<FieldSoldier>> enemies = attacking ? _defenders : _attackers;
		FieldSoldier best = null;
		float bestFar = within;
		int reach = Mathf.CeilToInt(within / Cell);
		Vector2I home = CellOf(from);
		for (int x = -reach; x <= reach; x++)
		{
			for (int y = -reach; y <= reach; y++)
			{
				Vector2I key = home + new Vector2I(x, y);
				float open = bestFar + MovedSince;
				if (OffCell(from, key) > open * open || !enemies.TryGetValue(key, out List<FieldSoldier> cell))
				{
					continue;
				}

				// The square of the distance first, which needs no root: only a man who might be nearer
				// than the best is measured properly, and he is measured exactly as he always was, so a
				// tie still goes the same way. The hair over is for the rounding between the two.
				float roughly = bestFar * bestFar * (1f + Rounding);
				foreach (FieldSoldier man in cell)
				{
					if (!man.IsStanding || from.DistanceSquaredTo(man.At) > roughly || (striker != null && !CanReach(striker, man)))
					{
						continue;
					}

					float far = from.DistanceTo(man.At);
					if (far < bestFar)
					{
						best = man;
						bestFar = far;
						roughly = bestFar * bestFar * (1f + Rounding);
					}
				}
			}
		}

		return best;
	}

	/// <summary>The square of how far a spot is from the nearest edge of a cell, nought inside it.</summary>
	private static float OffCell(Vector2 from, Vector2I key)
	{
		float dx = Mathf.Max(0f, Mathf.Max((key.X * Cell) - from.X, from.X - ((key.X + 1) * Cell)));
		float dy = Mathf.Max(0f, Mathf.Max((key.Y * Cell) - from.Y, from.Y - ((key.Y + 1) * Cell)));
		return (dx * dx) + (dy * dy);
	}

	/// <summary>Walks a man toward a spot and stops him <paramref name="short"/> of it.</summary>
	private void Walk(FieldSoldier man, Vector2 to, float @short)
	{
		// A wall in the way is gone round: to the nearest gap the man can use, and through it.
		bool mounted = man.Squad.Kind.Mounted;
		// The castle's men hold their walls: sent at anything outside, a man goes to the wall facing
		// it and fights from behind the stone, which is what a garrison is for.
		if (Wall != null && !man.Squad.IsAttacking && Wall.IsInside(man.At))
		{
			to = Wall.Within(to);
		}

		// An engine goes nowhere inside the walls, and no way round them: it is pushed straight at
		// where it is sent, and stops at the stone.
		bool isEngine = man.Squad.Kind.IsEngine;
		if (Wall != null && !isEngine)
		{
			Vector2 next = Wall.Detour(man.At, to, mounted);
			if (next != to)
			{
				to = next;
				@short = 0f;
			}
		}

		Vector2 way = to - man.At;
		float far = way.Length();
		man.IsMoving = far > @short + 0.05f;
		if (man.IsMoving && man.Waiting > 0f)
		{
			// Not yet: he has heard the order, and is a moment taking it up.
			man.Waiting -= Slice;
			man.IsMoving = false;
		}

		if (!man.IsMoving)
		{
			return;
		}

		man.Facing = way / far;
		float pace = man.Squad.Pace * Hurry * man.Stride * (Wall != null && Wall.Climbing(man.At) ? FieldWall.Climb : 1f);
		Vector2 step = man.At + (man.Facing * Mathf.Min(pace * Slice, far - @short));
		man.At = Wall == null ? step
			: isEngine && (Wall.IsInside(step) || Wall.Blocked(man.At, step, true)) ? man.At
			: Wall.Stop(man.At, step, mounted);
	}

	/// <summary>Takes the fallen out of the ranks, closes their files up behind them, and tells the
	/// screen where they lie.</summary>
	private void Bury()
	{
		foreach (FieldSquad squad in Squads)
		{
			// In the order they stood, as each closing-up leaves the files for the next; walked by hand,
			// because a list of the dead made for every squad ten times a second was garbage for nothing.
			for (int i = 0; i < squad.Soldiers.Count; i++)
			{
				FieldSoldier man = squad.Soldiers[i];
				if (man.IsStanding)
				{
					continue;
				}

				Fell.Add(new Fall(man, man.At, man.Facing, squad));
				squad.Soldiers.RemoveAt(i--);
				_meeting.Remove(man);
				squad.CloseUp(man);
			}
		}
	}
}
