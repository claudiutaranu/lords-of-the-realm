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
		OneAtATime();
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
			LoyaltyFromRations = -18f,
			Achieved = RationLevel.Normal, // the famine line is not what is under test here
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
		Achieved = RationLevel.Quarter,
		LoyaltyFromRations = -5f,
	};

	/// <summary>A season where the people have no complaint, so only the world's half can speak.</summary>
	private static TurnSummary Calm(ProvinceEconomy province) => new()
	{
		LoyaltyBefore = province.Loyalty,
		LoyaltyAfter = province.Loyalty,
	};

	/// <summary>A balance with the world held perfectly still, so a case decides for itself what may
	/// happen in it. Left as they ship, a check would pass or fail on the weather.</summary>
	/// <summary>A realm of six counties hears of the world one disaster at a time. Rolled county by
	/// county, the world found somebody nearly every season and the advisor had rats on top of murrain
	/// on top of plague; now the realm rolls once, one county is visited, and the realm is left alone
	/// for WorldEventGap turns after. With the dice fixed to always, that is the only thing standing
	/// between six counties and six disasters a season.</summary>
	private void OneAtATime()
	{
		GameBalance b = Balance();
		b.WorldEventChance = 1f;
		b.QuietOpeningTurns = 0;
		b.EventQuietTurns = 0;
		b.BanditWeight = 1f; // no garrison anywhere, so every county is open to it

		var definitions = new List<ProvinceDefinition>();
		var realms = new Dictionary<string, string>();
		foreach (string name in new[] { "Ash", "Birch", "Cedar", "Elm", "Fir", "Oak" })
		{
			ProvinceDefinition definition = Definition();
			definition.ProvinceName = name;
			definitions.Add(definition);
			realms[name] = "crown";
		}

		var turns = new TurnManager(b, definitions, realms, "crown", Difficulty.Medium);
		int most = 0;
		int fired = 0;
		int last = -100;
		bool spaced = true;
		for (int season = 0; season < 24; season++)
		{
			turns.AdvanceTurn();
			// The world's rolled disasters only: the weather is the sky's own step (Climate), not one of them.
			int world = turns.News.FindAll(item => !item.FromThePeople
				&& !item.Said.Id.StartsWith("flood") && !item.Said.Id.StartsWith("drought")).Count;
			most = Mathf.Max(most, world);
			if (world > 0)
			{
				spaced &= turns.Turn - last > b.WorldEventGap;
				last = turns.Turn;
				fired++;
			}
		}

		Is("six counties, the world always willing, and one disaster a season at most", most, 1);
		Is("  with the realm left alone for a breath after each", spaced, true);
		Is("  but the world does still come", fired >= 4, true);
	}

	private static GameBalance Balance() => new()
	{
		WorldEventChance = 0f,
		WorldEventFloor = 0f, // no "and nothing happens" share: a case that opens a door gets it

		PlagueWeight = 0f,
		PlagueWeightHungry = 0f,
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
