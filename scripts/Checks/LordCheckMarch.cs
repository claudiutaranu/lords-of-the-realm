using System.Collections.Generic;
using Godot;

/// <summary>The rival on the road: his companies marched over a straight test road to the county he
/// chose, by the turn's own rules.</summary>
public partial class LordCheck
{
	/// <summary>An army leaving home. The men ARE the county's roster, so the thing to pin down is
	/// that they are only ever in one place: a march that copied them instead of moving them would
	/// give a lord two armies out of one and cost him nothing.
	///
	/// Where the ground is passable and what it costs to cross is the map's arithmetic and is checked
	/// against the map; what is checked here is the ledger's half — men, budget, and whose county is
	/// being walked into.</summary>
	private void TheMarch(GameBalance b)
	{
		var definitions = new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Redmoor") };
		var realms = new Dictionary<string, string>
		{
			["Kingsreach"] = "royal-crown",
			["Redmoor"] = "royal-crown",
		};
		var turns = new TurnManager(b, definitions, realms, "royal-crown", Difficulty.Medium);

		ProvinceEconomy home = turns.GetProvince("Kingsreach");
		FieldArmy spears = home.Raise(b.MarchReach);
		spears.Men["spear"] = 60;

		Is("an army opens with a season's ground in hand", spears.MarchLeft, b.MarchReach);
		Is("the men march", turns.March(spears, "Redmoor", new Vector2(400, 300), 200f), true);
		Is("  and they are standing in the county they were sent to", spears.County, "Redmoor");
		Is("  on the ground they were sent to", spears.X, 400f);

		// The men are the company's, and the company is its own county's however far it walks: the
		// county that raised them is the one that goes on paying and feeding them.
		Is("  still on the roster of the county that feeds them", home.Soldiers, 60);
		Is("  and not on the roster of the one they are standing in",
			turns.GetProvince("Redmoor").Soldiers, 0);

		// What the season had left is the army's own and not the county's.
		Is("the march costs the ground it covered", spears.MarchLeft, b.MarchReach - 200f);

		// And no further than the season allows, however good the road looks.
		Is("a march further than the season is refused",
			turns.March(spears, "Kingsreach", new Vector2(100, 100), b.MarchReach), false);

		// A second company raised at home is a SECOND company: the barracks does not quietly pour
		// its intake into whatever is already standing in the field three counties away.
		FieldArmy bows = home.Raise(b.MarchReach);
		bows.Men["bow"] = 20;
		Is("a company raised at home is its own army", home.Armies.Count, 2);
		Is("  with the whole county behind both of them", home.FieldMen, 80);
		Is("  and its own legs", bows.MarchLeft, b.MarchReach);
		Is("one army's march is not the other's", turns.March(bows, "Redmoor", new Vector2(400, 300), 50f), true);
		Is("  and spends only its own ground", bows.MarchLeft, b.MarchReach - 50f);
		Is("  while the first army's is where the first march left it",
			spears.MarchLeft, b.MarchReach - 200f);

		// Standing in the same field is not being one army. That is an order the lord gives.
		Is("two companies in one county are still two", home.Armies.Count, 2);
		Is("joining them makes one", turns.Merge(spears, bows), true);
		Is("  with everybody in it", spears.Strength, 80);
		Is("  and one banner left standing", home.Armies.Count, 1);
		Is("  on the slower pair of legs", spears.MarchLeft, b.MarchReach - 200f);

		// Walking onto ground nobody holds does NOT take it any more: the county's own people stand
		// up for it, and men in the way have to be beaten rather than walked past.
		var free = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown" }, "royal-crown",
			Difficulty.Medium, new List<ProvinceDefinition> { Definition("Ashenvale") });

		FieldArmy walkers = free.GetProvince("Kingsreach").Raise(b.MarchReach);
		walkers.Men["spear"] = 40;
		Is("an unheld county is nobody's until somebody takes it", free.AnyProvince("Ashenvale") == null, true);
		Is("  and its own people are what stands in the way", free.DefendersOf("Ashenvale").Men,
			Mathf.FloorToInt(Definition("Ashenvale").InitialPopulation * b.MilitiaShare[(int)Difficulty.Medium]));

