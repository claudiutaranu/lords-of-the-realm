using System.Collections.Generic;
using Godot;

/// <summary>What a soldier is worth in a fight, read off the same recruits.json the training yard
/// deals its cards from.
///
/// The yard already had these four bars on every card — attack, range, defence, speed — and a
/// player who has been reading them since his first intake is owed a battle that is decided by
/// them. A second set of numbers kept somewhere else for the fighting would make every card in the
/// barracks a decoration.
///
/// A hired band is a kind of its own, as in Lords of the Realm: Swiss Pikemen are not spearmen, they
/// fight on the band's own bars (mercenaries.json) and carry its name onto every roster, so a lord
/// looking at his company can see who in it is hired. They take the look of the kind they fight as.
///
/// Loaded once and kept, the same way <see cref="Fortifications"/> keeps the wall ladder.</summary>
public static class Units
{
	private const string DataPath = "res://data/recruits.json";

	/// <summary>One kind of soldier. <paramref name="Mounted"/> is what a man cannot take up a
	/// ladder: horses are useless against a wall, and the data says so rather than a string
	/// comparison somewhere in the battle deciding it. <paramref name="Icon"/> is his glyph under
	/// assets/ui/icons, which is his own key unless the file says otherwise.</summary>
	public readonly record struct Unit(string Name, int Attack, int Range, int Defence, int Speed,
		bool Mounted, string Icon);

	/// <summary>A man nobody has written down. Not zero: a company that fought as nothing would make
	/// a typo in a save file into a massacre, and this way it fights badly and is noticed.</summary>
	private static readonly Unit Unknown = new("Men", 1, 1, 1, 1, false, "men");

	private static Dictionary<string, Unit> _units;

	public static Unit Of(string unit)
	{
		_units ??= Read();
		if (_units.TryGetValue(unit, out Unit found))
		{
			return found;
		}

		GD.PushError($"Units: nothing in {DataPath} called {unit}");
		return Unknown;
	}

	/// <summary>Whether this kind is a hired band rather than the county's own men.</summary>
	public static bool IsHired(string unit) => Mercenaries.Find(unit) != null;

	/// <summary>Every kind there is, in the order the file lists them, then the hired bands — which is the order the yard
	/// deals them out in, so a muster reads the same on both screens.</summary>
	public static IEnumerable<string> All()
	{
		_units ??= Read();
		return _units.Keys;
	}

	private static Dictionary<string, Unit> Read()
	{
		var units = new Dictionary<string, Unit>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Units: no readable {DataPath}");
			return units;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["items"].AsGodotArray())
		{
			Godot.Collections.Dictionary man = entry.AsGodotDictionary();
			string key = man["key"].AsString();
			units[key] = new Unit(
				man["name"].AsString(),
				man["attack"].AsInt32(),
				man["range"].AsInt32(),
				man["defence"].AsInt32(),
				man["speed"].AsInt32(),
				man.TryGetValue("mounted", out Variant mounted) && mounted.AsBool(),
				man.TryGetValue("icon", out Variant icon) ? icon.AsString() : key);
		}

		foreach (MercenaryBand band in Mercenaries.All)
		{
			Unit like = units.GetValueOrDefault(band.Unit, Unknown);
			units[band.Key] = new Unit(band.Name, band.Attack, band.Range, band.Defence, band.Speed, like.Mounted, like.Icon);
		}

		return units;
	}
}
