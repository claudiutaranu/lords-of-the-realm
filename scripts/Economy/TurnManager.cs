using System.Collections.Generic;

/// <summary>Owns every player-owned province's runtime economy and advances them one
/// season per End Turn. Each province keeps its own stockpiles — there is no shared
/// empire-wide treasury. UI reads state through this and calls AdvanceTurn(); it never
/// calls EconomySimulation directly (design doc section 33 — keep simulation independent
/// of UI so AI-controlled provinces and save/load can drive it the same way later).</summary>
public class TurnManager
{
	private readonly GameBalance _balance;
	private readonly List<ProvinceDefinition> _definitions;
	private readonly Dictionary<string, ProvinceEconomy> _provincesByName = new();

	/// <summary>Calendar year the campaign opens on. Four seasons make a year, so the
	/// displayed year advances every fourth turn.</summary>
	private const int StartYear = 1268;
	private const int SeasonsPerYear = 4;

	public int Turn { get; private set; } = 1;
	public Season CurrentSeason => (Season)((Turn - 1) % SeasonsPerYear);
	public int CurrentYear => StartYear + ((Turn - 1) / SeasonsPerYear);

	public TurnManager(GameBalance balance, List<ProvinceDefinition> definitions)
	{
		_balance = balance;
		_definitions = definitions;
		foreach (ProvinceDefinition definition in definitions)
		{
			_provincesByName[definition.ProvinceName] = ProvinceEconomy.FromDefinition(definition);
		}
	}

	public ProvinceEconomy GetProvince(string name) =>
		_provincesByName.GetValueOrDefault(name);

	/// <summary>Every province in authored order — what a save writes out.</summary>
	public List<ProvinceEconomy> Provinces =>
		_definitions.ConvertAll(definition => _provincesByName[definition.ProvinceName]);

	/// <summary>Puts a save's state back in place. Provinces are matched by name, so a save
	/// written before a province was added or renamed still loads: the missing one simply keeps
	/// the starting values the definitions gave it.</summary>
	public void Restore(int turn, List<ProvinceEconomy> provinces)
	{
		Turn = turn;
		foreach (ProvinceEconomy province in provinces)
		{
			if (_provincesByName.ContainsKey(province.ProvinceName))
			{
				_provincesByName[province.ProvinceName] = province;
			}
		}
	}

	// Fixed iteration order (the authored definitions list), not dictionary enumeration
	// order, so a turn's outcome is reproducible (design doc section 34).
	public List<TurnSummary> AdvanceTurn()
	{
		Season season = CurrentSeason;
		var summaries = new List<TurnSummary>(_definitions.Count);
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy province = _provincesByName[definition.ProvinceName];
			summaries.Add(EconomySimulation.RunTurn(province, definition, _balance, season));
		}

		Turn++;
		return summaries;
	}
}
