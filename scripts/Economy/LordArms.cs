using System.Collections.Generic;
using Godot;

/// <summary>A rival lord's smithy and muster yard, once a season. He forges what his yard can pay for
/// and raises a company when he has the arms and the county will bear it; where it marches is
/// LordsCampaign's business.
///
/// How many men he keeps in the field is the difficulty, and it is a share of his people rather than
/// a number (LordArmyShare): an easy lord one in sixteen, a hard one one in seven, which is the
/// difference between a rival who takes the empty country around him and one who comes for the
/// player's. Never more than his taxes will pay: an army he cannot pay deserts on him, and a rival
/// who raised one anyway would only be feeding the player's. Men waiting at home before they march
/// are quartered on the county and it resents them, which is its own brake on a greedy lord.</summary>
public static class LordArms
{
	/// <summary>What one batch of ten of each weapon costs him — spears when there is iron, bows when
	/// there is only timber. ponytail: a rival buys his arms outright a season ahead instead of
	/// staffing a smithy the way the player's county does (Smithy); the day LordAI deals smiths
	/// their hands, this goes and he lights a forge like anyone else.</summary>
	private const int SpearWood = 30;
	private const int SpearIron = 8;
	private const int BowWood = 25;
	private const int BowGold = 10;
	private const int Batch = 10;

	/// <param name="realmOf">Who holds a county, by name — for telling the lord's own ground from
	/// the ground his men are campaigning on.</param>
	/// <param name="market">Where he buys what his smithy cannot make fast enough; none, and he
	/// makes do with his smithy.</param>
	public static void Arm(ProvinceEconomy p, GameBalance b, Difficulty skill, System.Func<string, string> realmOf,
		Market market = null)
	{
		int room = Room(p, b, skill);
		if (room < 0)
		{
			MusterOut(p, -room, realmOf);
			return;
		}

		if (room == 0)
		{
			return;
		}

		// A band standing in the county is bought whole when the chest will stand it: men who need no
		// arms and cost no sons.
		room -= Hire(p, b, skill, room);

		// Paid for now, racked after the levy: what he forges or buys this season arms next season's
		// men, as it always has.
		(string weapon, int forged) = Forge(p, b, room);
		int bought = Buy(p, b, skill, market, room - forged);
		Levy(p, b, skill, room);
		if (forged > 0)
		{
			p.Add(weapon, forged);
		}

		if (bought > 0)
		{
			p.Add("spear", bought);
		}
	}

	/// <summary>What he can spend on the war this season: a share of what is above the gold he keeps
	/// back whatever happens.</summary>
	private static int WarChest(ProvinceEconomy p, GameBalance b, Difficulty skill) =>
		Mathf.FloorToInt(Mathf.Max(0, p.Gold - b.LordGoldReserve) * b.LordWarChest[(int)skill]);

	/// <summary>Spears off the market for the men his smithy has not armed yet, out of the war chest.
	/// Returns how many, for the armoury after the levy.</summary>
	private static int Buy(ProvinceEconomy p, GameBalance b, Difficulty skill, Market market, int unarmed)
	{
		int armed = p.Armoury.GetValueOrDefault("spear") + p.Armoury.GetValueOrDefault("bow");
		int wanted = unarmed - armed;
		if (market == null || wanted <= 0)
		{
			return 0;
		}

		// The price climbs as he buys, so the order is cut back until the whole of it fits the chest.
		int chest = WarChest(p, b, skill);
		int count = Mathf.Min(wanted, chest / Mathf.Max(1, market.Asking("spear")));
		while (count > 0 && market.Worth("spear", count, buying: true) > chest)
		{
			count = count * 3 / 4;
		}

		if (count <= 0 || !market.Buy(p, "spear", count))
		{
			return 0;
		}

		// Market.Buy racks them at once; they are taken back off and racked after the levy instead.
		p.Add("spear", -count);
		return count;
	}

	/// <summary>Takes a band standing in the county, whole, if the war chest pays for it and he has
	/// room in the field for most of it. Returns how many men that was.</summary>
	private static int Hire(ProvinceEconomy p, GameBalance b, Difficulty skill, int room)
	{
		MercenaryBand band = Mercenaries.Standing(p);
		int men = p.MercenaryMen;
		if (band == null || men <= 0 || band.Gold > WarChest(p, b, skill) || men > room * 2)
		{
			return 0;
		}

		p.Gold -= band.Gold;
		FieldArmy company = p.Raise(b.MarchReach);
		company.Men[band.Key] = men;
		Mercenaries.Hire(p, men);
		return men;
	}

