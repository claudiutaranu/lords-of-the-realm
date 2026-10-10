using System.Collections.Generic;
using Godot;

/// <summary>An assault fought by hand on the walls: the flag in the bailey, the wall that stands whole
/// until the engines bring it down, and the stone nobody walks through.</summary>
public partial class BattleCheck
{
	/// <summary>The flag in the bailey: men round it with no defender near bring it down and take the
	/// castle whoever still stands on the walls; and the castle's reserve stands by it from the first.</summary>
	private void TheFlag(GameBalance b)
	{
		var bowsOnly = new Defenders(Men(("bow", 20)), new Dictionary<string, int>(), "large-castle", 50f, false);
		var assault = new FieldBattle(Men(("sword", 30)), bowsOnly, false, b, atTheWalls: true);
		FieldSquad swords = assault.Squads.Find(squad => squad.IsAttacking);
		foreach (FieldSoldier man in swords.Soldiers)
		{
			man.At = assault.Flag + new Vector2((man.Slot % 5) - 2f, (man.Slot / 5) - 2f);
			man.Was = man.At;
		}

		swords.At = assault.Flag;
		while (!assault.IsOver)
		{
			assault.Step();
		}

		Is("men holding the flag with nobody near it bring it down", assault.IsFlagTaken, true);
		Is("  and the castle is theirs", assault.Result().AttackerWon, true);
		Is("  though its bowmen still stand", assault.Squads.Exists(squad => !squad.IsAttacking && squad.IsStanding), true);

		var garrison = new Defenders(Men(("spear", 120), ("bow", 20)), new Dictionary<string, int>(), "large-castle", 50f, false);
		var held = new FieldBattle(Men(("sword", 60)), garrison, false, b, atTheWalls: true);
		Is("the castle's reserve stands by its flag", held.Squads.Exists(squad => !squad.IsAttacking && squad.At.DistanceTo(held.Flag) < 15f), true);

		// Two companies of foot and as many ladders: one still keeps the flag.
		var few = new Defenders(Men(("spear", 60)), new Dictionary<string, int>(), "large-castle", 50f, false);
		var thin = new FieldBattle(Men(("sword", 60)), few, false, b, atTheWalls: true);
		Is("  however few the garrison", thin.Squads.Exists(squad => !squad.IsAttacking && squad.At.DistanceTo(thin.Flag) < 15f), true);

		// One man over the far corner of the wall does not draw the keepers off the flag.
		FieldSquad over = held.Squads.Find(squad => squad.IsAttacking);
		Vector2 corner = held.Wall.Middle + new Vector2(held.Wall.Half, held.Wall.Half) * 0.85f;
		foreach (FieldSoldier man in over.Soldiers)
		{
			man.At = corner + new Vector2((man.Slot % 6) - 3f, (man.Slot / 6) - 3f);
			man.Was = man.At;
		}

		over.At = corner;
		held.Step();
		Is("  and an enemy over the far wall does not draw its keepers off it",
			held.Squads.Exists(squad => !squad.IsAttacking && squad.At.DistanceTo(held.Flag) < 15f && squad.Target == over), false);
	}

