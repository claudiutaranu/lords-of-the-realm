using System;
using System.Collections.Generic;
using Godot;

/// <summary>Pure per-province turn math: production, food, cattle, tax, growth. Takes
/// state + config, returns a TurnSummary; never touches Godot nodes, scenes or UI, so it
/// can be driven by the player's turn, a save/load replay, or (later) AI lords alike.
///
/// Mirrors design-doc section 27's pipeline, trimmed to the Phase 1 steps: buildings,
/// blacksmith, recruitment, construction and events don't exist yet.</summary>
public static class EconomySimulation
{
	public static TurnSummary RunTurn(ProvinceEconomy province, ProvinceDefinition definition, GameBalance balance, Season season)
	{
		int popBefore = province.Population;
		float loyaltyBefore = province.Loyalty;
		int goldBefore = province.Gold;
		int grainBefore = province.Grain;
		int cattleBefore = province.Cattle;
		int woodBefore = province.Wood;
		int stoneBefore = province.Stone;
		int ironBefore = province.Iron;

		ProduceResources(province, definition, balance, season);
		ForgeWeapons(province);
		ConsumeFood(province, balance);
		GrowCattle(province, definition, balance);
		CollectTaxes(province, balance);
		GrowPopulation(province, balance);

		return new TurnSummary
		{
			ProvinceName = province.ProvinceName,
			PopulationBefore = popBefore,
			PopulationAfter = province.Population,
			LoyaltyBefore = loyaltyBefore,
			LoyaltyAfter = province.Loyalty,
			GoldBefore = goldBefore,
			GoldAfter = province.Gold,
			GrainBefore = grainBefore,
			GrainAfter = province.Grain,
			CattleBefore = cattleBefore,
			CattleAfter = province.Cattle,
			WoodBefore = woodBefore,
			WoodAfter = province.Wood,
			StoneBefore = stoneBefore,
			StoneAfter = province.Stone,
			IronBefore = ironBefore,
			IronAfter = province.Iron,
		};
	}

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

	private static void ProduceResources(ProvinceEconomy p, ProvinceDefinition def, GameBalance b, Season season)
	{
		int s = (int)season;
		p.Grain += Mathf.RoundToInt(p.GrainWorkers * b.GrainYieldPerWorker * def.GrainModifier * b.GrainSeasonMultiplier[s]);
		p.Wood += Mathf.RoundToInt(p.WoodWorkers * b.WoodYieldPerWorker * def.WoodModifier * b.WoodSeasonMultiplier[s]);
		p.Stone += Mathf.RoundToInt(p.StoneWorkers * b.StoneYieldPerWorker * def.StoneModifier * b.StoneSeasonMultiplier[s]);
		p.Iron += Mathf.RoundToInt(p.IronWorkers * b.IronYieldPerWorker * def.IronModifier * b.IronSeasonMultiplier[s]);
	}

	// Design doc section 9 + 11: consumption, then starvation's population/loyalty penalty.
	private static void ConsumeFood(ProvinceEconomy p, GameBalance b)
	{
		float rationMultiplier = b.RationFoodMultiplier[(int)p.Ration];
		int required = Mathf.CeilToInt(p.Population / b.PeoplePerGrain * rationMultiplier);
		int deficit = required - p.Grain;
		p.Grain = Mathf.Max(0, p.Grain - required);
		p.StarvedThisTurn = deficit > 0;

		if (p.StarvedThisTurn)
		{
			float deficitFraction = (float)deficit / required;
			p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * deficitFraction * b.StarvationPopulationLossPerDeficit));
			p.Loyalty = Mathf.Clamp(p.Loyalty - deficitFraction * b.StarvationLoyaltyLossPerDeficit, 0f, 100f);
		}
	}

	// Design doc section 12: base regrowth plus a small yield from workers tending the herd.
	private static void GrowCattle(ProvinceEconomy p, ProvinceDefinition def, GameBalance b)
	{
		p.Cattle += Mathf.RoundToInt(p.Cattle * b.CattleBaseGrowthRate * def.CattleModifier + p.CattleWorkers * b.CattleYieldPerWorker);
	}

	// Design doc sections 18-20: tax income, and the loyalty cost/benefit of tax + ration choices.
	private static void CollectTaxes(ProvinceEconomy p, GameBalance b)
	{
		p.Gold += Mathf.RoundToInt(p.Population * b.GoldPerPopulation * b.TaxIncomeMultiplier[(int)p.Tax]);
		p.Loyalty = Mathf.Clamp(p.Loyalty + b.TaxLoyaltyDelta[(int)p.Tax] + b.RationLoyaltyDelta[(int)p.Ration], 0f, 100f);
	}

	// Design doc section 21: skipped the same turn the province starves.
	private static void GrowPopulation(ProvinceEconomy p, GameBalance b)
	{
		if (p.StarvedThisTurn)
		{
			return;
		}

		float growthRate = b.BasePopulationGrowthRate * b.RationGrowthMultiplier[(int)p.Ration];
		p.Population += Mathf.RoundToInt(p.Population * growthRate);
	}

	// Mirrors ProduceResources/GrowCattle without mutating state, for a live worker-allocation
	// preview (design doc point 32) where the player is trying out counts before committing them.
	public static int ProjectedYield(ResourceType type, int workers, int currentStock, ProvinceDefinition def, GameBalance b, Season season)
	{
		int s = (int)season;
		return type switch
		{
			ResourceType.Grain => Mathf.RoundToInt(workers * b.GrainYieldPerWorker * def.GrainModifier * b.GrainSeasonMultiplier[s]),
			ResourceType.Wood => Mathf.RoundToInt(workers * b.WoodYieldPerWorker * def.WoodModifier * b.WoodSeasonMultiplier[s]),
			ResourceType.Stone => Mathf.RoundToInt(workers * b.StoneYieldPerWorker * def.StoneModifier * b.StoneSeasonMultiplier[s]),
			ResourceType.Iron => Mathf.RoundToInt(workers * b.IronYieldPerWorker * def.IronModifier * b.IronSeasonMultiplier[s]),
			ResourceType.Cattle => Mathf.RoundToInt(currentStock * b.CattleBaseGrowthRate * def.CattleModifier + workers * b.CattleYieldPerWorker),
			_ => 0,
		};
	}

	/// <summary>Hand-verified regression check for the formulas above — call this (e.g.
	/// temporarily from an autoload's _Ready) after touching GameBalance or this file.</summary>
	public static void SelfCheck()
	{
		var balance = new GameBalance();
		var definition = new ProvinceDefinition();
		var province = new ProvinceEconomy
		{
			ProvinceName = "Test",
			Population = 1000,
			Grain = 100,
			GrainWorkers = 50,
			Cattle = 40,
			CattleWorkers = 10,
			Tax = TaxRate.Normal,
			Ration = RationLevel.Normal,
			Loyalty = 70f,
		};

		RunTurn(province, definition, balance, Season.Summer);

		Check(province.Grain == 120, $"Grain expected 120, got {province.Grain}");
		Check(province.Cattle == 44, $"Cattle expected 44, got {province.Cattle}");
		Check(province.Gold == 150, $"Gold expected 150, got {province.Gold}");
		Check(province.Population == 1010, $"Population expected 1010, got {province.Population}");
		Check(Mathf.IsEqualApprox(province.Loyalty, 70f), $"Loyalty expected 70, got {province.Loyalty}");
		Check(!province.StarvedThisTurn, "Expected no starvation with grain surplus");

		GD.Print("EconomySimulation.SelfCheck passed.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException($"EconomySimulation.SelfCheck failed: {message}");
		}
	}
}
