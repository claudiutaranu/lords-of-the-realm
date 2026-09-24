using Godot;

/// <summary>The fields and the herd by Lords of the Realm's own rules (docs/lotr2-engine-checklist.md,
/// "Câmpuri, grâne, fertilitate" and "Vite"), with Advanced Farming on.
///
/// Grain is sown at the end of winter — as many sacks a field, ten down to one, as the barn and the
/// hands can manage — and the crop is twelve times the seed. Spring and summer each cap it at what
/// the hands can tend and grow it by half the county's soil; autumn reaps what the reapers can carry
/// in. The soil is one figure for the county, −100 to 100, fed by fields left fallow and spent by
/// fields cropped.
///
/// The herd breeds and dies by how crowded its pasture is, and by whether it has herdsmen enough —
/// three a head, and a cow feeds five people off her milk.</summary>
public static class Husbandry
{
	// --- the grain -----------------------------------------------------------------------------

	/// <summary>The most sacks a field is sown with, and what a sack grows into.</summary>
	public const int MostSacksAField = 10;
	public const int CropPerSack = 12;

	/// <summary>Hands the sowing wants: twelve for every sack, five sacks to a man with Advanced
	/// Farming.</summary>
	private const int SowingPerSack = 12;
	private const int SowingDivisor = 5;

	/// <summary>What one man tends through a growing season, and what he reaps: three sacks for every
	/// two men.</summary>
	private const int TendedPerMan = 10;

	/// <summary>How many sacks a field would be sown with, and whether the hands can sow them: the
	/// most the barn and the sowers both allow, ten a field down to one.</summary>
	public static int SacksAField(ProvinceEconomy p, int sowers)
	{
		int fields = p.FieldsUnder(FieldUse.Grain);
		if (fields == 0)
		{
			return 0;
		}

		for (int sacks = MostSacksAField; sacks >= 1; sacks--)
		{
			if (p.Grain >= fields * sacks && sowers >= SowingPerSack * fields * sacks / SowingDivisor)
			{
				return sacks;
			}
		}

		return 0;
	}

	/// <summary>One season of the farming year, the turn played in it.</summary>
	public static void WorkTheFields(ProvinceEconomy p, Season season, TurnSummary summary)
	{
		int hands = p.GrainWorkers;
		switch (season)
		{
			case Season.Winter:
				// The end of winter is the sowing. Where not even a sack a field can be sown, a county
				// with any seed at all puts ten sacks into the whole of its ground.
				int fields = p.FieldsUnder(FieldUse.Grain);
				int sacks = SacksAField(p, hands) * fields;
				if (sacks == 0 && fields > 0)
				{
					sacks = Mathf.Min(MostSacksAField, p.Grain);
				}

				p.Grain -= sacks;
				p.StandingCrop = sacks * CropPerSack;
				p.SownFields = fields;
				summary.Sown = sacks;
				break;

			case Season.Spring:
			case Season.Summer:
				p.StandingCrop = Mathf.Min(p.StandingCrop, hands * TendedPerMan);
				p.StandingCrop += Livelihood.Pct(p.StandingCrop, p.Soil / 2);
				break;

			case Season.Autumn:
				// A field lost since the sowing takes its share of the crop with it.
				int standing = p.StandingCrop;
				if (p.SownFields > 0 && p.FieldsUnder(FieldUse.Grain) < p.SownFields)
				{
					standing = standing * p.FieldsUnder(FieldUse.Grain) / p.SownFields;
				}

				summary.Harvest = Mathf.Min(standing, hands / 2 * 3);
				p.Grain += summary.Harvest;
				p.StandingCrop = 0;
				break;
		}
	}

	/// <summary>The soil, every season: six for every field resting, three off for every one cropped.</summary>
	public static void Rest(ProvinceEconomy p) =>
		p.Soil = Mathf.Clamp(p.Soil + (6 * p.FieldsUnder(FieldUse.Fallow)) - (3 * p.FieldsUnder(FieldUse.Grain)),
			-100, 100);

	/// <summary>The most hands the fields can use this season: the sowers for ten sacks a field, the
	/// tenders for the crop in the ground, or the reapers for all of it.</summary>
	public static int FieldWork(ProvinceEconomy p, Season season) => season switch
	{
		Season.Winter => SowingPerSack * p.FieldsUnder(FieldUse.Grain) * MostSacksAField / SowingDivisor,
		Season.Autumn => Livelihood.DivCeil(p.StandingCrop * 2, 3),
		_ => Livelihood.DivCeil(p.StandingCrop, TendedPerMan),
	};

	// --- the herd ------------------------------------------------------------------------------

	/// <summary>Births and deaths in hundredths of a percent a season, by how many head stand on
	/// each pasture field.</summary>
	private static (int Births, int Deaths) Crowding(int herd, int pastures)
	{
		int perField = pastures <= 0 ? int.MaxValue : Livelihood.DivCeil(herd, pastures);
		return perField switch
		{
			<= 10 => (1400, 100),
			<= 20 => (900, 300),
			<= 30 => (500, 500),
			_ => (200, 700),
		};
	}

	/// <summary>Herdsmen a herd wants to be fully tended: three to every head, as the original reckons
	/// its staffing (labour / (herd × 3)). Up to twice that — six a head, the original's absolute
	/// ceiling — still helps it breed.</summary>
	public static int Herdsmen(int herd) => herd * 3;

	/// <summary>The death rate a herd with nobody tending it runs at, in hundredths of a percent.</summary>
	private const int UntendedDeaths = 3400;

	/// <summary>A season of the herd: born, dead, and nothing at all without pasture to stand on.</summary>
	public static void TendTheHerd(ProvinceEconomy p, Season season, TurnSummary summary)
	{
		int herd = p.Cattle;
		if (herd <= 0)
		{
			return;
		}

		int pastures = p.FieldsUnder(FieldUse.Pasture);
		if (pastures == 0)
		{
			p.Cattle = herd < 6 ? 0 : herd / 2;
			summary.Calved = 0;
			return;
		}

		(int births, int deaths) = Crowding(herd, pastures);

		// Tended, the herd breeds by as much as twice its herdsmen allow; short of them, it dies
		// toward what an untended herd dies at.
		int wanted = Herdsmen(herd);
		int tended = wanted == 0 ? 200 : Mathf.Min(200, p.CattleWorkers * 100 / wanted);
		if (tended < 100)
		{
			deaths += (UntendedDeaths - deaths) * (100 - tended) / 100;
		}
		else
		{
			births = births * tended / 100;
		}

		if (season == Season.Spring)
		{
			births = births * 3 / 2;
		}

		if (season == Season.Winter)
		{
			deaths = deaths * 3 / 2;
		}

		int born = herd * births / 10_000;
		int died = herd * deaths / 10_000;
		p.Cattle = Mathf.Max(0, herd + born - died);
		summary.Calved = born - died;
	}
}
