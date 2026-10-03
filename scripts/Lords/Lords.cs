using System.Collections.Generic;
using Godot;

/// <summary>One of the four lords of the realm, as data/lords.json has him. <paramref name="Letters"/>
/// is what he writes back, by what he is answering.</summary>
public record Lord(string Key, string Title, string Personality, int OffersAllianceEvery, int GrudgeLimit,
	IReadOnlyDictionary<string, string> Letters)
{
	/// <summary>His line for a reply, with a gift's size written in. A line he has not been given
	/// comes back empty rather than as another lord's words.</summary>
	public string Says(string reply, int gold = 0) =>
		Letters.GetValueOrDefault(reply, "").Replace("{gold}", gold.ToString("N0"));
}

/// <summary>The roster of the four lords, read once for every campaign. A campaign only names which
/// of them holds a realm; what a lord is — how soon he offers friendship, how long he keeps it, how
/// he writes — belongs to him and not to the map he is played on.</summary>
public static class Lords
{
	private const string DataPath = "res://data/lords.json";

	private static Dictionary<string, Lord> _roster;

	/// <summary>The lord of that key, or null for a realm nobody has seated one in — which is what
	/// diplomacy reads as a realm that does not write letters.</summary>
	public static Lord Find(string key)
	{
		Load();
		return key is { Length: > 0 } ? _roster.GetValueOrDefault(key) : null;
	}

	private static void Load()
	{
		if (_roster != null)
		{
			return;
		}

		_roster = new Dictionary<string, Lord>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Lords: no readable {DataPath}");
			return;
		}

		foreach (KeyValuePair<Variant, Variant> entry in file.Data.AsGodotDictionary()["lords"].AsGodotDictionary())
		{
			Godot.Collections.Dictionary fields = entry.Value.AsGodotDictionary();
			var letters = new Dictionary<string, string>();
			foreach (KeyValuePair<Variant, Variant> line in fields["letters"].AsGodotDictionary())
			{
				letters[line.Key.AsString()] = line.Value.AsString();
			}

			string key = entry.Key.AsString();
			_roster[key] = new Lord(key, fields["title"].AsString(), fields["personality"].AsString(),
				fields["offersAllianceEvery"].AsInt32(), fields["grudgeLimit"].AsInt32(), letters);
		}
	}
}
