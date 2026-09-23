using Godot;

/// <summary>What a lord the player is not does with his county, once a season. One province, decided
/// in the moment before <see cref="EconomySimulation.RunTurn"/> is run over it — so a rival is not a
/// second economy with rules of his own, he is another hand on the same four levers the player has:
/// the ration, the tax, where the hands go, and what goes to market. What he cannot do through
/// those, he cannot do.
///
/// THAT IS THE RULE WORTH KEEPING. The moment a lord is given a number the player cannot reach —
/// grain from nowhere, a harvest multiplier, gold the reeve never collected — the player's own
/// economy stops being a way to read his rival's, and a game about running a county becomes
/// guesswork about what the computer is allowed. So difficulty here is COMPETENCE and not a
/// handicap: an easy lord leaves hands standing about, sells his bread for whatever is offered and
/// squeezes his people until they walk out; a hard one does none of the three. Both play the game
/// the player is playing. If a campaign ever really wants a lord to start ahead, that belongs in his
/// province's authored stores, where it is on the map and can be seen.
///
/// He gives his orders at the end of the turn, where the player's own orders land — the season is
/// then run over every county at once, so nobody is answering a harvest that has not happened yet.</summary>
public static class LordAI
{
	/// <summary>How far above his floor a county's goodwill has to stand before its lord risks the
	/// heavy tax. A band rather than a line: a county sitting exactly at the floor would otherwise
	/// flip rate every season, which teaches its people nothing and reads as a lord who cannot make
	/// up his mind.</summary>
	private const float ComfortBand = 20f;

	/// <summary>How far off the fair rate he moves when he does move: lighter on a county that has
	/// had enough of him, heavier on one that can plainly carry it.</summary>
	private const int TaxRelief = 3;
	private const int TaxGreed = 4;

	public static void TakeTurn(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Market market,
		Season season, Difficulty skill)
	{
		Feed(p, b, skill);
		Victual(p, b, skill);
		Tax(p, b, skill);
		Plough(p, b, season, skill);
		Work(p, def, b, season, skill);
		Trade(p, b, market, skill);
		SellSurplus(p, b, market);
	}

	/// <summary>Bread is the cheapest goodwill in this game and hunger the most expensive grievance,
	/// so he feeds his people as well as the barn allows and cuts the ration only when it plainly
	/// cannot carry them. Cutting early keeps grain he did not need to keep; cutting late is a
	/// famine, and a famine costs him people as well as their goodwill.
	///
	/// Measured against the reserve he means to keep rather than a flat pile of sacks, so the lord
	/// who counts three seasons ahead is also the one who is slow to panic and slow to feast: he
	/// goes short only when the barn is under half of what he wants in it, and feeds well only once
	/// it holds a season's slack above it. A lord who keeps one season in hand is feasting on what a
	/// careful one would call a thin barn — which is the whole difference between them in the year
	/// they both have a bad harvest.</summary>
	private static void Feed(ProvinceEconomy p, GameBalance b, Difficulty skill)
	{
		// Double only when the barn can pay for the feast and still hold the reserve afterwards. It
		// was laid on a season's ordinary bread of slack, which a double table eats twice over: the
		// feast came out of the reserve, and the market bought it back.
		int reserve = Reserve(p, b, skill);
		p.Ration = p.Grain * 2 < reserve ? RationLevel.Half
			: p.Grain >= reserve + Meal(p, b, RationLevel.Double) ? RationLevel.Double
			: RationLevel.Normal;
	}

	/// <summary>He keeps bread behind his own gate.
	///
	/// A castle is worth what it can hold out on, and a lord who only thinks of it once there is an
	/// army at his border is a lord whose keep falls in three seasons — which, before this, is
	/// exactly what every rival in the game would have done. It is the same lever the player has and
	/// on the same terms: his own county's grain, nothing spent, and the walls hold what the walls
	/// hold. A careful lord keeps a deeper reserve, so he stocks later and more surely; that is the
	/// whole of the difference between them, the way it is everywhere else in here.
	///
	/// Not while he is being besieged. Nothing gets in through a siege line, and a lord quietly
	/// restocking his larder out of fields somebody else's army is camped on would make a siege a
	/// thing that never ends.</summary>
	private static void Victual(ProvinceEconomy p, GameBalance b, Difficulty skill)
	{
		int room = Fortifications.Of(p.Fortification).Stores;
		if (room <= p.CastleStores || p.BesiegedFrom.Length > 0)
		{
			return;
		}

		int carried = Mathf.Clamp(p.Grain - Kept(p, b, skill), 0, room - p.CastleStores);
		p.CastleStores += carried;
		p.Grain -= carried;
	}

