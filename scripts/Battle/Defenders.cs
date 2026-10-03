using System.Collections.Generic;

/// <summary>Who stands between an army and a county: the men in front of the walls, the men on
/// them, what the walls are, and how much the place is worth to the people holding it.
///
/// One answer for two questions that must never disagree — the banner the map hangs over a county,
/// and the battle that is fought when somebody rides up to it. It is also the whole of what a battle
/// needs to know about the defending side, so nothing downstream has to go back to the province and
/// read four more fields off it.
///
/// The two rosters are the two halves of a fight for a county, in the order they happen: whatever is
/// in the FIELD is beaten first, out in the open where numbers tell; whatever is in the CASTLE is
/// beaten afterwards and on much worse terms, which is the whole reason anybody quarries stone.</summary>
public readonly record struct Defenders(
	Dictionary<string, int> Field,
	Dictionary<string, int> Castle,
	string Fortification,
	float Loyalty,
	bool InOpenCountry = false)
{
	// InOpenCountry: two companies meeting away from any town. Nobody is holding his own town and no
	// people are at anybody's back, so neither the town's nor the people's term is reckoned.

	/// <summary>Everybody who would have to be got through, on the walls and in front of them. What
	/// the map puts on the shield under the banner.</summary>
	public int Men => ProvinceEconomy.Men(Field) + ProvinceEconomy.Men(Castle);

	/// <summary>Whether the county has walls with somebody behind them. Walls with nobody on them
	/// stop nothing: a castle is men, and the stone is only what they are standing on.</summary>
	public bool Held => Fortification.Length > 0 && ProvinceEconomy.Men(Castle) > 0;
}
