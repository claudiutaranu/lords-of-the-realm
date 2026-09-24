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

	/// <summary>What the season did with the land and the herd, which the before/after numbers
	/// cannot show: a granary that stayed level may have been fed by the harvest and emptied by the
	/// people in the same turn.</summary>
	public int Sown, Harvest, Dairy, Slaughtered;

	/// <summary>What the county had to be fed this season and where it came from: the whole bill,
	/// then the part of it the granary paid. The dairy and the knife are counted in Dairy and
	/// Slaughtered above, so these four together are the meal — which is what lets the ration table
	/// show a lord what he is about to eat without working it out a second way.</summary>
	public int Needed, Bread;

	/// <summary>The ration the county was actually served, which is what its goodwill and its births
	/// both answer to. A lord can order triple into an empty barn all he likes.</summary>
	public RationLevel Achieved;

	/// <summary>Food the province could not find anywhere — after the dairy, the granary and the
	/// knife. Anything above zero is a province starving.</summary>
	public int FoodShort;

	/// <summary>What each thing the lord decides took out of the province's goodwill this season,
	/// as signed numbers. Kept apart rather than summed because the event engine has to name the
	/// heaviest of them: a narrator who tells a lord he taxed his people to rebellion on the turn he
	/// actually starved them is worse than a narrator who says nothing.</summary>
	public float LoyaltyFromTax, LoyaltyFromRations, LoyaltyFromStarvation, LoyaltyFromConscription,
		LoyaltyFromGarrison;

	/// <summary>And what the world did to it on its own account — a plague through the village, or a
	/// harvest so good they drank to his health. Not the lord's doing either way, which is why it is
	/// the one line on the happiness table he cannot answer for.</summary>
	public float LoyaltyFromEvents;

	/// <summary>And what the lord's OTHER counties cost this one: word of a shire being squeezed
	/// travels, and the next shire draws its own conclusions. The only grievance here that is not
	/// about this county at all, which is why it is not folded in with the tax — the advisor must
	/// never tell a lord his people resent a tax he did not levy on them.</summary>
	public float LoyaltyFromNeighbours;

	/// <summary>What the men under arms cost this season: the food they ate, the gold they were
	/// owed, and how many walked away because the treasury could not find it.</summary>
	public int SoldierFood, Wages, Deserted;

	/// <summary>The wall the masons finished this season, or empty. Kept because it happens behind
	/// the turn's curtain: the steward has to be able to say so after it lifts.</summary>
	public string WallRaised = "";

	/// <summary>Hands that did nothing: allocated to a task that could not use them, or never
	/// allocated at all.</summary>
	public int IdleWorkers;

	/// <summary>Stamps the after-numbers off the province as it stands now. Called once when the
	/// turn's arithmetic is done, and again after the world has had its turn — a plague that killed
	/// a tenth of the county between those two calls has to show in the same summary.</summary>
	public void Restate(ProvinceEconomy province)
	{
		PopulationAfter = province.Population;
		LoyaltyAfter = province.Loyalty;
		GoldAfter = province.Gold;
		GrainAfter = province.Grain;
		CattleAfter = province.Cattle;
		WoodAfter = province.Wood;
		StoneAfter = province.Stone;
		IronAfter = province.Iron;
	}

	public int PopulationChange => PopulationAfter - PopulationBefore;
	public float LoyaltyChange => LoyaltyAfter - LoyaltyBefore;
	public int GoldChange => GoldAfter - GoldBefore;
	public int GrainChange => GrainAfter - GrainBefore;
	public int CattleChange => CattleAfter - CattleBefore;
	public int WoodChange => WoodAfter - WoodBefore;
	public int StoneChange => StoneAfter - StoneBefore;
	public int IronChange => IronAfter - IronBefore;
}