	/// <summary>How many more men this county will put in the field: its share of the people, less
	/// the companies it already has out, and no more than his taxes and a share of his treasury pay
	/// for with the watch on the gate paid first. The watch is not counted against the share — it is the county's defence, not
	/// its army, and counting it left Valmere, with sixty on its walls, no room to raise a man.</summary>
	public static int Room(ProvinceEconomy p, GameBalance b, Difficulty skill)
	{
		int share = Mathf.FloorToInt(p.Population * b.LordArmyShare[(int)skill]) - p.FieldMen;
		// ponytail: the realm's one purse is counted by each of his counties as if it were its own;
		// a lord with many counties would overspend it, and the share wants dividing among them then.
		float wages = EconomySimulation.TaxDue(p, b) * b.LordWagesShare
			+ Mathf.Max(0, p.Gold - b.LordGoldReserve) * b.LordTreasuryShare[(int)skill];
		int paid = Mathf.FloorToInt(wages / Mathf.Max(0.01f, b.WagePerSoldier)) - p.Soldiers;
		return Mathf.Min(share, paid);
	}

	/// <summary>Sends home what the county has in the field past its share, from companies standing on
	/// the lord's own ground — never from men on campaign or at somebody's gate. A lord who never let
	/// a man go ended the war with eight hundred under arms, all of them paid and fed by whichever
	/// county their banners had been merged into, and starved it. They go back to the county that
	/// raised them, largest company first.</summary>
	private static void MusterOut(ProvinceEconomy p, int surplus, System.Func<string, string> realmOf)
	{
		var home = p.Armies.FindAll(army => realmOf(army.County) == p.Realm);
		home.Sort((x, y) => y.Strength != x.Strength ? y.Strength.CompareTo(x.Strength) : x.Id.CompareTo(y.Id));
		foreach (FieldArmy army in home)
		{
			var units = new List<string>(army.Men.Keys);
			units.Sort(System.StringComparer.Ordinal);
			foreach (string unit in units)
			{
				int going = Mathf.Min(surplus, army.Men[unit]);
				army.Men[unit] -= going;
				if (army.Men[unit] <= 0)
				{
					army.Men.Remove(unit);
				}

				p.Population += going;
				surplus -= going;
				if (surplus <= 0)
				{
					break;
				}
			}

			if (army.Strength == 0)
			{
				p.Disband(army);
			}

			if (surplus <= 0)
			{
				return;
			}
		}
	}

	private static (string Weapon, int Forged) Forge(ProvinceEconomy p, GameBalance b, int room)
	{
		int armed = p.Armoury.GetValueOrDefault("spear") + p.Armoury.GetValueOrDefault("bow");
		if (armed >= room)
		{
			return ("", 0);
		}

		int batches = Mathf.Min(b.LordSmithyBatches, Mathf.CeilToInt((room - armed) / (float)Batch));
		int spears = Mathf.Min(batches, Mathf.Min(p.Wood / SpearWood, p.Iron / SpearIron));
		if (spears > 0)
		{
			p.Wood -= spears * SpearWood;
			p.Iron -= spears * SpearIron;
			return ("spear", spears * Batch);
		}

		int bows = Mathf.Min(batches, Mathf.Min(p.Wood / BowWood, p.Gold / BowGold));
		if (bows > 0)
		{
			p.Wood -= bows * BowWood;
			p.Gold -= bows * BowGold;
			return ("bow", bows * Batch);
		}

		return ("", 0);
	}

	/// <summary>One new company, out of the arms in the armoury and, to fill it out, up to as many
	/// farmhands again as half the armed men. Not from a county that has turned against him — the
	/// sons are the last thing a resentful county gives — and not more than LordLevyShare of the
	/// people in one season.</summary>
	private static void Levy(ProvinceEconomy p, GameBalance b, Difficulty skill, int room)
	{
		if (p.Loyalty < b.LordLevyAbove || p.Population < b.LordLeastPeopleToLevy)
		{
			return;
		}

		int most = Mathf.Min(room, Mathf.FloorToInt(p.Population * b.LordLevyShare));
		var raised = new Dictionary<string, int>();
		int taken = 0;
		foreach (string arm in new[] { "spear", "bow" })
		{
			int men = Mathf.Min(most - taken, p.Armoury.GetValueOrDefault(arm));
			if (men > 0)
			{
				raised[arm] = men;
				p.Armoury[arm] -= men;
				taken += men;
			}
		}

		int hands = Mathf.Min(most - taken, taken / 2);
		if (hands > 0)
		{
			raised["peasant"] = hands;
			taken += hands;
		}

		if (taken < b.LordLeastCompany)
		{
			// Put back: a company of three is not a company, and the arms keep for next season.
			foreach ((string arm, int men) in raised)
			{
				if (arm != "peasant")
				{
					p.Armoury[arm] += men;
				}
			}

			return;
		}

		EconomySimulation.Conscript(p, taken);
		FieldArmy company = p.Raise(b.MarchReach);
		foreach ((string arm, int men) in raised)
		{
			company.Men[arm] = men;
		}
	}
}
