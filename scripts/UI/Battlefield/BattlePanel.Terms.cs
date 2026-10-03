using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The reckoning laid out before the lord decides: both sides' men, the terms that weigh
/// for and against him, and the captain's word on the odds.</summary>
public partial class BattlePanel
{
	/// <summary>Both halves of the table, and the frame cut to the longer of them: a skirmish between
	/// two kinds of man is not served on a table laid for seven.</summary>
	private void ShowSides(Dictionary<string, int> ours, Dictionary<string, int> theirs,
		Dictionary<string, int> ourLost = null, Dictionary<string, int> theirLost = null)
	{
		(int mine, float high) = _ourSide.Show(_us.Name, $"Men of {_attacker.Home}", _us.Key, _us.Accent, ours, ourLost);
		(int yours, float _) = _theirSide.Show(_them.Name,
			_enemy != null ? $"Men of {_enemy.Home}" : _walls ? $"On the walls of {_county}" : $"Holding {_county}",
			_them.Key, _them.Accent, theirs, theirLost);

		// The middle column has a floor of its own, so a table this short stops shrinking there.
		int kinds = Mathf.Max(MiddleRows, Mathf.Max(mine, yours));
		// The art is laid for the county's own kinds; a hired band is a row past them.
		Draw(PanelSize.Y - ((Units.All().Count(kind => !Units.IsHired(kind)) - kinds) * high / Writable));
	}

	/// <summary>Every reason the day will go the way it goes, in the order a captain would say them.
	/// Each line is a number <see cref="Battle"/> genuinely multiplies by — there is nothing on this
	/// list for flavour, because a lord who learns that one of these lines is decoration stops
	/// believing the rest of them.</summary>
	private void ShowTerms(Defenders against, Dictionary<string, int> ours, bool spent)
	{
		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		if (_walls)
		{
			Fortifications.Wall wall = Fortifications.Of(against.Fortification);
			Term($"Behind the {wall.Name}", wall.Defence, good: false);
			Term("A man on a ladder cannot defend himself", 1f / _balance.AssaultExposure, good: false);

			int men = ProvinceEconomy.Men(ours);
			if (men > wall.Frontage)
			{
				Term($"Only {wall.Frontage:N0} of our {men:N0} can reach the wall",
					wall.Frontage / (float)men, good: false);
			}

			foreach ((string unit, int riders) in ours)
			{
				if (Units.Of(unit).Mounted)
				{
					Note($"Our {riders:N0} {Units.Of(unit).Name.ToLowerInvariant()} are no use on a ladder");
				}
			}

			// What a besieging captain would put at: the size of the place and the number of mouths
			// in it. It is the whole basis of choosing to sit down rather than climb, so a lord who
			// cannot see it is choosing blind.
			int mouths = Mathf.CeilToInt(ProvinceEconomy.Men(against.Castle)
				/ _balance.PeoplePerGrain * _balance.SoldierAppetite);
			int seasons = mouths <= 0 ? 0 : Mathf.CeilToInt(wall.Stores / (float)mouths);
			Note($"A place that size holds perhaps {seasons:N0} seasons of bread");
		}

		float heart = 1f + ((Mathf.Clamp(against.Loyalty, 0f, 100f) - 50f) / 50f * _balance.LoyaltyDefence);
		if (!against.InOpenCountry)
		{
			Term("Holding their own town", _balance.TownDefence, good: false);
		}

		if (!against.InOpenCountry && Mathf.Abs(heart - 1f) > 0.01f)
		{
			Term(heart > 1f ? "The people are with them" : "The people have had enough of them",
				heart, good: heart < 1f);
		}

		if (spent)
		{
			Term("Our men have walked all season", _balance.MarchedOutOffence, good: false);
		}
	}

	private void Term(string what, float weight, bool good)
	{
		_terms.AddChild(Chrome.Line(what, 16, Chrome.Soft));
		Label figure = Chrome.Line($"×{weight:0.00}", 16, good ? Chrome.Gain : Bad);
		figure.HorizontalAlignment = HorizontalAlignment.Right;
		figure.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_terms.AddChild(figure);
	}

	private void Note(string what)
	{
		_terms.AddChild(Chrome.Line(what, 16, Chrome.Dim));
		_terms.AddChild(new Control());
	}

	/// <summary>What the captain makes of it, in words rather than in a percentage. He is looking at
	/// the same two figures the bar is drawn from, so he cannot say one thing while it says
	/// another.</summary>
	private static string Reckoned(float ours, float theirs)
	{
		float share = ours / Mathf.Max(0.01f, ours + theirs);
		return share switch
		{
			>= 0.72f => "The day should be ours, my lord.",
			>= 0.58f => "We have the better of them.",
			>= 0.45f => "It will be a close thing.",
			>= 0.30f => "They have the better of us, my lord.",
			_ => "This would be a slaughter. Ours.",
		};
	}
}
