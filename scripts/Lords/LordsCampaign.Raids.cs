using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>The one raid each lord keeps out: peasants sent from his most peopled county to harry
/// his nearest enemy's fields and diggings, and walked home after.</summary>
public partial class LordsCampaign
{
	/// <summary>The raids (the original AI's step 10): each lord keeps one band of about fifty
	/// peasants out over his nearest enemy's land, walking from one of his fields or diggings to the
	/// next a season — treading the corn, scattering the herd, shutting the mine it halts on
	/// (TurnManager.Trample) — for LordRaidSeasons, and then home, where the men go back to the
	/// county. It fights nobody unless somebody comes for it. With no map ground to walk to, no raids.</summary>
	private void Raid(TurnManager turns, GameBalance b, int skill, List<RivalMarch> walked)
	{
		if (_groundOf == null)
		{
			return;
		}

		var sent = new HashSet<string>();
		foreach (FieldArmy raid in new List<FieldArmy>(turns.Armies()))
		{
			if (!raid.Raider || raid.Strength == 0)
			{
				continue;
			}

			string realm = turns.RealmOf(raid);
			sent.Add(realm);
			if (raid.RaidLeft > 0)
			{
				string prey = Prey(turns, raid, realm, b, skill);
				if (prey != null)
				{
					Harry(turns, raid, prey, walked);
					raid.RaidLeft--;
					continue;
				}

				raid.RaidLeft = 0;
			}

			// Home, and back to the fields they were taken from.
			if (raid.County == raid.Home && Pixel(raid).DistanceTo(_towns.GetValueOrDefault(raid.Home, Pixel(raid))) <= Gathering)
			{
				ProvinceEconomy home = turns.AnyProvince(raid.Home);
				home.Population += raid.Strength;
				home.Disband(raid);
			}
			else if (_towns.TryGetValue(raid.Home, out Vector2 seat))
			{
				Go(turns, raid, seat, walked, _reach * 2f);
			}
		}

		foreach (ProvinceEconomy county in turns.Provinces)
		{
			string realm = county.Realm;
			if (realm == turns.PlayerRealm || realm.Length == 0 || sent.Contains(realm)
				|| county.Population < b.LordRaidMen * 4 || county.BesiegedFrom.Length > 0)
			{
				continue;
			}

			// Sent from his most peopled county, which can best spare fifty farmhands.
			ProvinceEconomy biggest = turns.Provinces.FindAll(other => other.Realm == realm)
				.OrderByDescending(other => other.Population).First();
			if (biggest != county)
			{
				continue;
			}

			var raid = county.Raise(b.MarchReach);
			raid.Men["peasant"] = b.LordRaidMen;
			raid.Raider = true;
			raid.RaidLeft = b.LordRaidSeasons;
			string prey = Prey(turns, raid, realm, b, skill);
			if (prey == null)
			{
				county.Disband(raid);
				continue;
			}

			county.Population -= b.LordRaidMen;
			sent.Add(realm);
			Harry(turns, raid, prey, walked);
		}
	}

	/// <summary>The nearest county of another lord's a raid could go over: the player's only if his
	/// difficulty lets the lord come for him at all. Nobody's land has no year to spoil.</summary>
	private string Prey(TurnManager turns, FieldArmy raid, string realm, GameBalance b, int skill)
	{
		Vector2 here = Pixel(raid);
		string best = null;
		float nearest = float.MaxValue;
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			if (county.Realm == realm || !_towns.TryGetValue(county.ProvinceName, out Vector2 town)
				|| (county.Realm == turns.PlayerRealm && b.LordWillAttackPlayer[skill] == 0))
			{
				continue;
			}

			float far = here.DistanceTo(town);
			if (far < nearest)
			{
				best = county.ProvinceName;
				nearest = far;
			}
		}

		return best;
	}

	/// <summary>A season of harrying: to one of the county's fields or diggings, picked by chance,
	/// over whatever else of it lies on the road.</summary>
	private void Harry(TurnManager turns, FieldArmy raid, string prey, List<RivalMarch> walked)
	{
		List<Vector2> ground = _groundOf(prey);
		Vector2 to = ground.Count > 0 ? ground[_dice.RandiRange(0, ground.Count - 1)] : _towns[prey];
		Go(turns, raid, to, walked);
	}
}
