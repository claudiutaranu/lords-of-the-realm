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
}
