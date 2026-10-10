using System.Linq;
using System.Collections.Generic;
using Godot;

/// <summary>The field the lord fights himself. It is fought man against man and a lord who fights
/// it well does better than his captain would — that is the point of fighting it. What is held here
/// is the field itself: with the captain giving the orders on BOTH sides, the day goes the way the
/// captain's reckoning says wherever the reckoning is not in doubt. A field where the stronger
/// army loses when nobody is being clever is a field that is wrong, not one the lord is good at.</summary>
public partial class BattleCheck
{
	/// <summary>How many days of each shape are fought on the field, each on its own dice.</summary>
	private const int Seeds = 9;

	/// <summary>How many of <see cref="Seeds"/> days the attacker carries, with both captains giving
	/// the orders.</summary>
	private static int Days(Dictionary<string, int> attack, Defenders against, GameBalance b)
	{
		int won = 0;
		int ours = 0;
		int theirs = 0;
		for (ulong seed = 1; seed <= Seeds; seed++)
		{
			Battle.Result day = new FieldBattle(attack, against, false, b, seed).Fought();
			won += day.AttackerWon ? 1 : 0;
			ours += day.AttackerFell;
			theirs += day.DefenderFell;
		}

		GD.Print($"	   by hand: {won} of {Seeds} — we lose {ours / Seeds}, they lose {theirs / Seeds}");
		return won;
	}

	/// <summary>A squad turned about takes the places nearest where its men stand: the rear rank is
	/// the new front and every man turns where he is, rather than the block swinging round its middle
	/// with each man running through the others to the far side of it.</summary>
	private void AboutFace(GameBalance b)
	{
		var field = new FieldBattle(Men(("peasant", 30)),
			new Defenders(Men(("spear", 10)), new Dictionary<string, int>(), "", 50f), false, b);
		FieldSquad squad = field.Squads.Find(each => each.IsAttacking);
		foreach (FieldSoldier man in squad.Soldiers)
		{
			man.At = squad.Place(man.Slot);
		}

		squad.Facing = -squad.Facing;
		float farthest = 0f;
		foreach (FieldSoldier man in squad.Soldiers)
		{
			farthest = Mathf.Max(farthest, man.At.DistanceTo(squad.Place(man.Slot)));
		}

		// A step and a half at most: the short rear rank has to come round to be the full front.
		// Sent once it walks, sent twice quick it runs: the same ten seconds, half the ground.
		var paces = new List<float>();
		foreach (bool running in new[] { false, true })
		{
			var march = new FieldBattle(Men(("peasant", 30)),
				new Defenders(Men(("spear", 10)), new Dictionary<string, int>(), "", 50f), false, b);
			FieldSquad sent = march.Squads.Find(each => each.IsAttacking);
			Vector2 from = sent.At;
			march.March(new[] { sent }, from - (sent.Facing * 200f), running);
			for (int slice = 0; slice < 100; slice++)
			{
				march.Step();
			}

			paces.Add(sent.At.DistanceTo(from));
		}

		Is("a squad marched at a walk covers half the ground of one at the run",
			Mathf.Abs((paces[0] * 2f) - paces[1]) < 0.5f, true);
		Is("turned about, no man of a squad goes further than a step and a half to his new place",
			farthest <= squad.Gap * 1.5f, true);
	}

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

