using System.Collections.Generic;
using Godot;

/// <summary>Pure per-province turn math: labour, the fields, the herd, food, tax and growth. Takes
/// state + config, returns a TurnSummary; never touches Godot nodes, scenes or UI, so it can be
/// driven by the player's turn, a save/load replay, or (later) AI lords alike.
///
/// The shape of it is Lords of the Realm II's, because that game's economy is about one thing and
/// it is the right thing: a province has only so many hands, and every season they can be in only
/// one place. Grain is a year rather than a season — sown out of the province's own store in
/// spring, weeded through summer, reaped in autumn — and autumn's reaping wants nearly everybody.
/// A lord who spends his autumn in the quarry has stone and no bread.
///
/// Everything a task can absorb is its <see cref="Demand"/>; what it actually gets is the hands
/// allocated to it, capped at that. Hands above the demand stand idle and are counted, so a player
/// can see the waste rather than wonder where his season went.</summary>
public static partial class EconomySimulation
{
	public static TurnSummary RunTurn(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		var summary = new TurnSummary
		{
			ProvinceName = province.ProvinceName,
			LoyaltyBefore = province.Loyalty,
			GoldBefore = province.Gold,
			GrainBefore = province.Grain,
			CattleBefore = province.Cattle,
			WoodBefore = province.Wood,
			StoneBefore = province.Stone,
			IronBefore = province.Iron,
			IdleWorkers = Idle(province, definition, balance, season),
		};

		// Lords of the Realm's own order (the checklist's pipeline): the reeve, the wages, the table,
		// what the table did to the county's health and its happiness, then the land, the industry —
		// the forge before the mine, so the smiths work last season's iron — and the masons.
		CollectTaxes(province);
		PayTheGarrison(province, balance, summary);
		Season next = (Season)(((int)season + 1) % 4);
		RationLevel served = Livelihood.Eat(province, province.Population + province.FieldMen, summary);
		Livelihood.Heal(province, served);
		SettleHappiness(province, served, summary);

		Husbandry.Rest(province);
		Husbandry.WorkTheFields(province, season, summary);
		Husbandry.Reclaim(province, balance);
		Husbandry.TendTheHerd(province, season, summary);
		ForgeWeapons(province, balance, summary);
		Dig(province, definition, balance, season);
		RaiseFortification(province, balance, summary);

		// The labour is dealt afresh for the season to come (step 19), and again once the births and
		// deaths have changed who there is to deal (step 25). Moving house between counties is
		// TurnManager's, since it takes the neighbours.
		Labour.Practise(province, balance);
		Labour.Deal(province, definition, balance, next);
		(summary.Born, summary.Died) = Livelihood.Generations(province, next);
		Labour.Deal(province, definition, balance, next);

		summary.Restate(province);
		return summary;
	}

	// --- labour ----------------------------------------------------------------------------------

	/// <summary>What the masons are asking for: enough to finish the wall this season, and nobody at
	/// all when there is nothing being built. Kept apart from the resources because a wall is not a
	/// store — it is the one task whose appetite shrinks as it is fed.</summary>
	public static int Masons(ProvinceEconomy province) =>
		province.Building.Length == 0 ? 0 : province.BuildLeft;

	/// <summary>What the smithy is asking for: enough hands to work everything in the stores into
	/// whatever it is making, and nobody at a cold forge. A hand past that stands at an anvil with no
	/// iron on it.</summary>
	public static int Smiths(ProvinceEconomy province, GameBalance balance) =>
		province.Forging.Length == 0 ? 0 : Smithy.Affordable(province, province.Forging) * balance.SmithsPerWeapon;

	/// <summary>How many hands a task can absorb this season. Past this, another body on the job
	/// does nothing: a herd has only so many beasts to milk, and a field in winter has nothing for
	/// anybody to do. The diggings are the exception — as in the original, they take all comers.</summary>
	public static int Demand(ResourceType type, ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season) =>
		Labour.Ceiling(province, definition, balance, season, Labour.JobOf(type));

