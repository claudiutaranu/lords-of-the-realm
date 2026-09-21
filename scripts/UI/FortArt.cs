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

	/// <summary>What moves on a fort that is otherwise a still picture: the flags on its poles and
	/// the banners down its walls, waved by a shader reading a mask painted over the same art. A
	/// material rather than a second picture, so the wall the player is looking at is the same
	/// pixels either way and a rung with nothing painted for it simply stands still.</summary>
	public static string Cloth(string fort) => $"{Directory}/scene/{fort}_cloth.tres";

	public static bool HasCloth(string fort) => ResourceLoader.Exists(Cloth(fort));

	/// <summary>Whatever hangs, swings or walks on a fort and cannot be done by pushing the picture's
	/// own pixels about — a stone swaying under a crane. A scene laid over the wall in the picture's
	/// own pixels, measured from its middle, so one placement holds however large the valley is
	/// drawn.</summary>
	public static string Life(string fort) => $"{Directory}/scene/{fort}-life.tscn";

	public static bool HasLife(string fort) => ResourceLoader.Exists(Life(fort));
}