	/// <summary>He taxes as hard as his people will carry and no harder. The floor is the whole
	/// difference between a poor lord and a good one: a poor one reads the treasury and squeezes
	/// until the county rises, and gold in a hall nobody obeys buys nothing.
	///
	/// Three rates rather than a point at a time, because a lord who nudges his tax by one percent
	/// every season is a lord whose county never learns what to expect of him.</summary>
	private static void Tax(ProvinceEconomy p, GameBalance b, Difficulty skill)
	{
		float floor = b.LordTaxFloor[(int)skill];
		p.Tax = p.Loyalty < floor ? Mathf.Max(0, b.FairTaxPercent - TaxRelief)
			: p.Loyalty > floor + ComfortBand ? Mathf.Min(b.MostTaxPercent, b.FairTaxPercent + TaxGreed)
			: b.FairTaxPercent;
	}

	/// <summary>The spring rotation: worn fields rested, rested ones sown, and if the county came
	/// through the winter short, one more field under grain. In spring only, because turning a field
	/// that is already sown throws the seed away with it.</summary>
	private static void Plough(ProvinceEconomy p, GameBalance b, Season season, Difficulty skill)
	{
		if (season != Season.Spring)
		{
			return;
		}

		// No more under grain than the county can reap. A field is worth its seed only if there are
		// hands for it in autumn; past that the same harvest comes in off more seed, and a lord who
		// ploughed up another field every hungry spring spent his last sacks sowing ground nobody
		// would cut — Valmere had seven fields in, hands for three, and starved on its own seed.
		int reapable = Mathf.Max(1, Mathf.FloorToInt(
			p.Population * b.LordReapShare / b.GrainWorkersPerField[(int)Season.Autumn]));

		// The rotation first: rested fields back under grain, then the most worn to rest — but no
		// more than a quarter of them in one spring. Every field starts the campaign equally fresh
		// and wears at the same pace, so resting all that were tired rested all of them at once, and
		// the county had a year with nothing in the ground.
		for (int field = 0; field < p.Fields.Length; field++)
		{
			if (p.Fields[field] == FieldUse.Fallow && p.Fertility[field] >= b.LordSowsAbove
				&& p.FieldsUnder(FieldUse.Grain) < reapable)
			{
				EconomySimulation.SetField(p, field, FieldUse.Grain);
			}
		}

		int resting = Mathf.CeilToInt(p.FieldsUnder(FieldUse.Grain) / 4f);
		for (int rested = 0; rested < resting || p.FieldsUnder(FieldUse.Grain) > reapable; rested++)
		{
			int worn = -1;
			for (int field = 0; field < p.Fields.Length; field++)
			{
				bool tired = p.Fertility[field] < b.LordRestsBelow || p.FieldsUnder(FieldUse.Grain) > reapable;
				if (p.Fields[field] == FieldUse.Grain && tired
					&& (worn < 0 || p.Fertility[field] < p.Fertility[worn]))
				{
					worn = field;
				}
			}

			if (worn < 0)
			{
				break;
			}

			EconomySimulation.SetField(p, worn, FieldUse.Fallow);
		}

		if (p.Grain >= Reserve(p, b, skill) || p.FieldsUnder(FieldUse.Grain) >= reapable)
		{
			return;
		}

		// Short of bread, one more field goes under the plough — the best-rested of what is lying
		// fallow, since a tired one gives him little for the seed.
		int best = -1;
		for (int field = 0; field < p.Fields.Length; field++)
		{
			if (p.Fields[field] == FieldUse.Fallow && (best < 0 || p.Fertility[field] > p.Fertility[best]))
			{
				best = field;
			}
		}

		if (best >= 0)
		{
			EconomySimulation.SetField(p, best, FieldUse.Grain);
		}
	}

	/// <summary>Where the county's hands go. The same deployment a province opens on — reused rather
	/// than written twice, so the day somebody changes what a season asks for, the lords hear about
	/// it in the same commit the player does.</summary>
	private static void Work(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, Difficulty skill)
	{
		EconomySimulation.Deploy(p, def, b, season);

		float astray = b.LordIdleHands[(int)skill];
		if (astray <= 0f)
		{
			return;
		}

		// A poor lord's county is not short of people. It is short of anybody telling them where to
		// be — the hands are there, counted, standing in the wrong field. Which is what the player
		// sees when his own harvest comes in heavier off worse land than his neighbour's.
		p.GrainWorkers = Attending(p.GrainWorkers, astray);
		p.CattleWorkers = Attending(p.CattleWorkers, astray);
		p.WoodWorkers = Attending(p.WoodWorkers, astray);
		p.StoneWorkers = Attending(p.StoneWorkers, astray);
		p.IronWorkers = Attending(p.IronWorkers, astray);
	}

	private static int Attending(int hands, float astray) => Mathf.FloorToInt(hands * (1f - astray));

