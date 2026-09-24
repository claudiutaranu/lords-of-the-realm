using System.Collections.Generic;
using Godot;

/// <summary>What one weapon costs the forge, read off the same weapons.json the smithy's room reads.
///
/// The forge works as Lords of the Realm's does: a county's smithy makes one kind of weapon, all the
/// time, and every season turns out as many as its smiths can hammer and its stores can pay for —
/// out of last season's iron and timber, since the forge is lit before anybody goes down the mine.
/// Nothing is ordered and waited on; the lord says what, and the labour bar says how many.
///
/// Loaded once and kept. The room parses the same file for what only it needs — blurbs and the
/// combat bars.</summary>
public static class Smithy
{
	private const string DataPath = "res://data/weapons.json";

	private static Dictionary<string, Dictionary<string, int>> _recipes;

	/// <summary>What one of a weapon takes out of the county's stores. Empty for a key nobody has
	/// written a recipe for, which the forge reads as nothing it can make.</summary>
	public static Dictionary<string, int> Recipe(string weapon)
	{
		_recipes ??= Read();
		return _recipes.TryGetValue(weapon, out Dictionary<string, int> recipe) ? recipe : new Dictionary<string, int>();
	}

	/// <summary>How many of a weapon the county's stores pay for, whoever is at the anvil.</summary>
	public static int Affordable(ProvinceEconomy p, string weapon)
	{
		Dictionary<string, int> recipe = Recipe(weapon);
		if (recipe.Count == 0)
		{
			return 0;
		}

		int most = int.MaxValue;
		foreach ((string store, int each) in recipe)
		{
			if (each > 0)
			{
				most = Mathf.Min(most, p.Stored(store) / each);
			}
		}

		return most;
	}

	private static Dictionary<string, Dictionary<string, int>> Read()
	{
		var recipes = new Dictionary<string, Dictionary<string, int>>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Smithy: no readable {DataPath}");
			return recipes;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["items"].AsGodotArray())
		{
			Godot.Collections.Dictionary item = entry.AsGodotDictionary();
			var cost = new Dictionary<string, int>();
			foreach (KeyValuePair<Variant, Variant> line in item["cost"].AsGodotDictionary())
			{
				cost[line.Key.AsString()] = line.Value.AsInt32();
			}

			recipes[item["key"].AsString()] = cost;
		}

		return recipes;
	}
}
