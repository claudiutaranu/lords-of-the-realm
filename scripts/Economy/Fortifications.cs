using System.Collections.Generic;
using Godot;

/// <summary>What a fortification is worth to the province that keeps it, read off the same
/// fortifications.json the building screen reads.
///
/// A castle is not only walls. A lord with a keep collects more from the same people than a lord
/// with a stockade does — his roads are safer, his market is worth coming to, and his reeve is
/// harder to refuse — which is what makes the stone worth quarrying for a province that is in no
/// danger of being attacked this year. And when it IS attacked, what stands around the seat is the
/// whole difference between a season lost and a county lost.
///
/// Every figure here is keyed by the rung actually built. The province carries that key on its
/// runtime state and the masons overwrite it the season they finish, so a wall counts for what it
/// IS and never for what it was or what is still going up behind it.
///
/// Loaded once and kept. The building screen parses the same file for the things only it needs —
/// blurbs, costs, the order they hang in — and the two should be made to meet the day something
/// else wants a fourth field out of it.</summary>
public static class Fortifications
{
	private const string DataPath = "res://data/fortifications.json";

	/// <summary>One rung of the ladder, as everything outside the building screen reads it.
	///
	/// <paramref name="Defence"/> multiplies what the men behind it are worth to kill.
	/// <paramref name="Frontage"/> is how many attackers can be brought against it at once, which is
	/// the figure that actually holds a castle: a multiplier can be answered by bringing more men,
	/// and a wall that could be answered that way is not a wall. Twenty-five at a time against a
	/// royal castle is why taking one is a matter of starving it rather than storming it.</summary>
	public readonly record struct Wall(string Name, float Tax, float Defence, int Frontage, int Stores);

	/// <summary>Open ground: no bonus, and every man an attacker has can be brought to bear at once.
	/// What a village with no walls answers to, and what an unreadable key answers to as well.</summary>
	private static readonly Wall None = new("open ground", 0f, 1f, int.MaxValue, 0);

	private static Dictionary<string, Wall> _walls;

	/// <summary>What stands at a province's seat, by the key it carries. An open village, or a rung
	/// nobody has written figures for, is open ground.</summary>
	public static Wall Of(string fortification)
	{
		if (string.IsNullOrEmpty(fortification))
		{
			return None;
		}

		_walls ??= Read();
		return _walls.TryGetValue(fortification, out Wall wall) ? wall : None;
	}

	/// <summary>How much a fortification adds to what the province collects, as a fraction of the
	/// tax it would take without one.</summary>
	public static float TaxBonus(string fortification) => Of(fortification).Tax;

	private static Dictionary<string, Wall> Read()
	{
		var walls = new Dictionary<string, Wall>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Fortifications: no readable {DataPath}");
			return walls;
		}

		foreach (Variant material in file.Data.AsGodotDictionary()["materials"].AsGodotArray())
		{
			foreach (Variant rung in material.AsGodotDictionary()["forts"].AsGodotArray())
			{
				Godot.Collections.Dictionary fort = rung.AsGodotDictionary();
				walls[fort["key"].AsString()] = new Wall(
					fort["name"].AsString(),
					fort.TryGetValue("tax", out Variant tax) ? (float)tax : 0f,
					fort.TryGetValue("defence", out Variant defence) ? (float)defence : 1f,
					fort.TryGetValue("frontage", out Variant frontage) ? (int)frontage : int.MaxValue,
					fort.TryGetValue("stores", out Variant stores) ? (int)stores : 0);
			}
		}

		return walls;
	}
}
