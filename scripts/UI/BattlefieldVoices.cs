using System.Collections.Generic;
using Godot;

/// <summary>The men answering the lord: a line when he takes a company in hand, another when he
/// marches it or draws its front, and another when he sends it at the enemy, in the voice of its
/// kind — assets/audio/voices/&lt;kind&gt;/select-1.mp3, move-1.mp3, attack-1.mp3 and on. A kind
/// nobody has recorded yet says nothing. Spoken through the Narrator, so a new order cuts the last
/// answer short instead of talking over it.</summary>
public partial class Battlefield
{
	private const string VoicesDirectory = "res://assets/audio/voices";

	/// <summary>The most lines of one moment a kind is looked for under.</summary>
	private const int MostLines = 9;

	/// <summary>How much quieter the men are heard from the furthest the eye goes than from down
	/// among them, in decibels, falling evenly with the eye's distance: at the top of the sky a
	/// whisper, and still plainly heard over the field where a battle opens (the user's ear — on a
	/// log scale most of the fall came in the first few metres up).</summary>
	private const float FurthestHushed = -30f;

	private readonly Dictionary<string, List<string>> _lines = new();
	private readonly RandomNumberGenerator _voice = new();
	private string _lastLine;

	/// <summary>One of the lines the first company in hand that has any says at this moment
	/// ("select", "move", "attack"), never the one it said last: three lines said at random came
	/// round twice in a row often enough to sound like one man stuck.</summary>
	private void Answer(string moment)
	{
		foreach (FieldSquad squad in _chosen)
		{
			List<string> lines = LinesOf(squad.Unit, moment);
			if (lines.Count == 0)
			{
				continue;
			}

			string line = lines[_voice.RandiRange(0, lines.Count - 1)];
			if (line == _lastLine && lines.Count > 1)
			{
				line = lines[(lines.IndexOf(line) + 1) % lines.Count];
			}

			_lastLine = line;
			Narrator.Say(line, Hushed());
			return;
		}
	}

	/// <summary>How much quieter the field is heard from where the eye is than from down among the men,
	/// in decibels.</summary>
	private float Hushed() => FurthestHushed * Mathf.Clamp((_eye - NearestEye) / (FurthestEye - NearestEye), 0f, 1f);

	/// <summary>A kind's recorded lines for a moment, looked up once. A hired band answers in the
	/// voice of the kind it fights as.</summary>
	private List<string> LinesOf(string unit, string moment)
	{
		string kind = Mercenaries.Find(unit)?.Unit ?? unit;
		string key = $"{kind}/{moment}";
		if (!_lines.TryGetValue(key, out List<string> lines))
		{
			lines = new List<string>();
			for (int n = 1; n <= MostLines; n++)
			{
				string path = $"{VoicesDirectory}/{key}-{n}.mp3";
				if (ResourceLoader.Exists(path))
				{
					lines.Add(path);
				}
			}

			_lines[key] = lines;
		}

		return lines;
	}
}
