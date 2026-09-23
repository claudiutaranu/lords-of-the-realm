using System;
using System.Collections.Generic;
using Godot;

/// <summary>One thing the lord's advisor can tell him, as data/events.json has it written: a
/// heading for the panel, the line he speaks, and the recording of him speaking it.</summary>
public record GameEvent(string Id, string Heading, string Text, string Voice);

/// <summary>An event that actually happened to a province this turn, and the line about it.
/// <paramref name="FromThePeople"/> separates the lord's own doing from the world's, which is what
/// decides who is cut when a turn carries more news than it can tell.</summary>
public record FiredEvent(string ProvinceName, GameEvent Said, bool FromThePeople);

/// <summary>What happens to a province besides its own arithmetic: the plague, the weather, the
/// rats, and the people's answer to how they are being ruled.
///
/// Two kinds of thing live here and they are not the same kind of thing.
///
/// The world's events — plague, drought, flood, rats, murrain, brigands — are rolled for and then
/// DO something: they kill, they drown a crop, they empty a granary. Chance is only half of each
/// one; the other half is the state the province has put itself in, because a granary spilling onto
/// the floor is what brings the rats and a crowded pasture is what breeds murrain. A lord should be
/// able to see one coming.
///
/// The people's events — famine, emigration, unrest, revolt — do nothing at all. They have already
/// happened, in <see cref="EconomySimulation"/>, and all that is left is to say so. Which is where
/// the cause comes in.
///
/// THE CAUSE IS THE POINT. The advisor does not say "your people are angry"; he says which of the
/// lord's decisions did it. That is only honest if the cause is read off the turn's own numbers, so
/// it is: <see cref="TurnSummary"/> carries what each thing took out of loyalty this season — the
/// tax, the rations, the starving, the sons taken for the army — and the cause is whichever of them
/// took the most. Choosing at random between the variants would have the narrator tell a lord he
/// taxed his people to rebellion on the turn he actually starved them, and in a management game a
/// lie in the feedback is worse than no feedback: the player corrects the wrong thing, it does not
/// work, and he stops believing the advisor at all.
///
/// AN EVENT NOBODY CAN BE TOLD ABOUT DOES NOT HAPPEN. Every event asks the book below for its line
/// before it does anything, and a missing line stops it dead — no effect, no message. That is what
/// lets a line be switched off by leaving its recording out, and it is why a cause with nothing
/// written for it falls silent instead of reaching for a neighbouring line that would lie. Lines
/// still waiting to be recorded — revolt by hunger, revolt by conscription, failing health — are
/// simply absent from the file.</summary>
public static class EventEngine
{
	private const string DataPath = "res://data/events.json";
	private const string VoiceDirectory = "res://assets/audio/events";

	private static Dictionary<string, GameEvent> _book;

	/// <summary>One thing the world might do, weighed against everything else it might do, and what
	/// it does to the province if it is what the season brings.</summary>
	private record Choice(float Weight, string Kind, string Id, Action Strike);

	/// <summary>Everything worth telling the lord about this province, after its turn has been run.
	/// The world's half may change the province; the people's half only reports it.
	///
	/// <paramref name="worldMayStir"/> is whether this is the county the world is allowed at this
	/// season. The realm decides that (TurnManager), because a roll per county made a lord of six
	/// counties hear of a disaster nearly every season.</summary>
	public static List<FiredEvent> AfterTurn(ProvinceEconomy province, GameBalance balance, Season season,
		int turn, TurnSummary summary, RandomNumberGenerator rng, bool worldMayStir = true)
	{
		var news = new List<FiredEvent>();
		Quieten(province);

		// The people's half first: it is the lord's own doing, and it is what he has to hear.
		Add(news, province, WhatThePeopleDid(province, balance, summary), fromThePeople: true);
		Add(news, province, WhatTheWorldDid(province, balance, season, turn, summary, rng, worldMayStir), fromThePeople: false);
		return news;
	}

	private static void Add(List<FiredEvent> news, ProvinceEconomy province, GameEvent said, bool fromThePeople)
	{
		if (said != null)
		{
			news.Add(new FiredEvent(province.ProvinceName, said, fromThePeople));
		}
	}

	// --- what the people did --------------------------------------------------------------------

