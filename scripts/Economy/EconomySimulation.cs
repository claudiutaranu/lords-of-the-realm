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

		Dig(province, definition, balance, season);
		WorkTheFields(province, definition, balance, season, summary);
		MendTheGround(province, balance, season);
		int dairy = TendTheHerd(province, definition, balance, season);
		summary.Dairy = dairy;
		Eat(province, balance, dairy, summary);
		CollectTaxes(province, balance);
		PayTheGarrison(province, balance, summary);
		ForgeWeapons(province);
		RaiseFortification(province, balance);
		SettleLoyalty(province, balance, summary);
		MovePeople(province, balance, summary);

		summary.Restate(province);
		return summary;
	}

	// --- labour ----------------------------------------------------------------------------------

	/// <summary>How many hands a task can absorb this season. Past this, another body on the job
	/// does nothing: a quarry has only so many faces to cut, a herd only so many beasts to milk,
	/// and a field in winter has nothing for anybody to do.</summary>
	/// <summary>What the masons are asking for: enough to finish the wall this season, and nobody at
	/// all when there is nothing being built. Kept apart from the resources because a wall is not a
	/// store — it is the one task whose appetite shrinks as it is fed.</summary>
	public static int Masons(ProvinceEconomy province) =>
		province.Building.Length == 0 ? 0 : province.BuildLeft;

	public static int Demand(ResourceType type, ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season) => type switch
	{
		ResourceType.Grain => GrainWork(province, balance, season) + province.FieldRepair,
		ResourceType.Cattle => Mathf.CeilToInt(province.Cattle / balance.CowsPerHerder),
		ResourceType.Wood => definition.WoodWorkerCapacity,
		ResourceType.Stone => definition.StoneWorkerCapacity,
		ResourceType.Iron => definition.IronWorkerCapacity,
		_ => 0,
	};

	/// <summary>What the year asks of the fields on its own, before any ground that needs putting
	/// right. Held apart from <see cref="Demand"/> because the sowing and the harvest are scaled by
	/// these hands alone: men out mending a flooded field are not reaping, and a lord who mans both
	/// should not read as having done neither.</summary>
	private static int GrainWork(ProvinceEconomy province, GameBalance balance, Season season) =>
		province.FieldsUnder(FieldUse.Grain) * balance.GrainWorkersPerField[(int)season];

	/// <summary>How many more seasons the torn ground needs at the strength the lord has on it now.
	/// Zero when there is nothing to mend, and -1 when the fields have nobody to spare, which is a
	/// field that lies open for as long as he leaves it that way.</summary>
	public static int SeasonsToMend(ProvinceEconomy province, GameBalance balance, Season season)
	{
		if (province.FieldRepair <= 0)
		{
			return 0;
		}

		int spare = Mathf.Max(0, province.GrainWorkers - GrainWork(province, balance, season));
		return spare > 0 ? Mathf.CeilToInt((float)province.FieldRepair / spare) : -1;
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
			+ Mathf.Max(0, province.BuildWorkers - Masons(province));
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

	private static float CoverFor(ResourceType type, ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season) =>
		Covered(Allocated(province, type), type == ResourceType.Grain
			? GrainWork(province, balance, season)
			: Demand(type, province, definition, balance, season));

	// --- what comes out of the ground -------------------------------------------------------------

	/// <summary>Wood, stone and iron, which unlike grain are worked every season there are hands for
	/// it. The winter multipliers are the only thing that slows them: frozen ground, short days.</summary>
	private static void Dig(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		int s = (int)season;
		p.Wood += Mathf.RoundToInt(Mathf.Min(p.WoodWorkers, def.WoodWorkerCapacity) * b.WoodYieldPerWorker * def.WoodModifier * b.WoodSeasonMultiplier[s]);
		p.Stone += Mathf.RoundToInt(Mathf.Min(p.StoneWorkers, def.StoneWorkerCapacity) * b.StoneYieldPerWorker * def.StoneModifier * b.StoneSeasonMultiplier[s]);
		p.Iron += Mathf.RoundToInt(Mathf.Min(p.IronWorkers, def.IronWorkerCapacity) * b.IronYieldPerWorker * def.IronModifier * b.IronSeasonMultiplier[s]);
	}

	/// <summary>One season of the farming year.
	///
	/// Spring sows: seed comes out of the province's own store, so a province that ate its grain
	/// over the winter has nothing to plant and nothing to reap. Summer weeds, and a crop nobody
	/// weeds does not fail outright but comes in light. Autumn reaps — the whole year in one turn,
	/// scaled by how many hands turned up for it — and then the land is rested or not.</summary>
	private static void WorkTheFields(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season, TurnSummary summary)
	{
		float covered = CoverFor(ResourceType.Grain, p, def, b, season);

		switch (season)
		{
			case Season.Spring:
				int wanted = p.FieldsUnder(FieldUse.Grain) * b.SeedPerField;
				int sown = Mathf.Min(p.Grain, Mathf.FloorToInt(wanted * covered));
				p.Grain -= sown;
				p.StandingCrop = sown;
				summary.Sown = sown;
				break;

			case Season.Summer:
				p.StandingCrop = Mathf.RoundToInt(
					p.StandingCrop * Mathf.Lerp(b.UntendedCropYield, 1f, covered));
				break;

			case Season.Autumn:
				summary.Harvest = Mathf.RoundToInt(
					p.StandingCrop * b.HarvestPerSeed * def.GrainModifier * p.GrainFertility() * covered);
				p.Grain += summary.Harvest;
				p.StandingCrop = 0;
				RestTheLand(p, b);
				break;

			case Season.Winter:
				break;
		}
	}

	/// <summary>The year's toll on the soil, taken once, after the harvest. A field under grain gives
	/// up some of its heart; one left fallow takes it back; one under the herd takes back half of it,
	/// because the beasts return some of what they eat where they stand.</summary>
	/// <summary>The hands the fields can spare, put on ground the water tore up. The work is counted
	/// in hand-seasons and comes down by whatever is left over once the year's own sowing, weeding
	/// and reaping have been manned — twice the spare hands, half the seasons. The heart the flood
	/// took comes back the turn the work is finished, and not a season before it.</summary>
	private static void MendTheGround(ProvinceEconomy p, GameBalance b, Season season)
	{
		if (p.FieldRepair <= 0)
		{
			return;
		}

		p.FieldRepair = Mathf.Max(0, p.FieldRepair - Mathf.Max(0, p.GrainWorkers - GrainWork(p, b, season)));
		if (p.FieldRepair > 0)
		{
			return;
		}

		for (int field = 0; field < p.Fields.Length; field++)
		{
			if (p.Fields[field] == FieldUse.Grain)
			{
				p.Fertility[field] = Mathf.Min(1f, p.Fertility[field] + b.FloodFertilityLoss);
			}
		}
	}

	private static void RestTheLand(ProvinceEconomy p, GameBalance b)
	{
		for (int field = 0; field < p.Fields.Length; field++)
		{
			float move = p.Fields[field] switch
			{
				FieldUse.Grain => -b.FertilityCropped,
				FieldUse.Pasture => b.FertilityRested / 2f,
				_ => b.FertilityRested,
			};

			p.Fertility[field] = Mathf.Clamp(p.Fertility[field] + move, b.FertilityFloor, 1f);
		}
	}

	/// <summary>Milk and cheese off the herd, and what the herd adds to itself. Both want herdsmen:
	/// an untended herd gives nothing and grows by nothing. Growth is also squeezed by the room the
	/// province has for it — at its pasture's capacity the herd grows at full rate, at twice that it
	/// does not grow at all.</summary>
	private static int TendTheHerd(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		float covered = CoverFor(ResourceType.Cattle, p, def, b, season);
		int dairy = Mathf.FloorToInt(p.Cattle * b.DairyPerCow * covered * b.CattleSeasonMultiplier[(int)season]);

		int room = p.FieldsUnder(FieldUse.Pasture) * b.CowsPerField;
		float crowding = room <= 0 ? 0f : Mathf.Clamp(2f - (float)p.Cattle / room, 0f, 1f);
		p.Cattle += Mathf.RoundToInt(p.Cattle * b.CattleBaseGrowthRate * def.CattleModifier * crowding * covered);
		return dairy;
	}

	// --- what goes into the people ----------------------------------------------------------------

	/// <summary>The province eats. Dairy first, because it is there whether anybody planned for it or
	/// not; then the granary; then, if it is still short, the herd goes under the knife. Only when
	/// all three fail does anybody starve — and a province that ate its cattle this winter has no
	/// milk next spring, which is the price of the last resort.</summary>
	private static void Eat(ProvinceEconomy p, GameBalance b, int dairy, TurnSummary summary)
	{
		// The people eat on the ration their lord set them. The garrison does not: an army is fed or
		// it is not an army, and a soldier eats more than a ploughman besides. Counting them here is
		// the whole difference between an army being a burden and an army being a saving — men are
		// taken out of the population when they are raised, so until they ate, every company a lord
		// trained quietly made his winter cheaper.
		summary.SoldierFood = Mathf.CeilToInt(p.Soldiers / b.PeoplePerGrain * b.SoldierAppetite);
		int required = Mathf.CeilToInt(p.Population / b.PeoplePerGrain * b.RationFoodMultiplier[(int)p.Ration])
			+ summary.SoldierFood;
		summary.Needed = required;

		// The dairy is milked first whatever the lord thinks: milk keeps for a week and a cow keeps
		// for years, so a county that lets its milk sour to save its herd has thrown away the one
		// food it cannot store.
		int owing = Mathf.Max(0, required - dairy);

		// Then the mix he asked for. Wanting the meal off the herd does not conjure a herd: what one
		// side cannot cover, the other is asked for, and what neither can cover is hunger.
		int wantedOffTheHerd = Mathf.RoundToInt(owing * p.BeefShare / 100f);
		int bread = Mathf.Min(p.Grain, owing - wantedOffTheHerd);
		int killed = Mathf.Min(p.Cattle, Mathf.CeilToInt(wantedOffTheHerd / b.BeefPerCow));
		int beef = Mathf.FloorToInt(killed * b.BeefPerCow);

		// A cow is not divisible and a sack is: whatever the knife over-served counts towards the
		// meal, and only then is either side asked to make up the other's shortfall.
		int shortfall = Mathf.Max(0, owing - bread - beef);
		if (shortfall > 0 && p.Grain > bread)
		{
			int more = Mathf.Min(p.Grain - bread, shortfall);
			bread += more;
			shortfall -= more;
		}

		if (shortfall > 0 && p.Cattle > killed)
		{
			int more = Mathf.Min(p.Cattle - killed, Mathf.CeilToInt(shortfall / b.BeefPerCow));
			int carved = Mathf.FloorToInt(more * b.BeefPerCow);
			killed += more;
			beef += carved;
			shortfall = Mathf.Max(0, shortfall - carved);
		}

		p.Grain -= bread;
		p.Cattle -= killed;
		summary.Bread = bread;
		summary.Slaughtered = killed;

		// What the county was actually handed, as a multiple of a man's bread — which is not always
		// what its lord ordered. Everything that follows from the ration follows from THIS: a county
		// promised double and served half knows perfectly well which of the two it ate, and an
		// order nobody could fill has never made anybody grateful.
		int mouths = Mathf.Max(1, Mathf.CeilToInt(p.Population / b.PeoplePerGrain));
		int served = Mathf.Max(0, dairy + bread + beef - summary.SoldierFood);
		summary.Achieved = Achieved((float)served / mouths, b);

		// And hunger is measured against what a man NEEDS, never against what he was promised. A
		// county ordered triple and served double has gone short of nothing at all; if the shortfall
		// against the order counted as famine, the most generous thing a lord can do would show up
		// in his ledger as starving his own people.
		int toLive = mouths + summary.SoldierFood;
		int owed = Mathf.Max(0, toLive - (dairy + bread + beef));
		summary.FoodShort = owed;
		p.StarvedThisTurn = owed > 0;
		if (!p.StarvedThisTurn)
		{
			return;
		}

		float deficit = (float)owed / toLive;
		p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * deficit * b.StarvationPopulationLossPerDeficit));
		summary.LoyaltyFromStarvation = -deficit * b.StarvationLoyaltyLossPerDeficit;
		p.Loyalty = Mathf.Clamp(p.Loyalty + summary.LoyaltyFromStarvation, 0f, 100f);
	}

	/// <summary>The ration a county was actually served, given the multiple of a man's bread it got.
	/// The best level it reached and not the nearest — a county handed one and nine tenths of a
	/// ration was not fed double, whatever it was promised.</summary>
	public static RationLevel Achieved(float multiple, GameBalance b)
	{
		var got = RationLevel.None;
		for (int level = 0; level < b.RationFoodMultiplier.Length; level++)
		{
			if (multiple + 0.001f >= b.RationFoodMultiplier[level])
			{
				got = (RationLevel)level;
			}
		}

		return got;
	}

	/// <summary>What the reeve will bring in this season at the rate the lord has set. Public because
	/// the tax table shows the player this figure before he commits to the rate, and a screen that
	/// works its own answer out is a screen that will one day disagree with the turn.</summary>
	public static int TaxDue(ProvinceEconomy p, GameBalance b) => Mathf.RoundToInt(
		p.Population * b.GoldPerHeadPerTaxPoint * p.Tax * (1f + Fortifications.TaxBonus(p.Fortification)));

	/// <summary>What a rate does to the county's goodwill each season. Below what the county calls
	/// fair the lord is thanked for it, above it he is not, and the thanks are capped while the
	/// resentment is not — a lord can always make things worse faster than he can make them
	/// better.</summary>
	public static float TaxGoodwill(int percent, GameBalance b) =>
		Mathf.Min((b.FairTaxPercent - percent) * b.LoyaltyPerTaxPoint, b.TaxGoodwillCap);

	/// <summary>What a county taxed this hard costs EACH of its lord's other counties, a season.
	/// Nothing at or under the fair rate: word only travels when there is something to tell.</summary>
	public static float TaxSpill(int percent, GameBalance b) =>
		-Mathf.Max(0, percent - b.FairTaxPercent) * b.LoyaltyPerTaxPoint * b.OtherCountiesTaxShare;

	/// <summary>What the reeve brings in. A castle is worth money as well as walls: safer roads, a
	/// market worth coming to, and a lord who is harder to refuse.</summary>
	private static void CollectTaxes(ProvinceEconomy p, GameBalance b)
	{
		p.Gold += TaxDue(p, b);
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
	private static int Disband(ProvinceEconomy p, float share) =>
		Thin(p.Garrison, share) + Thin(p.Castle, share);

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

	/// <summary>What the province thinks of its lord this season, out of what he charged it, what he
	/// fed it, and whose sons he took. Starvation has already taken its own cut by the time this
	/// runs, and recorded it.
	///
	/// Each grievance is worked out on its own line and written into the summary before they are
	/// added up, because the advisor has to be able to say which one did the damage. Summing them
	/// first and reconstructing the cause afterwards is how a narrator ends up blaming the tax on
	/// the turn the people were starving.
	///
	/// An intake is remembered and then forgotten: the county resents the sons taken this year and
	/// has largely stopped counting them by the next, which is what keeps conscription a cost a lord
	/// pays rather than a debt he can never work off.</summary>
	private static void SettleLoyalty(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		summary.LoyaltyFromTax = TaxGoodwill(p.Tax, b);
		summary.LoyaltyFromRations = b.RationLoyaltyDelta[(int)summary.Achieved];
		summary.LoyaltyFromConscription = -p.ConscriptedRecently / 100f * b.ConscriptionLoyaltyPerHundred;
		summary.LoyaltyFromGarrison = -Billeted(p, b);

		p.Loyalty = Mathf.Clamp(
			p.Loyalty + summary.LoyaltyFromTax + summary.LoyaltyFromRations + summary.LoyaltyFromConscription
				+ summary.LoyaltyFromGarrison,
			0f, 100f);

		p.ConscriptedRecently = Mathf.Max(0,
			p.ConscriptedRecently - Mathf.CeilToInt(p.ConscriptedRecently * b.ConscriptionForgetRate));
	}

	/// <summary>What the men standing in the county cost their lord in goodwill, as a positive
	/// number for <see cref="SettleLoyalty"/> to subtract. Weighed against the people they stand
	/// among and not counted flat: a hundred spears in a village of two hundred is an occupation and
	/// in a county of five thousand is the watch, and only one of those is worth resenting.
	///
	/// Unlike the intake, this is not forgotten. Sons taken for the army are a grievance that fades
	/// because the county gets used to their absence; men billeted on it are in front of it every
	/// morning. It stops the season they are disbanded or marched out, and not before.</summary>
	private static float Billeted(ProvinceEconomy p, GameBalance b)
	{
		if (p.Population <= 0)
		{
			return 0f;
		}

		float resented = (float)p.Soldiers / p.Population - b.GarrisonTolerated;
		return resented <= 0f ? 0f : resented * 10f * b.GarrisonLoyaltyPerTenth;
	}

	/// <summary>Births, and the traffic at the county line. A content province is somewhere people
	/// walk towards and a resented one empties from the edges, so the same loyalty that keeps a lord
	/// from being deposed is also what fills his fields. Nobody is born the season they starve.</summary>
	private static void MovePeople(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		// Nobody is born the season the county starves, and nobody settles down in one people are
		// walking out of — a county packing its carts is not also having its best year for weddings.
		//
		// That second half is load-bearing and was missing. Births run at one percent a season and
		// the leavers at one and a fifth, so the two very nearly cancelled: a lord could tax his
		// county to the edge of revolt, watch the goodwill collapse, and see his population sit
		// perfectly still. The number he was watching told him the tax had cost him nothing.
		bool leaving = p.Loyalty <= b.EmigrationBelow;
		if (!p.StarvedThisTurn && !leaving)
		{
			float growth = b.BasePopulationGrowthRate * b.RationGrowthMultiplier[(int)summary.Achieved];
			p.Population += Mathf.RoundToInt(p.Population * growth);
		}

		if (p.Loyalty >= b.ImmigrationAbove)
		{
			p.Population += Mathf.RoundToInt(p.Population * b.ImmigrationRate);
			return;
		}

		if (!leaving)
		{
			return;
		}

		// And the further past the line, the faster they go. A county at nothing is not leaking at
		// the same pace as one that has only just crossed it.
		float depth = (b.EmigrationBelow - p.Loyalty) / Mathf.Max(1f, b.EmigrationBelow);
		float rate = b.EmigrationRate * (1f + depth * b.EmigrationDepthMultiple);
		p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * rate));
	}

	// --- work already paid for --------------------------------------------------------------------

	/// <summary>The smithy works off the order the player placed, which was paid for when it was
	/// placed; a turn here is one turn of work, and the batch lands in the armoury when the last one
	/// is done. Nothing to do when the forge is cold.</summary>
	private static void ForgeWeapons(ProvinceEconomy p)
	{
		if (p.Forging.Length == 0)
		{
			return;
		}

		p.ForgeTurnsLeft--;
		if (p.ForgeTurnsLeft > 0)
		{
			return;
		}

		p.Armoury[p.Forging] = p.Armoury.GetValueOrDefault(p.Forging) + p.ForgeBatch;
		p.Forging = "";
		p.ForgeBatch = 0;
	}

	/// <summary>The masons work another season on whatever was ordered, and the new wall replaces
	/// the old one on the season it is finished. It was paid for when the order was placed, so
	/// nothing is spent here — a province that falls on hard times still gets the castle it already
	/// bought.</summary>
	private static void RaiseFortification(ProvinceEconomy p, GameBalance b)
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
		p.Population = Mathf.Max(0, p.Population - men);
		p.ConscriptedRecently += men;
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

		int[] hands = { p.GrainWorkers, p.CattleWorkers, p.WoodWorkers, p.StoneWorkers, p.IronWorkers, p.BuildWorkers };
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
	}

	// --- what the player is shown before committing to it -----------------------------------------

	/// <summary>What a given number of hands on a given task would bring in this season, without
	/// moving anything — the preview the worker panel shows while the player is still deciding.
	///
	/// Grain is the odd one: for three seasons of four it brings in nothing at all, because the crop
	/// is in the ground. What it reports instead is what those hands are worth to the year — the
	/// share of the autumn harvest they are keeping alive — so a player moving men off the fields in
	/// spring can see what it will cost him in autumn rather than finding out two turns later.</summary>
	public static int ProjectedYield(ResourceType type, int workers, ProvinceEconomy province, ProvinceDefinition def, GameBalance b, Season season)
	{
		int s = (int)season;
		float covered = Covered(workers, Demand(type, province, def, b, season));
		return type switch
		{
			ResourceType.Grain => season == Season.Autumn
				? Mathf.RoundToInt(province.StandingCrop * b.HarvestPerSeed * def.GrainModifier * province.GrainFertility() * covered)
				: Mathf.RoundToInt(province.FieldsUnder(FieldUse.Grain) * b.SeedPerField * b.HarvestPerSeed
					* def.GrainModifier * province.GrainFertility() * covered),
			ResourceType.Cattle => Mathf.FloorToInt(province.Cattle * b.DairyPerCow * covered * b.CattleSeasonMultiplier[s]),
			ResourceType.Wood => Mathf.RoundToInt(Mathf.Min(workers, def.WoodWorkerCapacity) * b.WoodYieldPerWorker * def.WoodModifier * b.WoodSeasonMultiplier[s]),
			ResourceType.Stone => Mathf.RoundToInt(Mathf.Min(workers, def.StoneWorkerCapacity) * b.StoneYieldPerWorker * def.StoneModifier * b.StoneSeasonMultiplier[s]),
			ResourceType.Iron => Mathf.RoundToInt(Mathf.Min(workers, def.IronWorkerCapacity) * b.IronYieldPerWorker * def.IronModifier * b.IronSeasonMultiplier[s]),
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
		FieldUse was = province.Fields[field];
		if (was == use)
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

	/// <summary>Spreads the province's hands over its tasks the way a reeve would, filling each task
	/// to what it can use in the order the province cannot do without: bread first, then the herd,
	/// then whatever is left to the diggings. It is what a campaign opens on, and what the worker
	/// panel's "auto" does — never what a turn runs, which uses whatever the player last set.</summary>
	public static void Deploy(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		int hands = p.Workers;
		p.GrainWorkers = Take(ref hands, Demand(ResourceType.Grain, p, def, b, season));
		p.CattleWorkers = Take(ref hands, Demand(ResourceType.Cattle, p, def, b, season));
		p.WoodWorkers = Take(ref hands, Demand(ResourceType.Wood, p, def, b, season));
		p.IronWorkers = Take(ref hands, Demand(ResourceType.Iron, p, def, b, season));
		p.StoneWorkers = Take(ref hands, Demand(ResourceType.Stone, p, def, b, season));
		p.BuildWorkers = Take(ref hands, Masons(p));
	}

	/// <summary>Divides the county's hands between the fields and the trades, the way Lords of the
	/// Realm's own labour bar does it: the lord sets the proportion and the men sort themselves out
	/// inside each half, by whatever the season is asking of it. The plaques stay for the lord who
	/// wants to argue with them.
	///
	/// <paramref name="toTrades"/> is the share that goes to industry — nothing puts every hand in
	/// the fields, one puts them all in the woods and the workings, and a half splits them evenly.
	/// A half that is given more hands than it has work for leaves the rest standing: the bar is the
	/// lord's decision, and hands he sent to the mine do not quietly walk back to the harvest.</summary>
	public static void Split(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season,
		float toTrades)
	{
		int trades = Mathf.RoundToInt(p.Workers * Mathf.Clamp(toTrades, 0f, 1f));
		int fields = p.Workers - trades;

		int[] sown = Spread(fields,
			Demand(ResourceType.Grain, p, def, b, season),
			Demand(ResourceType.Cattle, p, def, b, season));
		p.GrainWorkers = sown[0];
		p.CattleWorkers = sown[1];

		int[] worked = Spread(trades,
			Demand(ResourceType.Wood, p, def, b, season),
			Demand(ResourceType.Stone, p, def, b, season),
			Demand(ResourceType.Iron, p, def, b, season),
			Masons(p));
		p.WoodWorkers = worked[0];
		p.StoneWorkers = worked[1];
		p.IronWorkers = worked[2];
		p.BuildWorkers = worked[3];
	}

	/// <summary>Lays one half of the county's hands over the trades on that side of the bar, in the
	/// proportion those trades are asking in — which is how Lords of the Realm divides them: equally
	/// between the tasks that want anybody, rather than filling the first in a list and handing the
	/// leftovers to the next.
	///
	/// Every man given to a side is put somewhere on it, including the ones past what the work can
	/// use. They do nothing there — that is what the idle count is for — but they stand where the
	/// lord put them instead of quietly falling off the bar he has just moved. A side that wants
	/// nobody at all takes nobody: in a winter with no field work, a bar pushed at the fields is a
	/// county standing about, and it should read as one.</summary>
	private static int[] Spread(int hands, params int[] wanted)
	{
		var given = new int[wanted.Length];
		int total = 0;
		foreach (int want in wanted)
		{
			total += want;
		}

		if (total <= 0 || hands <= 0)
		{
			return given;
		}

		int handed = 0;
		for (int i = 0; i < wanted.Length; i++)
		{
			given[i] = (int)((long)hands * wanted[i] / total);
			handed += given[i];
		}

		// Whole men only, so the shares leave a remainder. It goes where the most is being asked.
		for (int i = 0; handed < hands; i++)
		{
			int hungriest = 0;
			for (int which = 1; which < wanted.Length; which++)
			{
				if (wanted[which] > wanted[hungriest])
				{
					hungriest = which;
				}
			}

			given[hungriest]++;
			handed++;
		}

		return given;
	}

	/// <summary>Where the labour bar stands for a county as it is manned now: the share of the hands
	/// at work that are in the trades. Read off the allocation rather than remembered beside it, so
	/// the bar can never disagree with the plaques — move a man with a plaque and the bar has
	/// already moved.</summary>
	public static float TradesShare(ProvinceEconomy p)
	{
		int working = p.AllocatedWorkers;
		return working <= 0 ? 0.5f : (float)(p.WoodWorkers + p.StoneWorkers + p.IronWorkers) / working;
	}

	private static int Take(ref int hands, int demand)
	{
		int taken = Mathf.Clamp(demand, 0, hands);
		hands -= taken;
		return taken;
	}
}