	/// <summary>What fraction of a task actually got done: all of it when the hands are there, and
	/// proportionally less when they are not. A task nobody can work — no fields under grain, no
	/// herd to keep — is not short-handed, it is simply not happening, and reads as zero.</summary>
	public static float Covered(int allocated, int demand) =>
		demand <= 0 ? 0f : Mathf.Min(1f, (float)allocated / demand);

	/// <summary>Hands standing about: allocated to tasks that cannot use them, plus any never
	/// allocated at all. Autumn is where this bites — the grain demand jumps fourfold and every
	/// hand left in the mine is a hand not reaping.</summary>
	public static int Idle(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		int idle = province.Workers - province.AllocatedWorkers;
		foreach (string job in Labour.Jobs)
		{
			idle += Mathf.Max(0, Labour.Hands(province, job) - Labour.Ceiling(province, definition, balance, season, job));
		}

		return Mathf.Max(0, idle);
	}

	// --- what goes into the people ----------------------------------------------------------------

	/// <summary>What the reeve will bring in this season at the rate the lord has set
	/// (Livelihood.TaxDue). Public because the tax table shows it before he commits to the rate.</summary>
	public static int TaxDue(ProvinceEconomy p, GameBalance b) => Livelihood.TaxDue(p);

	/// <summary>What the reeve brings in, off the people there are now. Not out of a county somebody
	/// else's army is sitting on: the reeve does not ride out through a siege camp, and this is most
	/// of what a siege costs the lord being besieged.</summary>
	private static void CollectTaxes(ProvinceEconomy p)
	{
		p.Gold += p.BesiegedFrom.Length > 0 ? 0 : Livelihood.TaxDue(p);
	}

	/// <summary>The season's happiness, the original's three terms: the rate against five, how
	/// healthy the county is, and the ration it was actually served. The realm's rates weigh in from
	/// TurnManager, which can see every county the lord holds. Each is written into the summary
	/// before they are added, so the advisor can say which one did it.</summary>
	private static void SettleHappiness(ProvinceEconomy p, RationLevel served, TurnSummary summary)
	{
		summary.LoyaltyFromTax = Livelihood.TaxTerm(p.Tax);
		summary.LoyaltyFromHealth = Livelihood.HealthTerm(p.Health);
		summary.LoyaltyFromRations = Livelihood.RationTerm(served);
		p.Loyalty = Mathf.Clamp(p.Loyalty + summary.LoyaltyFromTax + summary.LoyaltyFromHealth
			+ summary.LoyaltyFromRations, 0f, 100f);
	}

	/// <summary>Trims the county's work back to the men it actually has.
	///
	/// Everything that takes people leaves the allocation exactly where it was: a levy, a famine, the
	/// plague, a county walking out over its taxes. The hands were set once and nothing puts them
	/// back, so a province that has lost half its people goes on reading as if every trade were still
	/// fully manned — and the yields are computed off those hands, which means a dead county keeps
	/// mining. One guard at the end of the season catches all of it, including whatever takes people
	/// next, rather than a patch at each place that kills somebody.
	///
	/// The idle go first. What is still owed comes off the trades in proportion to what each holds,
	/// so a hard winter does not empty the fields and leave the mine untouched.</summary>
	public static void FitWorkforce(ProvinceEconomy p)
	{
		int over = p.AllocatedWorkers - p.Workers;
		if (over <= 0)
		{
			return; // the idle covered it
		}

		int[] hands = System.Array.ConvertAll(Labour.Jobs, job => Labour.Hands(p, job));
		int total = p.AllocatedWorkers;
		int taken = 0;
		for (int i = 0; i < hands.Length; i++)
		{
			int off = Mathf.Min(hands[i], (int)((long)over * hands[i] / total));
			hands[i] -= off;
			taken += off;
		}

		// Whole men only, so the shares leave a remainder: it comes off wherever there are still the
		// most hands, which is also the trade that misses them least.
		while (taken < over)
		{
			int biggest = 0;
			for (int i = 1; i < hands.Length; i++)
			{
				if (hands[i] > hands[biggest])
				{
					biggest = i;
				}
			}

			if (hands[biggest] == 0)
			{
				break;
			}

			hands[biggest]--;
			taken++;
		}

		for (int i = 0; i < hands.Length; i++)
		{
			Labour.Put(p, Labour.Jobs[i], hands[i]);
		}
	}

