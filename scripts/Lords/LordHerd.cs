using Godot;

/// <summary>What a rival lord does with his cattle: grass under them as they grow, a herd bought if
/// he has none, and beef off it only once it is more than its grass will breed. The same levers the
/// player has — the fields, the market and the lord's share of beef at the table — and nothing else.
///
/// A cow is worth keeping alive. Milked, she feeds five a season and eats nothing out of the barn;
/// killed, she feeds ten once. So the lord eats only the head his pasture cannot breed, and puts
/// the rest to grass. Left alone, every rival's herd crowded the one pasture the campaign gave it
/// inside two years and stood there, neither breeding nor eaten.</summary>
public static class LordHerd
{
	/// <summary>Head a pasture field carries before the lord wants another under them: a couple under
	/// twenty, the top of the original's second crowding band, where births and deaths come level —
	/// so the season's calves do not tip a full pasture into a herd that stands still.</summary>
	private const int HeadAField = 18;

	/// <summary>What a lord who has no beasts buys to start a herd: a pasture's worth, lightly
	/// stocked.</summary>
	private const int SeedHerd = 10;

	public static void Tend(ProvinceEconomy p, GameBalance b, Market market)
	{
		Graze(p);
		Stock(p, b, market);
		Cull(p);
	}

	/// <summary>Grass enough for the herd, out of the fields lying fallow — a fallow field has no
	/// seed in it to lose, so it can go to pasture in any season.</summary>
	private static void Graze(ProvinceEconomy p)
	{
		while (WantsGrass(p))
		{
			EconomySimulation.SetField(p, System.Array.IndexOf(p.Fields, FieldUse.Fallow), FieldUse.Pasture);
		}
	}

	/// <summary>Whether the herd has outgrown its grass, and may have another field of it. Never more
	/// pastures than fields under grain, never while the soil is spent, and never the fallow the
	/// grain needs: a fallow field puts back twice what a grain field takes out, so he keeps one for
	/// every two under grain, and always one. Grass does nothing for the soil and grows no bread —
	/// without those limits Valmere was all pasture inside thirty years, the soil spent, not a field
	/// sown and nowhere left to sow one.</summary>
	public static bool WantsGrass(ProvinceEconomy p)
	{
		int pastures = p.FieldsUnder(FieldUse.Pasture);
		int grain = p.FieldsUnder(FieldUse.Grain);
		int fallow = p.FieldsUnder(FieldUse.Fallow);
		bool isOutgrown = pastures == 0 || p.Cattle > pastures * HeadAField;
		return isOutgrown && pastures < Mathf.Max(1, grain) && p.Soil > 0
			&& fallow > 1 && (fallow - 1) * 2 >= grain;
	}

	/// <summary>A lord with a pasture and no herd to speak of buys one, out of what he holds above his
	/// reserve: bought once, a herd breeds itself for ever after.</summary>
	private static void Stock(ProvinceEconomy p, GameBalance b, Market market)
	{
		if (p.Cattle >= SeedHerd || p.FieldsUnder(FieldUse.Pasture) == 0 || !market.Trades("cattle")
			|| p.Gold <= b.LordGoldReserve)
		{
			return;
		}

		int affordable = Mathf.Min(market.Affordable(p, "cattle"),
			(p.Gold - b.LordGoldReserve) / Mathf.Max(1, market.Price("cattle")));
		market.Buy(p, "cattle", Mathf.Min(SeedHerd - p.Cattle, affordable));
	}

	/// <summary>Beef only off what the grass will not carry, once he has no more grass to give it: the
	/// head above what his pastures hold are put on the table this season, as a share of what the
	/// dairy leaves to feed. Culled any sooner, the herd never outgrew its pastures and he never gave
	/// it another.</summary>
	private static void Cull(ProvinceEconomy p)
	{
		int surplus = p.Cattle - (p.FieldsUnder(FieldUse.Pasture) * HeadAField);
		int rest = Livelihood.Portions(p.Population + p.FieldMen, p.Ration) - (p.Cattle * Livelihood.FedByDairy);
		p.BeefShare = surplus <= 0 || rest <= 0 ? 0
			: Mathf.Min(100, surplus * Livelihood.FedByBeef * 100 / rest);
	}
}
