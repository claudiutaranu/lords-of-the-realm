using Godot;

/// <summary>Where a city's pictures are: the town itself, and one tile per building on it.
///
/// Buildings belong to the engine rather than to a campaign — every realm raises the same set — so
/// they live under assets/city, keyed the way buildings.json keys them.
///
/// A tile may not be drawn yet. A page asks <see cref="HasTile"/> before it draws, so a building
/// with no picture shows an empty mount instead of a missing-resource error, and lights up on its
/// own the day the file is dropped in.</summary>
public static class CityArt
{
	private const string Directory = "res://assets/city";

	public static string Town => $"{Directory}/town.png";

	public static string Tile(string building) => $"{Directory}/buildings/{building}.png";

	public static bool HasTile(string building) => ResourceLoader.Exists(Tile(building));

	/// <summary>The banner a building's name is written on: its shield and a bar to write on.
	/// Optional too — a building without one keeps the plain plate.</summary>
	public static string Plaque(string building) => $"{Directory}/plaques/{building}.png";

	public static bool HasPlaque(string building) => ResourceLoader.Exists(Plaque(building));

	/// <summary>The sheet a building's moving piece is drawn on — the sails, the wheel, the crane.
	/// Optional: most buildings stand still.</summary>
	public static string Rotor(string building) => $"{Directory}/buildings/{building}-rotor.png";

	public static bool HasRotor(string building) => ResourceLoader.Exists(Rotor(building));

	/// <summary>Whatever lives on a building's ground and moves of its own accord — the herd in a
	/// pasture, the men in a yard. A scene rather than a picture, because each of them animates on
	/// its own clock and no two should do it in step.</summary>
	public static string Life(string building) => $"{Directory}/buildings/{building}-life.tscn";

	public static bool HasLife(string building) => ResourceLoader.Exists(Life(building));
}
