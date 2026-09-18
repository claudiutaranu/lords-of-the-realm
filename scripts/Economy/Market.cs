using Godot;

/// <summary>What a store is worth, and what a purse can actually do at that price.
///
/// Every trade in the game goes through here rather than through whatever screen asked for it: the
/// screen says "buy 50 grain for Kingsreach" and is told yes or no. That is the point of it being
/// engine and not UI — a caravan, an AI lord or a scripted event can trade on the same terms as the
/// player, and none of them can spend gold the province does not have.
///
/// One price per store, paid either way. A spread between what the realm buys at and sells at, and
/// a price that answers what has been hoarded or dumped, both belong here when there is a market to
/// tune them against. They are not here yet on purpose.</summary>
public class Market
{
	private readonly GameBalance _balance;

	public Market(GameBalance balance) => _balance = balance;

	/// <summary>What one unit fetches. Zero for anything the market does not deal in, which is what
	/// stops it being traded at all rather than being traded for free.</summary>
	public int Price(string store) => store switch
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

	public bool Trades(string store) => Price(store) > 0;

	/// <summary>What the whole order comes to.</summary>
	public int Worth(string store, int amount) => Price(store) * amount;

	/// <summary>How many units the province's gold will stretch to. This is the ceiling the buy
	/// side is held to — you cannot buy on credit here.</summary>
	public int Affordable(ProvinceEconomy province, string store)
	{
		int price = Price(store);
		return price <= 0 || province == null ? 0 : province.Gold / price;
	}

	/// <summary>How many units the province could put on the counter.</summary>
	public int Sellable(ProvinceEconomy province, string store) =>
		!Trades(store) || province == null ? 0 : province.Stored(store);

	/// <summary>Gold out, goods in. False — and nothing moved — if the province cannot pay for all
	/// of it: a trade is one thing or nothing, never as much as the purse happened to cover.</summary>
	public bool Buy(ProvinceEconomy province, string store, int amount)
	{
		if (province == null || amount <= 0 || !Trades(store) || province.Gold < Worth(store, amount))
		{
			return false;
		}

		province.Gold -= Worth(store, amount);
		province.Add(store, amount);
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
		province.Gold += Worth(store, amount);
		return true;
	}

	/// <summary>The engine's own prices, not a campaign's: every realm trades on the same terms.</summary>
	public static Market FromBalance() =>
		new(GD.Load<GameBalance>("res://data/game-balance.tres"));
}
