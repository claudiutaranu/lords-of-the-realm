using System.Collections.Generic;
using Godot;

/// <summary>The other lords' half of the letters, taken first in their turn as the original's AI
/// did (its program opens with the inbox and diplomacy, before taxes).</summary>
public partial class Diplomacy
{
	/// <summary>A season of letters. <paramref name="rivals"/> are the realms the other lords hold, in
	/// a fixed order so the same season plays the same way; <paramref name="lordOf"/> seats a lord in
	/// each. A realm with no lord seated writes nothing and answers nothing.</summary>
	public void Season(int turn, string player, List<string> rivals,
		IReadOnlyDictionary<string, Lord> lordOf, GameBalance balance)
	{
		// An offer the player let lie for a season has lapsed; the lord will ask again in his time.
		Offers.Clear();

		foreach (Letter letter in Outbox)
		{
			if (lordOf.ContainsKey(letter.To))
			{
				Inbox.Add(Answer(letter, balance));
			}
		}

		Outbox.Clear();
		Mend(rivals, balance);
		Bear(player, rivals, lordOf);
		Warn(player, rivals, lordOf, balance);
		Court(turn, player, rivals, lordOf, balance);

		// Five letters on the table, as in the original; the oldest are the ones that go unread.
		if (Inbox.Count > balance.InboxSize)
		{
			Inbox.RemoveRange(0, Inbox.Count - balance.InboxSize);
		}
	}

	/// <summary>The letters waiting for the player, handed over once: read is read.</summary>
	public List<Letter> TakeLetters()
	{
		var read = new List<Letter>(Inbox);
		Inbox.Clear();
		foreach (Letter letter in read)
		{
			Keep(letter);
		}

		return read;
	}

	/// <summary>A lord reads the player's letter, and it moves him; what he writes back is his line
	/// for what it did.</summary>
	private Letter Answer(Letter letter, GameBalance b)
	{
		string me = letter.To;
		string him = letter.From;
		string key = From(him, me);
		string reply;
		switch (letter.Kind)
		{
			case Gift:
				// Weighed against the biggest he was ever sent: a smaller one is an insult in a purse.
				bool slight = letter.Gold < BiggestGift.GetValueOrDefault(key);
				Move(me, him, slight ? b.GiftSlight : Mathf.Min(b.GiftMostGoodwill, letter.Gold / b.GoldPerGoodwill), b);
				BiggestGift[key] = Mathf.Max(letter.Gold, BiggestGift.GetValueOrDefault(key));
				reply = slight ? "gift-slight" : "gift-thanks";
				break;

			case Compliment:
				int paid = Compliments.GetValueOrDefault(key) + 1;
				Compliments[key] = paid;
				int worth = paid <= b.ComplimentGoodwill.Length ? b.ComplimentGoodwill[paid - 1] : b.ComplimentWeary;
				Move(me, him, worth, b);
				reply = worth > 0 ? "compliment-pleased" : "compliment-weary";
				break;

			case Insult:
				Move(me, him, b.InsultGoodwill, b);
				reply = "insult";
				break;

			case OfferAlliance:
				// Taken only by a lord who thinks well enough of the one asking and is sworn to nobody.
				// Asked twice in one season — two lords writing to the same one — the first to be
				// answered has him.
				bool takes = StandingOf(me, him) >= b.AllianceAt && AllyOf(me).Length == 0
					&& AllyOf(him).Length == 0 && !AtWar(me, him);
				if (takes)
				{
					Swear(me, him);
				}

				reply = takes ? "alliance-yes" : "alliance-no";
				break;

			case BreakAlliance:
				Part(him);
				Move(me, him, b.BreakGoodwill, b);
				reply = "alliance-ended";
				break;

			default:
				// Help, or an attack on a realm he names: only an ally is asked, and only one who
				// still thinks well enough of him goes. Never against himself.
				bool goes = AllyOf(him) == me && StandingOf(me, him) >= b.ErrandAt
					&& (letter.Kind == AskHelp || (letter.About.Length > 0 && letter.About != me));
				if (goes)
				{
					Errands[me] = letter.Kind == AskHelp ? "" : letter.About;
				}

				reply = $"{letter.Kind}-{(goes ? "yes" : "no")}";
				break;
		}

		return new Letter(me, him, reply, letter.Gold, letter.About);
	}

