using System.Collections.Generic;
using Godot;

/// <summary>What a store is worth, and what a purse can actually do at that price.
///
/// Every trade in the game goes through here rather than through whatever screen asked for it: the
/// screen says "buy 50 grain for Kingsreach" and is told yes or no. That is the point of it being
/// engine and not UI — a caravan, an AI lord or a scripted event can trade on the same terms as the
/// player, and none of them can spend gold the province does not have.
///
/// A price is not a number in a table. It is made of three things:
///
/// The BASE, out of the balance, which is what the good is worth in an ordinary year.
///
/// The SEASON, which is why anybody plans ahead. Grain is cheap the week it is reaped and dear the
/// week before the next harvest, and a lord who sells his whole granary in autumn buys it back in
/// spring at half again. Nothing else in the game asks a player to look further than next turn.
///
/// The PRESSURE, which is what his own trading did to it. Dumping four hundred sacks on the market
/// drives the price down and it stays down for a few seasons; buying the country dry drives it up
/// the same way. That is the loop this game is copied from: a surplus is only worth what somebody
/// will pay for it, and everyone else is selling their surplus in the same week you are.
///
/// And a SPREAD between the two sides of the counter, because without it a moving price is a money
/// printer: sell until the price drops, buy it all back cheaper, repeat. The merchant takes his cut
/// both ways, so a round trip loses money and holding goods through a season is the only way to
/// make any.</summary>
public class Market
{
	private readonly GameBalance _balance;
	private Season _season;

	/// <summary>How far each store has been pushed off its base price by trading, as a fraction: a
	/// half means half again as dear. Public because a campaign has to be able to save it — a price
	/// that resets when the game is reloaded is a price a player can launder a granary through.</summary>
	public Dictionary<string, float> Pressure { get; set; } = new();

	public Market(GameBalance balance) => _balance = balance;

	/// <summary>What a store is worth in an ordinary year, before the season or anybody's trading.
	/// Zero for anything the market does not deal in, which is what stops it being traded at all
	/// rather than being traded for free.</summary>
	public int Base(string store) => store switch
	{
		"grain" => _balance.GrainPrice,
		"cattle" => _balance.CattlePrice,
		"wood" => _balance.WoodPrice,
		"stone" => _balance.StonePrice,
		"iron" => _balance.IronPrice,

		// Finished arms, traded out of the armoury the smithy fills.
		"sword" => _balance.SwordPrice,
		"bow" => _balance.BowPrice,
		"crossbow" => _balance.CrossbowPrice,
		"spear" => _balance.SpearPrice,
		"mace" => _balance.MacePrice,
		"horse" => _balance.HorsePrice,

		_ => 0,
	};

	public bool Trades(string store) => Base(store) > 0;

	/// <summary>What the good is worth today — the middle of the counter, which neither side of a
	/// trade actually pays. The season moves it, the realm's own trading moves it, and the balance's
	/// floor and ceiling stop either from running away: a market that can be driven to nothing is
	/// one a player empties his granary into once and never uses again.</summary>
	public int Price(string store) => PriceAt(store, Pressure.GetValueOrDefault(store));

	/// <summary>What the good would be worth if the market had been pushed this far off its base.
	/// Taken as an argument rather than read off the book, because an order has to be priced along
	/// the move it is itself causing — see <see cref="Worth"/>.</summary>
	private int PriceAt(string store, float pressure)
	{
		int start = Base(store);
		if (start <= 0)
		{
			return 0;
		}

		float moved = InSeason(store) * (1f + pressure);
		return Mathf.Max(1, Mathf.RoundToInt(start * Mathf.Clamp(moved, _balance.PriceFloor, _balance.PriceCeiling)));
	}

	/// <summary>What the merchant asks for one, and what he offers for one. He is not a charity: the
	/// spread between these is his, and it is what a trade costs before the goods have moved at
	/// all.</summary>
	public int Asking(string store) => Mathf.CeilToInt(Price(store) * (1f + _balance.MarketSpread));

	public int Offered(string store) => Mathf.FloorToInt(Price(store) * (1f - _balance.MarketSpread));

