using System.Collections.Generic;
using Godot;

/// <summary>The lords' letters, held to the original's rules where they are known: compliments that
/// wear thin, a gift weighed against the biggest, alliances one to a lord and a grudge that ends
/// them, two warnings and then war. And that it all survives a save, because a slight a lord forgets
/// on reload is a slight the player can wash out by loading.
///
/// Run it: Godot --headless --path . res://scene/checks/diplomacy-check.tscn</summary>
public partial class DiplomacyCheck : Node
{
	private const string Player = "crown";
	private const string Watch = "watch";
	private const string Marsh = "marsh";

	/// <summary>The realm these checks write in has two rival lords, Watch and Marsh, which is what an
	/// alliance needs to be made at all (Diplomacy.IsAllianceOpen).</summary>
	private const int Rivals = 2;

	private int _failed;

	public override void _Ready()
	{
		Compliments();
		Gifts();
		Alliances();
		Mending();
		Grudges();
		War();
		Courting();
		Errands();
		Purse();
		Saved();

		GD.Print(_failed == 0
			? "\ndiplomacy: all checks passed"
			: $"\ndiplomacy: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	private void Compliments()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		int[] after = { 15, 23, 23, 19 };
		string[] said = { "compliment-pleased", "compliment-pleased", "compliment-weary", "compliment-weary" };
		for (int letter = 0; letter < after.Length; letter++)
		{
			d.Send(new Letter(Player, Watch, Diplomacy.Compliment), Rivals);
			Letter reply = Season(d, b)[0];
			Is($"compliment {letter + 1} leaves him at {after[letter]}", d.StandingOf(Player, Watch), after[letter]);
			Is($"  and he says so", reply.Kind, said[letter]);
		}
	}

	private void Gifts()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Send(new Letter(Player, Watch, Diplomacy.Gift, Gold: 500), Rivals);
		Is("five hundred crowns thank him five points", Season(d, b)[0].Kind, "gift-thanks");
		Is("  as the standing shows", d.StandingOf(Player, Watch), 5);

		d.Send(new Letter(Player, Watch, Diplomacy.Gift, Gold: 300), Rivals);
		Is("a smaller gift than the last is a slight", Season(d, b)[0].Kind, "gift-slight");
		Is("  and costs eight", d.StandingOf(Player, Watch), -3);

		d.Send(new Letter(Player, Watch, Diplomacy.Gift, Gold: 9000), Rivals);
		Season(d, b);
		Is("no gift buys more than ten points", d.StandingOf(Player, Watch), 7);

		Is("an empty purse is no gift", d.Send(new Letter(Player, Watch, Diplomacy.Gift, Gold: 0), Rivals), false);
		d.Send(new Letter(Player, Watch, Diplomacy.Insult), Rivals);
		Is("one letter a lord a season", d.Send(new Letter(Player, Watch, Diplomacy.Compliment), Rivals), false);
		Season(d, b);
		Is("an insult costs ten", d.StandingOf(Player, Watch), -3);
	}

	private void Alliances()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Send(new Letter(Player, Watch, Diplomacy.OfferAlliance), Rivals);
		Is("a lord who does not know you refuses", Season(d, b)[0].Kind, "alliance-no");

		d.Move(Player, Watch, b.AllianceAt, b);
		d.Send(new Letter(Player, Watch, Diplomacy.OfferAlliance), Rivals);
		Is("one who thinks well enough of you swears", Season(d, b)[0].Kind, "alliance-yes");
		Is("  and is your ally", d.AllyOf(Player), Watch);
		Is("an ally is written the three errands", d.Writable(Player, Watch, Rivals).Contains(Diplomacy.AskAttack), true);
		Is("  and alliance is offered to nobody else while it stands",
			d.Writable(Player, Marsh, Rivals).Contains(Diplomacy.OfferAlliance), false);