	/// <summary>The other lords come round to one another, a point a season. Never to the player: he
	/// has to earn it.</summary>
	private void Mend(List<string> rivals, GameBalance b)
	{
		for (int i = 0; i < rivals.Count; i++)
		{
			for (int j = i + 1; j < rivals.Count; j++)
			{
				Move(rivals[i], rivals[j], b.DiplomacyMends, b);
			}
		}
	}

	/// <summary>Every alliance chafes. A season of it adds to the grudge, and a lord breaks it once
	/// the grudge is past what he, of all four, will stand.</summary>
	private void Bear(string player, List<string> rivals, IReadOnlyDictionary<string, Lord> lordOf)
	{
		foreach (string realm in rivals)
		{
			string ally = AllyOf(realm);
			if (ally.Length == 0 || !lordOf.TryGetValue(realm, out Lord lord))
			{
				continue;
			}

			// Counted once a pair: by the first of the two in the order the realms are walked.
			string pair = Pair(realm, ally);
			if (ally != player && rivals.IndexOf(ally) < rivals.IndexOf(realm))
			{
				continue;
			}

			int borne = Grudge.GetValueOrDefault(pair) + 1;
			Grudge[pair] = borne;
			Lord other = lordOf.GetValueOrDefault(ally);
			bool mine = borne >= lord.GrudgeLimit;
			if (!mine && (other == null || borne < other.GrudgeLimit))
			{
				continue;
			}

			Part(realm);
			if (ally == player)
			{
				Inbox.Add(new Letter(realm, player, "alliance-broken"));
			}
		}
	}

	/// <summary>A lord who thinks as ill of the player as a lord can warns him, a season at a time,
	/// and then declares a war that is never made up.</summary>
	private void Warn(string player, List<string> rivals, IReadOnlyDictionary<string, Lord> lordOf,
		GameBalance b)
	{
		foreach (string realm in rivals)
		{
			if (!lordOf.ContainsKey(realm) || AtWar(realm, player)
				|| StandingOf(realm, player) > -b.DiplomacyStandingMost)
			{
				continue;
			}

			string key = From(realm, player);
			int warned = Warnings.GetValueOrDefault(key);
			if (warned < b.WarWarnings)
			{
				Warnings[key] = warned + 1;
				Inbox.Add(new Letter(realm, player, "warning"));
				continue;
			}

			Wars.Add(Pair(realm, player));
			if (AllyOf(realm) == player)
			{
				Part(realm);
			}

			Inbox.Add(new Letter(realm, player, "war"));
		}
	}

	/// <summary>Each lord, in his own time, looks for a friend: the one who thinks best of him among
	/// those free to swear. Another lord who thinks well enough of him simply takes it; the player is
	/// written to and has the season to answer.</summary>
	private void Court(int turn, string player, List<string> rivals,
		IReadOnlyDictionary<string, Lord> lordOf, GameBalance b)
	{
		foreach (string realm in rivals)
		{
			if (!lordOf.TryGetValue(realm, out Lord lord) || AllyOf(realm).Length > 0
				|| turn - LastOffered.GetValueOrDefault(realm) < lord.OffersAllianceEvery)
			{
				continue;
			}

			LastOffered[realm] = turn;

			// The player is looked at first, so that on a tie it is him the lord writes to.
			string best = "";
			int bestStanding = int.MinValue;
			if (IsAllianceOpen(rivals.Count) && AllyOf(player).Length == 0 && !AtWar(realm, player)
				&& StandingOf(realm, player) >= b.LordOffersAt)
			{
				best = player;
				bestStanding = StandingOf(realm, player);
			}

			foreach (string other in rivals)
			{
				if (other != realm && AllyOf(other).Length == 0 && !AtWar(realm, other)
					&& StandingOf(realm, other) >= b.AllianceAt && StandingOf(realm, other) > bestStanding)
				{
					best = other;
					bestStanding = StandingOf(realm, other);
				}
			}

			if (best == player)
			{
				Offers.Add(realm);
				Inbox.Add(new Letter(realm, player, "alliance-offer"));
			}
			else if (best.Length > 0)
			{
				Swear(realm, best);
			}
		}
	}
}
