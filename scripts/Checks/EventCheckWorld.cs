using Godot;

/// <summary>Events from outside the county: the world's stirrings, the pace they come at, and the
/// hirelings who come looking for work.</summary>
public partial class EventCheck
{
	// --- the world's half, which actually does something --------------------------------------------

	private void World()
	{
		// The world stirs every season here, and the only thing it can reach for is the rats: what
		// is under test is what an event does, not how often one turns up.
		GameBalance b = Balance();
		b.WorldEventChance = 1f;
		b.RatsWeight = 1f;

		// A granary under the threshold is not worth a rat's trouble.
		ProvinceEconomy lean = Province();
		lean.Grain = b.RatsGranary - 1;
		Told(lean, b, Calm(lean));
		Is("a granary nobody is hoarding keeps its grain", lean.Grain, b.RatsGranary - 1);

		// One spilling onto the floor is.
		ProvinceEconomy hoard = Province();
		hoard.Grain = b.RatsGranary * 2;
		string said = Told(hoard, b, Calm(hoard));
		Is("a hoard brings the rats", said, "rats-01");
		Is("and they take their share", hoard.Grain, b.RatsGranary * 2 - Mathf.RoundToInt(b.RatsGranary * 2 * b.RatsGrainLoss));

		// The Black Death runs for a stretch of seasons and is announced again when it lifts, so a
		// lord knows when he can stop burying people. It is the one thing that ignores the weather
		// of the world entirely, so the roll is turned off under it and it still runs.
		b.RatsWeight = 0f;
		b.WorldEventChance = 0f;
		ProvinceEconomy struck = Province();
		struck.PlagueSeasonsLeft = 2;
		int before = struck.Population;
		Is("a plague still running says nothing new", Told(struck, b, Calm(struck)), "");
		Is("but it kills every season it runs", struck.Population < before, true);
		Is("and it is announced when it lifts", Told(struck, b, Calm(struck)), "plague-ended-01");
	}

	// --- how often the world stirs ----------------------------------------------------------------

	/// <summary>Most seasons nothing happens, and the opening year nothing happens at all. Both are
	/// invisible while they work — a game that throws a disaster every turn looks busy rather than
	/// broken — so both are pinned here.</summary>
	private void Pace()
	{
		GameBalance b = Balance();
		b.WorldEventChance = 1f;
		b.RatsWeight = 1f;

		// A county doing everything wrong, in a world where something happens every single season.
		// (Balance() leaves the attention pool at zero, so the one open door is the one taken.)
		ProvinceEconomy young = Province();
		young.Grain = b.RatsGranary * 2;
		Is("the opening year is spared", Spoken(young, b, turn: 1), "");
		Is("and the rest of it", Spoken(young, b, turn: b.QuietOpeningTurns), "");
		Is("after that the world is allowed at him", Spoken(young, b, turn: b.QuietOpeningTurns + 1), "rats-01");

		// And with the pace turned down to nothing, the same county is left alone for ever.
		b.WorldEventChance = 0f;
		ProvinceEconomy spared = Province();
		spared.Grain = b.RatsGranary * 2;
		Is("a quiet world stays quiet", Spoken(spared, b, turn: 40), "");
		Is("and leaves the granary alone", spared.Grain, b.RatsGranary * 2);

		// The floor is what keeps a county's one exposure from being its certainty. With the world
		// stirring every season and the pool ten times the only weight on the table, nine seasons in
		// ten the world looks elsewhere — so twenty seasons of a full granary are not twenty
		// visits from the rats.
		b.WorldEventChance = 1f;
		b.WorldEventFloor = 10f;
		b.EventQuietTurns = 0; // the cooldown is not what is being measured here

		// A hundred seasons off one stream of dice. The expected count is ten — one weight against a
		// pool of ten — so the bounds are wide enough that a run of bad luck cannot fail the build
		// and narrow enough that losing the floor entirely (which would give a hundred) cannot pass.
		var dice = new RandomNumberGenerator();
		dice.Seed = 7;
		ProvinceEconomy exposed = Province();
		int visits = 0;
		for (int season = 0; season < 100; season++)
		{
			exposed.Grain = b.RatsGranary * 2; // kept full, so the door stays open every season
			if (Spoken(exposed, b, turn: 20 + season, dice).Length > 0)
			{
				visits++;
			}
		}

		Is("one way in is not one certainty", visits < 35, true);
		Is("but it is not nothing either", visits > 0, true);
	}

	// --- soldiers for hire -------------------------------------------------------------------------

	private void Hirelings()
	{
		GameBalance b = Balance();
		b.WorldEventChance = 1f;
		b.MercenaryChance = 1f;
		b.MercenarySeasons = 3;
		var rng = new RandomNumberGenerator();

		// A company walks in of its own accord — and says nothing. The lord is not told about it the
		// way he is told about a flood: the map marks the county and the yard has the men in it.
		ProvinceEconomy county = Province();
		Mercenaries.Season(county, b, rng);
		Is("a band walks into the county", Mercenaries.Standing(county) != null, true);
		Is("  and brings its men with it", Mercenaries.Standing(county)?.Men > 0, true);
		Is("  and waits the seasons it was given", county.MercenarySeasonsLeft, b.MercenarySeasons);
		Is("  and the advisor says nothing about it", Told(county, b, Calm(county)), "");

		// Two bands haggling in the same yard is one of them going home.
		string first = county.MercenaryBand;
		Mercenaries.Season(county, b, rng);
		Is("a county with a band standing is offered no other", county.MercenaryBand, first);

		// They do not wait for ever. (The door is shut for this stretch, or the season the band walks
		// off is the season the next one walks in and nothing ever looks empty.)
		b.MercenaryChance = 0f;
		Mercenaries.Season(county, b, rng);
		Is("  still there with a season left", Mercenaries.Standing(county) != null, true);
		Mercenaries.Season(county, b, rng);
		Is("the band walks on when its patience runs out", Mercenaries.Standing(county) == null, true);
		Is("  and takes its men with it", county.MercenaryMen, 0);

		// A band with nothing written for it can never be announced in the yard, so a band in the
		// book and no line for it is dead data that nobody would ever notice was dead.
		foreach (MercenaryBand company in Mercenaries.All)
		{
			Is($"  the {company.Key} band has a line to be announced with",
				EventEngine.Find($"merc-{company.Key}") != null, true);
		}

		// A company is bought whole, at the price it names, and then it is gone: there is no coming
		// back next season for the half a lord could not afford today.
		ProvinceEconomy paid = Province();
		MercenaryBand band = Mercenaries.All[0];
		Mercenaries.Arrive(paid, band, b.MercenarySeasons);
		Is("a company stands at its own strength", paid.MercenaryMen, band.Men);
		Mercenaries.Hire(paid, band.Men);
		Is("hired once, it is off the books", Mercenaries.Standing(paid) == null, true);
		Is("  with nobody left to come back for", paid.MercenaryMen, 0);

		// And what it asks is a war chest, not pocket money: the cheapest company on the road costs
		// more than a county holds when the campaign opens.
		foreach (MercenaryBand company in Mercenaries.All)
		{
			Is($"  the {company.Key} company costs more than an opening treasury",
				company.Gold > 800, true);
		}
	}
}
