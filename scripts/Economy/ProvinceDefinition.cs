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

	[Export] public int GrainWorkerCapacity = 60;
	[Export] public int CattleWorkerCapacity = 30;
	[Export] public int WoodWorkerCapacity = 50;
	[Export] public int StoneWorkerCapacity = 30;
	[Export] public int IronWorkerCapacity = 20;

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