	/// <summary>He trades at the same counter the player does, on the realm's one market. That is
	/// deliberate: a lord dumping his harvest is a price the player watches fall, and a bad year in
	/// the north is dear bread in the south. A rival who traded on a market of his own would be a
	/// number in a file nobody can feel.</summary>
	private static void Trade(ProvinceEconomy p, GameBalance b, Market market, Difficulty skill)
	{
		int kept = Kept(p, b, skill);
		if (p.Grain < kept)
		{
			// Bread first and at any price. A county that is short does not haggle, and the purse is
			// the only ceiling — the market will not sell him what he cannot pay for — so he fills
			// the purse first out of whatever his county makes and does not eat.
			RaiseSilver(p, market, market.Worth("grain", kept - p.Grain, buying: true));
			market.Buy(p, "grain", Mathf.Min(kept - p.Grain, market.Affordable(p, "grain")));
			return;
		}

		// Surplus, and only at a price he is willing to take. An easy lord takes almost whatever is
		// on the counter; a hard one sits on his grain until the market wants it — which, in a game
		// where wheat is cheap every autumn and dear every spring, is most of what trading well is.
		//
		// The lot is halved until the WHOLE of it clears his price, because a big enough sale pushes
		// the price down through it: he would be selling the tail of his own order into the hole he
		// dug with the front of it. So he sells the largest lot that still clears — which is what a
		// man with grain to sell actually does, and it is also what keeps one lord's good harvest
		// from flattening the realm's grain price, the player's included, every spring.
		int lot = p.Grain - kept;
		int least = Mathf.CeilToInt(market.Base("grain") * b.LordSellsAbove[(int)skill]);
		while (lot > 0 && market.Worth("grain", lot, buying: false) < lot * least)
		{
			lot /= 2;
		}

		market.Sell(p, "grain", lot);
	}

	/// <summary>What the county digs and does not use goes to market every season, not only in the
	/// season the bread runs out. A county whose fields cannot feed it lives on its mines, and a lord
	/// who sold iron only when the barn was already empty sold it all at once, into a price his own
	/// sale had flattened, and starved anyway.</summary>
	private static void SellSurplus(ProvinceEconomy p, GameBalance b, Market market)
	{
		foreach (string store in Wares)
		{
			int spare = p.Stored(store) - b.LordKeepsWares;
			if (spare > 0 && market.Trades(store))
			{
				market.Sell(p, store, spare);
			}
		}
	}

	/// <summary>Sells what the county makes and does not eat — its iron, its timber, its stone, down
	/// to a working stock — until the purse holds <paramref name="bill"/>, and not a sack further.
	/// Valmere's fields feed about three fifths of its people and its mines are the richest on the
	/// island: its lord traded grain and nothing else, and starved on top of five hundred ingots.
	/// Only as much as the bread costs, because every lot sold pushes the price down under the next,
	/// and a lord who dumps his whole yard to buy one season's bread is poorer every season after.</summary>
	private static void RaiseSilver(ProvinceEconomy p, Market market, int bill)
	{
		foreach (string store in Wares)
		{
			int spare = p.Stored(store) - WorkingStock;
			if (p.Gold >= bill)
			{
				return;
			}

			if (spare <= 0 || !market.Trades(store))
			{
				continue;
			}

			int lot = Mathf.Min(spare, Mathf.CeilToInt((bill - p.Gold) / (float)Mathf.Max(1, market.Offered(store))));
			market.Sell(p, store, lot);
		}
	}

	/// <summary>What a lord sells for bread, dearest first, and how much of each he keeps back to work
	/// with.</summary>
	private static readonly string[] Wares = { "iron", "stone", "wood" };
	private const int WorkingStock = 60;

	/// <summary>The bread he means to keep in the barn: so many seasons of it, counted for the people
	/// he has and the men he keeps under arms. Everything above it he will sell and everything below
	/// it he will buy, so this one number is his whole food policy — and how many seasons ahead he
	/// counts is most of what separates a lord who survives a bad year from one who does not.</summary>
	private static int Reserve(ProvinceEconomy p, GameBalance b, Difficulty skill) =>
		Mathf.CeilToInt(Need(p, b) * b.LordGrainSeasons[(int)skill]);

	/// <summary>What stays in the barn whatever the market offers: the reserve, and on top of it the
	/// meal the county is about to eat on the ration he has just set. He used to sell everything
	/// above the reserve and then feed his people out of the reserve itself — a lord with a double
	/// table laid emptied his own barn in a season and bought it back the next at any price, and
	/// the Northern Watch starved to death in twenty years without a blow being struck.</summary>
	private static int Kept(ProvinceEconomy p, GameBalance b, Difficulty skill) =>
		Reserve(p, b, skill) + Meal(p, b, p.Ration);

	/// <summary>What the county eats in a season on a given ration, the garrison with it — the same
	/// arithmetic the meal itself is served by (EconomySimulation.Eat).</summary>
	private static int Meal(ProvinceEconomy p, GameBalance b, RationLevel ration) =>
		Mathf.CeilToInt(p.Population / b.PeoplePerGrain * b.RationFoodMultiplier[(int)ration])
		+ Mathf.CeilToInt(p.Fed / b.PeoplePerGrain * b.SoldierAppetite);

	/// <summary>One season's bread at the ordinary ration: the people on it, and the garrison, which
	/// is not on it. Read off the same constants the simulation actually eats by rather than a second
	/// set that would drift from them.</summary>
	private static int Need(ProvinceEconomy p, GameBalance b) =>
		Mathf.CeilToInt(p.Population / b.PeoplePerGrain)
		+ Mathf.CeilToInt(p.Soldiers / b.PeoplePerGrain * b.SoldierAppetite);
}
