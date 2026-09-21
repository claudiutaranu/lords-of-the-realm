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
		Tax(p, b, skill);
		Plough(p, b, season, skill);
		Work(p, def, b, season, skill);
		Trade(p, b, market, skill);
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
		int reserve = Reserve(p, b, skill);
		p.Ration = p.Grain * 2 < reserve ? RationLevel.Half
			: p.Grain >= reserve + Need(p, b) ? RationLevel.Double
			: RationLevel.Normal;
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

	/// <summary>A county that came through the winter short puts another field under grain. In
	/// spring only, because turning a field that is already sown throws the seed away with it, and
	/// one field a year, because the rest of the land still has to rest or there is no harvest to
	/// have. A lord with bread in the barn leaves his land alone.</summary>
	private static void Plough(ProvinceEconomy p, GameBalance b, Season season, Difficulty skill)
	{
		if (season != Season.Spring || p.Grain >= Reserve(p, b, skill))
		{
			return;
		}

		for (int field = 0; field < p.Fields.Length; field++)
		{
			if (p.Fields[field] == FieldUse.Fallow)
			{
				EconomySimulation.SetField(p, field, FieldUse.Grain);
				return;
			}
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
		int reserve = Reserve(p, b, skill);
		if (p.Grain < reserve)
		{
			// Bread first and at any price. A county that is short does not haggle, and the purse is
			// the only ceiling — the market will not sell him what he cannot pay for.
			market.Buy(p, "grain", Mathf.Min(reserve - p.Grain, market.Affordable(p, "grain")));
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
		int lot = p.Grain - reserve;
		int least = Mathf.CeilToInt(market.Base("grain") * b.LordSellsAbove[(int)skill]);
		while (lot > 0 && market.Worth("grain", lot, buying: false) < lot * least)
		{
			lot /= 2;
		}

		market.Sell(p, "grain", lot);
	}

	/// <summary>The bread he means to keep in the barn: so many seasons of it, counted for the people
	/// he has and the men he keeps under arms. Everything above it he will sell and everything below
	/// it he will buy, so this one number is his whole food policy — and how many seasons ahead he
	/// counts is most of what separates a lord who survives a bad year from one who does not.</summary>
	private static int Reserve(ProvinceEconomy p, GameBalance b, Difficulty skill) =>
		Mathf.CeilToInt(Need(p, b) * b.LordGrainSeasons[(int)skill]);

	/// <summary>One season's bread at the ordinary ration: the people on it, and the garrison, which
	/// is not on it. Read off the same constants the simulation actually eats by rather than a second
	/// set that would drift from them.</summary>
	private static int Need(ProvinceEconomy p, GameBalance b) =>
		Mathf.CeilToInt(p.Population / b.PeoplePerGrain)
		+ Mathf.CeilToInt(p.Soldiers / b.PeoplePerGrain * b.SoldierAppetite);
}
