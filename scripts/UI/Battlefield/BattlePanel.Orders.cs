using System.Collections.Generic;
using Godot;

/// <summary>What the lord can answer: lead the field in person, or strike and let the captain's
/// reckoning settle it. Sitting down before a castle is BattlePanel.Siege.</summary>
public partial class BattlePanel
{
	/// <summary>Takes the lord down onto the field to fight the day himself. The panel steps aside
	/// while he is there, and comes back with the reckoning of whatever he made of it.</summary>
	private void Lead()
	{
		// At the walls the men who fight are the castle's own, behind its wall; in the field, whoever
		// stands in front of the town.
		Defenders against = Against();
		var battle = _walls
			? new FieldBattle(_attacker.Men, against with { Field = against.Castle, Castle = new Dictionary<string, int>() },
				_attacker.MarchLeft <= 0f, _balance, atTheWalls: true)
			: new FieldBattle(_attacker.Men, against, _attacker.MarchLeft <= 0f, _balance);
		var field = new Battlefield();
		GetParent().AddChild(field);
		field.Begin(battle, _us, _them);
		Visible = false;
		Fielded?.Invoke(true);
		field.Finished += day =>
		{
			field.QueueFree();
			Fielded?.Invoke(false);
			Visible = true;
			Strike(day);
		};
	}

	/// <summary>Fights the one on the table, and says what it cost — or, given the day the lord
	/// fought himself, writes that one in instead. Whether the county has changed hands is read
	/// back off the ledger rather than worked out here — the ledger is what decides it, and a panel
	/// with a second opinion about who owns a county is a panel that will one day be wrong.</summary>
	private void Strike(Battle.Result? fought = null)
	{
		// Both sides as they stood before the day, so the table can say what each had and, in
		// brackets, what each lost.
		var oursBefore = new Dictionary<string, int>(_attacker.Men);
		Defenders standing = Against();
		var theirsBefore = new Dictionary<string, int>(_walls ? standing.Castle : standing.Field);
		if (_enemy != null)
		{
			Battle.Result met = _turns.Engage(_attacker, _enemy, fought);
			Settled?.Invoke();
			foreach (Node old in _terms.GetChildren())
			{
				old.QueueFree();
			}

			ShowSides(oursBefore, theirsBefore, met.AttackerLosses, met.DefenderLosses);
			Note($"We lost {met.AttackerFell:N0}");
			Note($"They lost {met.DefenderFell:N0}");
			_question.Text = "";
			_verdict.Text = met.AttackerWon ? "Their company is broken, my lord." : "We are thrown back, my lord.";
			_verdict.AddThemeColorOverride("font_color", met.AttackerWon ? Chrome.Cream : Bad);
			_attack.Visible = false;
			_lead.Visible = false;
			_siege.Visible = false;
			_leaveWord.Text = "Done";
			return;
		}

		Battle.Result day = _turns.Attack(_attacker, _county, _at, _walls, fought);
		Settled?.Invoke();

		bool taken = _turns.GetProvince(_county) != null;
		bool fellBack = day.AttackerWon && !taken;

		foreach (Node old in _terms.GetChildren())
		{
			old.QueueFree();
		}

		Defenders left = taken
			? new Defenders(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 0f)
			: _turns.DefendersOf(_county);

		ShowSides(oursBefore, theirsBefore, day.AttackerLosses, day.DefenderLosses);

		Note($"We lost {day.AttackerFell:N0}");
		Note($"They lost {day.DefenderFell:N0}");

		_question.Text = "";
		_verdict.Text = taken
			? $"{_county} is yours."
			: fellBack
				? "The field is ours. They have fallen back behind their walls."
				: "We are thrown back, my lord.";
		_verdict.AddThemeColorOverride("font_color", taken || fellBack ? Chrome.Cream : Bad);

		// A beaten field army with a castle still standing is the second question, and it is a real
		// one: the lord may storm it now, on what the first fight left him, or leave it and come
		// back with more men next season.
		_attack.Visible = fellBack && ProvinceEconomy.Men(left.Castle) > 0;
		_lead.Visible = false;
		_siege.Visible = _attack.Visible;
		_leaveWord.Text = _attack.Visible ? "Retreat" : "Done";
		if (_attack.Visible)
		{
			_walls = true;
			_question.Text = "Will you storm the walls?";
			_attackWord.Text = "Storm the Walls";
		}
	}
}
