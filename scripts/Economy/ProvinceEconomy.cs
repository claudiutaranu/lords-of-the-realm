using Godot;

/// <summary>Runtime economic state for one province — everything that changes turn to
/// turn. Paired with a ProvinceDefinition (fixed identity) and the shared GameBalance
/// (global constants) by EconomySimulation. Plain data, no Godot node/Resource ties, so
/// it can be created, copied and saved freely.</summary>
public class ProvinceEconomy
{
	public string ProvinceName;
	public int Population;
	public float Loyalty = 70f;
	public TaxRate Tax = TaxRate.Normal;
	public RationLevel Ration = RationLevel.Normal;

	public int Gold;
	public int Grain;
	public int Cattle;
	public int Wood;
	public int Stone;
	public int Iron;

	public int GrainWorkers;
	public int CattleWorkers;
	public int WoodWorkers;
	public int StoneWorkers;
	public int IronWorkers;

	// Set by EconomySimulation each turn; population growth is skipped the same turn a
	// province starves (design doc section 11).
	public bool StarvedThisTurn;

	public int AllocatedWorkers => GrainWorkers + CattleWorkers + WoodWorkers + StoneWorkers + IronWorkers;

	public int AvailableWorkers(GameBalance balance) => Mathf.FloorToInt(Population * balance.WorkerRatio);

	public static ProvinceEconomy FromDefinition(ProvinceDefinition definition) => new()
	{
		ProvinceName = definition.ProvinceName,
		Population = definition.InitialPopulation,
		Gold = definition.InitialGold,
		Grain = definition.InitialGrain,
		Cattle = definition.InitialCattle,
		Wood = definition.InitialWood,
		Stone = definition.InitialStone,
		Iron = definition.InitialIron,
	};
}
