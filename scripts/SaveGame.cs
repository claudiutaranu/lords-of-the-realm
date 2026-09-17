using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

/// <summary>One saved campaign: the turn number plus every province's runtime economy,
/// written as JSON under user://saves. Only runtime state is stored — province identity,
/// balance constants and map layout come from the project's own resources on load, so an
/// old save still opens after those are re-authored.</summary>
public class SaveGame
{
	private const string SaveDirectory = "user://saves";

	// ProvinceEconomy keeps its numbers in public fields, which System.Text.Json skips by default;
	// read-only properties are derived values (AllocatedWorkers, SavedAtDisplay) that would only
	// go stale in a file that can't feed them back in.
	// Tax/Ration go in as names, not ordinals: reordering those enums later must not silently
	// turn every old save's Normal tax into Severe.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		IncludeFields = true,
		IgnoreReadOnlyProperties = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	/// <summary>Handover slot between LoadGamePage and the campaign map, which rebuilds its
	/// TurnManager from this instead of the province definitions' starting values.</summary>
	public static SaveGame Pending;

	public string CampaignName { get; set; } = "";
	public int Turn { get; set; }
	public long SavedAtUnix { get; set; }
	public List<ProvinceEconomy> Provinces { get; set; } = new();

	public string SavedAtDisplay => Time.GetDatetimeStringFromUnixTime(SavedAtUnix, true).Replace("T", " ");

	public static SaveGame Snapshot(string campaignName, int turn, List<ProvinceEconomy> provinces) => new()
	{
		CampaignName = campaignName,
		Turn = turn,
		SavedAtUnix = (long)Time.GetUnixTimeFromSystem(),
		Provinces = provinces,
	};

	public static void Write(string campaignName, int turn, List<ProvinceEconomy> provinces)
	{
		DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);
		SaveGame save = Snapshot(campaignName, turn, provinces);

		// Unix seconds name the file, so two saves a second apart never collide and the same
		// second overwrites itself rather than piling up duplicates of one state.
		using FileAccess file = FileAccess.Open($"{SaveDirectory}/{save.SavedAtUnix}.json", FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PushError($"SaveGame: could not write save ({FileAccess.GetOpenError()})");
			return;
		}

		file.StoreString(JsonSerializer.Serialize(save, JsonOptions));
	}

	/// <summary>Newest first. A save that fails to parse is skipped, not fatal — one corrupt
	/// file must not take the whole list down with it.</summary>
	public static List<SaveGame> List()
	{
		var saves = new List<SaveGame>();
		if (!DirAccess.DirExistsAbsolute(SaveDirectory))
		{
			return saves;
		}

		foreach (string fileName in DirAccess.GetFilesAt(SaveDirectory))
		{
			if (!fileName.EndsWith(".json"))
			{
				continue;
			}

			SaveGame save = Read($"{SaveDirectory}/{fileName}");
			if (save != null)
			{
				saves.Add(save);
			}
		}

		// Sorted on the timestamp inside the file, not on the directory order, which no
		// platform guarantees.
		saves.Sort((left, right) => right.SavedAtUnix.CompareTo(left.SavedAtUnix));
		return saves;
	}

	private static SaveGame Read(string path)
	{
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushWarning($"SaveGame: could not read {path} ({FileAccess.GetOpenError()})");
			return null;
		}

		try
		{
			return JsonSerializer.Deserialize<SaveGame>(file.GetAsText(), JsonOptions);
		}
		catch (JsonException exception)
		{
			GD.PushWarning($"SaveGame: {path} is not a readable save ({exception.Message})");
			return null;
		}
	}
}