		int Militia(Difficulty skill) => new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown" }, "royal-crown", skill,
			new List<ProvinceDefinition> { Definition("Ashenvale") }).DefendersOf("Ashenvale").Men;
		Defenders town = free.DefendersOf("Ashenvale");
		Is("  and not only farmhands: the town's watch has bows", town.Field.GetValueOrDefault("bow") > 0, true);
		Is("  and spears", town.Field.GetValueOrDefault("spear") > 0, true);
		Is("  with the farmhands still the most of them",
			town.Field.GetValueOrDefault("peasant") > town.Field.GetValueOrDefault("bow") + town.Field.GetValueOrDefault("spear"), true);
		Is("  and more of them turn out the harder the game",
			Militia(Difficulty.Easy) < Militia(Difficulty.Medium) && Militia(Difficulty.Medium) < Militia(Difficulty.Hard), true);
		Is("the men can still be walked onto it",
			free.March(walkers, "Ashenvale", new Vector2(500, 400), 150f), true);
		Is("  but it is nobody's still", free.AnyProvince("Ashenvale") == null, true);
		Is("  and they are standing on its ground", walkers.X, 500f);
		Is("  on the roster of the county that feeds them", free.GetProvince("Kingsreach").Soldiers, 40);

		// Beating them is what takes it, and it is the only thing that does.
		Is("beating them takes it", free.Claim(walkers, "Ashenvale", new Vector2(500, 400)), true);
		Is("  and it answers to the lord who took it", free.GetProvince("Ashenvale").Realm, "royal-crown");
		Is("  with his men standing on it", free.DefendersOf("Ashenvale").Men, 40);
		Is("  still fed by the county that raised them", free.GetProvince("Kingsreach").Soldiers, 40);
		Is("  drawing on the same purse as the rest of his realm",
			free.GetProvince("Ashenvale").Purse == free.GetProvince("Kingsreach").Purse, true);
		Is("  and it takes its turns from now on", free.AdvanceTurn().Count, 2);

		// And an empty company cannot march: there is nobody in it to go.
		FieldArmy nobody = free.GetProvince("Kingsreach").Raise(b.MarchReach);
		Is("a company with no men in it marches nowhere",
			free.March(nobody, "Ashenvale", new Vector2(500, 400), 10f), false);

		// Another lord's ground can be crossed. Crossing it is all it is — his county is his until
		// somebody takes its seat off him, and ground that changed hands for being walked over would
		// be the fastest way to win the campaign and the least interesting.
		var rival = new TurnManager(b, new List<ProvinceDefinition> { Definition("Kingsreach"), Definition("Valmere") },
			new Dictionary<string, string> { ["Kingsreach"] = "royal-crown", ["Valmere"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);

		FieldArmy crossing = rival.GetProvince("Kingsreach").Raise(b.MarchReach);
		crossing.Men["spear"] = 80;
		Is("a rival's border no longer stops an army",
			rival.March(crossing, "Valmere", new Vector2(900, 200), 100f), true);
		Is("  but crossing his county has not taken it", rival.AnyProvince("Valmere").Realm, "northern-watch");
		Is("  and his own county is none the worse for it", rival.AnyProvince("Valmere").Soldiers, 0);
		Is("  while our men are still ours to feed", rival.GetProvince("Kingsreach").Soldiers, 80);
		Is("  standing on his ground", crossing.X, 900f);

		// Ground inside a county's own borders: the men walk, and nothing changes hands.
		ProvinceEconomy walking = rival.GetProvince("Kingsreach");
		float had = crossing.MarchLeft;
		Is("men can walk their own county", rival.March(crossing, "Kingsreach", new Vector2(370, 500), 60f), true);
		Is("  and it still costs them ground", crossing.MarchLeft, had - 60f);
		Is("  and nobody has taken anything", walking.Realm, "royal-crown");

		// The walls and the field are two rosters. The county feeds, pays and resents both of them;
		// only one of them ever goes anywhere. Without the second, a lord could not leave men on his
		// own gate and march out with the rest — he could only choose — and every castle in the realm
		// would stand empty the season its county went to war.
		walking.Castle["spear"] = 20;
		Is("the gate watch is counted with the men the county keeps", walking.Soldiers, 100);
		Is("  but it is not what marches", walking.FieldMen, 80);
		walking.Armies.Clear();
		Is("a county with nobody but a gate watch has nothing to march",
			walking.Armies.Count, 0);
	}

	/// <summary>A road that runs straight, a step every twelve pixels, a pixel of march a pixel.</summary>
	private static List<(Vector2 At, float Spent)> Straight(Vector2 from, Vector2 to)
	{
		var road = new List<(Vector2, float)>();
		float far = from.DistanceTo(to);
		for (float along = 12f; along < far + 12f; along += 12f)
		{
			float step = Mathf.Min(along, far);
			road.Add((from.Lerp(to, step / Mathf.Max(far, 0.01f)), step));
		}

		return road;
	}
}
