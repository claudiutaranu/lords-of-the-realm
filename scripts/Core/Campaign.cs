using System.Linq;
using Godot;

/// <summary>Which campaign is being played, and where its files live.
///
/// A campaign is a realm fought over map after map, and each map owns a folder and nothing outside
/// it: data/campaigns/&lt;campaign&gt;/maps/&lt;map&gt; holds its provinces and its map data,
/// assets/campaigns/&lt;campaign&gt;/maps/&lt;map&gt; its map images and art. Folder is that whole
/// path below campaigns/ ("england/maps/royal-crown"). Two maps can therefore never read each other's. What the engine applies to all of them the same way stays
/// shared where it is — game-balance.tres, the shaders, the ground textures, the scenes, and every
/// script here. Adding a campaign is a folder of each kind plus one entry on CampaignPage's list,
/// with no engine code to touch.
///
/// A static handover slot rather than an autoload, the same way SaveGame.Pending and
/// LoadingPage.TargetScenePath hand state to the scene that opens next.</summary>
public static class Campaign
{
	/// <summary>Set when a campaign card is chosen, and read by the campaign-map scene while it
	/// builds. The Royal Crown by default, so that scene still opens straight from the editor — or
	/// whichever map the game was started on with --map=&lt;campaign&gt;/maps/&lt;map&gt; after the
	/// "--", to play a later map without winning the ones before it.</summary>
	public static string Folder = OS.GetCmdlineUserArgs().FirstOrDefault(arg => arg.StartsWith(MapArgument))?[MapArgument.Length..]
		?? "england/maps/royal-crown";

	/// <summary>What the campaign is called on screen and in a save's header.</summary>
	public static string Name = Field(Folder, "name") ?? Folder;

	private const string MapArgument = "--map=";

	/// <summary>How well the lords who are not the player will play it. Chosen on the briefing page,
	/// the same way the folder is, and handed to the TurnManager when the map builds — after which
	/// the campaign's own copy of it is what counts, so that a loaded save is played at the
	/// difficulty it was started on.</summary>
	public static Difficulty Difficulty = Difficulty.Medium;

	/// <summary>The lord the player plays (lords.json), chosen before the campaign: the Crown unless
	/// he chose another.</summary>
	public static string Player = Crown;
	public const string Crown = "crown";

	public static string Data(string file) => DataOf(Folder, file);

	/// <summary>A map's realms (provinces.json "realms") with the chosen lord in the player's seat: the
	/// played realm under his realm's name, and a realm the map had given him passed to the Crown
	/// under the Crown's, so no lord stands on a map twice and every map keeps the balance it was
	/// drawn with (the user's call: you start where the player always starts). A copy, since the
	/// file's own dictionary is the one every later load of it is handed.</summary>
	public static Godot.Collections.Dictionary Seated(Godot.Collections.Dictionary realms, string playerRealm)
	{
		Godot.Collections.Dictionary seated = realms.Duplicate(true);
		if (Player == Crown || Lords.Find(Player) is not Lord chosen)
		{
			return seated;
		}

		foreach (Variant realm in seated.Values)
		{
			Godot.Collections.Dictionary fields = realm.AsGodotDictionary();
			if (fields.TryGetValue("lord", out Variant lord) && lord.AsString() == Player)
			{
				fields["lord"] = Crown;
				fields["name"] = Lords.Find(Crown)?.Realm ?? fields["name"];
			}
		}

		seated[playerRealm].AsGodotDictionary()["name"] = chosen.Realm;
		return seated;
	}

	/// <summary>Sets a map of a campaign to be played, under the name its own provinces.json gives it.</summary>
	public static void Open(string folder)
	{
		Folder = folder;
		Name = Field(folder, "name") is { Length: > 0 } name ? name : folder;
	}

	/// <summary>Moves on to the map after this one (provinces.json "next") and says whether there was
	/// one: on a campaign's last map there is nothing further to march to.</summary>
	public static bool Advance()
	{
		if (Field(Folder, "next") is not { Length: > 0 } next)
		{
			return false;
		}

		Open(next);
		return true;
	}

	/// <summary>One top-level word of a map's provinces.json, or null where it says nothing of it.</summary>
	public static string Field(string folder, string key) =>
		GD.Load<Json>(DataOf(folder, "provinces.json"))?.Data.AsGodotDictionary().TryGetValue(key, out Variant value) == true
			? value.AsString()
			: null;

	public static string Asset(string file) => AssetOf(Folder, file);

	/// <summary>A campaign other than the one being played — the card list drawing all of them,
	/// or a briefing showing the rival's portrait.</summary>
	public static string AssetOf(string folder, string file) => $"res://assets/campaigns/{folder}/{file}";

	private static string DataOf(string folder, string file) => $"res://data/campaigns/{folder}/{file}";
}