	// --- what the player is shown before committing to it -----------------------------------------

	/// <summary>What a given number of hands on a given task would bring in this season, without
	/// moving anything — the preview the worker panel shows while the player is still deciding.
	///
	/// Grain is the odd one: for three seasons of four it brings in nothing at all, because the crop
	/// is in the ground. What it reports instead is the crop those hands leave standing — sown,
	/// tended, or reaped — so a player moving men off the fields can see what it costs him. The
	/// fields and the herd are played out on a copy with that many hands, so the figure is the
	/// season's own.</summary>
	public static int ProjectedYield(ResourceType type, int workers, ProvinceEconomy province, ProvinceDefinition def, GameBalance b, Season season)
	{
		int s = (int)season;
		ProvinceEconomy trial = province.Copy();
		var played = new TurnSummary();
		if (type == ResourceType.Grain)
		{
			trial.GrainWorkers = workers;
			Husbandry.WorkTheFields(trial, season, played);
		}
		else if (type == ResourceType.Cattle)
		{
			trial.CattleWorkers = workers;
			Husbandry.TendTheHerd(trial, season, played);
		}

		return type switch
		{
			ResourceType.Grain => season == Season.Autumn ? played.Harvest : trial.StandingCrop,
			ResourceType.Cattle => played.Calved,
			ResourceType.Wood => Dug(Labour.Wood, workers, def.WoodWorkerCapacity, b.WoodYieldPerWorker * def.WoodModifier, b.WoodSeasonMultiplier[s], province),
			ResourceType.Stone => Dug(Labour.Stone, workers, def.StoneWorkerCapacity, b.StoneYieldPerWorker * def.StoneModifier, b.StoneSeasonMultiplier[s], province),
			ResourceType.Iron => Dug(Labour.Iron, workers, def.IronWorkerCapacity, b.IronYieldPerWorker * def.IronModifier, b.IronSeasonMultiplier[s], province),
			_ => 0,
		};
	}

	/// <summary>Turns one field to another use, and takes the consequence with it.
	///
	/// A crop already in the ground goes under the plough with the field it was sown on: a lord who
	/// turns his wheat to pasture in August has turned it to pasture, and loses that field's share
	/// of the standing crop. Turning land TO grain out of season adds nothing — there is no sowing
	/// in autumn — so the gain waits for spring, which is the whole reason the decision is made
	/// before the seed goes down.</summary>
	public static void SetField(ProvinceEconomy province, int field, FieldUse use)
	{
		// Waste is the weather's to make. The lord's only orders over it are to set the reclaimers
		// on it, or to call them off; and a field struck this very season takes no order at all.
		FieldUse was = province.Fields[field];
		bool ruined = was is FieldUse.Waste or FieldUse.Reclaiming;
		bool allowed = was == FieldUse.Waste ? use == FieldUse.Reclaiming
			: was == FieldUse.Reclaiming ? use == FieldUse.Waste
			: use is FieldUse.Fallow or FieldUse.Grain or FieldUse.Pasture;
		if (was == use || !allowed || field == province.Weathered)
		{
			return;
		}

		if (ruined)
		{
			province.Fields[field] = use;
			return;
		}

		if (was == FieldUse.Grain && province.StandingCrop > 0)
		{
			int under = province.FieldsUnder(FieldUse.Grain);
			province.StandingCrop = Mathf.Max(0, province.StandingCrop - province.StandingCrop / under);
		}

		province.Fields[field] = use;
	}

	/// <summary>What next season will actually do to the province, played out on a copy and thrown
	/// away — production and what eats it, in one number per store.
	///
	/// It is the turn itself rather than a second set of formulas that predicts it, so the figure on
	/// the panel and the figure the season delivers cannot drift apart. A projection written
	/// separately is a projection that is wrong the first time somebody changes a yield and forgets
	/// it exists.</summary>
	public static TurnSummary Preview(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season) =>
		RunTurn(province.Copy(), definition, balance, season);
}
