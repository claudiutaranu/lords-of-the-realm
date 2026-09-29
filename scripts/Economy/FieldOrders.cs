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
			int ranked = squad.Soldiers.Count - (squad.Captain is { IsStanding: true } ? 1 : 0);
			int files = Mathf.Clamp(wide, 1, Mathf.Max(1, ranked));
			for (int slot = 0; slot < ranked; slot++)
			{
				float across = ((slot % files) - ((files - 1) / 2f)) * squad.Gap;
				spots.Add(front + (right * across) - (facing * ((slot / files) * squad.Gap)));
			}

			if (squad.Captain is { IsStanding: true })
			{
				spots.Add(front - (facing * ((Mathf.CeilToInt(ranked / (float)files) * squad.Gap) + 1f)));
			}
		}

		return spots;
	}

	/// <summary>How a front drawn from one point to another is shared out: each squad, in the order
	/// they already stand along it, gets an equal length of it — as many men wide as fit — and the
	/// middle of its share is where its front rank stands.</summary>
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
		for (int i = 0; i < order.Count; i++)
		{
			yield return (order[i], Mathf.Max(1, Mathf.FloorToInt((each - SquadSpacing) / order[i].Gap) + 1),
				from + (along * (each * (i + 0.5f))));
		}
	}

	public void Charge(IReadOnlyList<FieldSquad> squads, FieldSquad foe)
	{
		foreach (FieldSquad squad in squads)
		{
			squad.Goal = null;
			squad.KeepsFront = false;
			squad.Target = foe;
		}
	}

	public void Hold(IReadOnlyList<FieldSquad> squads)
	{
		foreach (FieldSquad squad in squads)
		{
			squad.Goal = null;
			squad.Target = null;
		}
	}

	/// <summary>The lord calls his men off. The day ends where it stands and nobody has won: what
	/// fell has fallen, and everyone else walks away under his banner.</summary>
	public void Withdraw()
	{
		IsOver = true;
	}
}
