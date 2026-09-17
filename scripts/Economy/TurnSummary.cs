/// <summary>Before/after snapshot of one province's numbers after an EconomySimulation.RunTurn
/// call — the data behind the turn-end results the player sees (design doc section 15).</summary>
public class TurnSummary
{
	public string ProvinceName;

	public int PopulationBefore, PopulationAfter;
	public float LoyaltyBefore, LoyaltyAfter;
	public int GoldBefore, GoldAfter;
	public int GrainBefore, GrainAfter;
	public int CattleBefore, CattleAfter;
	public int WoodBefore, WoodAfter;
	public int StoneBefore, StoneAfter;
	public int IronBefore, IronAfter;

	public int PopulationChange => PopulationAfter - PopulationBefore;
	public float LoyaltyChange => LoyaltyAfter - LoyaltyBefore;
	public int GoldChange => GoldAfter - GoldBefore;
	public int GrainChange => GrainAfter - GrainBefore;
	public int CattleChange => CattleAfter - CattleBefore;
	public int WoodChange => WoodAfter - WoodBefore;
	public int StoneChange => StoneAfter - StoneBefore;
	public int IronChange => IronAfter - IronBefore;
}
