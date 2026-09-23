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

	/// <summary>The men the province stands up the day the campaign opens, by unit key. A lord's
	/// county opens with his own army on it; a county nobody holds has none authored at all, because
	/// what it fields is not an army — it is the place itself, and how many of them stand up is read
	/// off its people (see <see cref="GameBalance.MilitiaShare"/>, by difficulty).
	///
	/// Authored and not derived for a lord, for the same reason his stores are: a rival meant to open
	/// ahead should be ahead on the map, where it can be seen and planned against, rather than in a
	/// number somewhere inside the engine.</summary>
	[Export] public Godot.Collections.Dictionary InitialGarrison = new();

	/// <summary>What stands around the seat when the campaign opens, by the key fortifications.json
	/// uses. Empty for a county with no walls.
	///
	/// It is not decoration. No lord in this game builds a castle of his own yet, so a campaign that
	/// authors none is a campaign where walls are only ever something the player stands behind and
	/// never something he has to take.</summary>
	[Export] public string InitialFortification = "";
}
