using System.Collections.Generic;
using Godot;

/// <summary>The men on their feet: where each is, whom he can reach, and how they keep out of each
/// other's way. Every man is looked up through a grid of the field rather than against every other
/// man, which is what lets a few hundred of them fight ten times a second.</summary>
public sealed partial class FieldBattle
{
	/// <summary>A man who fell: where, facing which way, and of which squad. The screen lays him down
	/// there, and lets him go a few seconds later.</summary>
	public readonly record struct Fall(Vector2 At, Vector2 Facing, FieldSquad Squad);

	private const float Cell = 4f;

	/// <summary>How much faster than his squad's pace a man hurries to his place.</summary>
	private const float Hurry = 1.3f;

	private readonly Dictionary<Vector2I, List<FieldSoldier>> _grid = new();

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
		foreach (List<FieldSoldier> cell in _grid.Values)
		{
			cell.Clear();
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
				if (!_grid.TryGetValue(key, out List<FieldSoldier> cell))
				{
					cell = new List<FieldSoldier>();
					_grid[key] = cell;
				}

				cell.Add(man);
			}
		}
	}

	private static Vector2I CellOf(Vector2 at) => new(Mathf.FloorToInt(at.X / Cell), Mathf.FloorToInt(at.Y / Cell));

	/// <summary>The nearest man of the other side still standing within <paramref name="within"/>
	/// metres of a spot.</summary>
	private FieldSoldier NearestMan(Vector2 from, bool attacking, float within)
	{
		FieldSoldier best = null;
		float bestFar = within;
		int reach = Mathf.CeilToInt(within / Cell);
		Vector2I home = CellOf(from);
		for (int x = -reach; x <= reach; x++)
		{
			for (int y = -reach; y <= reach; y++)
			{
				if (!_grid.TryGetValue(home + new Vector2I(x, y), out List<FieldSoldier> cell))
				{
					continue;
				}

				foreach (FieldSoldier man in cell)
				{
					float far = from.DistanceTo(man.At);
					if (man.Squad.IsAttacking != attacking && man.IsStanding && far < bestFar)
					{
						best = man;
						bestFar = far;
					}
				}
			}
		}

		return best;
	}

	/// <summary>Walks a man toward a spot and stops him <paramref name="short"/> of it.</summary>
	private static void Walk(FieldSoldier man, Vector2 to, float @short)
	{
		Vector2 way = to - man.At;
		float far = way.Length();
		man.IsMoving = far > @short + 0.05f;
		if (!man.IsMoving)
		{
			return;
		}

		man.Facing = way / far;
		man.At += man.Facing * Mathf.Min(man.Squad.Pace * Hurry * Slice, far - @short);
	}

	/// <summary>Takes the fallen out of the ranks, closes their files up behind them, and tells the
	/// screen where they lie.</summary>
	private void Bury()
	{
		foreach (FieldSquad squad in Squads)
		{
			foreach (FieldSoldier man in squad.Soldiers.FindAll(man => !man.IsStanding))
			{
				Fell.Add(new Fall(man.At, man.Facing, squad));
				squad.Soldiers.Remove(man);
				_meeting.Remove(man);
				squad.CloseUp(man);
			}
		}
	}
}
