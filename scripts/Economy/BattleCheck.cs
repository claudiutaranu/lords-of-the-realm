using System.Collections.Generic;
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
		TheSiege(b);

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

	/// <summary>The walls. The thing being pinned down is that they are held by FRONTAGE and not by
	/// the multiplier: three hundred swordsmen take a palisade and the same three hundred do not
	/// take a royal castle, because only twenty-five of them can be at it at a time. If that ever
	/// stops being true, every castle in the game becomes a speed bump for whoever brought the
	/// bigger army, and the whole stone ladder stops being worth quarrying.</summary>
	private void TheWalls(GameBalance b)
	{
		Dictionary<string, int> host = Men(("sword", 300));
		Dictionary<string, int> garrison = Men(("spear", 40));

		(int palisade, _, _) = Fought(host, Behind(garrison, "small-palisade"), true, false, b);
		Is("a host takes a palisade off forty men", palisade >= Tries * 7 / 10, true);

		(int royal, _, _) = Fought(host, Behind(garrison, "grand-castle"), true, false, b);
		Is("  and the same host does not take a royal castle off the same forty",
			royal <= Tries / 10, true);

		// The county's own militia, which loses in the open, behind one rung of stone. A small force
		// is not crowded by a frontage and is not stopped by a multiplier either — if a keep did not
		// turn THIS around it would be worth nothing against exactly the army it is built for.
		Dictionary<string, int> crown = Men(("spear", 25), ("bow", 15));
		(int open, _, _) = Fought(crown, Militia(70), false, false, b);
		(int walled, _, _) = Fought(crown, Behind(Men(("peasant", 70)), "medium-castle"), true, false, b);
		Is("what a keep is worth: the same militia that lost in the open holds it",
			walled < open / 4, true);

		// Past the frontage, more men buy nothing at all. THIS is the property the whole stone
		// ladder rests on — three hundred and four hundred and fifty come out the same, because only
		// forty of either can be at the wall. If it ever stops being true, a castle is a delay
		// rather than a decision and the quarry stops being worth digging.
		Dictionary<string, int> keep = Men(("spear", 30), ("bow", 20), ("sword", 10));
		(int threeHundred, _, _) = Fought(Men(("sword", 300)), Behind(keep, "medium-castle"), true, false, b);
		(int halfAgain, _, _) = Fought(Men(("sword", 450)), Behind(keep, "medium-castle"), true, false, b);
		Is("past the frontage, half as many men again buy nothing",
			halfAgain <= threeHundred + Tries / 20, true);

		// Walls with nobody on them are not walls.
		Defenders hollow = Behind(new Dictionary<string, int>(), "grand-castle");
		Is("a castle is men, and stone is what they stand on", hollow.Held, false);
		Is("  so an empty one is walked into",
			Battle.OnTheWalls(crown, hollow, false, b, Dice()).AttackerWon, true);

		// Horses against a wall.
		(float mounted, _) = Battle.Weighed(Men(("horse", 60)), Behind(garrison, "medium-castle"), true, false, b);
		(float afoot, _) = Battle.Weighed(Men(("sword", 60)), Behind(garrison, "medium-castle"), true, false, b);
		Is("cavalry are worth nothing on a ladder", mounted, 0f);
		Is("  where the same sixty on foot are worth something", afoot > 0f, true);

		// And in the open, where they are the most expensive men in the realm for a reason.
		(float charging, _) = Battle.Weighed(Men(("horse", 60)), Militia(56), false, false, b);
		Is("  and everything in a field", charging > afoot, true);
	}

	/// <summary>The rungs. Every step up the ladder has to be worth what it costs in both of the
	/// ways it is worth anything — harder to kill the men behind it, and fewer of the enemy able to
	/// reach them at once. A rung that is not both is a rung a lord would be right to skip, and the
	/// building screen would be quietly lying about the progression it draws.</summary>
	private void TheLadder(GameBalance b)
	{
		string[] rungs =
		{
			"small-palisade", "medium-fort", "large-fort",
			"small-castle", "medium-castle", "large-castle", "grand-castle",
		};

		Is("open ground is worth nothing and holds nobody back", Fortifications.Of("").Defence, 1f);
		for (int rung = 1; rung < rungs.Length; rung++)
		{
			Fortifications.Wall below = Fortifications.Of(rungs[rung - 1]);
			Fortifications.Wall above = Fortifications.Of(rungs[rung]);
			Is($"{rungs[rung]} is harder to kill men behind than {rungs[rung - 1]}",
				above.Defence > below.Defence, true);
			Is($"  and fewer of them can be got at at once", above.Frontage < below.Frontage, true);
		}
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
		Is("  because the survivors are behind the walls now", watch.FieldMen, 0);
		Is("  standing with the gate watch", watch.CastleMen > 20, true);
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
		Is("  and we are down the men it cost", few.FieldMen, 40 - thrown.AttackerFell);
		Is("  which was not nothing", thrown.AttackerFell > 0, true);
	}

	/// <summary>The other way a castle falls, and the one the stone rungs actually fall to. What is
	/// pinned down here is that it ENDS: a siege that never resolves is worse than no siege at all,
	/// because a lord would sit an army in front of a gate for the rest of the campaign waiting for
	/// something the engine was never going to do.</summary>
	private void TheSiege(GameBalance b)
	{
		TurnManager turns = Realm(b, out ProvinceEconomy crown, out ProvinceEconomy watch);
		watch.Fortification = "large-fort";
		watch.Castle["spear"] = 20;
		crown.Muster("sword", 100, b.MarchReach);
		FieldArmy sitting = crown.Armies[0];
		sitting.X = 900f;
		sitting.Y = 200f;

		// Two seasons of bread for twenty men, and an empty larder after that.
		int eaten = Mathf.CeilToInt(20 / b.PeoplePerGrain * b.SoldierAppetite);
		watch.CastleStores = eaten * 2;

		Is("an army can sit down in front of a manned gate", turns.Besiege(sitting, "Valmere"), true);
		Is("  and the county knows which company is out there", watch.BesiegedFrom, sitting.Key);

		turns.AdvanceTurn();
		Is("a besieged garrison eats its own larder", watch.CastleStores, eaten);
		Is("  and not the county's granary", watch.Fed, 0);
		// Read as "not enough left for another season" rather than as a figure: a garrison nobody
		// can pay walks off a man at a time, and fewer mouths eat through the larder more slowly.
		// What the siege turns on is that the bread RUNS OUT, not that it runs out to the grain.
		turns.AdvanceTurn();
		Is("  until there is not enough left for another season", watch.CastleStores < eaten, true);

		turns.AdvanceTurn();
		Is("then they go hungry", watch.HungrySeasons, 1);
		Is("  and they thin", watch.CastleMen < 20, true);
		Is("  but they do not give up on the first empty week",
			turns.AnyProvince("Valmere").Realm, "northern-watch");

		for (int season = 1; season < b.SurrenderAfterHungrySeasons; season++)
		{
			turns.AdvanceTurn();
		}

		Is("the gate is opened", turns.GetProvince("Valmere") != null, true);
		Is("  by the lord who was sitting outside it", turns.GetProvince("Valmere").Realm, "royal-crown");
		Is("  with his men in it", turns.DefendersOf("Valmere").Men > 0, true);
		Is("  and nobody besieging anything any more", turns.GetProvince("Valmere").BesiegedFrom, "");

		// A siege is the army BEING there. The moment it is anywhere else, there is no siege.
		TurnManager left = Realm(b, out ProvinceEconomy ours, out ProvinceEconomy theirs);
		theirs.Fortification = "medium-castle";
		theirs.Castle["spear"] = 20;
		ours.Muster("sword", 100, b.MarchReach);
		FieldArmy camped = ours.Armies[0];
		Is("a siege is laid", left.Besiege(camped, "Valmere"), true);
		Is("  and marching away lifts it",
			left.March(camped, "Kingsreach", new Vector2(370, 500), 60f) && theirs.BesiegedFrom == "",
			true);

		// And what cannot be sat down in front of.
		TurnManager wrong = Realm(b, out ProvinceEconomy host, out ProvinceEconomy open);
		host.Muster("sword", 100, b.MarchReach);
		FieldArmy outside = host.Armies[0];
		open.Muster("peasant", 30);
		Is("a county with no walls cannot be besieged", wrong.Besiege(outside, "Valmere"), false);
		open.Fortification = "medium-castle";
		open.Castle["spear"] = 10;
		Is("  nor one whose field army is still standing", wrong.Besiege(outside, "Valmere"), false);
		open.Armies.Clear();
		Is("  but a shut gate with nobody outside it can be", wrong.Besiege(outside, "Valmere"), true);

		// What it costs the lord being sat on: his county stops paying him. Run on two copies of the
		// same county so nothing but the siege is different between them.
		ProvinceDefinition definition = Definition("Valmere");
		ProvinceEconomy paying = ProvinceEconomy.FromDefinition(definition);
		paying.Grain = 2000;
		paying.Castle["spear"] = 20;
		ProvinceEconomy shut = paying.Copy();
		shut.BesiegedFrom = "Kingsreach#1";
		int due = EconomySimulation.TaxDue(paying, b);
		EconomySimulation.RunTurn(paying, definition, b, Season.Summer);
		EconomySimulation.RunTurn(shut, definition, b, Season.Summer);
		Is("a besieged county pays its lord nothing", paying.Gold - shut.Gold, due);
		Is("  and its granary is not feeding the men behind the gate", shut.Grain > paying.Grain, true);
	}

	// --- scaffolding -------------------------------------------------------------------------------

	/// <summary>Two counties, two realms, nobody in either of them yet.</summary>
	private static TurnManager Realm(GameBalance b, out ProvinceEconomy crown, out ProvinceEconomy watch)
	{
		var turns = new TurnManager(b,
			new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string>
			{
				["Kingsreach"] = "royal-crown",
				["Valmere"] = "northern-watch",
			},
			"royal-crown", Difficulty.Medium);

		crown = turns.AnyProvince("Kingsreach");
		watch = turns.AnyProvince("Valmere");
		crown.Armies.Clear();
		watch.Armies.Clear();
		watch.Castle.Clear();
		return turns;
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
