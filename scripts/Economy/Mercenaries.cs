using System.Collections.Generic;
using Godot;

/// <summary>A company of foreign soldiers offering itself for pay, as data/mercenaries.json has it
/// written. <paramref name="Unit"/> is the company of the garrison they fall in with once hired:
/// the muster is kept in units a province understands, not in nationalities, so forty Scots stand
/// in the roster as forty swordsmen.</summary>
/// <summary><paramref name="Gold"/> is what the whole company asks, not what one of its men costs:
/// a band is taken whole or it walks on.</summary>
public record MercenaryBand(string Key, string Name, string Unit, string Blurb, int Men, int Gold,
	int Attack, int Range, int Defence, int Speed);

/// <summary>Soldiers a lord can buy instead of raise.
///
/// Everything else in the barracks costs a province its own people and the arms its smithy forged.
/// These cost gold and nothing else, which is the whole point of them: a county with a full
/// treasury and an empty armoury can still field an army, and a lord who has taxed his people to
/// the edge of revolt can buy men rather than take more sons.
///
/// They are not on the menu. A band walks into the county of its own accord, stands there for a
/// few seasons, and walks on — so a lord cannot plan around mercenaries, only take them when they
/// come. That is what keeps them from being simply a better barracks.</summary>
public static class Mercenaries
{
	private const string DataPath = "res://data/mercenaries.json";

	private static List<MercenaryBand> _book;

	/// <summary>Every band that exists, in the order the file lists them.</summary>
	public static IReadOnlyList<MercenaryBand> All
	{
		get
		{
			Load();
			return _book;
		}
	}

	public static MercenaryBand Find(string key)
	{
		Load();
		return key is not { Length: > 0 } ? null : _book.Find(band => band.Key == key);
	}

	/// <summary>Whichever band is standing in this province now, with however many of them are still
	/// unspoken for. Null when nobody is on offer, which is most seasons.</summary>
	public static MercenaryBand Standing(ProvinceEconomy province) =>
		province is { MercenaryMen: > 0, MercenarySeasonsLeft: > 0 } ? Find(province.MercenaryBand) : null;

	/// <summary>One band at random, for the season the world sends somebody.</summary>
	public static MercenaryBand Wandering(RandomNumberGenerator rng)
	{
		Load();
		return _book.Count == 0 ? null : _book[rng.RandiRange(0, _book.Count - 1)];
	}

	/// <summary>Puts a band in front of the lord for a few seasons.</summary>
	public static void Arrive(ProvinceEconomy province, MercenaryBand band, int seasons)
	{
		province.MercenaryBand = band.Key;
		province.MercenaryMen = band.Men;
		province.MercenarySeasonsLeft = seasons;
	}

	/// <summary>Takes men off the offer. A company is bought whole, so in practice this empties it
	/// and sends it off the books the moment a lord meets its price.</summary>
	public static void Hire(ProvinceEconomy province, int men)
	{
		province.MercenaryMen = Mathf.Max(0, province.MercenaryMen - men);
		Clear(province);
	}

	/// <summary>A county's own season of luck with hired soldiers: whoever is standing there grows a
	/// season more impatient, and if the ground is clear another company may walk in.
	///
	/// Deliberately not run through the advisor. A band for hire is not something that HAPPENED to a
	/// county the way a flood did — it is something standing in it — so there is no panel and no
	/// voice at the moment it arrives. The map shows a mark over the county that has one, and the
	/// lord hears about them where he would go to hire them.</summary>
	public static void Season(ProvinceEconomy province, GameBalance balance, RandomNumberGenerator rng)
	{
		Wane(province);
		if (Standing(province) != null || rng.Randf() >= balance.MercenaryChance)
		{
			return;
		}

		MercenaryBand band = Wandering(rng);
		if (band != null)
		{
			Arrive(province, band, balance.MercenarySeasons);
		}
	}

	/// <summary>A season of standing about. A band whose patience runs out walks on.</summary>
	public static void Wane(ProvinceEconomy province)
	{
		if (province.MercenarySeasonsLeft > 0)
		{
			province.MercenarySeasonsLeft--;
		}

		Clear(province);
	}

	private static void Clear(ProvinceEconomy province)
	{
		if (province.MercenaryMen <= 0 || province.MercenarySeasonsLeft <= 0)
		{
			province.MercenaryBand = "";
			province.MercenaryMen = 0;
			province.MercenarySeasonsLeft = 0;
		}
	}

	private static void Load()
	{
		if (_book != null)
		{
			return;
		}

		_book = new List<MercenaryBand>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"Mercenaries: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["bands"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			_book.Add(new MercenaryBand(
				fields["key"].AsString(),
				fields["name"].AsString(),
				fields["unit"].AsString(),
				fields["blurb"].AsString(),
				fields["men"].AsInt32(),
				fields["gold"].AsInt32(),
				fields["attack"].AsInt32(),
				fields["range"].AsInt32(),
				fields["defence"].AsInt32(),
				fields["speed"].AsInt32()));
		}
	}
}
