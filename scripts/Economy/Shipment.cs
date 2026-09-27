using System.Collections.Generic;

/// <summary>A cart of the lord's own stores on the road from one of his counties to another (Lords
/// of the Realm's "Send supplies", which only ever carted grain and cattle; here anything the county
/// keeps can go — timber for a wall going up, iron for a smithy that has none, spears for the
/// company raised at the far end).
///
/// A realm keeps one purse but its stores stay where they were reaped, so a granary in the south is
/// no use to a hungry county in the north until somebody carts it there — and a cart is slow, and it
/// is out in the open. The goods left the source's stores the day it set out; they are in nobody's
/// while it rolls, and they reach the destination's only when it does.</summary>
public class Shipment
{
	public string From = "";
	public string To = "";

	/// <summary>What is on it, by the store names <see cref="ProvinceEconomy.Stored"/> reads.</summary>
	public Dictionary<string, int> Goods = new();

	/// <summary>Where the cart stands, in map pixels: its source's village the day it sets out, then
	/// wherever the season's road ended.</summary>
	public float X, Y;
}
