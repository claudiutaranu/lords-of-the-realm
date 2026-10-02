using System.Collections.Generic;
using Godot;

/// <summary>The lord's orders on the field: march, draw up on a front, fall on an enemy, stand, or
/// call the day off.</summary>
public sealed partial class FieldBattle
{
	/// <summary>Sends squads to a spot on the field, keeping the shape they stand in.</summary>
	public void March(IReadOnlyList<FieldSquad> squads, Vector2 to)
	{
		if (squads.Count == 0)
		{
			return;
		}

		Vector2 middle = Vector2.Zero;
		foreach (FieldSquad squad in squads)
		{
			middle += squad.At;
		}

		middle /= squads.Count;
		foreach (FieldSquad squad in squads)
		{
			squad.Target = null;
			squad.FaceGoal = null;
			squad.KeepsFront = false;
			squad.IsPlaced = true;
			squad.Goal = to + (squad.At - middle);
		}
	}

	/// <summary>Draws squads up along a line the lord drew on the field, from one end to the other,
	/// facing <paramref name="facing"/>: side by side in the order they already stand along it,
	/// each as wide as its share of the line — so a long line is a thin one and a short one deep.</summary>
	public void Form(IReadOnlyList<FieldSquad> squads, Vector2 from, Vector2 to, Vector2 facing)
	{
		foreach ((FieldSquad squad, int files, Vector2 front) in Plan(squads, from, to))
		{
			squad.Reform(files);
			squad.Target = null;
			squad.KeepsFront = false;
			squad.IsPlaced = true;
			squad.FaceGoal = facing;
			squad.Goal = front - (facing * squad.Front);
			if (squad.Goal.Value.DistanceTo(squad.At) <= Arrived)
			{
				squad.Facing = facing;
				squad.FaceGoal = null;
				squad.KeepsFront = true;
			}
		}
	}

	/// <summary>Where every man of these squads would stand on a front drawn from one point to
	/// another, and the middle of each squad's front — what the lord is shown while he is still
	/// drawing it.</summary>
	public List<Vector2> Planned(IReadOnlyList<FieldSquad> squads, Vector2 from, Vector2 to, Vector2 facing,
		out List<Vector2> fronts)
	{
		var spots = new List<Vector2>();
		fronts = new List<Vector2>();
		var right = new Vector2(-facing.Y, facing.X);
		foreach ((FieldSquad squad, int wide, Vector2 front) in Plan(squads, from, to))
		{
			fronts.Add(front);
			for (int slot = 0; slot < squad.Soldiers.Count; slot++)
			{
				float across = ((slot % wide) - ((wide - 1) / 2f)) * squad.Gap;
				spots.Add(front + (right * across) - (facing * ((slot / wide) * squad.Gap)));
			}
		}

		return spots;
	}

	/// <summary>How a front drawn from one point to another is shared out: each squad, in the order
	/// they already stand along it, gets an equal length of it — as many men wide as fit, and never
	/// wider than all its men in one rank — and they stand side by side from where the line began.
	///
	/// From its start, and not each in the middle of its share: centred, a squad already one rank wide
	/// slid along after the cursor as the lord dragged on, and the front he had begun to lay down at
	/// a spot of his choosing would not stay there.</summary>
	private static IEnumerable<(FieldSquad Squad, int Files, Vector2 Front)> Plan(IReadOnlyList<FieldSquad> squads,
		Vector2 from, Vector2 to)
	{
		if (squads.Count == 0)
		{
			yield break;
		}

		Vector2 along = (to - from).Normalized();
		var order = new List<FieldSquad>(squads);
		order.Sort((a, b) => a.At.Dot(along).CompareTo(b.At.Dot(along)));
		float each = from.DistanceTo(to) / order.Count;
		float laid = 0f;
		foreach (FieldSquad squad in order)
		{
			int files = Mathf.Clamp(Mathf.FloorToInt((each - SquadSpacing) / squad.Gap) + 1, 1,
				Mathf.Max(1, squad.Soldiers.Count));
			float wide = (files - 1) * squad.Gap;
			yield return (squad, files, from + (along * (laid + (wide / 2f))));
			laid += wide + SquadSpacing;
		}
	}

	public void Charge(IReadOnlyList<FieldSquad> squads, FieldSquad foe)
	{
		foreach (FieldSquad squad in squads)
		{
			squad.Goal = null;
			squad.KeepsFront = false;
			squad.IsPlaced = false;
			squad.Target = foe;
		}
	}

	public void Hold(IReadOnlyList<FieldSquad> squads)
	{
		foreach (FieldSquad squad in squads)
		{
			squad.Goal = null;
			squad.Target = null;
			squad.IsPlaced = true;
		}
	}

	/// <summary>The lord calls his men off. The day ends where it stands and nobody has won: what
	/// fell has fallen, and everyone else walks away under his banner.</summary>
	public void Withdraw()
	{
		IsOver = true;
	}
}
