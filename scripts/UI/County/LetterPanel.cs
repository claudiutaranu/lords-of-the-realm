using System.Collections.Generic;
using Godot;

/// <summary>A lord's letter, laid in front of the player when his season opens, as Lords of the
/// Realm lays them: straight onto the map, one at a time, after the steward has had his say. Straight
/// on rather than as a notice to look at later, because some of them will not wait — an offer of
/// alliance lapses at the season's end, and a warning is the last thing said before a war.
///
/// An offer carries its answer on the letter itself. Closed unanswered it still stands on the
/// diplomacy page until the season is out.</summary>
public partial class LetterPanel : CountyPanel
{
	private readonly Queue<Letter> _waiting = new();
	private TurnManager _turns;
	private System.Func<string, string> _realmName;
	private CenterContainer _face;
	private Label _realm;
	private Label _words;
	private HBoxContainer _answer;

	/// <summary>Raised when the last letter has been put down — and straight away when there were
	/// none. What else the season has to say waits for it.</summary>
	public event System.Action Emptied;

	/// <summary>Raised when an offer has been taken, so whatever shows the lords can catch up.</summary>
	public event System.Action Answered;

	protected override int Width => 600;

	protected override void Furnish()
	{
		// Whoever wrote it, looking up from the page, where there is a portrait of him.
		_face = new CenterContainer();
		Column.AddChild(_face);

		_realm = Chrome.Line("", 16, Chrome.Dim);
		_realm.HorizontalAlignment = HorizontalAlignment.Center;
		Column.AddChild(_realm);

		_words = Chrome.Line("", 22, Chrome.Cream);
		_words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_words.HorizontalAlignment = HorizontalAlignment.Center;
		_words.CustomMinimumSize = new Vector2(540, 90);
		_words.VerticalAlignment = VerticalAlignment.Center;
		Column.AddChild(_words);

		_answer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_answer.AddThemeConstantOverride("separation", 16);
		Column.AddChild(_answer);

		Closed += Next;
	}

	/// <summary>Lays the season's letters out, one after another.</summary>
	public void Read(TurnManager turns, List<Letter> letters, System.Func<string, string> realmName)
	{
		_turns = turns;
		_realmName = realmName;
		foreach (Letter letter in letters)
		{
			_waiting.Enqueue(letter);
		}

		if (!Visible)
		{
			Next();
		}
	}

	private void Next()
	{
		if (_waiting.Count == 0)
		{
			Emptied?.Invoke();
			return;
		}

		Letter letter = _waiting.Dequeue();
		Lord lord = Lords.Find(_turns.LordOf.GetValueOrDefault(letter.From, ""));
		_realm.Text = _realmName(letter.From);
		foreach (Node old in _face.GetChildren())
		{
			old.QueueFree();
		}

		if (LordPortrait.Of(lord, 150) is Control face)
		{
			_face.AddChild(face);
		}

		_words.Text = $"“{Words(letter, lord, _realmName)}”";

		foreach (Node old in _answer.GetChildren())
		{
			old.QueueFree();
		}

		if (letter.Kind == "alliance-offer" && _turns.Diplomacy.Offers.Contains(letter.From))
		{
			_answer.AddChild(Answer("Accept", () =>
			{
				_turns.Diplomacy.Accept(_turns.PlayerRealm, letter.From);
				Answered?.Invoke();
				Close();
			}));
			_answer.AddChild(Answer("Refuse", () =>
			{
				_turns.Diplomacy.Decline(letter.From);
				Answered?.Invoke();
				Close();
			}));
		}

		Reveal($"From {lord?.Title ?? _realmName(letter.From)}");

		// And he reads it out, in his own voice, where it has been recorded
		// (assets/audio/lords/<lord>/<reply>.mp3); a line nobody has recorded is only read.
		if (lord != null)
		{
			Narrator.Say($"res://assets/audio/lords/{lord.Key}/{letter.Kind}.mp3");
		}
	}

	private static Button Answer(string text, System.Action pressed)
	{
		var answer = new Button { Text = text, CustomMinimumSize = new Vector2(180, 44) };
		answer.AddThemeFontSizeOverride("font_size", 18);
		answer.Pressed += pressed;
		return answer;
	}

	/// <summary>A letter in words: a lord's in his own voice (Lord.Says), the player's as his clerk
	/// would file it. What the diplomacy page reads back, too.</summary>
	/// <remarks><paramref name="lord"/> is the writer, or null for the player: a lord's answer to an
	/// insult is keyed "insult" like the insult itself, so the kind alone cannot say whose words they are.</remarks>
	public static string Words(Letter letter, Lord lord, System.Func<string, string> realmName) => lord != null
		? lord.Says(letter.Kind, letter.Gold)
		: letter.Kind switch
		{
			Diplomacy.Gift => $"A gift of {letter.Gold:N0} crowns.",
			Diplomacy.Compliment => "A letter full of compliments.",
			Diplomacy.Insult => "A letter full of insults.",
			Diplomacy.OfferAlliance => "An offer of alliance.",
			Diplomacy.BreakAlliance => "Word that the alliance is at an end.",
			Diplomacy.AskHelp => "A call for help.",
			Diplomacy.AskAttack => $"A request to march on {realmName(letter.About)}.",
			_ => "",
		};
}