		d.Send(new Letter(Player, Watch, Diplomacy.BreakAlliance), Rivals);
		Is("breaking it is answered", Season(d, b)[0].Kind, "alliance-ended");
		Is("  and ends it", d.AllyOf(Watch), "");
		Is("  and costs ten", d.StandingOf(Player, Watch), 0);
	}

	private void Mending()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		for (int season = 0; season < 3; season++)
		{
			d.Season(season + 1, Player, new List<string> { Watch, Marsh }, Seated(Watch, Marsh), b);
		}

		Is("two other lords come round a point a season", d.StandingOf(Watch, Marsh), 3);
		Is("  and never to the player", d.StandingOf(Player, Watch), 0);
	}

	private void Grudges()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Move(Player, Watch, b.AllianceAt, b);
		d.Send(new Letter(Player, Watch, Diplomacy.OfferAlliance), Rivals);
		Season(d, b, Short());
		Season(d, b, Short());
		Is("an alliance stands while the grudge is under his limit", d.AllyOf(Player), Watch);
		List<Letter> broke = Season(d, b, Short());
		Is("  and he breaks it on the season it reaches it", d.AllyOf(Player), "");
		Is("  and writes to say so", broke.Exists(letter => letter.Kind == "alliance-broken"), true);
	}

	private void War()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Move(Player, Watch, -b.DiplomacyStandingMost, b);
		Is("a lord at his worst warns first", Season(d, b)[0].Kind, "warning");
		Is("  and again", Season(d, b)[0].Kind, "warning");
		Is("  and then declares war", Season(d, b)[0].Kind, "war");
		Is("  which stands", d.AtWar(Player, Watch), true);
		d.Move(Player, Watch, 60, b);
		Is("and is never made up into an alliance", d.Writable(Player, Watch, Rivals).Contains(Diplomacy.OfferAlliance), false);
	}

	private void Courting()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		Lord margrave = Lords.Find("margrave");
		Is("the Margrave is on the roster", margrave?.Title, "The Margrave");
		var seated = new Dictionary<string, Lord> { [Watch] = margrave };
		var both = new List<string> { Watch, Marsh };
		// Thought of better than the other lord, whom he comes round to a point a season, so it is the
		// player he writes to.
		d.Move(Watch, Player, 20, b);
		for (int turn = 1; turn < margrave.OffersAllianceEvery; turn++)
		{
			d.Season(turn, Player, both, seated, b);
		}

		Is("he does not offer before his time", d.Offers.Count, 0);

		// Alone with the player in the realm, he never offers: there would be nobody left to fight.
		var alone = new Diplomacy();
		alone.Season(margrave.OffersAllianceEvery, Player, new List<string> { Watch }, seated, b);
		Is("the only rival lord offers no alliance", alone.Offers.Count, 0);
		Is("  and is offered none", alone.Writable(Player, Watch, 1).Contains(Diplomacy.OfferAlliance), false);
		Is("  but is still written to", alone.Writable(Player, Watch, 1).Contains(Diplomacy.Gift), true);

		d.Season(margrave.OffersAllianceEvery, Player, both, seated, b);
		Is("  and does on it", d.TakeLetters().Exists(letter => letter.Kind == "alliance-offer"), true);
		Is("  and the letter is kept for the diplomacy page", d.Kept.Exists(letter => letter.Kind == "alliance-offer"), true);
		d.Decline(Watch);
		Is("an offer turned down is off the table", d.Offers.Count, 0);
		Is("  and cannot be taken after", d.Accept(Player, Watch), false);
		d.Offers.Add(Watch);
		Is("the offer can be taken", d.Accept(Player, Watch), true);
		Is("  and he is sworn", d.AllyOf(Watch), Player);
		Is("his line for it is his own", margrave.Says("gift-thanks", 1500).StartsWith($"{1500:N0} crowns"), true);
	}

	private void Errands()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Move(Player, Watch, b.AllianceAt, b);
		d.Send(new Letter(Player, Watch, Diplomacy.OfferAlliance), Rivals);
		Season(d, b);
		d.Send(new Letter(Player, Watch, Diplomacy.AskAttack, About: Marsh), Rivals);
		Is("an ally agrees to march", Season(d, b)[0].Kind, "attack-yes");
		Is("  on the realm named", d.Errands.GetValueOrDefault(Watch), Marsh);
		d.Send(new Letter(Player, Watch, Diplomacy.AskAttack, About: Watch), Rivals);
		Is("but never on himself", Season(d, b)[0].Kind, "attack-no");
	}

	private void Purse()
	{
		var b = new GameBalance();
		var definitions = new List<ProvinceDefinition> { Definition("Ash"), Definition("Birch") };
		var realms = new Dictionary<string, string> { ["Ash"] = Player, ["Birch"] = Watch };
		var turns = new TurnManager(b, definitions, realms, Player, Difficulty.Medium)
		{
			LordOf = new Dictionary<string, string> { [Watch] = "margrave" },
		};

		int gold = turns.GetProvince("Ash").Gold;
		Is("a gift the purse cannot cover is not sent",
			turns.Write(new Letter(Player, Watch, Diplomacy.Gift, Gold: gold + 1)), false);
		Is("a gift it can is", turns.Write(new Letter(Player, Watch, Diplomacy.Gift, Gold: 100)), true);
		Is("  and is paid as it goes", turns.GetProvince("Ash").Gold, gold - 100);
		turns.AdvanceTurn();
		Is("the Margrave has answered by the next season",
			turns.Diplomacy.Inbox.Exists(letter => letter.From == Watch && letter.Kind == "gift-thanks"), true);
	}

	private void Saved()
	{
		var b = new GameBalance();
		var d = new Diplomacy();
		d.Move(Player, Watch, -12, b);
		d.Send(new Letter(Player, Watch, Diplomacy.Gift, Gold: 400), Rivals);
		d.Wars.Add(Diplomacy.Pair(Watch, Marsh));
		SaveGame.Write("Check", turn: 3, provinces: new() { new ProvinceEconomy { ProvinceName = "Ash" } },
			new Dictionary<string, float>(), Difficulty.Medium, d);
		List<SaveGame> saves = SaveGame.List();
		saves.Sort((left, right) => right.SavedAtUnix.CompareTo(left.SavedAtUnix));
		SaveGame read = saves[0];
		SaveGame.Forget(read);

		Is("a lord's grudge survives the save", read.Diplomacy.StandingOf(Player, Watch), -12);
		Is("  and the letter on the road", read.Diplomacy.Outbox.Count == 1 && read.Diplomacy.Outbox[0].Gold == 400, true);
		Is("  and the war", read.Diplomacy.AtWar(Marsh, Watch), true);
	}

	/// <summary>A season with the one rival, the Margrave unless told otherwise, and what he wrote.</summary>
	private static List<Letter> Season(Diplomacy d, GameBalance b, Lord lord = null)
	{
		d.Season(1, Player, new List<string> { Watch },
			new Dictionary<string, Lord> { [Watch] = lord ?? Lords.Find("margrave") }, b);
		return d.TakeLetters();
	}

	private static Dictionary<string, Lord> Seated(params string[] realms)
	{
		var seated = new Dictionary<string, Lord>();
		foreach (string realm in realms)
		{
			seated[realm] = Lords.Find("margrave");
		}

		return seated;
	}

	/// <summary>A lord who stomachs an alliance for three seasons, for a check that has to see one end.</summary>
	private static Lord Short() =>
		new("short", "The Short", "baron", OffersAllianceEvery: 99, GrudgeLimit: 3, new Dictionary<string, string>());

	private static ProvinceDefinition Definition(string name) => new()
	{
		ProvinceName = name,
		InitialPopulation = 850,
		Fields = 9,
		InitialGrainFields = 4,
		InitialPastureFields = 2,
	};

	private void Is<T>(string what, T got, T wanted)
	{
		bool ok = Equals(got, wanted);
		if (!ok)
		{
			_failed++;
		}

		GD.Print(ok ? $"ok   {what}" : $"FAIL {what}: got {got}, wanted {wanted}");
	}
}
