using System.Collections.Generic;

/// <summary>One body of men standing on the map, with its own legs and its own orders.
///
/// A county used to have exactly one — its roster WAS its army — and that made two things
/// impossible that a lord expects to be able to do: raise a second company without it walking into
/// the first, and leave men holding one place while the rest go somewhere else. So the men live
/// here now, and a county holds a list of these rather than a roster.
///
/// It stays on the county that raised it for as long as it exists, wherever it walks to: that
/// county pays it, feeds it and is thinned when it deserts. <see cref="County"/> is where the men
/// are, which is a different question and the one the map and the battles ask.</summary>
public class FieldArmy
{
	/// <summary>The county that raised them, which is also the list they live in. Kept on the army
	/// as well so that an order needs one thing passed to it and not two.</summary>
	public string Home = "";

	/// <summary>Which of its home county's armies this is. Never reused, so a banner the player has
	/// hold of cannot quietly become a different company when another one is disbanded.</summary>
	public int Id;

	/// <summary>The county they are standing in. Their own until they are marched out of it.</summary>
	public string County = "";

	/// <summary>The men, by unit key. A kind wiped out is gone from here rather than left at
	/// nought.</summary>
	public Dictionary<string, int> Men = new();

	/// <summary>What this season has left in their legs, in map pixels of road. Per army and not per
	/// county: two companies of the same county were sent to different places, and the ground one of
	/// them spent is not the other's to answer for.</summary>
	public float MarchLeft;

	/// <summary>Where they stand, in map pixels. Zero means they have never been sent anywhere and
	/// belong at their county's seat, which is where they were raised.</summary>
	public float X, Y;

	/// <summary>How the map and the screens name one army. The county alone no longer picks one
	/// out.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string Key => $"{Home}#{Id}";

	[System.Text.Json.Serialization.JsonIgnore]
	public int Strength => ProvinceEconomy.Men(Men);
}