	/// <summary>The wall stands whole until the engines bring it down: no breach and no gate open as
	/// the day begins, both open once their health is spent; and a man on the walkway is out of reach
	/// of a blade on the ground outside, but not of one on a ladder.</summary>
	private void WholeWalls(GameBalance b)
	{
		// A garrison big enough to keep its flag while the engines work: forty spears lost it to the first
		// swords over a ladder, and the day ended before the ram was at the gate.
		var against = new Defenders(Men(("spear", 90)), new Dictionary<string, int>(), "large-castle", 50f, false, 1, 1);
		var assault = new FieldBattle(Men(("sword", 60)), against, false, b, atTheWalls: true);
		FieldWall wall = assault.Wall;
		bool IsOpen(FieldWall.Kind kind) => wall.Openings.Exists(gap => gap.Is == kind);
		Is("the walls stand whole as an assault begins", IsOpen(FieldWall.Kind.Breach) || IsOpen(FieldWall.Kind.Gate), false);
		Is("  and the engines are on the field", assault.Squads.FindAll(squad => squad.Kind.IsEngine).Count, 2);
		wall.Pound(120f, assault.Squads);
		Is("  where they were drawn up, out of reach, they bring nothing down", IsOpen(FieldWall.Kind.Breach) || IsOpen(FieldWall.Kind.Gate), false);
		FieldSquad catapult = assault.Squads.Find(squad => squad.Unit == SiegeEngines.Catapult);
		assault.March(new[] { catapult }, wall.Middle + new Vector2(0f, wall.Half + 40f));
		Is("  a catapult only marched, even into its throw, is not set to work", catapult.IsBattering, false);
		assault.March(new[] { catapult }, wall.Middle);
		Is("  sent at the walls, it is", catapult.IsBattering, true);
		var palisade = new FieldBattle(Men(("sword", 60)), new Defenders(Men(("spear", 40)), new Dictionary<string, int>(),
			"small-palisade", 50f, false, 0, 1), false, b, atTheWalls: true);
		Is("  and every face of the smallest wall has a stretch to throw at", System.Array.TrueForAll(FieldWall.Faces,
			face => palisade.Wall.Battered.Exists(target => target.Gap.On == face && target.Gap.Is == FieldWall.Kind.Breach)), true);
		assault.IsAttackCaptained = true;
		while (!assault.IsOver && !(IsOpen(FieldWall.Kind.Breach) && IsOpen(FieldWall.Kind.Gate)))
		{
			assault.Step();
		}

		Is("  taken up to the walls by the captain, the catapult opens a breach", IsOpen(FieldWall.Kind.Breach), true);
		Is("  and the ram the gate", IsOpen(FieldWall.Kind.Gate), true);

		// The castle's bows shoot first at an engine at work: left to the nearest company, they only
		// ever shot at the men round the ladders, and no engine was so much as scratched.
		var shot = new FieldBattle(Men(("sword", 60)), new Defenders(Men(("spear", 90), ("bow", 40)),
			new Dictionary<string, int>(), "large-castle", 50f, false, 1, 2), false, b, atTheWalls: true) { IsAttackCaptained = true };
		for (int slice = 0; slice < 900 && !shot.IsOver; slice++)
		{
			shot.Step();
		}

		Is("the castle's bows wound the engines at work", shot.Squads.Exists(squad => squad.Kind.IsEngine
			&& (!squad.IsStanding || squad.Soldiers[0].Health < squad.Soldiers[0].MostHealth)), true);

		FieldWall.Opening ladder = wall.Openings.Find(gap => gap.Is == FieldWall.Kind.Ladder);
		(Vector2 post, Vector2 outward) = wall.Post(ladder.On, ladder.Middle);
		Is("a company posted at a ladder stands on the walls", wall.OnWalls(post), true);
		Is("  and is out of the bailey", wall.OnWalls(wall.Middle), false);
		Is("  and the man at the ladder's foot is on it", wall.Climbing(wall.Behind(ladder, -1.2f)), true);
		Is("  but one a few paces back is on the ground", wall.Climbing(wall.Behind(ladder, -6f)), false);
	}

	/// <summary>An assault by hand: in every slice of it no man's step goes through stone, and horse
	/// gets in by no ladder.</summary>
	private void ThroughTheWalls(GameBalance b)
	{
		foreach ((string fort, int rams, int catapults) in new[] { ("large-castle", 1, 2), ("small-palisade", 0, 0) })
		{
			var against = new Defenders(Men(("spear", 40), ("bow", 20)), new Dictionary<string, int>(), fort, 50f, false,
				rams, catapults);
			var assault = new FieldBattle(Men(("sword", 120), ("horse", 30)), against, false, b, atTheWalls: true);
			assault.IsAttackCaptained = true;
			int through = 0;
			int horseIn = 0;
			while (!assault.IsOver)
			{
				var was = new Dictionary<FieldSoldier, Vector2>();
				foreach (FieldSquad squad in assault.Squads)
				{
					foreach (FieldSoldier man in squad.Soldiers)
					{
						was[man] = man.At;
					}
				}

				assault.Step();
				foreach (FieldSquad squad in assault.Squads)
				{
					foreach (FieldSoldier man in squad.Soldiers)
					{
						through += was.TryGetValue(man, out Vector2 from) && assault.Wall.Blocked(from, man.At, squad.Kind.Mounted) ? 1 : 0;
						horseIn += rams == 0 && catapults == 0 && squad.Kind.Mounted && squad.IsAttacking
							&& assault.Wall.IsInside(man.At) ? 1 : 0;
					}
				}
			}

			Is($"at the walls of a {fort}, nobody steps through stone", through, 0);
			Is($"  and no horse comes in by a ladder", horseIn, 0);
		}
	}
}