	/// <summary>The province's answer to how it is being ruled, worst first: a county in revolt is
	/// not also told it is restless. None of these does anything — the turn has already done it, and
	/// none of them is left to chance either. A famine is not a dice roll, it is the news.
	///
	/// Worst first, but not worst only: each rung falls through to the next when the one above it
	/// has nothing to say — either because it did not happen or because it was said too recently.
	/// Returning on the first rung that MATCHED instead of the first that SPOKE is a quiet way to
	/// mute a county: its second season of famine is still famine, the famine line is still resting,
	/// and the lord would be told nothing at all while his people walked out of the county.</summary>
	private static GameEvent WhatThePeopleDid(ProvinceEconomy p, GameBalance b, TurnSummary summary)
	{
		GameEvent said = null;

		if (p.Loyalty <= 0f)
		{
			said = Speak(p, "revolt", $"revolt-{Blame(summary)}", b.EventQuietTurns);
		}

		if (said == null && summary.FoodShort > 0)
		{
			// Two different failures wearing the same hunger: a lord who cut the bread himself, and
			// a lord whose granary is simply empty. He can fix the first one this afternoon.
			said = Speak(p, "famine",
				p.Ration < RationLevel.Normal ? "famine-low-rations" : "famine-empty-granaries",
				b.EventQuietTurns);
		}

		if (said == null && summary.PopulationChange < 0 && p.Loyalty < b.EmigrationBelow)
		{
			// Hungry or simply sick of him: people leave for both, and the two lines are not
			// interchangeable. A lord told his people are leaving over taxes will cut taxes, and
			// they will keep leaving, because what they wanted was bread.
			said = Speak(p, "emigration",
				summary.LoyaltyFromStarvation < 0f ? "emigration-hunger" : "emigration-unhappy",
				b.EventQuietTurns);
		}

		if (said == null && p.Loyalty < b.UnrestBelow && summary.LoyaltyChange < 0f)
		{
			said = Speak(p, "unrest", $"unrest-{Blame(summary)}", b.EventQuietTurns);
		}

		return said;
	}

	/// <summary>Which of the lord's decisions cost him the most goodwill this season. Read off what
	/// the turn actually subtracted, not guessed from the state afterwards — a province can be both
	/// taxed hard and half-starved, and the advisor has to name the heavier of the two.</summary>
	private static string Blame(TurnSummary summary)
	{
		// Hunger is one grievance whether it came from short rations or from an empty granary: the
		// people cannot tell the difference and neither should the advisor.
		float hunger = summary.LoyaltyFromRations + summary.LoyaltyFromStarvation;
		float taxes = summary.LoyaltyFromTax;
		float sons = summary.LoyaltyFromConscription;
		float billets = summary.LoyaltyFromGarrison;
		float neighbours = summary.LoyaltyFromNeighbours;

		if (hunger <= taxes && hunger <= sons && hunger <= billets && hunger <= neighbours && hunger < 0f)
		{
			return "hunger";
		}

		if (taxes <= sons && taxes <= billets && taxes <= neighbours && taxes < 0f)
		{
			return "taxes";
		}

		if (sons <= billets && sons <= neighbours && sons < 0f)
		{
			return "conscription";
		}

		if (billets <= neighbours && billets < 0f)
		{
			return "garrison";
		}

		// Nothing the lord decided is to blame, so nothing is named. An id with no cause on the end
		// of it matches no line, and the advisor holds his tongue — which is the right answer and
		// the whole rule of this file. Defaulting to the tax here would put a guess in the one place
		// that must not guess.
		return neighbours < 0f ? "neighbours" : "";
	}

	// --- what the world did ---------------------------------------------------------------------

