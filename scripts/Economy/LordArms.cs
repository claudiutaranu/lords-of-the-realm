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
	/// <summary>What one batch of ten of each weapon costs, the same recipe the player's smithy uses
	/// (data/weapons.json) — spears when there is iron, bows when there is only timber.</summary>
	private const int SpearWood = 30;
	private const int SpearIron = 8;
	private const int BowWood = 25;
	private const int BowGold = 10;
	private const int Batch = 10;

	/// <param name="realmOf">Who holds a county, by name — for telling the lord's own ground from
	/// the ground his men are campaigning on.</param>
	public static void Arm(ProvinceEconomy p, GameBalance b, Difficulty skill, System.Func<string, string> realmOf)
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

		Forge(p, b, room);
		Levy(p, b, skill, room);
	}

	/// <summary>How many more men this county will put in the field: its share of the people, less
	/// the companies it already has out, and no more than its taxes pay for with the watch on the
	/// gate paid first. The watch is not counted against the share — it is the county's defence, not
	/// its army, and counting it left Valmere, with sixty on its walls, no room to raise a man.</summary>
	public static int Room(ProvinceEconomy p, GameBalance b, Difficulty skill)
	{
		int share = Mathf.FloorToInt(p.Population * b.LordArmyShare[(int)skill]) - p.FieldMen;
		int paid = Mathf.FloorToInt(EconomySimulation.TaxDue(p, b) * b.LordWagesShare / Mathf.Max(0.01f, b.WagePerSoldier))
			- p.Soldiers;
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

	private static void Forge(ProvinceEconomy p, GameBalance b, int room)
	{
		int armed = p.Armoury.GetValueOrDefault("spear") + p.Armoury.GetValueOrDefault("bow");
		if (p.Forging.Length > 0 || armed >= room)
		{
			return;
		}

		int batches = Mathf.Min(b.LordSmithyBatches, Mathf.CeilToInt((room - armed) / (float)Batch));
		int spears = Mathf.Min(batches, Mathf.Min(p.Wood / SpearWood, p.Iron / SpearIron));
		if (spears > 0)
		{
			p.Wood -= spears * SpearWood;
			p.Iron -= spears * SpearIron;
			(p.Forging, p.ForgeTurnsLeft, p.ForgeBatch) = ("spear", 1, spears * Batch);
			return;
		}

		int bows = Mathf.Min(batches, Mathf.Min(p.Wood / BowWood, p.Gold / BowGold));
		if (bows > 0)
		{
			p.Wood -= bows * BowWood;
			p.Gold -= bows * BowGold;
			(p.Forging, p.ForgeTurnsLeft, p.ForgeBatch) = ("bow", 1, bows * Batch);
		}
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
