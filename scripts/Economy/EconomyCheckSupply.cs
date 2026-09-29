using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The lord's carts (TurnManager.Dispatch, Haul): the goods leave the barn the day they are
/// sent, roll a march's reach a season, and are unloaded where they were going — unless the county
/// is lost under them or another lord's men are standing on the road.</summary>
public partial class EconomyCheck
{
	private void Carts(GameBalance b)
	{
		// Two counties of the crown two seasons' reach and a bit apart on a straight road, so three
		// seasons on the road.
		TurnManager turns = Carting(b, out ProvinceEconomy home, out ProvinceEconomy far);
		Is("two reaches and a bit is three seasons on the road", turns.SupplySeasons("Home", "Far"), 3);
		Is("  and there is no road to a county off the map", turns.SupplySeasons("Home", "Nowhere"), -1);

		int grain = home.Grain;
		int cattle = home.Cattle;
		Is("more than the barn holds is not sent", turns.Dispatch("Home", "Far", Load(grain + 1, 0)) == null, true);
		Is("  nor is an empty cart", turns.Dispatch("Home", "Far", Load(0, 0)) == null, true);
		Is("  nor anything to a county that is not ours", turns.Dispatch("Home", "Rival", Load(10, 0)) == null, true);
		Is("  and nothing left the barn for any of them", home.Grain, grain);

		Shipment cart = turns.Dispatch("Home", "Far", Load(200, 10));
		Is("a cart sent", cart != null, true);
		Is("  takes its grain out of the barn the day it leaves", home.Grain, grain - 200);
		Is("  and its cattle off the pasture", home.Cattle, cattle - 10);

		// Anything the county keeps goes by cart, not only the original's grain and cattle; gold does
		// not, the realm keeping one purse.
		home.Wood = 60;
		home.Armoury["spear"] = 5;
		Shipment timber = turns.Dispatch("Home", "Far", new Dictionary<string, int> { ["wood"] = 50, ["spear"] = 5 });
		Is("timber and spears go by cart too", timber != null && home.Wood == 10 && home.Armoury["spear"] == 0, true);
		Is("  but gold does not", turns.Dispatch("Home", "Far", new Dictionary<string, int> { ["gold"] = 1 }) == null, true);
		turns.Shipments.Remove(timber);

		// Through the real file: a cart dropped by a save is a granary burnt.
		SaveGame.Write("Check", turns.Turn, turns.Provinces, new Dictionary<string, float>(), Difficulty.Medium,
			shipments: turns.Shipments);
		SaveGame read = SaveGame.List()[0];
		SaveGame.Forget(read);
		Is("a save carries the cart", read.Shipments.Count, 1);
		Is("  and what is on it", read.Shipments[0].Goods["grain"], 200);

		TurnManager loaded = Carting(b, out _, out ProvinceEconomy farLoaded);
		loaded.Restore(read.Turn, read.Provinces, read.Prices, read.Difficulty, read.Diplomacy, read.Shipments);
		farLoaded = loaded.GetProvince("Far");
		int farGrain = farLoaded.Grain;
		loaded.Haul();
		loaded.Haul();
		Is("two seasons on, the loaded cart is still on the road", loaded.Shipments.Count, 1);
		Is("  and has brought nothing yet", farLoaded.Grain, farGrain);
		loaded.Haul();
		Is("the third season it is in", loaded.Shipments.Count, 0);
		Is("  and its grain is in the barn it was sent to", farLoaded.Grain, farGrain + 200);
		Is("  and the lord is told", Heard(loaded, "supply-arrived"), true);

		// A county lost under a cart: it turns for home, and comes in there.
		TurnManager lost = Carting(b, out ProvinceEconomy sender, out ProvinceEconomy taken);
		lost.Dispatch("Home", "Far", Load(100, 0));
		int senderGrain = sender.Grain;
		lost.Haul();
		taken.Realm = "northern-watch";
		lost.Haul();
		Is("a cart bound for a county lost turns for home", lost.Shipments.Count, 0);
		Is("  and brings its grain back to the barn it left, a season's road away", sender.Grain, senderGrain + 100);
		Is("  and the lord is told", Heard(lost, "supply-turned"), true);

		// Another lord's men on the road take it.
		TurnManager waylaid = Carting(b, out _, out ProvinceEconomy robbedOf);
		FieldArmy foe = waylaid.AnyProvince("Rival").Raise(b.MarchReach);
		foe.Men["spear"] = 40;
		foe.X = 300f;
		foe.Y = 0f;
		waylaid.Dispatch("Home", "Far", Load(100, 0));
		int before = robbedOf.Grain;
		for (int season = 0; season < 3; season++)
		{
			waylaid.Haul();
		}

		Is("a cart that meets another lord's men on the road is taken", waylaid.Shipments.Count, 0);
		Is("  and brings nothing", robbedOf.Grain, before);
		Is("  and the lord is told", Heard(waylaid, "supply-lost"), true);

		// And the whole turn runs it: a cart sent on turn one is not in by turn two.
		TurnManager turning = Carting(b, out _, out _);
		turning.Dispatch("Home", "Far", Load(50, 0));
		turning.AdvanceTurn();
		Is("a turn moves the cart along its road", turning.Shipments.Count == 1 && turning.Shipments[0].X > 0f, true);
	}

	private static bool Heard(TurnManager turns, string id) =>
		turns.RivalNews.Any(item => item.Said.Id == id);

	/// <summary>Home and Far are the crown's, a straight road apart; Rival is far off and nobody's
	/// friend. The road is the straight line, a step every ten paces, which is all MarchGrid.Way is
	/// to a cart.</summary>
	private static TurnManager Carting(GameBalance b, out ProvinceEconomy home, out ProvinceEconomy far)
	{
		var towns = new Dictionary<string, Vector2>
		{
			["Home"] = Vector2.Zero,
			// Two full seasons' reach and a bit, whatever the reach is.
			["Far"] = new Vector2(b.MarchReach * 2.3f, 0f),
			["Rival"] = new Vector2(0f, 6000f),
		};
		var definitions = new List<ProvinceDefinition>();
		foreach (string county in towns.Keys)
		{
			definitions.Add(new ProvinceDefinition { ProvinceName = county, InitialGrain = 1000, InitialCattle = 40 });
		}

		var turns = new TurnManager(b, definitions,
			new Dictionary<string, string> { ["Home"] = "royal-crown", ["Far"] = "royal-crown", ["Rival"] = "northern-watch" },
			"royal-crown", Difficulty.Medium);
		turns.Survey(Straight, _ => "", towns, 38f);
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			county.Armies.Clear();
		}

		home = turns.GetProvince("Home");
		far = turns.GetProvince("Far");
		return turns;
	}

	private static Dictionary<string, int> Load(int grain, int cattle) => new() { ["grain"] = grain, ["cattle"] = cattle };

	private static List<(Vector2 At, float Spent)> Straight(Vector2 from, Vector2 to)
	{
		var road = new List<(Vector2, float)>();
		float length = from.DistanceTo(to);
		for (float walked = 0f; walked < length; walked += 10f)
		{
			road.Add((from.Lerp(to, walked / length), walked));
		}

		road.Add((to, length));
		return road;
	}
}
