using System.Collections.Generic;
using Godot;

/// <summary>The captain on a field the lord is fighting himself: he has the defence always, and the
/// attack whenever the lord hands it to him — and both, when the check fights a day with nobody
/// watching.</summary>
public sealed partial class FieldBattle
{
	/// <summary>How near a defending captain lets the enemy come before his foot go out to meet
	/// them. Closer than a bowshot on purpose: he would rather be shot at from behind his own line
	/// than walk out of it — until he IS being shot at, when standing still is dying for nothing.</summary>
	private const float HoldUntil = 55f;

	/// <summary>How near an attacker's bows count as keeping up behind their foot.</summary>
	private const float Straggle = 2f;

	/// <summary>What the captain does with one squad this slice. He goes for the nearest; he sends
	/// his bows to where they can shoot and no further; and holding the ground he stands on, he
	/// lets the enemy come to him — until they are close, or until they are shooting at him.</summary>
	private void Captain(FieldSquad squad)
	{
		// Whatever the lord placed, the captain has it now, and moves it as he sees fit.
		squad.IsPlaced = false;
		if (Wall != null && !squad.IsAttacking)
		{
			Defend(squad);
			return;
		}

		if (!(squad.IsFighting && squad.InMelee != null) && Storm(squad))
		{
			return;
		}

		if (squad.Kind.IsEngine)
		{
			Engineer(squad);
			return;
		}

		// Whoever his men are already fighting is whom he is fighting.
		if (squad.IsFighting && squad.InMelee != null && !squad.Shoots)
		{
			squad.Target = squad.InMelee;
			squad.Goal = null;
			return;
		}

		if (squad.Target is { IsStanding: true })
		{
			return;
		}

		FieldSquad nearest = Nearest(squad, float.MaxValue);
		if (nearest == null)
		{
			return;
		}

		// Holding, he waits — until the enemy is close, or shooting, or at grips with any of his
		// companies: once the lines have met, a company that stood off on the wing would stand there
		// all day while the enemy beat the rest one at a time.
		if (!squad.IsAttacking && !squad.Shoots && !UnderFire(false) && !Joined(false)
			&& nearest.At.DistanceTo(squad.At) > HoldUntil)
		{
			squad.Target = null;
			return;
		}

		if (squad.Shoots && squad.IsAttacking && Screen(squad, nearest) is Vector2 behind)
		{
			// With an enemy in bowshot they stand and loose; with none, they walk on behind the foot
			// — steadily, and not in fits and starts to stop and loose at nothing.
			squad.Goal = Nearest(squad, squad.Range) == null && squad.At.DistanceTo(behind) > Straggle ? behind : null;
			squad.Target = null;
			return;
		}

		// A defender's bows stay where they were put: the ground is his, and they shoot from it.
		squad.Target = squad.Shoots && !squad.IsAttacking ? null : nearest;
		squad.Goal = null;
	}

	/// <summary>Where an attacker's bows should stand: behind the foremost of his own foot, on the
	/// line to the enemy. Faster on their feet than the men with spears, they would otherwise be
	/// the first thing the enemy reached — and a bowman is the last man who should be. Null once
	/// there is no foot left to stand behind.</summary>
	private Vector2? Screen(FieldSquad bows, FieldSquad enemy)
	{
		FieldSquad front = null;
		foreach (FieldSquad squad in Squads)
		{
			if (squad.IsAttacking && !squad.Shoots && squad.IsStanding
				&& (front == null || squad.At.DistanceTo(enemy.At) < front.At.DistanceTo(enemy.At)))
			{
				front = squad;
			}
		}

		if (front == null)
		{
			return null;
		}

		Vector2 toward = (enemy.At - front.At).Normalized();
		var aside = new Vector2(bows.At.X - front.At.X, 0f).LimitLength(front.Reach);
		return front.At - (toward * (front.Depth + RowDepth)) + aside;
	}

	private bool Joined(bool attacking)
	{
		foreach (FieldSquad squad in Squads)
		{
			if (squad.IsAttacking == attacking && squad.IsFighting)
			{
				return true;
			}
		}

		return false;
	}

	private bool UnderFire(bool attacking)
	{
		foreach (FieldSquad squad in Squads)
		{
			if (squad.IsAttacking != attacking && squad.ShootingAt != null)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>The most figures a company is drawn up with.</summary>
	private const int MostInCompany = 30;

	/// <summary>The captain draws his side up before anybody moves: foot in front, horse on the
	/// wings, bows behind.</summary>
	private void DrawUp(Dictionary<string, int> roster, bool attacking)
	{
		var keys = new List<string>(roster.Keys);
		keys.Sort(System.StringComparer.Ordinal);
		var foot = new List<FieldSquad>();
		var bows = new List<FieldSquad>();
		foreach (string unit in keys)
		{
			// A kind is drawn up in companies of no more than MostInCompany figures, as even as they
			// will go: seventy spearmen are three companies of twenty-three or twenty-four, each with
			// its own card, to be sent where the lord wants them — not one block.
			int men = roster[unit];
			int companies = Mathf.CeilToInt(men / (float)(MostInCompany * _menPerFigure));
			for (int company = 0; company < companies; company++)
			{
				int these = (men / companies) + (company < men % companies ? 1 : 0);
				var squad = new FieldSquad(unit, attacking, these, _menPerFigure, _balance.FieldManHealth);
				(squad.Shoots ? bows : foot).Add(squad);
				Squads.Add(squad);
			}
		}

		// Horse on the wings, where it has room to go round.
		foot.Sort((a, b) => a.Kind.Mounted.CompareTo(b.Kind.Mounted));
		var line = new List<FieldSquad>();
		foreach (FieldSquad squad in foot)
		{
			if (squad.Kind.Mounted && line.Count % 2 == 0)
			{
				line.Insert(0, squad);
			}
			else
			{
				line.Add(squad);
			}
		}

		float side = attacking ? 1f : -1f;
		Stand(line, side * Gap / 2f, side);
		Stand(bows, side * ((Gap / 2f) + RowDepth), side);
	}

	/// <summary>A row of squads shoulder to shoulder across the field, centred on it.</summary>
	private static void Stand(List<FieldSquad> row, float y, float side)
	{
		float wide = 0f;
		foreach (FieldSquad squad in row)
		{
			wide += (squad.Reach * 2f) + SquadSpacing;
		}

		float x = -wide / 2f;
		foreach (FieldSquad squad in row)
		{
			x += squad.Reach + (SquadSpacing / 2f);
			squad.At = new Vector2(x, y);
			squad.Facing = new Vector2(0f, -side);
			x += squad.Reach + (SquadSpacing / 2f);
		}
	}
}
