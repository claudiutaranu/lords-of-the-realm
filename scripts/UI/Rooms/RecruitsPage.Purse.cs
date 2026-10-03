using System.Collections.Generic;
using Godot;

/// <summary>What a muster costs and who pays it: the purses an enlistment draws on, what the county
/// holds of each, the upkeep line, and the bill reckoned before a man is raised.</summary>
public partial class RecruitsPage
{
	/// <summary>What is left of a store after the muster already standing on the table has claimed
	/// its share. A slider that offers a lord men he has already promised to another company is a
	/// slider that lets him raise the same peasant twice.</summary>
	private int Mustered(string purse)
	{
		int left = Held(purse);
		foreach ((string key, int men) in _muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item != null && item.Cost.TryGetValue(purse, out int each))
			{
				left -= each * men;
			}
		}

		return Mathf.Max(0, left);
	}

	/// <summary>Stores are read by their own icon; a weapon is read by the icon the thing itself is
	/// read by, wherever it is counted — cavalry by its helm on the card, in the price and in the
	/// armoury alike. Asking the data file rather than repeating the mapping here is what keeps the
	/// three of them from drifting apart.</summary>
	protected override string IconFor(string purse) => purse switch
	{
		"people" => "population",
		"grain" => "food",
		"cattle" => "livestock",
		_ => Items.Find(item => item.Key == purse)?.Icon ?? purse,
	};

	/// <summary>People and stores are the province's own; weapons are counted out of the armoury the
	/// smithy fills.</summary>
	protected override int Held(string purse) => purse switch
	{
		"people" => Province?.Population ?? 0,
		"gold" => Province?.Gold ?? 0,
		"grain" => Province?.Grain ?? 0,
		"cattle" => Province?.Cattle ?? 0,
		"wood" => Province?.Wood ?? 0,
		"stone" => Province?.Stone ?? 0,
		"iron" => Province?.Iron ?? 0,
		_ => Province?.Armoury.GetValueOrDefault(purse) ?? 0,
	};

	/// <summary>Men are the one thing this game builds that goes on costing. They eat off the same
	/// granary the county does — more than a ploughman, and not on the ration the lord sets his
	/// people — and they are owed wages every season out of the same purse the walls are paid for.
	/// Said here, where the order is placed, because it is the only place the decision is being made
	/// and the cost itself does not appear until the season after — and so is what taking them costs
	/// the county's goodwill, which lands the moment the muster is called and was a surprise.</summary>
	protected override string UpkeepLine()
	{
		GameBalance balance = GameBalance.Engine;
		int grain = Mathf.CeilToInt(10 / balance.PeoplePerGrain * balance.SoldierAppetite);
		int gold = Mathf.CeilToInt(10 * balance.WagePerSoldier);
		int people = Mathf.Max(1, Province?.Population ?? 0);
		string keeping = $"Keeping them: every ten men eat {grain} grain a season and are owed {gold} gold. "
			+ "Men who cannot be paid go home.";

		// A hired company takes nobody's son, so it costs no goodwill.
		if (_lit != null && _lit.Key == _band?.Key)
		{
			return keeping;
		}

		return keeping + " "
			+ $"Taking them costs the county's goodwill: a tenth of its people ({people / 10:N0}) costs "
			+ $"{Livelihood.RecruitingCost(people / 10, people)}, half of them {Livelihood.RecruitingCost(people / 2, people)}.";
	}

	protected override void Pay(string purse, int amount)
	{
		switch (purse)
		{
			// Taken out of the fields, and remembered: the county resents an intake for a while
			// afterwards, which is what gives the advisor's "you took too many sons" something real
			// behind it.
			case "people":
				// Taken out of the fields as well as off the roll: a man handed a spear is not also
				// bringing the harvest in, and the county resents an intake for a while afterwards,
				// which is what gives the advisor's "you took too many sons" something behind it.
				EconomySimulation.Conscript(Province, amount);
				break;
			case "gold": Province.Gold -= amount; break;
			case "grain": Province.Grain -= amount; break;
			case "cattle": Province.Cattle -= amount; break;
			case "wood": Province.Wood -= amount; break;
			case "stone": Province.Stone -= amount; break;
			case "iron": Province.Iron -= amount; break;
			default: Province.Armoury[purse] = Province.Armoury.GetValueOrDefault(purse) - amount; break;
		}
	}

	/// <summary>Whether the county could pay for the whole muster with this added to it. Asked
	/// against the total and not against one company, because the purse is one purse.</summary>
	private bool Affordable(string key, int more)
	{
		var wanted = new Dictionary<string, int>(_muster);
		wanted[key] = wanted.GetValueOrDefault(key) + more;
		foreach ((string purse, int owed) in Bill(wanted))
		{
			if (Held(purse) < owed)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>What a muster comes to, store by store.</summary>
	private Dictionary<string, int> Bill(Dictionary<string, int> muster)
	{
		var owed = new Dictionary<string, int>();
		foreach ((string key, int men) in muster)
		{
			Item item = Items.Find(card => card.Key == key);
			if (item == null)
			{
				continue;
			}

			foreach ((string purse, int each) in item.Cost)
			{
				owed[purse] = owed.GetValueOrDefault(purse) + (each * (Sized(item) ? men : 1));
			}
		}

		return owed;
	}
}
