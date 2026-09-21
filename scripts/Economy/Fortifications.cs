using System.Collections.Generic;
using Godot;

/// <summary>What a fortification is worth to the province that keeps it, read off the same
/// fortifications.json the building screen reads.
///
/// A castle is not only walls. A lord with a keep collects more from the same people than a lord
/// with a stockade does — his roads are safer, his market is worth coming to, and his reeve is
/// harder to refuse — which is what makes the stone worth quarrying for a province that is in no
/// danger of being attacked this year.
///
/// Loaded once and kept. The building screen parses the same file for the things only it needs —
/// blurbs, costs, the order they hang in — and the two should be made to meet the day something
/// else wants a third field out of it.</summary>
public static class Fortifications
{
	private const string DataPath = "res://data/fortifications.json";

	private static Dictionary<string, float> _tax;

	/// <summary>How much a fortification adds to what the province collects, as a fraction of the
	/// tax it would take without one. An open village, or a wall nobody has written a figure for,
	/// adds nothing.</summary>
	public static float TaxBonus(string fortification)
	{
		if (string.IsNullOrEmpty(fortification))
		{
			return 0f;
		}

		_tax ??= Read();
		return _tax.GetValueOrDefault(fortification);
	}

	private static Dictionary<string, float> Read()
	{
		var bonus = new Dictionary<string, float>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Fortifications: no readable {DataPath}");
			return bonus;
		}

		foreach (Variant material in file.Data.AsGodotDictionary()["materials"].AsGodotArray())
		{
			foreach (Variant rung in material.AsGodotDictionary()["forts"].AsGodotArray())
			{
				Godot.Collections.Dictionary fort = rung.AsGodotDictionary();
				bonus[fort["key"].AsString()] =
					fort.TryGetValue("tax", out Variant tax) ? (float)tax : 0f;
			}
		}

		return bonus;
	}
}
