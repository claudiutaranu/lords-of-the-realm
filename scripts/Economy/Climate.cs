using System.Collections.Generic;
using Godot;

/// <summary>The weather, by Lords of the Realm II's own rules (docs/lotr2-engine-checklist.md,
/// "Vremea"). It is not a dice roll for a disaster: every county keeps a dryness that climbs through
/// spring, summer and autumn and falls back through winter, a little less each season by chance,
/// and the season's weather is read off where it stands. A drought comes after dry summers running,
/// a flood after wet ones, and a lord who watches the sky can see either coming.
///
/// Drought and flood each ruin a field — it lies waste until it is reclaimed (Husbandry.Reclaim) —
/// and every weather moves the crop at the sowing, the growing and the reaping, and the herd.</summary>
public static class Climate
{
	/// <summary>Where a county's dryness opens: the middle of the Cloudy band. ponytail: the
	/// original's opening value is not known.</summary>
	public const int Opening = 45;

	/// <summary>What each season adds to the dryness, in the order of <see cref="Season"/>.</summary>
	private static readonly int[] Drying = { 8, 24, 12, -12 };

	/// <summary>The most chance takes off a season's drying: (rnd &amp; 0x7F) / 8.</summary>
	private const int MostJitter = 0x7F;

	private const int FloodBelow = 5;
	private const int StormsBelow = 20;
	private const int CloudyBelow = 70;
	private const int SunnyBelow = 95;
	private const int FrostAbove = 74;

	/// <summary>Where the dryness is set back to after the weather broke: wet enough after a drought,
	/// dry enough after a flood, that the next one is years off.</summary>
	private const int AfterFlood = 30;
	private const int AfterDrought = 70;

	/// <summary>How many kinds of climate the counties are spread over (0–4): the band a county's
	/// dryness is read against is shifted by its own, so the same sky does not fall on every county
	/// alike.</summary>
	private const int Climates = 5;

	/// <summary>The season's weather over every county given, rolled at the season's turn as the
	/// original does (pipeline step 3). One county, drawn at random, and its neighbours take an
	/// extra swing of the sky (the neighbours half of it) — that is how a wet year settles over one
	/// corner of the map rather than everywhere. Returns each county whose drought or flood ruined
	/// a field, and which field.</summary>
	/// <param name="entering">The season the clock has just turned to: its drying is added.</param>
	public static List<(ProvinceEconomy County, int Field)> Turn(IReadOnlyList<ProvinceEconomy> counties,
		Season entering, GameBalance b, RandomNumberGenerator dice, System.Func<string, IEnumerable<string>> neighboursOf)
	{
		var ruined = new List<(ProvinceEconomy, int)>();
		if (counties.Count == 0)
		{
			return ruined;
		}

		// ponytail: the size and sign of the extra swing is not known from the original (only who
		// gets it), so it is WeatherSwing either way; tune it if the weather reads too even.
		ProvinceEconomy struck = counties[dice.RandiRange(0, counties.Count - 1)];
		int swing = dice.Randf() < 0.5f ? -b.WeatherSwing : b.WeatherSwing;
		var nearby = new HashSet<string>(neighboursOf(struck.ProvinceName));

		foreach (ProvinceEconomy county in counties)
		{
			int extra = county == struck ? swing : nearby.Contains(county.ProvinceName) ? swing / 2 : 0;
			county.Dryness += Drying[(int)entering] - ((int)(dice.Randi() & MostJitter) / 8) + extra;
			county.Weather = Read(county, entering);
			county.Weathered = -1;

			if (county.Weather is Weather.Flooding or Weather.Drought)
			{
				int field = Ruin(county);
				if (field >= 0)
				{
					ruined.Add((county, field));
				}
			}

			// The weather broke: the sky starts again from the far side.
			if (county.Weather == Weather.Flooding)
			{
				county.Dryness = AfterFlood;
			}
			else if (county.Dryness + ClimateOf(county) >= SunnyBelow)
			{
				county.Dryness = AfterDrought;
			}
		}

		return ruined;
	}

	/// <summary>The weather a county's dryness reads as, in the season being entered. In winter and
	/// spring a drought is a frost, and so is a sunny spell high up the band.</summary>
	public static Weather Read(ProvinceEconomy county, Season entering)
	{
		int dry = county.Dryness + ClimateOf(county);
		bool cold = entering is Season.Winter or Season.Spring;
		return dry switch
		{
			< FloodBelow => Weather.Flooding,
			< StormsBelow => Weather.Storms,
			< CloudyBelow => Weather.Cloudy,
			< SunnyBelow => cold && dry > FrostAbove ? Weather.Frost : Weather.Sunny,
			_ => cold ? Weather.Frost : Weather.Drought,
		};
	}

	/// <summary>A county's own climate, 0 to 4. ponytail: the original reads it off the county's
	/// place in its list; ours is off the name, so a county keeps its climate whoever holds it.</summary>
	private static int ClimateOf(ProvinceEconomy county)
	{
		int sum = 0;
		foreach (char letter in county.ProvinceName)
		{
			sum += letter;
		}

		return sum % Climates;
	}

	/// <summary>The field a flood or a drought ruins: one under grain, taking its corn with it, or —
	/// with nothing sown — a pasture, then a resting field. It lies waste, and the lord cannot give
	/// it an order this season. Returns the field, or -1 where there was none to ruin.</summary>
	public static int Ruin(ProvinceEconomy p)
	{
		foreach (FieldUse under in new[] { FieldUse.Grain, FieldUse.Pasture, FieldUse.Fallow })
		{
			int field = System.Array.IndexOf(p.Fields, under);
			if (field < 0)
			{
				continue;
			}

			if (under == FieldUse.Grain && p.StandingCrop > 0)
			{
				p.StandingCrop -= p.StandingCrop / p.FieldsUnder(FieldUse.Grain);
			}

			p.Fields[field] = FieldUse.Waste;
			p.Reclaimed[field] = 0;
			p.Weathered = field;
			return field;
		}

		return -1;
	}

	// --- what the weather does to the land -------------------------------------------------------

	/// <summary>The crop as the sowing leaves it: a flood takes three quarters, a frost or a storm
	/// half.</summary>
	public static int Sown(int crop, Weather weather) => weather switch
	{
		Weather.Flooding => crop / 4,
		Weather.Frost or Weather.Storms => crop / 2,
		_ => crop,
	};

	/// <summary>The crop through a growing season: the sun grows it by half again, a drought or a
	/// flood halves it.</summary>
	public static int Grown(int crop, Weather weather) => weather switch
	{
		Weather.Sunny => crop * 3 / 2,
		Weather.Drought or Weather.Flooding => crop / 2,
		_ => crop,
	};

	/// <summary>The harvest as the weather lets it in — of what the reapers could carry, not of all
	/// that stood (the original's bug, not copied).</summary>
	public static int Reaped(int harvest, Weather weather) => weather switch
	{
		Weather.Sunny => harvest * 3 / 2,
		Weather.Flooding => harvest / 4,
		Weather.Frost or Weather.Storms => harvest / 2,
		_ => harvest,
	};

	/// <summary>What the weather does to the herd's season, in hundredths of a percent: a good one
	/// breeds, a bad one kills.</summary>
	public static int Herd(Weather weather) => weather switch
	{
		Weather.Sunny => 500,
		Weather.Frost => -200,
		Weather.Storms => -500,
		Weather.Drought or Weather.Flooding => -1000,
		_ => 0,
	};
}
