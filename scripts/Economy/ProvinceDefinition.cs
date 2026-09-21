using Godot;

/// <summary>A province's fixed economic identity: how many workers each industry can hold,
/// how good the province is at each one, and what it starts with. Runtime state (current
/// population, stockpiles, worker allocation) lives in <see cref="ProvinceEconomy"/> instead,
/// so resetting/replaying a campaign never mutates this authored data.</summary>
[GlobalClass]
public partial class ProvinceDefinition : Resource
{
	[Export] public string ProvinceName = "";

	[Export] public int InitialPopulation = 700;

	/// <summary>How much land the province has to work, in fields. It is the ceiling on everything
	/// the province eats: a county of eight fields cannot both feed itself on grain and keep a herd,
	/// and deciding which is what a lord is for.</summary>
	[Export] public int Fields = 10;

	/// <summary>How many of those fields are under grain when the campaign opens; the rest are split
	/// between pasture and rest. Authored rather than computed, so a province can start in trouble.</summary>
	[Export] public int InitialGrainFields = 4;
	[Export] public int InitialPastureFields = 3;

	[Export] public int GrainWorkerCapacity = 240;
	[Export] public int CattleWorkerCapacity = 120;
	[Export] public int WoodWorkerCapacity = 200;
	[Export] public int StoneWorkerCapacity = 120;
	[Export] public int IronWorkerCapacity = 80;

	[Export] public float GrainModifier = 1.0f;
	[Export] public float CattleModifier = 1.0f;
	[Export] public float WoodModifier = 1.0f;
	[Export] public float StoneModifier = 1.0f;
	[Export] public float IronModifier = 1.0f;

	[Export] public int InitialGold = 400;
	[Export] public int InitialGrain = 250;
	[Export] public int InitialCattle = 40;
	[Export] public int InitialWood = 180;
	[Export] public int InitialStone = 150;
	[Export] public int InitialIron = 60;
}