	/// <summary>At most one thing out of the lord's hands, and most seasons nothing at all.
	///
	/// One roll decides WHETHER the world stirs, and only then is it decided WHAT — so how eventful
	/// a reign is comes down to a single number a designer can turn, and a quiet spring is the
	/// normal outcome rather than the gap left over by six separate dice. Six independent rolls, one
	/// per disaster, is how a game ends up with something happening nearly every turn: each one
	/// looks rare on its own and together they never shut up.
	///
	/// What the season brings is then drawn out of the things the province is actually open to. A
	/// county with an ordinary granary cannot draw the rats at all, and a half-starved one is four
	/// times likelier to draw the plague than a fed one — the state is the loaded half of the dice,
	/// which is what makes these events readable rather than arbitrary.
	///
	/// The opening year is spared: a lord who loses his herd to murrain in his first spring has
	/// learnt nothing about murrain, only that the game is unfair.</summary>
	private static GameEvent WhatTheWorldDid(ProvinceEconomy p, GameBalance b, Season season, int turn,
		TurnSummary summary, RandomNumberGenerator rng, bool worldMayStir)
	{
		// A plague already running is not a roll and not subject to any of the above: it kills every
		// season until it burns out, and it says so when it lifts.
		if (p.PlagueSeasonsLeft > 0)
		{
			p.PlagueSeasonsLeft--;
			p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * b.PlagueDeathRate));
			return p.PlagueSeasonsLeft == 0 ? Find("plague-ended-01") : null;
		}

		if (!worldMayStir || turn <= b.QuietOpeningTurns || rng.Randf() >= b.WorldEventChance)
		{
			return null; // the usual season: nothing happens, and the lord gets on with his year
		}

		var choices = new List<Choice>();

		// A hungry body cannot fight the pestilence, so hunger is what weights it rather than what
		// permits it: the plague can find anybody.
		bool weak = p.Ration < RationLevel.Normal || summary.FoodShort > 0;
		Offer(choices, weak ? b.PlagueWeightHungry : b.PlagueWeight, "plague",
			weak ? "plague-outbreak-weak-health" : "plague-outbreak-traveler",
			() =>
			{
				p.PlagueSeasonsLeft = b.PlagueSeasons;
				p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * b.PlagueDeathRate));
				Move(p, summary, -b.PlagueLoyaltyLoss);
			});

		// Weather takes the crop, so it is only on the table while there is one standing.
		if (season == Season.Spring && p.StandingCrop > 0)
		{
			// Land cropped year on year holds no water. The advisor says which flood this was,
			// because one of them is the lord's rotation and the other is just rain.
			bool exhausted = p.GrainFertility() < b.TiredSoil;
			Offer(choices, b.FloodWeight, "flood", exhausted ? "flood-overworked-fields" : "flood-01",
				() =>
				{
					p.StandingCrop = 0;
					p.FieldRepair = b.FieldRepairWork;
					for (int field = 0; field < p.Fields.Length; field++)
					{
						if (p.Fields[field] == FieldUse.Grain)
						{
							p.Fertility[field] = Mathf.Max(b.FertilityFloor, p.Fertility[field] - b.FloodFertilityLoss);
						}
					}
				});
		}

		if (season == Season.Summer && p.StandingCrop > 0)
		{
			Offer(choices, b.DroughtWeight, "drought", "drought-01",
				() => p.StandingCrop = Mathf.RoundToInt(p.StandingCrop * (1f - b.DroughtCropLoss)));
		}

		// The rats come for a granary that is overfull, which is the lord's own hoarding: grain in
		// the barn past what the people can eat is grain he should have sold.
		if (p.Grain > b.RatsGranary)
		{
			Offer(choices, b.RatsWeight, "rats", "rats-01",
				() => p.Grain -= Mathf.RoundToInt(p.Grain * b.RatsGrainLoss));
		}

		// Murrain breeds in a herd with nowhere to stand: past what the pastures carry, not past
		// some flat number, so the answer is either fewer beasts or more pasture.
		if (p.Cattle > p.FieldsUnder(FieldUse.Pasture) * b.CowsPerField)
		{
			Offer(choices, b.MurrainWeight, "murrain", "cattle-disease-01",
				() => p.Cattle -= Mathf.RoundToInt(p.Cattle * b.MurrainHerdLoss));
		}

		// Brigands are either outsiders taking advantage of an unguarded county, or the county's own
		// people driven into the woods. The second is the lord's doing and is named as such.
		bool desperate = p.Loyalty < b.UnrestBelow;
		if (desperate || p.Soldiers == 0)
		{
			Offer(choices, b.BanditWeight, "bandits",
				desperate ? "bandits-desperate-peasants" : "bandits-no-garrison",
				() =>
				{
					p.Gold -= Mathf.RoundToInt(p.Gold * b.BanditGoldLoss);
					p.Grain -= Mathf.RoundToInt(p.Grain * b.BanditGrainLoss);
				});
		}

		// The one piece of good news, and it is earned: a harvest off land the lord let rest.
		if (season == Season.Autumn && summary.Harvest > 0 && p.GrainFertility() > b.GoodHeart)
		{
			Offer(choices, b.BumperWeight, "bumper", "bumper-crop-01",
				() =>
				{
					p.Grain += Mathf.RoundToInt(summary.Harvest * b.BumperCropBonus);
					Move(p, summary, b.BumperCropLoyalty);
				});
		}

		return Draw(choices, p, b, rng);
	}

	/// <summary>Puts one possibility on the season's table. A weight of nothing keeps it off
	/// entirely, so turning an event down to zero in the balance really does switch it off.</summary>
	private static void Offer(List<Choice> choices, float weight, string kind, string id, Action strike)
	{
		if (weight > 0f)
		{
			choices.Add(new Choice(weight, kind, id, strike));
		}
	}

	/// <summary>Draws one of the season's possibilities by weight, and lets it happen — if the
	/// advisor has words for it and has not just said them. If he has not, the season passes
	/// quietly: the drawn event is not silently swapped for the runner-up, because a disaster
	/// nobody reports is worse than no disaster.</summary>
	private static GameEvent Draw(List<Choice> choices, ProvinceEconomy p, GameBalance b, RandomNumberGenerator rng)
	{
		float weighed = 0f;
		foreach (Choice choice in choices)
		{
			weighed += choice.Weight;
		}

		if (weighed <= 0f)
		{
			return null;
		}

		// The world's attention is a fixed pool, and a county that offers it only one way in does
		// not therefore get that one thing constantly. Without this floor the weights are shares of
		// whatever is on the table, so a well-fed, garrisoned, sensibly-stocked province — whose
		// only exposure is the plague, which can find anybody — would draw the plague EVERY time the
		// world stirred: once every five seasons instead of once a reign. The remainder above the
		// weights on the table is the world looking elsewhere.
		float total = Mathf.Max(weighed, b.WorldEventFloor);

		float roll = rng.Randf() * total;
		foreach (Choice choice in choices)
		{
			roll -= choice.Weight;
			if (roll > 0f)
			{
				continue;
			}

			GameEvent said = Speak(p, choice.Kind, choice.Id, b.EventQuietTurns);
			if (said != null)
			{
				choice.Strike();
			}

			return said;
		}

		return null;
	}

	/// <summary>Moves a county's goodwill and writes down that the WORLD did it, not the lord. The
	/// happiness table adds its own lines up against what actually happened to the number, so a
	/// season where a plague moved it without saying so is a table that visibly does not balance —
	/// and the player, quite reasonably, stops believing the lines that are there.
	///
	/// What is recorded is what landed, after the clamp, and not what was asked for: a plague that
	/// wanted six from a county with two left took two.</summary>
	private static void Move(ProvinceEconomy p, TurnSummary summary, float hearts)
	{
		float before = p.Loyalty;
		p.Loyalty = Mathf.Clamp(p.Loyalty + hearts, 0f, 100f);
		summary.LoyaltyFromEvents += p.Loyalty - before;
	}

	// --- saying it ------------------------------------------------------------------------------

	/// <summary>The one gate every event passes through: the advisor has to have words for it, and
	/// this kind of news has to not have been told recently. Both refusals return null and the
	/// caller does nothing at all — no effect, no message.</summary>
	private static GameEvent Speak(ProvinceEconomy p, string kind, string id, int quiet)
	{
		if (p.EventQuiet.GetValueOrDefault(kind) > 0)
		{
			return null;
		}

		GameEvent said = Find(id);
		if (said == null)
		{
			return null;
		}

		p.EventQuiet[kind] = quiet;
		return said;
	}

	/// <summary>Every kind of news is one season nearer to being worth saying again.</summary>
	private static void Quieten(ProvinceEconomy p)
	{
		foreach (string kind in new List<string>(p.EventQuiet.Keys))
		{
			p.EventQuiet[kind] = Mathf.Max(0, p.EventQuiet[kind] - 1);
		}
	}

	// --- the book -------------------------------------------------------------------------------

	/// <summary>The advisor's line for this event, or null when nothing has been written for it.
	/// Callers treat null as "this does not happen", never as "say something else".</summary>
	public static GameEvent Find(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}

		Load();
		return _book.GetValueOrDefault(id);
	}

	/// <summary>Where the recording of a line lives, or an empty string when there is none on disk
	/// yet — a freshly written line reads itself until the voice is cut.</summary>
	public static string VoicePath(GameEvent said)
	{
		if (string.IsNullOrEmpty(said?.Voice))
		{
			return "";
		}

		foreach (string extension in new[] { ".mp3", ".ogg" })
		{
			string path = $"{VoiceDirectory}/{said.Voice}{extension}";
			if (ResourceLoader.Exists(path))
			{
				return path;
			}
		}

		return "";
	}

	private static void Load()
	{
		if (_book != null)
		{
			return;
		}

		_book = new Dictionary<string, GameEvent>();
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"EventEngine: {DataPath} is missing or is not a dictionary of events");
			return;
		}

		foreach (var pair in file.Data.AsGodotDictionary())
		{
			Godot.Collections.Dictionary fields = pair.Value.AsGodotDictionary();
			string id = pair.Key.AsString();
			_book[id] = new GameEvent(
				id,
				fields.TryGetValue("heading", out Variant heading) ? heading.AsString() : id,
				fields.TryGetValue("text", out Variant text) ? text.AsString() : "",
				fields.TryGetValue("voice", out Variant voice) ? voice.AsString() : "");
		}
	}
}
