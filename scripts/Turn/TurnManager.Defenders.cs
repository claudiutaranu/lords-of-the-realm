using System.Collections.Generic;
using Godot;

/// <summary>Who stands at a county's gate when somebody comes for it: the companies in it, the
/// watch on its walls, and an unheld county's militia off its farmhands.</summary>
public partial class TurnManager
{
	/// <summary>What a county with no lord puts in the way of one who wants it, by the keys
	/// recruits.json uses: its farmhands, and the town's own watch of bowmen and spears.</summary>
	private const string MilitiaUnit = "peasant";
	private const string WatchBows = "bow";
	private const string WatchSpears = "spear";

	/// <summary>Who would have to be beaten to take a county: the men a lord has standing in it, or
	/// the militia an unclaimed one raises out of its own people.
	///
	/// One answer and not two, because the banner the map hangs over a county is read off the same
	/// call the battle is fought against. A county that shows forty men from the hilltop and fields
	/// ninety when the shooting starts is the game lying to the player about the one thing he planned
	/// his season around.
	///
	/// The rosters of a county somebody holds are that county's own and not copies: what a battle
	/// takes out of them, it takes out of the men themselves. An unclaimed county's militia is raised
	/// fresh on every call, so what a failed attack cost them is forgotten by the next one — they are
	/// not in the turn, and there is nowhere for it to be remembered.
	/// ponytail: militia damage is not kept, add when unheld counties take turns of their own.</summary>
	public Defenders DefendersOf(string county)
	{
		ProvinceEconomy held = _provincesByName.GetValueOrDefault(county);
		if (held != null)
		{
			// One company's own roster where there is one company, so a battle takes its dead off
			// the men who died. Where a lord has several standing there it is a reading and not a
			// roster — Attack and Besiege put them under one banner first, and then there is one.
			List<FieldArmy> there = StandingIn(county);
			Dictionary<string, int> field = there.Count == 1 ? there[0].Men : new Dictionary<string, int>();
			if (there.Count > 1)
			{
				foreach (FieldArmy army in there)
				{
					foreach ((string unit, int men) in army.Men)
					{
						field[unit] = field.GetValueOrDefault(unit) + men;
					}
				}
			}

			// Nobody of the lord's standing there: the town turns out for itself, the way an unclaimed
			// one does — unless it already turned out this season and was beaten, or a siege has it
			// shut in behind its gate.
			if (there.Count == 0 && held.MilitiaRoutedTurn != Turn && held.BesiegedFrom.Length == 0)
			{
				field = Militia(held.Population);
			}

			return new Defenders(field, held.Castle, held.Fortification, held.Loyalty);
		}

		ProvinceDefinition free = _unheld.GetValueOrDefault(county);
		if (free == null)
		{
			return new Defenders(new Dictionary<string, int>(), new Dictionary<string, int>(), "", 0f);
		}

		return new Defenders(Militia(free.InitialPopulation), new Dictionary<string, int>(), free.InitialFortification,
			ProvinceEconomy.OpeningLoyalty);
	}

	/// <summary>What a town turns out when nobody else is standing in it: MilitiaShare of its people,
	/// the watch first — MilitiaArmed of them, bows and spears half and half — and every other man
	/// with what hangs in the barn.</summary>
	private Dictionary<string, int> Militia(int people)
	{
		var raised = new Dictionary<string, int>();
		int militia = Mathf.FloorToInt(people * _balance.MilitiaShare[(int)Difficulty]);
		int armed = Mathf.FloorToInt(militia * _balance.MilitiaArmed[(int)Difficulty]);
		int bows = armed / 2;
		foreach ((string unit, int men) in new[] { (WatchBows, bows), (WatchSpears, armed - bows), (MilitiaUnit, militia - armed) })
		{
			if (men > 0)
			{
				raised[unit] = men;
			}
		}

		return raised;
	}
}
