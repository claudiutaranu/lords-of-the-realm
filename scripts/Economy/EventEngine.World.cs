using System;
using System.Collections.Generic;
using Godot;

/// <summary>What the world does to a county when it stirs: the events it is open to offered by
/// weight against the world's attention, and one drawn.</summary>
public static partial class EventEngine
{
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
		bool weak = summary.Achieved < RationLevel.Normal || Livelihood.BandOf(p.Health) <= Livelihood.Band.Sick;
		Offer(choices, weak ? b.PlagueWeightHungry : b.PlagueWeight, "plague",
			weak ? "plague-outbreak-weak-health" : "plague-outbreak-traveler",
			() =>
			{
				p.PlagueSeasonsLeft = b.PlagueSeasons;
				p.Population = Mathf.Max(0, p.Population - Mathf.RoundToInt(p.Population * b.PlagueDeathRate));
				Move(p, summary, -b.PlagueLoyaltyLoss);
			});

		// The rats come for a granary that is overfull, which is the lord's own hoarding: grain in
		// the barn past what the people can eat is grain he should have sold.
		if (p.Grain > b.RatsGranary)
		{
			Offer(choices, b.RatsWeight, "rats", "rats-01",
				() => p.Grain -= Mathf.RoundToInt(p.Grain * b.RatsGrainLoss));
		}

		// Murrain breeds in a herd with nowhere to stand: past what the pastures carry, not past
		// some flat number, so the answer is either fewer beasts or more pasture.
		if (p.Cattle > p.FieldsUnder(FieldUse.Pasture) * 20) // past "average" crowding (Husbandry)
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
		if (season == Season.Autumn && summary.Harvest > 0 && p.Soil > b.GoodHeart)
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
}
