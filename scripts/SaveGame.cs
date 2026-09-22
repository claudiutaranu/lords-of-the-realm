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

	/// <summary>Reads a tax rate written either way round. Files up to version 1 wrote one of five
	/// named steps; a rate is a percentage now, and each old name is read as the percentage it stood
	/// for. This is the thing the version stamp was written down for in the first place.
	///
	/// It is also cheaper than the alternative. Without it every save made before today fails to
	/// parse, and a save that fails to parse is skipped — so the player would open the load screen,
	/// find his campaign simply gone, and have nothing to read that explains why.</summary>
	/// <summary>Reads a ration written under its old name. Up to version 2 the levels were Low,
	/// Normal and High; they are multiples of a man's bread now, and the two that were renamed read
	/// as the multiple they always meant. Same reason as the tax beside it: without this, every save
	/// made before today fails to parse and vanishes off the load screen without a word.</summary>
	public sealed class RationJson : JsonConverter<RationLevel>
	{
		public override RationLevel Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options)
		{
			string named = reader.GetString() ?? "";
			return named switch
			{
				"Low" => RationLevel.Half,
				"High" => RationLevel.Double,
				_ => System.Enum.TryParse(named, out RationLevel level) ? level : RationLevel.Normal,
			};
		}

		public override void Write(Utf8JsonWriter writer, RationLevel level, JsonSerializerOptions options) =>
			writer.WriteStringValue(level.ToString());
	}

	public sealed class TaxPercentJson : JsonConverter<int>
	{
		private static readonly Dictionary<string, int> Steps = new()
		{
			["None"] = 0,
			["Low"] = 6,
			["Normal"] = ProvinceEconomy.OpeningTaxPercent,
			["High"] = 18,
			["Severe"] = 25,
		};

		public override int Read(ref Utf8JsonReader reader, System.Type type, JsonSerializerOptions options) =>
			reader.TokenType == JsonTokenType.String
				? Steps.GetValueOrDefault(reader.GetString() ?? "", ProvinceEconomy.OpeningTaxPercent)
				: reader.GetInt32();

		public override void Write(Utf8JsonWriter writer, int percent, JsonSerializerOptions options) =>
			writer.WriteNumberValue(percent);
	}

	/// <summary>What shape this file is in. Nothing reads it yet, and that is the point of writing
	/// it now: a version stamp is the one field that cannot be added retroactively. The day a save
	/// has to be migrated — a store renamed, a number rescaled — every file written before that day
	/// either says what it is or is unreadable guesswork.</summary>
	/// <summary>4: a county's men live in companies of their own (<see cref="FieldArmy"/>) rather
	/// than in one roster with one budget of ground. A file written at 3 still loads — ProvinceEconomy
	/// reads its old roster, march and position into the one company it describes.</summary>
	public int Version { get; set; } = 4;

	public string CampaignName { get; set; } = "";

	/// <summary>How well the other lords were playing. Defaulted rather than required: a save written
	/// before there were any lords to be good at it says nothing on the subject, and the middle
	/// setting is the honest reading of a file that never had one.</summary>
	public Difficulty Difficulty { get; set; } = Difficulty.Medium;

	public int Turn { get; set; }
	public long SavedAtUnix { get; set; }
	public List<ProvinceEconomy> Provinces { get; set; } = new();

	/// <summary>How far each store has been driven off its base price by the realm's own trading.
	/// Saved because a market that forgets is a market a player can launder a granary through: sell
	/// the lot, save, reload, sell it again at the price it started at.</summary>
	public Dictionary<string, float> Prices { get; set; } = new();

	public string SavedAtDisplay => Time.GetDatetimeStringFromUnixTime(SavedAtUnix, true).Replace("T", " ");

	public static SaveGame Snapshot(string campaignName, int turn, List<ProvinceEconomy> provinces,
		Dictionary<string, float> prices, Difficulty difficulty) => new()
	{
		CampaignName = campaignName,
		Difficulty = difficulty,
		Turn = turn,
		SavedAtUnix = (long)Time.GetUnixTimeFromSystem(),
		Provinces = provinces,
		Prices = prices,
	};

	/// <summary>Takes a save off disk. For the checks, which write real files to prove the round trip
	/// and must take them away again: they share this folder with the player's own campaigns, and a
	/// check that tidied up by clearing the folder took the player's saves with it.</summary>
	public static void Forget(SaveGame save) =>
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath($"{SaveDirectory}/{save.SavedAtUnix}.json"));

	public static void Write(string campaignName, int turn, List<ProvinceEconomy> provinces,
		Dictionary<string, float> prices, Difficulty difficulty)
	{
		DirAccess.MakeDirRecursiveAbsolute(SaveDirectory);
		SaveGame save = Snapshot(campaignName, turn, provinces, prices, difficulty);

		// Unix seconds name the file, so two saves a second apart never collide and the same
		// second overwrites itself rather than piling up duplicates of one state.
		string path = $"{SaveDirectory}/{save.SavedAtUnix}.json";
		string draft = $"{path}.writing";

		// Written beside the real file and moved over it only once it is whole. Opening the save
		// itself truncates it first, so a crash — or a laptop lid, or a power cut — halfway through
		// serialising leaves the player with a corrupt file where his campaign used to be, and the
		// one moment he is most likely to be saving is the one he least wants to lose. A rename is
		// the one file operation the filesystem promises to do all at once.
		using (FileAccess file = FileAccess.Open(draft, FileAccess.ModeFlags.Write))
		{
			if (file == null)
			{
				GD.PushError($"SaveGame: could not write save ({FileAccess.GetOpenError()})");
				return;
			}

			file.StoreString(JsonSerializer.Serialize(save, JsonOptions));
		}

		// Some platforms refuse to rename onto a file that is already there, so the old one goes
		// first. The window between the two is the only one where a save can be lost, and it is a
		// file operation wide rather than a serialisation wide.
		if (FileAccess.FileExists(path))
		{
			DirAccess.RemoveAbsolute(path);
		}

		Error moved = DirAccess.RenameAbsolute(draft, path);
		if (moved != Error.Ok)
		{
			GD.PushError($"SaveGame: could not put the save in place ({moved})");
		}
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
			// A draft left behind by a write that never finished is not a save. Skipped rather than
			// cleaned up: the next write to that second replaces it anyway, and deleting files in a
			// listing call is how a listing call ends up deleting somebody's campaign.
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
