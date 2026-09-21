using Godot;

/// <summary>The one runnable check behind the market engine. It moves gold, so a silent regression
/// here either mints it or burns it, and neither shows up on screen until a save is already wrong.
///
/// Run it: Godot --headless --path . res://scene/checks/market-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class MarketCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		// The first half holds the market perfectly still — no merchant's cut, no season, no depth
		// to push against — because what it is testing is that a trade moves exactly what it says it
		// moves. Those guarantees did not change when prices learned to move, and they are the ones
		// that mint or burn gold if they break. The second half turns the market back on.
		var balance = Still();
		var market = new Market(balance);

		Is("grain is priced from the balance", market.Price("grain"), balance.GrainPrice);
		Is("an unlisted store has no price", market.Price("turnips"), 0);
		Is("an unlisted store is not traded", market.Trades("turnips"), false);

		// What the purse stretches to, rounded down: you cannot buy nine tenths of a sack.
		ProvinceEconomy province = Province(gold: 95, grain: 0);
		Is("gold 95 at 10 buys 9", market.Affordable(province, "grain"), 9);
		Is("no gold buys nothing", market.Affordable(Province(0, 0), "grain"), 0);

		// A trade is all of it or none of it.
		province = Province(gold: 100, grain: 20);
		Is("buying 5 grain at 10 succeeds", market.Buy(province, "grain", 5), true);
		Is("  gold paid", province.Gold, 50);
		Is("  grain delivered", province.Grain, 25);

		province = Province(gold: 40, grain: 20);
		Is("buying 5 on 40 gold fails", market.Buy(province, "grain", 5), false);
		Is("  gold untouched", province.Gold, 40);
		Is("  grain untouched", province.Grain, 20);

		province = Province(gold: 0, grain: 30);
		Is("selling 10 grain succeeds", market.Sell(province, "grain", 10), true);
		Is("  gold taken in", province.Gold, 100);
		Is("  grain handed over", province.Grain, 20);

		province = Province(gold: 0, grain: 5);
		Is("selling more than is held fails", market.Sell(province, "grain", 10), false);
		Is("  gold untouched", province.Gold, 0);
		Is("  grain untouched", province.Grain, 5);

		// Arms are not fields on the province — they live in the armoury the smithy fills — and the
		// market has to reach the same way into both kinds of pile.
		Is("a sword is priced from the balance", market.Price("sword"), balance.SwordPrice);
		province = Province(gold: 500, grain: 0);
		Is("an empty armoury holds no swords", province.Stored("sword"), 0);
		Is("buying 2 swords at 120 succeeds", market.Buy(province, "sword", 2), true);
		Is("  gold paid", province.Gold, 260);
		Is("  swords racked", province.Stored("sword"), 2);
		Is("  and racked under their own name", province.Armoury["sword"], 2);
		Is("selling 1 sword back succeeds", market.Sell(province, "sword", 1), true);
		Is("  gold taken in", province.Gold, 380);
		Is("  one sword left", province.Stored("sword"), 1);
		Is("selling a sword the rack has not got fails", market.Sell(province, "sword", 5), false);
		Is("  rack untouched", province.Stored("sword"), 1);
		Is("buying 4 swords on 380 gold fails", market.Buy(province, "sword", 4), false);
		Is("  gold untouched", province.Gold, 380);
		Is("  rack untouched", province.Stored("sword"), 1);

		// The ways a caller might try to conjure something out of nothing.
		province = Province(gold: 100, grain: 10);
		Is("buying nothing fails", market.Buy(province, "grain", 0), false);
		Is("buying a negative amount fails", market.Buy(province, "grain", -5), false);
		Is("selling a negative amount fails", market.Sell(province, "grain", -5), false);
		Is("trading an unlisted store fails", market.Buy(province, "turnips", 1), false);
		Is("  gold untouched by all of that", province.Gold, 100);
		Is("  grain untouched by all of that", province.Grain, 10);
		Is("  and nothing was racked under it", province.Armoury.ContainsKey("turnips"), false);

		Moving();

		GD.Print(_failed == 0 ? "\nmarket engine: all checks passed" : $"\nmarket engine: {_failed} FAILED");
		GetTree().Quit(_failed);
	}

	/// <summary>The half that makes the market worth having: a price that answers the season and
	/// answers what the realm has been doing to it, with a merchant's cut across the middle so that
	/// answering cannot be farmed.</summary>
	private void Moving()
	{
		var balance = new GameBalance();
		var market = new Market(balance);
		market.Turned(Season.Summer); // a season that does nothing to grain either way

		// The cut. Without it a moving price is a money printer, so this is the load-bearing one.
		Is("the merchant asks more than he offers", market.Asking("grain") > market.Offered("grain"), true);

		ProvinceEconomy trader = Province(gold: 10000, grain: 0);
		int purse = trader.Gold;
		market.Buy(trader, "grain", 40);
		market.Sell(trader, "grain", 40);
		Is("a round trip loses money", trader.Gold < purse, true);
		Is("and leaves the goods where they were", trader.Grain, 0);

		// The same trip at a size that used to turn a profit: priced at the figure before the order
		// rather than along it, buying this much moved the price further than the merchant's cut,
		// and selling it straight back minted gold out of nothing.
		var big = new Market(balance);
		big.Turned(Season.Summer);
		ProvinceEconomy whale = Province(gold: 100000, grain: 0);
		purse = whale.Gold;
		big.Buy(whale, "grain", 600);
		big.Sell(whale, "grain", 600);
		Is("and a big round trip loses money too", whale.Gold < purse, true);

		// Dumping a granary is worth less per sack the more of it you dump.
		var glut = new Market(balance);
		glut.Turned(Season.Summer);
		int before = glut.Offered("grain");
		ProvinceEconomy farmer = Province(gold: 0, grain: 4000);
		glut.Sell(farmer, "grain", 3000);
		int after = glut.Offered("grain");
		Is("selling a mountain of grain drives the price down", after < before, true);
		Is("but not below the floor",
			glut.Price("grain") >= Mathf.RoundToInt(balance.GrainPrice * balance.PriceFloor), true);

		// And it comes back, a season at a time, or one glut would ruin grain for the campaign.
		for (int season = 0; season < 8; season++)
		{
			glut.Turned(Season.Summer);
		}

		Is("a glut fades with the seasons", glut.Offered("grain") > after, true);

		// Buying the country dry does the opposite.
		var run = new Market(balance);
		run.Turned(Season.Summer);
		int asking = run.Asking("grain");
		run.Buy(Province(gold: 100000, grain: 0), "grain", 3000);
		Is("buying the country dry drives the price up", run.Asking("grain") > asking, true);
		Is("but not past the ceiling",
			run.Price("grain") <= Mathf.RoundToInt(balance.GrainPrice * balance.PriceCeiling), true);

		// The calendar, which is the only thing in this game that asks a lord to look past next turn.
		var year = new Market(balance);
		year.Turned(Season.Autumn);
		int reaped = year.Offered("grain");
		year.Turned(Season.Spring);
		Is("grain is cheap the week it is reaped and dear in spring", year.Offered("grain") > reaped, true);
		year.Turned(Season.Autumn);
		Is("a quarry does not care what month it is", year.Price("stone"), balance.StonePrice);
	}

	/// <summary>A balance with the market's own weather turned off: no cut, no season, and a depth
	/// nothing can shift.</summary>
	private static GameBalance Still() => new()
	{
		MarketSpread = 0f,
		MarketDepth = 1000000000f,
		GrainSeasonPrice = new[] { 1f, 1f, 1f, 1f },
		CattleSeasonPrice = new[] { 1f, 1f, 1f, 1f },
	};

	private static ProvinceEconomy Province(int gold, int grain) =>
		new() { ProvinceName = "Check", Gold = gold, Grain = grain };

	private void Is<T>(string what, T got, T wanted)
	{
		bool ok = Equals(got, wanted);
		if (!ok)
		{
			_failed++;
		}

		GD.Print($"{(ok ? "ok  " : "FAIL")} {what}{(ok ? "" : $" — got {got}, wanted {wanted}")}");
	}
}
