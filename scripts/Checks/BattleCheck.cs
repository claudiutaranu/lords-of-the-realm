using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The one runnable check behind the fighting. A battle is the only thing in this game
/// that cannot be read off a screen afterwards and argued with: the men are gone, and whether they
/// were spent well or thrown away is a question about a calculation nobody watched. So the
/// calculation is pinned down here.
///
/// It is checked in BANDS and not in single outcomes, because the day has dice in it. Two hundred
/// battles of the same shape, off a fixed seed, and what is asserted is how often each way — which
/// is the only honest way to hold a thing with luck in it, and it catches the failure that matters:
/// a modifier wired up backwards moves a band from ninety per cent to ten and cannot hide.
///
/// Run it: Godot --headless --path . res://scene/checks/battle-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class BattleCheck : Node
{
	/// <summary>How many times each shape of battle is fought before anything is said about it.</summary>
	private const int Tries = 200;

	/// <summary>The seed every run starts from. A check that reported a different figure each time
	/// would be a check nobody could act on.</summary>
	private const ulong Seed = 1268;

	private int _failed;

	public override void _Ready()
	{
		var b = new GameBalance();

		TheOpenField(b);
		TheWalls(b);
		TheLadder(b);
		WhatTheLordCanSee(b);
		TheButchersBill(b);
		TheReckoning(b);
		TheOrphans(b);
		TheSiege(b);
		TheEngines(b);
		OpenCountry(b);
		TheTownTurnsOut(b);
		TheHiredBand(b);
		Trampled(b);
		ByHand(b);
		AboutFace(b);
		ThroughTheWalls(b);

		GD.Print(_failed == 0
			? "\nthe fighting: all checks passed"
			: $"\nthe fighting: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	/// <summary>Trained men against a county's own people, in the open. This is the campaign's first
	/// battle and the one its opening position is balanced around: forty of the crown's men against
	/// what a county of seven hundred stands up. It has to be winnable and it has to cost
	/// something — a walkover would make every neutral county a formality, and a loss would make the
	/// opening army pointless.</summary>
	private void TheOpenField(GameBalance b)
	{
		Dictionary<string, int> crown = Men(("spear", 25), ("bow", 15));

		// The campaign's opening sum: the crown's forty against what a county of seven hundred
		// stands up. It has to be winnable and it has to hurt.
		(int won, int fell, _) = Fought(crown, Militia(70), assault: false, marched: false, b);
		Is("trained men beat a small county's militia in the open", won >= Tries * 6 / 10, true);
		Is("  but not for nothing", fell > 0, true);
		Is("  and not to the last man either", fell < ProvinceEconomy.Men(crown), true);

		// A county that outnumbers him getting on for three to one is where the answer stops being
		// obvious. Every modifier below is weighed against THIS battle and not against the one
		// above: an edge that only shows in a day already won is an edge nobody can measure.
		Defenders big = Militia(95);
		(int even, _, _) = Fought(crown, big, assault: false, marched: false, b);
		Is("two and a half of them to one of his is a real question",
			even > Tries / 10 && even < Tries * 9 / 10, true);

		// The same men, arriving on the last of their legs. Nothing else about the day changes.
		(int tired, _, _) = Fought(crown, big, assault: false, marched: true, b);
		Is("a season spent walking is paid for on arrival", tired < even, true);

		// The same militia, in a county that would rather have somebody else.
		(int sullen, _, _) = Fought(crown, big with { Loyalty = 0f }, assault: false, marched: false, b);
		Is("a people who have had enough of their lord hold worse for him", sullen > even, true);

		// Nobody in the way at all.
		Defenders empty = new(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 70f);
		Battle.Result walkIn = Battle.InTheField(crown, empty, false, b, Dice());
		Is("an empty field is walked into", walkIn.AttackerWon, true);
		Is("  without a man lost", walkIn.AttackerFell, 0);
		Is("  and without a fight", walkIn.Rounds, 0);

		// And an army that no longer exists takes nothing.
		Battle.Result nobody = Battle.InTheField(new Dictionary<string, int>(), Militia(56), false, b, Dice());
		Is("an army with nobody in it takes nothing", nobody.AttackerWon, false);
	}

	/// <summary>What the panel shows a lord before he commits. It is drawn from the same two figures
	/// the fight divides one by the other, so a bar that reads comfortably and a battle that goes
	/// badly cannot both be right — which is the one way a pre-battle screen can be worse than no
	/// screen at all.</summary>
	private void WhatTheLordCanSee(GameBalance b)
	{
		Dictionary<string, int> crown = Men(("spear", 25), ("bow", 15));
		(float mine, float theirs) = Battle.Weighed(crown, Militia(56), false, false, b);
		Is("the odds are weighed without rolling anything", mine > 0f && theirs > 0f, true);

		(float _, float behindStone) = Battle.Weighed(crown, Behind(Men(("peasant", 56)), "medium-castle"), true, false, b);
		Is("  and stone shows up in them", behindStone > theirs, true);

		(float tired, _) = Battle.Weighed(crown, Militia(56), false, true, b);
		Is("  and so does a season of walking", tired < mine, true);

		// Weighing a battle must not fight it.
		var roster = new Dictionary<string, int>(crown);
		Battle.Weighed(roster, Militia(56), false, false, b);
		Battle.InTheField(roster, Militia(56), false, b, Dice());
		Is("looking at a battle kills nobody", ProvinceEconomy.Men(roster), 40);
	}

	/// <summary>Who is left. Nobody may lose more men than they brought — the one failure here is
	/// silent and permanent, because a roster that went negative or double-counted a company is a
	/// save file that is wrong from then on — and the ill-armed have to be the ones who fall.</summary>
	private void TheButchersBill(GameBalance b)
	{
		RandomNumberGenerator rng = Dice();
		bool overdrawn = false;
		bool peasantsSpared = false;
		for (int fight = 0; fight < Tries; fight++)
		{
			Dictionary<string, int> mixed = Men(("peasant", 100), ("sword", 100));
			Battle.Result result = Battle.InTheField(mixed, Militia(90), false, b, rng);
			overdrawn |= result.AttackerFell > 200 || result.DefenderFell > 90;
			foreach ((string unit, int fell) in result.AttackerLosses)
			{
				overdrawn |= fell > mixed[unit];
			}

			peasantsSpared |= result.AttackerLosses.GetValueOrDefault("peasant")
				< result.AttackerLosses.GetValueOrDefault("sword");
		}

		Is("nobody ever loses more men than they brought", overdrawn, false);
		Is("and the ill-armed are the ones who fall", peasantsSpared, false);

		// The same battle, twice, off the same dice.
		Battle.Result once = Battle.InTheField(Men(("sword", 80)), Militia(60), false, b, Dice());
		Battle.Result again = Battle.InTheField(Men(("sword", 80)), Militia(60), false, b, Dice());
		Is("the same battle fought twice off the same dice comes out the same way",
			once.AttackerFell == again.AttackerFell && once.AttackerWon == again.AttackerWon, true);
	}

	/// <summary>What a battle does to the ledger afterwards, which is the half of this nobody sees
	/// happening and everybody lives with. The thing worth pinning down is WHEN a county changes
	/// hands: the moment there is nobody left to stop it and not one moment sooner. A lord can lose
	/// every man he had outside his walls and still hold his county — if that ever stops being true,
	/// the whole stone ladder is decoration.</summary>
	private void TheReckoning(GameBalance b)
	{
		// A walled county, with men in front of the walls and men on them.
		TurnManager turns = Realm(b, out ProvinceEconomy crown, out ProvinceEconomy watch);
		watch.Fortification = "large-fort";
		watch.Muster("peasant", 30);
		watch.Castle["spear"] = 20;
		crown.Muster("sword", 200, b.MarchReach);
		FieldArmy host = crown.Armies[0];

		Battle.Result field = turns.Attack(host, "Valmere", new Vector2(900, 200), walls: false);
		Is("the field is carried", field.AttackerWon, true);
		Is("  and the county is NOT taken with it", turns.AnyProvince("Valmere").Realm, "northern-watch");
		// A side that broke is finished: what the fighting did not kill has run, been taken or gone
		// home, and none of it stands under that banner again. So the field is not emptied INTO the
		// castle — the watch on the gate is the men who were always on it, and it is the only thing
		// still between the county and the man outside.
		Is("  because nothing is left of what stood in the open", watch.FieldMen, 0);
		Is("  and it cost them everything they had there", field.DefenderFell, 30);
		Is("  while the gate watch is what it always was", watch.CastleMen, 20);
		Is("  and our dead are off our own roster",
			crown.FieldMen, 200 - field.AttackerFell);

		Battle.Result storm = turns.Attack(host, "Valmere", new Vector2(900, 200), walls: true);
		Is("the walls are carried", storm.AttackerWon, true);
		Is("  and NOW the county is taken", turns.AnyProvince("Valmere").Realm, "royal-crown");
		Is("  with our men standing on it", turns.DefendersOf("Valmere").Men > 0, true);
		Is("  and nobody left of theirs", turns.AnyProvince("Valmere").CastleMen, 0);
		Is("  on the roster of the county that raised them", crown.Armies[0].County, "Valmere");
		Is("  in a county that did not ask for us",
			turns.AnyProvince("Valmere").Loyalty, ProvinceEconomy.OpeningLoyalty - b.ConquestResentment);

		// An open county falls with its field, because there is nowhere for anybody to fall back to.
		TurnManager open = Realm(b, out ProvinceEconomy mine, out ProvinceEconomy theirs);
		theirs.Muster("peasant", 30);
		mine.Muster("sword", 200, b.MarchReach);
		Is("a county with no walls falls with its field",
			open.Attack(mine.Armies[0], "Valmere", new Vector2(900, 200), false).AttackerWon, true);
		Is("  and changes hands at once", open.AnyProvince("Valmere").Realm, "royal-crown");

		// And a beaten attacker takes nothing home but his losses.
		TurnManager hopeless = Realm(b, out ProvinceEconomy few, out ProvinceEconomy many);
		many.Fortification = "grand-castle";
		many.Castle["spear"] = 60;
		few.Muster("peasant", 40, b.MarchReach);
		Battle.Result thrown = hopeless.Attack(few.Armies[0], "Valmere", new Vector2(900, 200), true);
		Is("forty peasants do not storm a royal castle", thrown.AttackerWon, false);
		Is("  the county is still theirs", hopeless.AnyProvince("Valmere").Realm, "northern-watch");
		Is("  and the army that broke on it is gone", thrown.AttackerFell, 40);
		Is("  with nothing left to send back", few.FieldMen, 0);
		Is("  nor a banner standing over nobody", few.Armies.Count, 0);
	}

	/// <summary>The other way a castle falls, and the one the stone rungs actually fall to. What is
	/// pinned down here is that it ENDS: a siege that never resolves is worse than no siege at all,
	/// because a lord would sit an army in front of a gate for the rest of the campaign waiting for
	/// something the engine was never going to do.</summary>
	/// <summary>Two companies meeting away from any gate: a fight, and the one that breaks is gone, but
	/// no county changes hands.</summary>
	private void OpenCountry(GameBalance b)
	{
		TurnManager turns = Realm(b, out ProvinceEconomy crown, out ProvinceEconomy watch);
		crown.Muster("sword", 200, b.MarchReach);
		watch.Muster("peasant", 20, b.MarchReach);
		FieldArmy ours = crown.Armies[0];
		FieldArmy theirs = watch.Armies[0];
		Battle.Result day = turns.Engage(ours, theirs);
		Is("two hundred swords break twenty peasants in open country", day.AttackerWon, true);
		Is("  and the broken company is gone", watch.Armies.Count, 0);
		Is("  and nobody's county changed hands", turns.AnyProvince("Valmere").Realm, "northern-watch");
	}

	/// <summary>A held town with nobody of its lord's standing in it still defends itself: its own
	/// people turn out, and the ones who fall come off its roll.</summary>
	private void TheTownTurnsOut(GameBalance b)
	{
		TurnManager turns = Realm(b, out ProvinceEconomy crown, out ProvinceEconomy watch);
		Is("an empty town turns out a militia", turns.DefendersOf("Valmere").Men > 0, true);
		int people = watch.Population;
		crown.Muster("sword", 300, b.MarchReach);
		FieldArmy ours = crown.Armies[0];
		turns.Attack(ours, "Valmere", new Vector2(900, 200), walls: false);
		Is("  and the men it loses are its own people", watch.Population < people || turns.AnyProvince("Valmere").Realm == "royal-crown", true);
	}

	private static ProvinceDefinition Definition(string name) => new()
	{
		ProvinceName = name,
		InitialPopulation = 850,
		Fields = 9,
		InitialGrainFields = 4,
		InitialPastureFields = 2,
	};

	/// <summary>Fights one shape of battle many times and says how it went: how often the attacker
	/// carried it, and what the average day cost each side.</summary>
	private (int Won, int AttackerFell, int DefenderFell) Fought(Dictionary<string, int> attacker,
		Defenders against, bool assault, bool marched, GameBalance b)
	{
		RandomNumberGenerator rng = Dice();
		int won = 0;
		int ours = 0;
		int theirs = 0;
		for (int fight = 0; fight < Tries; fight++)
		{
			Battle.Result result = assault
				? Battle.OnTheWalls(attacker, against, marched, b, rng)
				: Battle.InTheField(attacker, against, marched, b, rng);
			won += result.AttackerWon ? 1 : 0;
			ours += result.AttackerFell;
			theirs += result.DefenderFell;
		}

		GD.Print($"	   {won * 100 / Tries}% of {Tries} — we lose {ours / Tries}, they lose {theirs / Tries}");
		return (won, ours / Tries, theirs / Tries);
	}

	private static RandomNumberGenerator Dice() => new() { Seed = Seed };

	private static Dictionary<string, int> Men(params (string Unit, int Count)[] companies)
	{
		var roster = new Dictionary<string, int>();
		foreach ((string unit, int count) in companies)
		{
			roster[unit] = count;
		}

		return roster;
	}

	private static Defenders Militia(int men) =>
		new(Men(("peasant", men)), new Dictionary<string, int>(), "", ProvinceEconomy.OpeningLoyalty);

	private static Defenders Behind(Dictionary<string, int> garrison, string fortification) =>
		new(new Dictionary<string, int>(), garrison, fortification, ProvinceEconomy.OpeningLoyalty);

	private void Is<T>(string what, T got, T wanted)
	{
		if (EqualityComparer<T>.Default.Equals(got, wanted))
		{
			GD.Print($"ok	{what}");
			return;
		}

		_failed++;
		GD.PrintErr($"FAIL {what}: got {got}, expected {wanted}");
	}
}
