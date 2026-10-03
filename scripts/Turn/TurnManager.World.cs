using System.Collections.Generic;

/// <summary>What the season brings from outside the walls: the weather over every held county and
/// the counties the world's events stir in.</summary>
public partial class TurnManager
{
	/// <summary>The season, everywhere at once. Every held county is run — the player's and the
	/// lords' — and what comes back is what happened in the player's own, because that is all the
	/// screens have any business drawing.
	///
	/// Fixed iteration order (the authored definitions list), not dictionary enumeration order, so a
	/// turn's outcome is reproducible (design doc section 34).</summary>
	/// <summary>The one county of each realm the world may visit this season, if the realm is not
	/// still getting over the last visit. Picked at random among the realm's counties, walked in
	/// authored order so the same seed picks the same county.</summary>
	private Dictionary<string, string> WhereTheWorldStirs()
	{
		var counties = new Dictionary<string, List<string>>();
		var realms = new List<string>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			string realm = _provincesByName[definition.ProvinceName].Realm;
			if (!counties.TryGetValue(realm, out List<string> held))
			{
				held = new List<string>();
				counties[realm] = held;
				realms.Add(realm);
			}

			held.Add(definition.ProvinceName);
		}

		var stirs = new Dictionary<string, string>();
		foreach (string realm in realms)
		{
			if (_worldLastStirred.TryGetValue(realm, out int last) && Turn - last <= _balance.WorldEventGap)
			{
				continue;
			}

			List<string> held = counties[realm];
			stirs[realm] = held[_rng.RandiRange(0, held.Count - 1)];
		}

		return stirs;
	}

	/// <summary>The season's weather over every held county (Climate), and what the lord hears of
	/// it: a field in one of his counties ruined by a flood or a drought. A sky that rains is rained
	/// on the map (Flooded), anybody's. And every field can be trampled afresh.</summary>
	private List<FiredEvent> Skies(Season entering)
	{
		var told = new List<FiredEvent>();
		var held = new List<ProvinceEconomy>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			held.Add(_provincesByName[definition.ProvinceName]);
		}

		foreach ((ProvinceEconomy county, int _) in Climate.Turn(held, entering, _balance, _rng,
			name => Neighbours.GetValueOrDefault(name, new List<string>())))
		{
			if (county.Realm != _playerRealm)
			{
				continue;
			}

			// A flood on land cropped past its rest is the lord's rotation, not only the rain, and the
			// steward says which.
			string id = county.Weather == Weather.Drought ? "drought-01"
				: county.Soil < _balance.TiredSoil ? "flood-overworked-fields" : "flood-01";
			GameEvent said = EventEngine.Find(id);
			if (said != null)
			{
				told.Add(new FiredEvent(county.ProvinceName, said, FromThePeople: false));
			}
		}

		foreach (ProvinceEconomy county in held)
		{
			if (county.Weather is Weather.Flooding or Weather.Storms)
			{
				Flooded.Add(county.ProvinceName);
			}

		}

		return told;
	}
}
