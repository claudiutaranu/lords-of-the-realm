using System.Collections.Generic;
using Godot;

/// <summary>The one runnable check behind the advisor, and it is about a single thing: does he name
/// the right cause.
///
/// This is worth a check and the panel that draws it is not, because everything else about an event
/// is visible the moment it happens and this is not. A narrator who tells a lord he taxed his people
/// to rebellion on the turn he actually starved them looks exactly like a narrator who is right —
/// the player corrects the wrong decision, the county keeps sliding, and he stops believing the
/// advisor at all. Nothing on screen would ever show it.
///
/// Run it: Godot --headless --path . res://scene/checks/events-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class EventCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		Blame();
		Silence();
		Quiet();
		Pace();
		World();
		Hirelings();

		GD.Print(_failed == 0
			? "\nadvisor events: all checks passed"
			: $"\nadvisor events: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	// --- naming the cause -------------------------------------------------------------------------

	private void Blame()
	{
		GameBalance b = Balance();

		// Starving on short rations and starving with an empty granary are the same hunger and two
		// different mistakes. The lord can fix the first one this afternoon, so he is told which.
		ProvinceEconomy p = Province();
		p.Ration = RationLevel.Half;
		Is("cut rations are named as cut rations", Told(p, b, Starving(p)), "famine-low-rations");

		p = Province();
		p.Ration = RationLevel.Normal;
		Is("an empty granary is named as an empty granary", Told(p, b, Starving(p)), "famine-empty-granaries");

		// Unrest, with the tax the heaviest thing on the county this season.
		p = Province();
		p.Loyalty = 20f;
		var taxed = new TurnSummary { LoyaltyBefore = 26f, LoyaltyAfter = 20f, LoyaltyFromTax = -10f, LoyaltyFromRations = -0f };
		Is("the tax is named when the tax did it", Told(p, b, taxed), "unrest-taxes");

		// Same county, same unrest, but the sons taken for the army cost more than the tax did.
		p = Province();
		p.Loyalty = 20f;
		var levied = new TurnSummary { LoyaltyBefore = 26f, LoyaltyAfter = 20f, LoyaltyFromTax = -4f, LoyaltyFromConscription = -9f };
		Is("the intake is named when the intake did it", Told(p, b, levied), "unrest-conscription");

		// Men billeted on the village, when they weigh heavier than the tax that pays for them. A
		// lord told it is the tax will cut the tax, keep the garrison, and wonder why nothing moved.
		p = Province();
		p.Loyalty = 20f;
		var billeted = new TurnSummary { LoyaltyBefore = 26f, LoyaltyAfter = 20f, LoyaltyFromTax = -2f, LoyaltyFromGarrison = -6f };
		Is("the garrison is named when the garrison did it", Told(p, b, billeted), "unrest-garrison");

		// What the lord is doing two counties away, when that is the heaviest thing on this one. He
		// must not be told to cut a tax he never levied here — the rate in front of him is fair, and
		// cutting it further would do nothing at all for the grievance he actually has.
		p = Province();
		p.Loyalty = 20f;
		var overheard = new TurnSummary { LoyaltyBefore = 26f, LoyaltyAfter = 20f, LoyaltyFromNeighbours = -5f };
		Is("his other counties are named when they did it", Told(p, b, overheard), "unrest-neighbours");

		// And the revolt, which is the one where getting it wrong costs the player the campaign.
		p = Province();
		p.Loyalty = 0f;
		var broken = new TurnSummary { LoyaltyBefore = 8f, LoyaltyAfter = 0f, LoyaltyFromTax = -10f };
		Is("a revolt over tax is a revolt over tax", Told(p, b, broken), "revolt-taxes");
	}

	// --- what he has no words for -----------------------------------------------------------------

	/// <summary>An event the book has no line for does not happen at all — it is not swapped for a
	/// neighbouring line that would be a lie. This is the rule that lets a line be switched off by
	/// leaving its recording out, so it has to hold even for the worst event in the game.</summary>
	private void Silence()
	{
		GameBalance b = Balance();

		Is("no words written for a revolt over hunger", EventEngine.Find("revolt-hunger") == null, true);

		ProvinceEconomy p = Province();
		p.Loyalty = 0f;
		var starved = new TurnSummary
		{
			LoyaltyBefore = 9f,
			LoyaltyAfter = 0f,
			LoyaltyFromStarvation = -18f,
			FoodShort = 0, // the famine line is not what is under test here
		};
		Is("so a revolt over hunger says nothing at all", Told(p, b, starved), "");
	}

	// --- not saying it four times -----------------------------------------------------------------

	private void Quiet()
	{
		GameBalance b = Balance();
		ProvinceEconomy p = Province();
		p.Ration = RationLevel.Half;

		Is("the first hard winter is reported", Told(p, b, Starving(p)), "famine-low-rations");
		Is("the second is not", Told(p, b, Starving(p)), "");
		Is("nor the third", Told(p, b, Starving(p)), "");

		// Four seasons on, it is worth saying again — the lord may have forgotten, and it is news
		// that the county is STILL starving.
		for (int season = 0; season < b.EventQuietTurns; season++)
		{
			Told(p, b, new TurnSummary { LoyaltyBefore = p.Loyalty, LoyaltyAfter = p.Loyalty });
		}

		Is("after it has gone quiet a while, it is news again", Told(p, b, Starving(p)), "famine-low-rations");
	}

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

	// --- the bench --------------------------------------------------------------------------------

	/// <summary>Runs one season's events over a province and gives back the id of what the advisor
	/// said, or an empty string when he said nothing. Only the first thing is taken: every case here
	/// is about which single line comes out.</summary>
	/// <summary>One season over a province, giving back what the advisor said. The dice are the
	/// caller's when it hands some over: a case that measures how OFTEN something happens has to
	/// roll a fresh number each season, and a bench that seeds its own generator per call would hand
	/// it the same season twenty times and call it a sample.</summary>
	private static string Spoken(ProvinceEconomy province, GameBalance balance, int turn,
		RandomNumberGenerator rng = null)
	{
		if (rng == null)
		{
			rng = new RandomNumberGenerator();
			rng.Seed = 1; // pinned: this caller is testing what happens, not how often
		}

		List<FiredEvent> news = EventEngine.AfterTurn(province, balance, Season.Winter,
			turn, Calm(province), rng);
		return news.Count == 0 ? "" : news[0].Said.Id;
	}

	private static string Told(ProvinceEconomy province, GameBalance balance, TurnSummary summary)
	{
		var rng = new RandomNumberGenerator();
		rng.Seed = 1; // the rolls are pinned; chance is not what any of this is testing
		// Well past the opening year the world is spared, so a case that wants an event can have one.
		List<FiredEvent> news = EventEngine.AfterTurn(province, balance, Season.Winter, 20, summary, rng);
		return news.Count == 0 ? "" : news[0].Said.Id;
	}

	/// <summary>A season that went badly: the province could not feed itself.</summary>
	private static TurnSummary Starving(ProvinceEconomy province) => new()
	{
		LoyaltyBefore = province.Loyalty,
		LoyaltyAfter = province.Loyalty,
		FoodShort = 40,
		LoyaltyFromStarvation = -12f,
	};

	/// <summary>A season where the people have no complaint, so only the world's half can speak.</summary>
	private static TurnSummary Calm(ProvinceEconomy province) => new()
	{
		LoyaltyBefore = province.Loyalty,
		LoyaltyAfter = province.Loyalty,
	};

	/// <summary>A balance with the world held perfectly still, so a case decides for itself what may
	/// happen in it. Left as they ship, a check would pass or fail on the weather.</summary>
	private static GameBalance Balance() => new()
	{
		WorldEventChance = 0f,
		WorldEventFloor = 0f, // no "and nothing happens" share: a case that opens a door gets it

		PlagueWeight = 0f,
		PlagueWeightHungry = 0f,
		FloodWeight = 0f,
		DroughtWeight = 0f,
		RatsWeight = 0f,
		MurrainWeight = 0f,
		BanditWeight = 0f,
		BumperWeight = 0f,
		MercenaryChance = 0f,
	};

	private static ProvinceDefinition Definition() => new()
	{
		ProvinceName = "Testshire",
		InitialPopulation = 800,
		Fields = 10,
		InitialGrainFields = 4,
		InitialPastureFields = 3,
	};

	/// <summary>A province with a garrison in it and a herd that fits its pastures, so that nothing
	/// but the case under test can set an event off.</summary>
	private static ProvinceEconomy Province()
	{
		ProvinceEconomy province = ProvinceEconomy.FromDefinition(Definition());
		province.Loyalty = 70f;
		province.Cattle = 10;
		province.Muster("militia", 20);
		return province;
	}

	private void Is(string what, string got, string expected) =>
		Report(got == expected, what, got.Length == 0 ? "(silence)" : got,
			expected.Length == 0 ? "(silence)" : expected);

	private void Is(string what, int got, int expected) =>
		Report(got == expected, what, got.ToString(), expected.ToString());

	private void Is(string what, bool got, bool expected) =>
		Report(got == expected, what, got.ToString(), expected.ToString());

	private void Report(bool passed, string what, string got, string expected)
	{
		if (passed)
		{
			GD.Print($"ok   {what}");
			return;
		}

		_failed++;
		GD.PrintErr($"FAIL {what}: got {got}, expected {expected}");
	}
}
