using Godot;

/// <summary>Every tunable economy constant, in one place, editable from a .tres without
/// touching code. Season/tax/ration arrays are indexed by the matching enum's ordinal —
/// keep their lengths and order in sync with <see cref="Season"/>, <see cref="TaxRate"/>
/// and <see cref="RationLevel"/>.</summary>
[GlobalClass]
public partial class GameBalance : Resource
{
	[Export] public float WorkerRatio = 0.25f;

	[Export] public float GrainYieldPerWorker = 2.0f;
	[Export] public float WoodYieldPerWorker = 1.5f;
	[Export] public float StoneYieldPerWorker = 1.2f;
	[Export] public float IronYieldPerWorker = 1.0f;

	[Export] public float CattleBaseGrowthRate = 0.05f;
	[Export] public float CattleYieldPerWorker = 0.2f;

	[Export] public float PeoplePerGrain = 10f;
	[Export] public float BasePopulationGrowthRate = 0.01f;
	[Export] public float GoldPerPopulation = 0.15f;

	[Export] public float StarvationPopulationLossPerDeficit = 0.075f;
	[Export] public float StarvationLoyaltyLossPerDeficit = 25f;

	// [Spring, Summer, Autumn, Winter]
	[Export] public float[] GrainSeasonMultiplier = { 0.90f, 1.20f, 1.40f, 0.20f };
	[Export] public float[] CattleSeasonMultiplier = { 1.00f, 1.10f, 1.10f, 0.80f };
	[Export] public float[] WoodSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };
	[Export] public float[] StoneSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.70f };
	[Export] public float[] IronSeasonMultiplier = { 1.00f, 1.00f, 1.00f, 0.80f };

	// [None, Low, Normal, High, Severe]
	[Export] public float[] TaxIncomeMultiplier = { 0f, 0.5f, 1.0f, 1.5f, 2.0f };
	[Export] public float[] TaxLoyaltyDelta = { 4f, 2f, 0f, -4f, -10f };

	// [Low, Normal, High]
	[Export] public float[] RationFoodMultiplier = { 0.7f, 1.0f, 1.3f };
	[Export] public float[] RationGrowthMultiplier = { 0.5f, 1.0f, 1.5f };
	[Export] public float[] RationLoyaltyDelta = { -5f, 0f, 3f };
}
