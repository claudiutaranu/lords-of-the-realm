using System.Collections.Generic;
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

	/// <summary>How much of one store the province holds, by the same key the market and the smithy
	/// price things in. The six raw stores and the head count are fields; anything else is a rack in
	/// the armoury, which is keyed by name and open-ended by design — the smithy fills it from
	/// weapons.json and the market trades out of it. A name nobody has ever put anything under reads
	/// as empty rather than throwing.</summary>
	public int Stored(string store) => store switch
	{
		"gold" => Gold,
		"grain" => Grain,
		"cattle" => Cattle,
		"wood" => Wood,
		"stone" => Stone,
		"iron" => Iron,
		"people" => Population,
		_ => Armoury.GetValueOrDefault(store),
	};

	/// <summary>Moves one store by a signed amount — the single place a trade, a wage or a harvest
	/// reaches into the pile. It does not ask whether the move makes sense: whether a name can be
	/// traded at all is the market's to answer, before it gets here.</summary>
	public void Add(string store, int amount)
	{
		switch (store)
		{
			case "gold": Gold += amount; break;
			case "grain": Grain += amount; break;
			case "cattle": Cattle += amount; break;
			case "wood": Wood += amount; break;
			case "stone": Stone += amount; break;
			case "iron": Iron += amount; break;
			case "people": Population += amount; break;
			default: Armoury[store] = Armoury.GetValueOrDefault(store) + amount; break;
		}
	}

	/// <summary>What the smithy is forging, by weapon key, and how many turns are left on it. Empty
	/// when the forge is cold. The order is paid for when it is placed, so a save carries only what
	/// is still owed.</summary>
	public string Forging = "";
	public int ForgeTurnsLeft;

	/// <summary>How many the order delivers when it finishes — carried with the order rather than
	/// looked up later, so retuning weapons.json never changes what is already on the anvil.</summary>
	public int ForgeBatch;

	/// <summary>Finished weapons the province holds, by the same key — what the yard arms its
	/// recruits out of.</summary>
	public Dictionary<string, int> Armoury = new();

	/// <summary>What the training yard is raising, and how many turns are left on the intake. Paid
	/// for when it is ordered, in people and in arms out of the armoury.</summary>
	public string Training = "";
	public int TrainTurnsLeft;
	public int TrainBatch;

	/// <summary>The men standing in the province, by unit key. What an army is drawn from, once
	/// there are armies.</summary>
	public Dictionary<string, int> Garrison = new();

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
