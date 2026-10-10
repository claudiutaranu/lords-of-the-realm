using System.Collections.Generic;
using Godot;

/// <summary>How the castle's captain holds his walls in an assault (FieldWall): every company has its
/// post — the bows along the walkway of the face the enemy comes at, a company of foot on the walkway
/// at the head of each ladder, and whatever foot is left in the bailey behind the gate as his reserve.
/// They stay on their posts and let the enemy come to them: the bows shoot from the walls, the men at
/// the ladders fight whoever climbs them. Only an enemy inside the walls — over a ladder, through the
/// gate or a breach once the engines have opened them — is gone for, by the reserve and by any post
/// near enough. A man on the walkway and a man on the ground outside cannot reach each other with a
/// blade (the user's call): only arrows pass between them, and the wall's stone guards only the men
/// standing on it.</summary>
public sealed partial class FieldBattle
{
	/// <summary>How near an enemy inside the walls has to be for a company on a post to leave it.</summary>
	private const float LeavesPostWithin = 22f;

	/// <summary>How far from its post a company stands before it is walked back to it.</summary>
	private const float OffPost = 2f;

	/// <summary>How far in behind the gate the reserve waits.</summary>
	private const float ReserveBack = 10f;
	private const int WalkwayRanks = 2;

	private readonly Dictionary<FieldSquad, (Vector2 At, Vector2 Facing)> _posts = new();
	private readonly HashSet<FieldSquad> _reserve = new();

	/// <summary>Gives the castle's men their posts and stands them on them as the day begins.</summary>
	private void Garrison()
	{
		var foot = Squads.FindAll(squad => !squad.IsAttacking && !squad.Shoots);
		var bows = Squads.FindAll(squad => !squad.IsAttacking && squad.Shoots);
		float side = Wall.Half * 2f;

		// The bows on the two towers at the corners of the face the enemy comes at, first, and then
		// spread along its walkway, clear of the gate's span in its middle.
		var towers = new HashSet<FieldSquad>();
		for (int i = 0; i < Mathf.Min(2, bows.Count); i++)
		{
			bows[i].Reform(Mathf.CeilToInt(bows[i].Soldiers.Count / (float)WalkwayRanks));
			_posts[bows[i]] = Wall.TowerPost(FieldWall.Face.South, i == 0, bows[i].Files * bows[i].Gap);
			towers.Add(bows[i]);
		}

		var walkway = bows.GetRange(towers.Count, bows.Count - towers.Count);
		for (int i = 0; i < walkway.Count; i++)
		{
			float along = side * (i + 0.5f) / walkway.Count;
			if (Mathf.Abs(along - Wall.Half) < 5f)
			{
				along += 6f;
			}

			_posts[walkway[i]] = Wall.Post(FieldWall.Face.South, along);
		}

		// A company at the head of each ladder, the south face's first, and the rest in reserve.
		var ladders = Wall.Openings.FindAll(gap => gap.Is == FieldWall.Kind.Ladder);
		ladders.Sort((a, b) => (a.On == FieldWall.Face.South ? 0 : 1).CompareTo(b.On == FieldWall.Face.South ? 0 : 1));
		var gate = new FieldWall.Opening(FieldWall.Face.South, Wall.Half - 1f, Wall.Half + 1f, FieldWall.Kind.Gate);
		for (int i = 0; i < foot.Count; i++)
		{
			if (i < ladders.Count)
			{
				_posts[foot[i]] = Wall.Post(ladders[i].On, ladders[i].Middle);
				continue;
			}

			_reserve.Add(foot[i]);
			_posts[foot[i]] = (Wall.Behind(gate, ReserveBack) + new Vector2((i - ladders.Count) * 7f, 0f), Vector2.Down);
		}

		foreach ((FieldSquad squad, (Vector2 at, Vector2 facing)) in _posts)
		{
			squad.At = at;
			squad.Facing = facing;
			// On the walkway two ranks deep, strung out along it, which is all the stone holds — the
			// companies at the corners standing their end files on the towers (the user's call).
			if (!_reserve.Contains(squad))
			{
				squad.Reform(Mathf.CeilToInt(squad.Soldiers.Count / (float)WalkwayRanks));
			}
		}
	}

	/// <summary>The castle's captain's order to one company for this slice.</summary>
	private void Defend(FieldSquad squad)
	{
		if (squad.IsFighting && squad.InMelee != null && !squad.Shoots)
		{
			squad.Target = squad.InMelee;
			squad.Goal = null;
			return;
		}

		// The nearest enemy who has got inside the walls, if any.
		FieldSquad intruder = null;
		float nearest = float.MaxValue;
		foreach (FieldSquad enemy in Squads)
		{
			if (enemy.IsAttacking && enemy.IsStanding && Wall.IsInside(enemy.At) && enemy.At.DistanceTo(squad.At) < nearest)
			{
				intruder = enemy;
				nearest = enemy.At.DistanceTo(squad.At);
			}
		}

		if (!squad.Shoots && intruder != null && (_reserve.Contains(squad) || nearest < LeavesPostWithin))
		{
			squad.Target = intruder;
			squad.Goal = null;
			return;
		}

		// Otherwise on his post, facing out over the wall, and let them come.
		squad.Target = null;
		if (!_posts.TryGetValue(squad, out (Vector2 At, Vector2 Facing) post))
		{
			return;
		}

		if (squad.At.DistanceTo(post.At) > OffPost)
		{
			squad.Goal = post.At;
			squad.FaceGoal = post.Facing;
		}
		else if (squad.Goal == null && !squad.IsFighting)
		{
			squad.Facing = post.Facing;
		}
	}

	/// <summary>Whether one man can reach another with a blade across the wall: not between the walkway
	/// and the ground outside it, unless the one outside is on a ladder.</summary>
	private bool CanReach(FieldSoldier one, FieldSoldier other) =>
		Wall == null || !(Apart(one, other) || Apart(other, one));

	private bool Apart(FieldSoldier up, FieldSoldier down) =>
		Wall.OnWalls(up.At) && !Wall.IsInside(down.At) && !Wall.Climbing(down.At);
}
