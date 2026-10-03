using System.Collections.Generic;
using Godot;

/// <summary>The walls: an assault at the gate, the ladder of fortifications, and a siege sat down
/// in front of it.</summary>
public partial class BattleCheck
{
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

	/// <summary>Engines built outside the gate: the seasons they take, and the walls they open.</summary>
	private void TheEngines(GameBalance b)
	{
		TurnManager turns = Realm(b, out ProvinceEconomy crown, out ProvinceEconomy watch);
		watch.Fortification = "large-castle";
		watch.Castle["spear"] = 40;
		watch.CastleStores = 100_000;
		crown.Muster("sword", 200, b.MarchReach);
		FieldArmy sitting = crown.Armies[0];
		sitting.X = 900f;
		sitting.Y = 200f;

		var bare = Battle.Weighed(sitting.Men, turns.DefendersOf("Valmere"), true, false, b);
		var engines = new System.Collections.Generic.Dictionary<string, int>
		{
			[SiegeEngines.Ram] = 1,
			[SiegeEngines.Catapult] = 2,
		};
		Is("a siege can be laid with engines to build", turns.Besiege(sitting, "Valmere", engines), true);
		int seasons = b.RamSeasons + (2 * b.CatapultSeasons);
		Is("  and they take their seasons", turns.SiegeSeasonsLeft("Valmere"), seasons);
		var built = Battle.Weighed(sitting.Men, turns.DefendersOf("Valmere"), true, false, b);
		Is("  a breached and rammed castle is weaker against the same host", built.Attacker > bare.Attacker
			|| built.Defender < bare.Defender, true);
		for (int season = 0; season < seasons; season++)
		{
			turns.AdvanceTurn();
		}

		Is("  and once they are built the walls can be stormed", turns.SiegeSeasonsLeft("Valmere"), 0);
		Is("a company knows what it is besieging", turns.Besieging(sitting), "Valmere");
	}

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
		paying.Tax = 5;
		paying.Castle["spear"] = 20;
		ProvinceEconomy shut = paying.Copy();
		shut.BesiegedFrom = "Kingsreach#1";
		int due = EconomySimulation.TaxDue(paying, b);
		EconomySimulation.RunTurn(paying, definition, b, Season.Summer);
		EconomySimulation.RunTurn(shut, definition, b, Season.Summer);
		Is("a besieged county pays its lord nothing", paying.Gold - shut.Gold, due);
		// The men on the walls never eat from the county's granary, siege or none: the original keeps
		// the garrison out of the stores.
		Is("  and its granary feeds nobody behind the gate", shut.Grain, paying.Grain);
		Is("  and paid something while it was open", due > 0, true);
	}
}
