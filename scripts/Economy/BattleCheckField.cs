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

		// Every squad's standard is carried by its captain: one of its own men, a little harder to
		// kill than the rest, and killed all the same.
		var led = new FieldBattle(Men(("sword", 100)), Militia(80), false, b);
		FieldSquad militia = led.Squads.Find(squad => !squad.IsAttacking);
		FieldSoldier captain = militia.Captain;
		Is("a squad's captain is harder to kill than his men",
			captain.MostHealth > militia.Soldiers.Find(man => man != captain).MostHealth, true);
		led.Fought();
		Is("  and falls with them all the same", captain.IsStanding, false);

		// And he goes in with them: across nine days, the beaten side's captain is seldom its last man.
		int lastOfAll = 0;
		for (ulong seed = 1; seed <= Seeds; seed++)
		{
			var day = new FieldBattle(Men(("sword", 60)), Militia(80), false, b, seed) { IsAttackCaptained = true };
			FieldSquad beaten = day.Squads.Find(squad => !squad.IsAttacking);
			while (!day.IsOver && beaten.Captain.IsStanding)
			{
				day.Step();
			}

			lastOfAll += beaten.Standing == 0 ? 1 : 0;
		}

		GD.Print($"	   the captain was his squad's last man {lastOfAll} of {Seeds} days");
		Is("  and does not stand at the back and outlive them", lastOfAll <= Seeds / 3, true);

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
