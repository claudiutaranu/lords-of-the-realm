using System.Collections.Generic;
using Godot;

/// <summary>A castle with men on its walls is not stormed off the march. The army sits down before
/// the gate and builds its engines first — rams for the gate, catapults for the curtain, no more than
/// GameBalance.SiegeEnginesMost of each — each costing seasons of the siege, and the season they are
/// built the assault is put to the lord (CampaignMapPage.StormWhenReady). Starving the garrison out
/// goes on all the while (TurnManager.Sieges).</summary>
public partial class BattlePanel
{
	private readonly Dictionary<string, int> _engines = new();
	private bool _isChoosing;
	private Label _siegeWord;

	/// <summary>What the gate offers, once Lay has read the walls: the siege to begin, the engines
	/// still building, or the assault they have made ready.</summary>
	private void Besieged(Defenders against)
	{
		_isChoosing = false;
		_engines.Clear();
		_siegeWord.Text = "Lay Siege";
		bool castle = _walls && ProvinceEconomy.Men(against.Castle) > 0;
		bool ours = castle && _turns.Besieging(_attacker) == _county;
		int left = ours ? _turns.SiegeSeasonsLeft(_county) : -1;

		_attack.Visible = !castle || left == 0;
		_siege.Visible = castle && !ours;
		// The assault, once it can go in, can be led by hand as the field can.
		_lead.Visible |= castle && left == 0;
		if (ours && left > 0)
		{
			_question.Text = "The engines are building";
			_verdict.Text = $"Ready to storm in {left} turn{(left == 1 ? "" : "s")}, my lord.";
		}
		else if (castle && !ours)
		{
			// Halted before the walls, the lord is asked straight away what to build: a castle is not
			// taken off the march, so there is no other question to put to him first.
			Sit();
		}
	}

	/// <summary>Lay Siege, pressed twice: first it lays out the engines to choose, then it sits the
	/// army down to build them.</summary>
	private void Sit()
	{
		if (!_isChoosing)
		{
			_isChoosing = true;
			_siegeWord.Text = "Begin the Siege";
			Choose();
			return;
		}

		if (!_turns.Besiege(_attacker, _county, _engines))
		{
			return;
		}

		Settled?.Invoke();
		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		int seasons = SiegeEngines.Seasons(_engines, _balance);
		Note("Our men hold the ground and nothing else, for as long as it takes");
		Note("The county pays its lord nothing while we sit here");
		_question.Text = "";
		_verdict.Text = seasons == 0
			? $"We sit down before {_county}, to starve it out."
			: $"We sit down before {_county}. The engines will be ready, and the assault go in, in {seasons} turn{(seasons == 1 ? "" : "s")}.";
		_verdict.AddThemeColorOverride("font_color", Chrome.Cream);
		_attack.Visible = false;
		_lead.Visible = false;
		_siege.Visible = false;
		_leaveWord.Text = "Done";
	}

	/// <summary>The engines on the table, each with how many and what it costs, and when the assault
	/// can go in with them.</summary>
	private void Choose()
	{
		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		Engine(SiegeEngines.Ram, $"Rams, up to {_balance.SiegeEnginesMost}", _balance.RamSeasons, "open the gate: more men at it at once");
		Engine(SiegeEngines.Catapult, $"Catapults, up to {_balance.SiegeEnginesMost}", _balance.CatapultSeasons, "breach the curtain, and the stone behind it");
		int seasons = SiegeEngines.Seasons(_engines, _balance);
		_question.Text = "What will you build?";
		_verdict.Text = seasons == 0
			? "No engines: we only starve them out."
			: $"The assault in {seasons} turn{(seasons == 1 ? "" : "s")}, my lord.";
		_attack.Visible = false;
		_lead.Visible = false;
	}

	private void Engine(string kind, string name, int seasons, string does)
	{
		int count = _engines.GetValueOrDefault(kind);
		Label said = Chrome.Line($"{name} ({seasons} turn{(seasons == 1 ? "" : "s")} each): {does}", 16, Chrome.Soft);
		said.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_terms.AddChild(said);
		_terms.AddChild(Chrome.Plate("−", 30, () =>
		{
			_engines[kind] = Mathf.Max(0, count - 1);
			Choose();
		}));
		Label many = Chrome.Line(count.ToString(), 20, Chrome.Bright);
		many.CustomMinimumSize = new Vector2(28, 0);
		many.HorizontalAlignment = HorizontalAlignment.Center;
		_terms.AddChild(many);
		Button more = Chrome.Plate("+", 30, () =>
		{
			_engines[kind] = count + 1;
			Choose();
		});
		more.Disabled = count >= _balance.SiegeEnginesMost;
		_terms.AddChild(more);
	}
}