	/// <summary>What the whole order comes to — priced HALFWAY ALONG the move it causes, not at the
	/// price before it.
	///
	/// This is the difference between a market and a money printer. Price the order at today's
	/// figure and then push the price afterwards, and a big enough order pays for itself: buy two
	/// hundred sacks at the old price, watch your own buying drive the price up, sell them straight
	/// back at the new one, profit. The merchant's cut only covers the small orders — anything past
	/// about sixty sacks moved the price further than the cut, and the exploit opened.
	///
	/// Pricing at the midpoint is what a real order book does to you: the first sack goes at the old
	/// price, the last at the new one, and you pay the average. A round trip then loses the cut, at
	/// every size, which is the invariant the check pins down.</summary>
	public int Worth(string store, int amount, bool buying)
	{
		float slip = Move(store, buying ? amount : -amount) * 0.5f;
		int along = PriceAt(store, Pressure.GetValueOrDefault(store) + slip);
		return (buying ? Mathf.CeilToInt(along * (1f + _balance.MarketSpread))
			: Mathf.FloorToInt(along * (1f - _balance.MarketSpread))) * amount;
	}

	/// <summary>How many units the province's gold will stretch to. This is the ceiling the buy
	/// side is held to — you cannot buy on credit here.</summary>
	public int Affordable(ProvinceEconomy province, string store)
	{
		int asking = Asking(store);
		if (asking <= 0 || province == null)
		{
			return 0;
		}

		// The marginal price is only the first sack's; the rest cost more as the order moves the
		// market. Halved down from there until the whole order fits the purse, so the number the
		// slider is capped at is one a trade will actually go through at — an "affordable" that the
		// till then refuses is worse than no figure at all.
		int most = province.Gold / asking;
		int least = 0;
		while (least < most)
		{
			int tried = (least + most + 1) / 2;
			if (Worth(store, tried, buying: true) <= province.Gold)
			{
				least = tried;
			}
			else
			{
				most = tried - 1;
			}
		}

		return least;
	}

	/// <summary>Gold out, goods in. False — and nothing moved — if the province cannot pay for all
	/// of it: a trade is one thing or nothing, never as much as the purse happened to cover.</summary>
	public bool Buy(ProvinceEconomy province, string store, int amount)
	{
		int cost = Worth(store, amount, buying: true);
		if (province == null || amount <= 0 || !Trades(store) || province.Gold < cost)
		{
			return false;
		}

		province.Gold -= cost;
		province.Add(store, amount);
		Push(store, amount);
		return true;
	}

	/// <summary>Goods out, gold in. False if the province does not hold that much of it.</summary>
	public bool Sell(ProvinceEconomy province, string store, int amount)
	{
		if (province == null || amount <= 0 || !Trades(store) || province.Stored(store) < amount)
		{
			return false;
		}

		province.Add(store, -amount);
		province.Gold += Worth(store, amount, buying: false);
		Push(store, -amount);
		return true;
	}

	/// <summary>A season passes over the market: what the realm's trading did to a price fades, a
	/// little at a time, back towards what the good is worth.
	///
	/// The fading is the whole reason a price is worth watching. Without it the first lord to dump a
	/// granary ruins grain for the rest of the campaign; with it, a glut is a few bad seasons to
	/// wait out, and knowing how long to wait is the skill.</summary>
	public void Turned(Season season)
	{
		_season = season;
		foreach (string store in new List<string>(Pressure.Keys))
		{
			float left = Pressure[store] * (1f - _balance.PriceDrift);
			// Below a hundredth it is not a price the player can see any more, and leaving the dust
			// in means the dictionary fills with stores nobody has touched in twenty years.
			if (Mathf.Abs(left) < 0.01f)
			{
				Pressure.Remove(store);
			}
			else
			{
				Pressure[store] = left;
			}
		}
	}

	/// <summary>What a trade does to the price of the thing traded. Measured in what the order was
	/// worth at the BASE price rather than in units, so one horse moves the market as much as
	/// twenty-six sacks of grain and a single depth figure covers every store on the counter.</summary>
	private void Push(string store, int amount) =>
		Pressure[store] = Mathf.Clamp(Pressure.GetValueOrDefault(store) + Move(store, amount),
			_balance.PriceFloor - 1f, _balance.PriceCeiling - 1f);

	/// <summary>How far an order of this size shifts the price, as a fraction of the base. Measured
	/// in what the order is worth at the BASE price rather than in units, so one horse moves the
	/// market as much as twenty-six sacks of grain and a single depth figure covers the counter.</summary>
	private float Move(string store, int amount) =>
		amount * Base(store) / Mathf.Max(1f, _balance.MarketDepth);

	/// <summary>What the time of year does to a price. Only the field's own produce answers to the
	/// calendar: a quarry cuts the same stone in March as in September, but grain is worth what
	/// hunger says it is worth, and everybody's barn is full in the same week.</summary>
	private float InSeason(string store) => store switch
	{
		"grain" => _balance.GrainSeasonPrice[(int)_season],
		"cattle" => _balance.CattleSeasonPrice[(int)_season],
		_ => 1f,
	};
}
