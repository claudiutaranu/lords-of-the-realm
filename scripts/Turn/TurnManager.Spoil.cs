using System.Collections.Generic;
using Godot;

/// <summary>The map's survey and the harm a march does on it: where an army's road first crosses
/// another lord's sown or grazed field, and the trampling that lays it waste.</summary>
public partial class TurnManager
{
	/// <summary>Hands the turn the ground the map lays out, so the other lords can march over it by
	/// the player's own rules — and, where the map has them, the fields and diggings a march can
	/// trample (<see cref="Trample"/>).</summary>
	public void Survey(LordsCampaign.Way way, System.Func<Vector2, string> countyAt,
		Dictionary<string, Vector2> towns, float reach,
		System.Func<Vector2, (string County, int Field)> fieldAt = null,
		System.Func<Vector2, (string County, string Site)> siteAt = null,
		System.Func<string, List<Vector2>> groundOf = null, System.Func<Vector2, Vector2, bool> reaches = null)
	{
		_campaign = new LordsCampaign(way, countyAt, towns, reach, groundOf, reaches);
		_way = way;
		_towns = towns;
		_gateReach = reach;
		_fieldAt = fieldAt;
		_siteAt = siteAt;
	}

	private System.Func<Vector2, (string County, int Field)> _fieldAt;
	private System.Func<Vector2, (string County, string Site)> _siteAt;

	/// <summary>Where along a road this company would first lay waste another lord's field — one under
	/// grain or grazed — or -1: none on the way, or it has already
	/// spoiled its one field this season. A march is cut there (<see cref="Trample"/>).</summary>
	public int FirstSpoil(FieldArmy army, IReadOnlyList<Vector2> road)
	{
		if (army.SpoiledTurn == Turn || _fieldAt == null)
		{
			return -1;
		}

		string realm = RealmOf(army);
		for (int step = 0; step < road.Count; step++)
		{
			(string county, int field) = _fieldAt(road[step]);
			ProvinceEconomy there = _provincesByName.GetValueOrDefault(county);
			if (there != null && there.Realm != realm && field >= 0 && field < there.Fields.Length
				&& there.Fields[field] is FieldUse.Grain or FieldUse.Pasture)
			{
				return step;
			}
		}

		return -1;
	}

	/// <summary>An army's march over the ground, as Lords of the Realm has it. The one field of
	/// another lord's it halts on (the march having been cut there, <see cref="FirstSpoil"/>) is laid
	/// waste — the corn standing on it or the beasts grazing it lost, and the field itself to be
	/// reclaimed by his hands, as after a flood — and that is the rest of the company's season: one
	/// field a season. A quarry, mine or wood it halts on is shut for OccupiedSeasons. What it costs the player's
	/// counties he hears of. A county nobody holds has no year to spoil.</summary>
	public void Trample(FieldArmy army, IReadOnlyList<Vector2> road)
	{
		string realm = RealmOf(army);
		var spoiled = new Dictionary<string, (int Corn, int Beasts, string Site)>();
		if (road.Count > 0 && FirstSpoil(army, road) == road.Count - 1)
		{
			(string county, int field) = _fieldAt(road[^1]);
			ProvinceEconomy there = _provincesByName[county];
			army.SpoiledTurn = Turn;
			army.MarchLeft = 0f;

			int corn = 0;
			int beasts = 0;
			if (there.Fields[field] == FieldUse.Grain && there.StandingCrop > 0)
			{
				corn = there.StandingCrop / there.FieldsUnder(FieldUse.Grain);
				there.StandingCrop -= corn;
			}
			else if (there.Fields[field] == FieldUse.Pasture && there.Cattle > 0)
			{
				beasts = there.Cattle / there.FieldsUnder(FieldUse.Pasture);
				there.Cattle -= beasts;
			}

			// Churned to waste, like ground a flood took: nothing grows or grazes on it until the
			// lord sets reclaimers on it (Husbandry.Reclaim).
			there.Fields[field] = FieldUse.Waste;
			there.Reclaimed[field] = Mathf.Max(0, _balance.FieldReclaimWork - _balance.TrampledReclaimWork);
			spoiled[county] = (corn, beasts, null);
		}

		if (road.Count > 0 && _siteAt?.Invoke(road[^1]) is ({ Length: > 0 } siteCounty, { Length: > 0 } siteKey))
		{
			ProvinceEconomy there = _provincesByName.GetValueOrDefault(siteCounty);
			if (there != null && there.Realm != realm)
			{
				there.Occupied[siteKey] = _balance.OccupiedSeasons;
				(int corn, int beasts, string _) = spoiled.GetValueOrDefault(siteCounty);
				spoiled[siteCounty] = (corn, beasts, siteKey);
			}
		}

		foreach ((string county, (int corn, int beasts, string site)) in spoiled)
		{
			if (_provincesByName[county].Realm == _playerRealm && (corn > 0 || beasts > 0 || site != null))
			{
				Told(county, "fields-trampled", new Dictionary<string, string>
				{
					["county"] = county,
					["harm"] = Harm(corn, beasts, site),
				});
			}
		}
	}

	/// <summary>What a march cost a county, in the steward's words.</summary>
	private string Harm(int corn, int beasts, string site)
	{
		var harm = new List<string>();
		if (corn > 0)
		{
			harm.Add($"{corn:N0} sacks of standing corn trodden into the mud");
		}

		if (beasts > 0)
		{
			harm.Add($"{beasts:N0} head of cattle run off or slaughtered");
		}

		if (site != null)
		{
			harm.Add($"the {Labour.SiteName(site)} held, and not a man of ours working it for {_balance.OccupiedSeasons} seasons");
		}

		return harm.Count switch
		{
			0 => "nothing worth the naming",
			1 => harm[0],
			_ => string.Join(", ", harm.GetRange(0, harm.Count - 1)) + " and " + harm[^1],
		};
	}
}
