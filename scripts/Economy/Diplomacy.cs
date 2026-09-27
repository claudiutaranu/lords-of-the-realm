using System.Collections.Generic;
using Godot;

/// <summary>A letter between two realms. Sent by the player it names what he is asking
/// (<see cref="Diplomacy.Gift"/> and the rest); come back to him it names which of the lord's lines
/// answers it (Lord.Letters). <paramref name="About"/> is the realm an errand is against.</summary>
public record Letter(string From, string To, string Kind, int Gold = 0, string About = "");

/// <summary>The lords' letters to one another, as Lords of the Realm had them: how well each pair of
/// realms thinks of the other, the seven things a lord can write, who is sworn to whom, and the war
/// that follows a lord pushed past his patience.
///
/// Letters are answered a season late. What the player writes goes out now and is read in the other
/// lords' turn; what they write back is waiting for him when the next season opens. Everything here
/// is saved whole with the campaign — a lord who forgot a slight on reload would be no lord at all.
///
/// Every table is keyed by realm, never by lord: a campaign seats one of the four lords (Lords) in
/// each realm it has, and one with a single rival is played with exactly the same rules as one with
/// four.</summary>
public partial class Diplomacy
{
	public const string Gift = "gift";
	public const string Compliment = "compliment";
	public const string Insult = "insult";
	public const string OfferAlliance = "alliance";
	public const string BreakAlliance = "break";
	public const string AskHelp = "help";
	public const string AskAttack = "attack";

	/// <summary>What a pair of realms thinks of each other, −30..+30, by <see cref="Pair"/>.</summary>
	public Dictionary<string, int> Standing = new();

	/// <summary>Compliments paid and the biggest gift ever sent, by <see cref="From"/> sender and
	/// receiver: a lord weighs the next one against the last.</summary>
	public Dictionary<string, int> Compliments = new();
	public Dictionary<string, int> BiggestGift = new();

	/// <summary>Who is sworn to whom, both ways round, and how long each alliance has been borne.</summary>
	public Dictionary<string, string> Allies = new();
	public Dictionary<string, int> Grudge = new();

	/// <summary>Warnings a lord has written a realm, and the pairs at a war that is never made up.</summary>
	public Dictionary<string, int> Warnings = new();
	public List<string> Wars = new();

	/// <summary>The turn each realm last offered anybody an alliance, and the realms whose offer to the
	/// player waits on his answer this season.</summary>
	public Dictionary<string, int> LastOffered = new();
	public List<string> Offers = new();

	/// <summary>What an ally has been asked to march on, by the ally: a realm, or empty for whoever
	/// comes for the lord who asked.</summary>
	public Dictionary<string, string> Errands = new();

	/// <summary>The player's letters on their way, and the answers waiting for him.</summary>
	public List<Letter> Outbox = new();
	public List<Letter> Inbox = new();

	public static string Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

	private static string From(string from, string to) => $"{from}>{to}";

	public int StandingOf(string a, string b) => Standing.GetValueOrDefault(Pair(a, b));

	public string AllyOf(string realm) => Allies.GetValueOrDefault(realm, "");

	public bool AtWar(string a, string b) => Wars.Contains(Pair(a, b));

	/// <summary>What the player can write to a lord this season: the four the original offers any
	/// lord, and the three that only an ally is written. An alliance is not offered to a lord already
	/// sworn to somebody, nor to one at war, and one letter a lord a season is all that goes out.</summary>
	public List<string> Writable(string player, string lord)
	{
		var kinds = new List<string>();
		if (Outbox.Exists(letter => letter.To == lord))
		{
			return kinds;
		}

		kinds.AddRange(new[] { Gift, Compliment, Insult });
		if (AllyOf(player) == lord)
		{
			kinds.AddRange(new[] { BreakAlliance, AskHelp, AskAttack });
		}
		else if (AllyOf(player).Length == 0 && AllyOf(lord).Length == 0 && !AtWar(player, lord))
		{
			kinds.Add(OfferAlliance);
		}

		return kinds;
	}

	/// <summary>Sends a letter, if it is one that can be written this season. A gift's gold is the
	/// caller's to take out of the purse: this book does not hold anybody's treasury.</summary>
	public bool Send(Letter letter)
	{
		if (!Writable(letter.From, letter.To).Contains(letter.Kind) || (letter.Kind == Gift && letter.Gold <= 0))
		{
			return false;
		}

		Outbox.Add(letter);
		return true;
	}

	/// <summary>The player takes a lord's offer of alliance, while it still stands.</summary>
	public bool Accept(string player, string lord)
	{
		if (!Offers.Remove(lord) || AllyOf(player).Length > 0 || AllyOf(lord).Length > 0)
		{
			return false;
		}

		Swear(player, lord);
		return true;
	}

	/// <summary>Moves a pair's standing, held to −30..+30.</summary>
	public void Move(string a, string b, int by, GameBalance balance) =>
		Standing[Pair(a, b)] = Mathf.Clamp(StandingOf(a, b) + by, -balance.DiplomacyStandingMost,
			balance.DiplomacyStandingMost);

	private void Swear(string a, string b)
	{
		Allies[a] = b;
		Allies[b] = a;
		Grudge[Pair(a, b)] = 0;
	}

	private void Part(string a)
	{
		string b = AllyOf(a);
		if (b.Length == 0)
		{
			return;
		}

		Allies.Remove(a);
		Allies.Remove(b);
		Grudge.Remove(Pair(a, b));
		Errands.Remove(a);
		Errands.Remove(b);
	}
}
