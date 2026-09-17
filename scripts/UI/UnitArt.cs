/// <summary>Where a soldier's pictures are, and which ground he stands on.
///
/// Soldiers belong to the engine rather than to a campaign — every realm musters the same six — so
/// all of this lives under assets/units and is keyed by the weapon the man carries, the same key
/// weapons.json and the weapon glyphs use. One key names his portrait, his backdrop and his icon.
///
/// The backdrops are shared: three scenes for six soldiers, because an archer and a crossbowman
/// train on the same ground.</summary>
public static class UnitArt
{
	private const string Directory = "res://assets/units";

	public static string Portrait(string unit) => $"{Directory}/{unit}.png";

	public static string Backdrop(string unit) => $"{Directory}/backgrounds/{Ground(unit)}.jpg";

	private static string Ground(string unit) => unit switch
	{
		"bow" or "crossbow" => "training-ground",  // the butts they shoot at
		"horse" or "peasant" => "meadow",          // the field: one charges over it, one works it
		_ => "courtyard",                          // foot soldiers muster inside the walls
	};
}
