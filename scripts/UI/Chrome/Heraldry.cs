using System.Collections.Generic;

/// <summary>Whose crest a realm carries: the lord who holds it on this map, the chosen lord for the
/// player's own, so a crest belongs to the lord and goes wherever he is seated
/// (assets/ui/icons/shield-&lt;lord&gt;.png). Filled by the map as it loads; a realm nobody has
/// seated is looked up under its own key.</summary>
public static class Heraldry
{
	private static readonly Dictionary<string, string> Wearers = new();

	public static void Clear() => Wearers.Clear();

	public static void Seat(string realm, string lord) => Wearers[realm] = lord;

	public static string CrestPath(string realm) =>
		$"{Chrome.IconDirectory}/shield-{Wearers.GetValueOrDefault(realm, realm)}.png";
}
