using Godot;

/// <summary>Where a fortification's picture is. Forts belong to the engine rather than to a
/// campaign — every realm builds the same ladder — so they live under assets/forts, keyed the same
/// way fortifications.json keys them.
///
/// The art may not be there yet. A page asks <see cref="Has"/> before it draws, so a fort with no
/// picture shows an empty mount instead of a missing-resource error, and lights up on its own the
/// day the file is dropped in.</summary>
public static class FortArt
{
	private const string Directory = "res://assets/forts";

	/// <summary>The thumbnail on a card: the fort on its own, cut out.</summary>
	public static string Picture(string fort) => $"{Directory}/{fort}.png";

	public static bool Has(string fort) => ResourceLoader.Exists(Picture(fort));

	/// <summary>The fort laid over the valley behind the page — a full frame, drawn to sit on the
	/// same hill as every other rung, so one can dissolve into the next without anything else in
	/// the picture moving.</summary>
	public static string Scene(string fort) => $"{Directory}/scene/{fort}.png";

	public static bool HasScene(string fort) => ResourceLoader.Exists(Scene(fort));
}
