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
public static class EconomySimulation
{
	public static TurnSummary RunTurn(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		var summary = new TurnSummary
		{
			ProvinceName = province.ProvinceName,
			PopulationBefore = province.Population,
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
		MendTheGround(province, balance, season);
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
	public static int Demand(ResourceType type, ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season) => type switch
	{
		ResourceType.Grain => Husbandry.FieldWork(province, season),
		ResourceType.Cattle => 2 * Husbandry.Herdsmen(province.Cattle), // six a head: past three they only help it breed
		ResourceType.Wood => Labour.Ceiling(province, definition, balance, season, Labour.Wood),
		ResourceType.Stone => Labour.Ceiling(province, definition, balance, season, Labour.Stone),
		ResourceType.Iron => Labour.Ceiling(province, definition, balance, season, Labour.Iron),
		_ => 0,
	};

	/// <summary>How many more seasons the torn ground needs at the strength the lord has on it now.
	/// Zero when there is nothing to mend, and -1 when the fields have nobody to spare, which is a
	/// field that lies open for as long as he leaves it that way.</summary>
	public static int SeasonsToMend(ProvinceEconomy province, GameBalance balance, Season season)
	{
		if (province.FieldRepair <= 0)
		{
			return 0;
		}

		int mending = Mathf.Min(province.ReclaimWorkers, balance.ReclaimPerSeason);
		return mending > 0 ? Mathf.CeilToInt((float)province.FieldRepair / mending) : -1;
	}

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
		int idle = province.Workers - province.AllocatedWorkers
			+ Mathf.Max(0, province.ReclaimWorkers - Mathf.Min(province.FieldRepair, balance.ReclaimPerSeason))
			+ Mathf.Max(0, province.BuildWorkers - Masons(province))
			+ Mathf.Max(0, province.SmithWorkers - Smiths(province, balance));
		foreach (ResourceType type in new[] { ResourceType.Grain, ResourceType.Cattle, ResourceType.Wood, ResourceType.Stone, ResourceType.Iron })
		{
			idle += Mathf.Max(0, Allocated(province, type) - Demand(type, province, definition, balance, season));
		}

		return Mathf.Max(0, idle);
	}

	private static int Allocated(ProvinceEconomy province, ResourceType type) => type switch
	{
		ResourceType.Grain => province.GrainWorkers,
		ResourceType.Cattle => province.CattleWorkers,
		ResourceType.Wood => province.WoodWorkers,
		ResourceType.Stone => province.StoneWorkers,
		_ => province.IronWorkers,
	};

	// --- what comes out of the ground -------------------------------------------------------------

	/// <summary>Wood, stone and iron, which unlike grain are worked every season there are hands for
	/// it. The winter multipliers slow them — frozen ground, short days — and so does a site still
	/// learning its work (Labour.Practise).</summary>
	private static void Dig(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		p.Wood += Dug(Labour.Wood, p.WoodWorkers, def.WoodWorkerCapacity, b.WoodYieldPerWorker * def.WoodModifier, b.WoodSeasonMultiplier[(int)season], p);
		p.Stone += Dug(Labour.Stone, p.StoneWorkers, def.StoneWorkerCapacity, b.StoneYieldPerWorker * def.StoneModifier, b.StoneSeasonMultiplier[(int)season], p);
		p.Iron += Dug(Labour.Iron, p.IronWorkers, def.IronWorkerCapacity, b.IronYieldPerWorker * def.IronModifier, b.IronSeasonMultiplier[(int)season], p);
	}

	/// <summary>What one diggings brings up: every hand on it at the site's rate, as practised as
	/// the site is. Nothing where the county has none of it, however many are sent.</summary>
	private static int Dug(string site, int hands, int capacity, float perHand, float seasonal, ProvinceEconomy p) =>
		capacity <= 0 ? 0 : Mathf.RoundToInt(hands * perHand * seasonal * p.EfficiencyOf(site) / 100f);

	/// <summary>The reclaimers at work on ground the water tore up. The work is counted in
	/// hand-seasons, no more than ReclaimPerSeason of them in one season — twice the reclaimers,
	/// half the seasons, up to that. Each FieldRepairWork of it done hands one wasted field back as
	/// fallow, and not a season before the work on it is finished.</summary>
	private static void MendTheGround(ProvinceEconomy p, GameBalance b, Season season)
	{
		if (p.FieldRepair <= 0)
		{
			return;
		}

		p.FieldRepair = Mathf.Max(0, p.FieldRepair - Mathf.Min(p.ReclaimWorkers, b.ReclaimPerSeason));
		int stillTorn = Mathf.CeilToInt((float)p.FieldRepair / b.FieldRepairWork);
		for (int field = 0; field < p.Fields.Length && p.FieldsUnder(FieldUse.Waste) > stillTorn; field++)
		{
			if (p.Fields[field] == FieldUse.Waste)
			{
				p.Fields[field] = FieldUse.Fallow;
			}
		}
	}

	/// <summary>The river over its banks, as Lords of the Realm had it: one field under grain is torn
	/// up and lies waste until the reclaimers have mended it, and the corn standing on it goes with
	/// the water. The rest of the crop stands.</summary>
	/// <returns>The field the water took, or -1 when there was no grain for it to take.</returns>
	public static int Flood(ProvinceEconomy p, GameBalance b)
	{
		int worst = -1;
		for (int field = 0; field < p.Fields.Length; field++)
		{
			if (p.Fields[field] == FieldUse.Grain && worst < 0)
			{
				worst = field;
			}
		}

		if (worst < 0)
		{
			return -1;
		}

		p.StandingCrop = Mathf.Max(0, p.StandingCrop - p.StandingCrop / p.FieldsUnder(FieldUse.Grain));
		p.Fields[worst] = FieldUse.Waste;
		p.FieldRepair += b.FieldRepairWork;
		return worst;
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

	/// <summary>The season's wages, out of what the reeve just brought in. A treasury that cannot
	/// cover them pays what it can and loses the difference in men: the unpaid do not stand about
	/// being unpaid, they go home. An army bigger than its county can carry therefore empties the
	/// purse first and then thins itself, which is the honest end of overreaching — rather than a
	/// negative number in the treasury that nothing in the game knows how to answer.</summary>
	private static void PayTheGarrison(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		int owed = Mathf.CeilToInt(p.Soldiers * b.WagePerSoldier);
		if (owed <= 0)
		{
			return;
		}

		summary.Wages = Mathf.Min(owed, p.Gold);
		p.Gold -= summary.Wages;

		float unpaid = (float)(owed - summary.Wages) / owed;
		if (unpaid > 0f)
		{
			summary.Deserted = Disband(p, unpaid * b.DesertionRate);
		}
	}

	/// <summary>Thins every company by the same share and gives back how many left. Rounded up, so
	/// a company that is owed anything at all loses somebody — a desertion of nought men is a
	/// consequence the player cannot see.</summary>
	private static int Disband(ProvinceEconomy p, float share)
	{
		int gone = Thin(p.Castle, share);
		foreach (FieldArmy standing in p.Armies)
		{
			gone += Thin(standing.Men, share);
		}

		// A company nobody is left in is a company that is not there any more, rather than a banner
		// standing over an empty field.
		p.Bury();
		return gone;
	}

	/// <summary>Thins one roster. The walls go on the same terms as the field: a man on the gate who
	/// is not paid walks home like anybody else, and a castle that quietly kept its garrison for
	/// nothing would be the one place in the realm where soldiering was free.</summary>
	private static int Thin(Dictionary<string, int> roster, float share)
	{
		int gone = 0;
		foreach (string unit in new List<string>(roster.Keys))
		{
			int leaving = Mathf.Min(roster[unit], Mathf.CeilToInt(roster[unit] * share));
			if (leaving <= 0)
			{
				continue;
			}

			gone += leaving;
			roster[unit] -= leaving;
			if (roster[unit] <= 0)
			{
				roster.Remove(unit);
			}
		}

		return gone;
	}

	// --- work already paid for --------------------------------------------------------------------

	/// <summary>How many weapons the smiths at the anvil can hammer in a season, as practised as the
	/// smithy is, whatever the stores say.</summary>
	public static int Forgeable(ProvinceEconomy p, GameBalance b) =>
		p.SmithWorkers * p.EfficiencyOf(Labour.Smith) / 100 / Mathf.Max(1, b.SmithsPerWeapon);

	/// <summary>A season at the anvil: as many of the weapon in hand as the smiths can make and the
	/// stores pay for, each paid for as it is made. Whatever an older save still had on order lands
	/// first — it was paid for long ago.</summary>
	private static void ForgeWeapons(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		if (p.Forging.Length == 0)
		{
			return;
		}

		p.Add(p.Forging, p.ForgeBatch);
		p.ForgeBatch = 0;

		int made = Mathf.Min(Forgeable(p, b), Smithy.Affordable(p, p.Forging));
		if (made <= 0)
		{
			return;
		}

		foreach ((string store, int each) in Smithy.Recipe(p.Forging))
		{
			p.Add(store, -each * made);
		}

		p.Add(p.Forging, made);
		summary.Forged = made;
	}

	/// <summary>The masons work another season on whatever was ordered, and the new wall replaces
	/// the old one on the season it is finished. It was paid for when the order was placed, so
	/// nothing is spent here — a province that falls on hard times still gets the castle it already
	/// bought.</summary>
	private static void RaiseFortification(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		if (p.Building.Length == 0)
		{
			return;
		}

		// A wall is work, not a wait: the masons on it this season take their share out of what is
		// left of it. The seasons the fortifications room quotes are what it takes at the nominal
		// gang, so a lord who puts his idle county on the walls finishes in half the time, and one
		// who puts nobody on them watches the scaffolding stand there.
		p.BuildLeft = Mathf.Max(0, p.BuildLeft - p.BuildWorkers);
		p.BuildSeasonsLeft = Mathf.CeilToInt((float)p.BuildLeft / Mathf.Max(1, b.MasonsPerBuildSeason));
		if (p.BuildLeft > 0)
		{
			return;
		}

		p.Fortification = p.Building;
		summary.WallRaised = p.Building;
		p.Building = "";
		p.BuildWorkers = 0;
	}

	/// <summary>Takes men out of the county and puts them under arms, which costs it twice: the
	/// people are gone from the roll, and the hands they were are gone from the work.
	///
	/// The idle are spent first — they are standing about for exactly this — and whatever is still
	/// owed comes off the industries in proportion to what each of them holds, so a levy does not
	/// gut the harvest and leave the mine untouched. A county cannot have more men at work than it
	/// has men, and the screens would otherwise go on reading the old allocation as if it were
	/// still being worked.</summary>
	public static void Conscript(ProvinceEconomy p, int men)
	{
		// The sons are resented the day they are taken, by the share of the county that went — as in
		// the original, where a county at nothing can give no more.
		p.Loyalty = Mathf.Max(0f, p.Loyalty - Livelihood.RecruitingCost(men, p.Population));
		p.Population = Mathf.Max(0, p.Population - men);
		FitWorkforce(p);
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

		int[] hands = { p.GrainWorkers, p.CattleWorkers, p.WoodWorkers, p.StoneWorkers, p.IronWorkers, p.BuildWorkers,
			p.SmithWorkers, p.ReclaimWorkers };
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

		p.GrainWorkers = hands[0];
		p.CattleWorkers = hands[1];
		p.WoodWorkers = hands[2];
		p.StoneWorkers = hands[3];
		p.IronWorkers = hands[4];
		p.BuildWorkers = hands[5];
		p.SmithWorkers = hands[6];
		p.ReclaimWorkers = hands[7];
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
		// Waste is the flood's to make and the reclaimers' to undo; no order turns a field into it
		// or out of it.
		FieldUse was = province.Fields[field];
		if (was == use || was == FieldUse.Waste || use == FieldUse.Waste)
		{
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