	private void ByHand(GameBalance b)
	{
		Dictionary<string, int> crown = Men(("spear", 25), ("bow", 15));
		Is("by hand, the crown's forty beat a small county's militia", Days(crown, Militia(70), b) > Seeds / 2, true);
		Is("  and a county of three hundred throws them back", Days(crown, Militia(300), b) > Seeds / 2, false);

		// Every shape the captain is sure of, the field agrees with.
		(Dictionary<string, int> Attack, Defenders Against)[] shapes =
		{
			(Men(("sword", 100)), Militia(80)),
			(Men(("spear", 50)), Militia(220)),
			(Men(("horse", 40)), Militia(90)),
			(Men(("bow", 60)), Militia(60)),
			(Men(("peasant", 120)), new Defenders(Men(("sword", 40)), new Dictionary<string, int>(), "", 50f)),
			(Men(("mace", 40), ("crossbow", 30)), new Defenders(Men(("spear", 40), ("bow", 20)),
				new Dictionary<string, int>(), "", 50f)),
			(Men(("sword", 30)), new Defenders(Men(("horse", 30), ("bow", 30)), new Dictionary<string, int>(), "", 50f)),
		};

		foreach ((Dictionary<string, int> attack, Defenders against) in shapes)
		{
			(int won, _, _) = Fought(attack, against, assault: false, marched: false, b);
			int byHand = Days(attack, against, b);
			if (won >= Tries * 8 / 10 || won <= Tries * 2 / 10)
			{
				Is($"  {string.Join("+", attack.Keys)} against {string.Join("+", against.Field.Keys)}: two captains on the field agree with the reckoning",
					byHand > Seeds / 2, won >= Tries / 2);
			}
		}

		// Taken from behind. Swordsmen set down at the enemy's back and sent at him walk up to his
		// rear rank and fight it, and his rear rank fights back.
		var rear = new FieldBattle(Men(("sword", 30)),
			new Defenders(Men(("spear", 40)), new Dictionary<string, int>(), "", 50f), false, b);
		FieldSquad swords = rear.Squads.Find(squad => squad.IsAttacking);
		FieldSquad spears = rear.Squads.Find(squad => !squad.IsAttacking);
		swords.At = spears.At - (spears.Facing * 25f);
		swords.Facing = spears.Facing;
		foreach (FieldSoldier man in swords.Soldiers)
		{
			man.At = swords.Place(man.Slot);
		}

		rear.Charge(new[] { swords }, spears);
		for (int slice = 0; slice < 300 && !rear.IsOver; slice++)
		{
			rear.Step();
		}

		Is("sent at the enemy's back, a squad falls on it", spears.Fallen > 0, true);
		Is("  and the men it falls on fight back", swords.Fallen > 0, true);

		// Left without orders with the enemy standing a few paces off, a squad goes for him.
		var near = new FieldBattle(Men(("sword", 30)),
			new Defenders(Men(("spear", 30)), new Dictionary<string, int>(), "", 50f), false, b)
		{
			IsDefenceCaptained = false,
		};
		FieldSquad unordered = near.Squads.Find(squad => squad.IsAttacking);
		FieldSquad standing = near.Squads.Find(squad => !squad.IsAttacking);
		unordered.At = standing.At + (standing.Facing * (standing.Depth + unordered.Depth + 10f));
		unordered.Facing = -standing.Facing;
		foreach (FieldSoldier man in unordered.Soldiers)
		{
			man.At = unordered.Place(man.Slot);
		}

		for (int slice = 0; slice < 300 && !near.IsOver; slice++)
		{
			near.Step();
		}

		Is("left without orders, a squad goes for an enemy standing a few paces off", standing.Fallen > 0, true);

		// Called out of a fight, a squad comes out of it — all of it, not just its standard.
		var called2 = new FieldBattle(Men(("sword", 40)), Militia(80), false, b) { IsAttackCaptained = true };
		FieldSquad pulled = called2.Squads.Find(squad => squad.IsAttacking);
		for (int slice = 0; slice < 900 && !pulled.IsFighting; slice++)
		{
			called2.Step();
		}

		called2.IsAttackCaptained = false;
		Vector2 from = pulled.At;
		called2.March(new[] { pulled }, from - (pulled.Facing * 40f));
		for (int slice = 0; slice < 150; slice++)
		{
			called2.Step();
		}

		Is("ordered out of a fight, a squad marches out of it", pulled.At.DistanceTo(from) > 20f, true);
		Is("  and its men go with it", pulled.Soldiers.TrueForAll(man => man.At.DistanceTo(pulled.At) < pulled.Reach + 4f), true);

		// A front drawn by the lord: a long line draws a company up one rank deep, a short one deep,
		// and they turn to face the way he drew it.
		var drawn = new FieldBattle(Men(("spear", 30)), Militia(40), false, b) { IsDefenceCaptained = false };
		FieldSquad line = drawn.Squads.Find(squad => squad.IsAttacking);
		drawn.Form(new[] { line }, line.At + new Vector2(-20f, 0f), line.At + new Vector2(20f, 0f), Vector2.Right.Orthogonal());
		Is("a long front is a single rank", line.Files >= line.Standing - 1, true);
		drawn.Form(new[] { line }, line.At, line.At + new Vector2(0f, 5f), Vector2.Right);
		Is("  a short one is several", line.Files < line.Standing / 3, true);
		for (int slice = 0; slice < 200; slice++)
		{
			drawn.Step();
		}

		Is("  and they turn to face the way it was drawn", line.Facing.Dot(Vector2.Right) > 0.99f, true);

		// A front is laid down from where the lord began drawing it: dragged on past the length the
		// men can fill, it stays where it began and does not slide after the cursor.
		var laid = new FieldBattle(Men(("spear", 20)), Militia(20), false, b);
		var spearmen = new[] { laid.Squads.Find(squad => squad.IsAttacking) };
		List<Vector2> dragged = laid.Planned(spearmen, Vector2.Zero, Vector2.Right * 60f, Vector2.Up, out _);
		List<Vector2> further = laid.Planned(spearmen, Vector2.Zero, Vector2.Right * 120f, Vector2.Up, out _);
		Is("a front drawn on past its length stays where it began", dragged[0].DistanceTo(further[0]) < 0.01f, true);
		Is("  and starts at the spot the drawing began", dragged.Min(spot => spot.X) < 2f, true);

		// A company marched through a friendly one standing in its road goes through it, and the one
		// standing stays where it was put.
		var road = new FieldBattle(Men(("spear", 60)), Militia(20), false, b) { IsDefenceCaptained = false };
		var companies = road.Squads.FindAll(squad => squad.IsAttacking);
		FieldSquad stays = companies[0];
		FieldSquad walks = companies[1];
		Vector2 put = stays.At;
		road.March(new[] { walks }, put + (put - walks.At));
		for (int slice = 0; slice < 400; slice++)
		{
			road.Step();
		}

		Is("a company marched through a friendly one leaves it where it stood", stays.At.DistanceTo(put) < 0.5f, true);

		// And two the lord placed stay where he put them, one brought up half on top of the other or not.
		var beside = new FieldBattle(Men(("spear", 60)), Militia(20), false, b) { IsDefenceCaptained = false };
		var pair = beside.Squads.FindAll(squad => squad.IsAttacking);
		FieldSquad placed = pair[0];
		FieldSquad brought = pair[1];
		Vector2 ground = placed.At;
		beside.Hold(new[] { placed });
		beside.March(new[] { brought }, ground + (Vector2.Right * placed.Reach * 0.5f));
		for (int slice = 0; slice < 400; slice++)
		{
			beside.Step();
		}

		Is("a company brought up beside a placed one does not shove it off its ground", placed.At.DistanceTo(ground) < 0.5f, true);
		Is("  and stays where it was sent itself", brought.At.DistanceTo(ground + (Vector2.Right * placed.Reach * 0.5f)) < 0.5f, true);

		// Archers with swordsmen at their corner: the men those swordsmen can reach fight them,
		// and the rest go on loosing — the company does not put its bows down because a few are at grips.
		var corner = new FieldBattle(Men(("bow", 20)),
			new Defenders(Men(("sword", 12)), new Dictionary<string, int>(), "", 50f), false, b) { IsDefenceCaptained = false };
		FieldSquad bows = corner.Squads.Find(squad => squad.IsAttacking);
		corner.Charge(new[] { corner.Squads.Find(squad => !squad.IsAttacking) }, bows);
		var loosing = new HashSet<FieldSoldier>();
		var lastStruck = new Dictionary<FieldSoldier, float>();
		for (int slice = 0; slice < 3000 && !corner.IsOver; slice++)
		{
			corner.Step();
			foreach (FieldSoldier man in bows.Soldiers)
			{
				if (bows.InMelee != null && man.Foe == null && man.Struck > lastStruck.GetValueOrDefault(man, -100f))
				{
					loosing.Add(man);
				}

				lastStruck[man] = man.Struck;
			}
		}

		GD.Print($"	   {loosing.Count} of 20 archers loosed while their company was at grips");
		Is("archers at grips at one corner go on shooting from the rest of the line", loosing.Count >= 10, true);

		// Two companies sent in single file at a handful: the men behind the heads of the columns come
		// round and fall on them too, rather than queueing to fight one at a time.
		var files = new FieldBattle(Men(("sword", 60)),
			new Defenders(Men(("spear", 8)), new Dictionary<string, int>(), "", 50f), false, b) { IsDefenceCaptained = false };
		var columns = files.Squads.FindAll(squad => squad.IsAttacking);
		FieldSquad few = files.Squads.Find(squad => !squad.IsAttacking);
		foreach (FieldSquad column in columns)
		{
			column.Reform(1);
		}

		files.Charge(columns, few);
		var struck = new HashSet<FieldSoldier>();
		int slices = 0;
		for (; slices < 1500 && few.IsStanding; slices++)
		{
			files.Step();
			foreach (FieldSquad column in columns)
			{
				struck.UnionWith(column.Soldiers.Where(man => man.Foe != null));
			}
		}

		GD.Print($"	   {struck.Count} of ours came to blows; the handful lasted {slices * FieldBattle.Slice:0} s");
		Is("a column that meets a handful swarms round it", struck.Count >= 16, true);

		// The field is fought to the last man.
		Battle.Result rout = new FieldBattle(Men(("sword", 100)), Militia(80), false, b).Fought();
		Is("the field is won when the other side's last man is down", rout.DefenderFell, 80);
		Is("  and the winner is still standing", rout.AttackerFell < 100, true);

		Is("the same day fought twice comes out the same",
			new FieldBattle(crown, Militia(70), false, b).Fought().AttackerFell,
			new FieldBattle(crown, Militia(70), false, b).Fought().AttackerFell);

		Is("an empty field is walked into by hand too",
			new FieldBattle(crown, new Defenders(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 50f),
				false, b).Result().AttackerWon, true);

		// A lord who calls his men off has not won, and has lost only who fell.
		var called = new FieldBattle(Men(("sword", 100)), Militia(80), false, b) { IsAttackCaptained = true };
		while (!called.IsOver && called.AttackLoss + called.DefenceLoss < 0.2f)
		{
			called.Step();
		}

		called.Withdraw();
		Battle.Result off = called.Result();
		Is("called off, nobody has won", off.AttackerWon, false);
		Is("  and the men who walked away are still his", off.AttackerFell < 100, true);

		// Two bodies of bowmen nobody orders anywhere, out of each other's reach: a day of nothing.
		var idle = new FieldBattle(Men(("bow", 30)), new Defenders(Men(("bow", 30)), new Dictionary<string, int>(), "", 50f),
			false, b);
		while (!idle.IsOver)
		{
			idle.Step();
		}

		Is("a day nobody fights ends at sundown with nobody the winner", idle.Result().AttackerWon, false);
		Is("  and nobody dead", idle.Result().AttackerFell + idle.Result().DefenderFell, 0);
	}
}
