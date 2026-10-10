using System.Collections.Generic;
using Godot;

/// <summary>The lords' war, the part that answers the player's call for help: a lord sworn to him
/// who has said he will come (Diplomacy.AskHelp, kept as an empty errand) comes. A company sitting
/// before one of the player's gates is gone for first, fallen on in the open, and the siege lifted
/// with it; with nobody at his gates, the realm whose men stand on his land, or that is at war with
/// him, is the one whose counties the ally marches on before any other (Wanted). He keeps coming for
/// as long as he is sworn.</summary>
public partial class LordsCampaign
{
	/// <summary>Whether a lord has answered the player's call for help and is still sworn to him.</summary>
	private static bool IsHelping(TurnManager turns, string realm) =>
		turns.Diplomacy.AllyOf(realm) == turns.PlayerRealm
		&& turns.Diplomacy.Errands.TryGetValue(realm, out string about) && about.Length == 0;

	/// <summary>The company sitting before one of the player's gates nearest this one, of anybody's
	/// but the helper's own realm, or null.</summary>
	private FieldArmy Besieger(TurnManager turns, FieldArmy army, string realm)
	{
		FieldArmy best = null;
		foreach (ProvinceEconomy county in turns.Provinces)
		{
			FieldArmy foe = county.Realm == turns.PlayerRealm && county.BesiegedFrom.Length > 0
				? turns.ArmyOf(county.BesiegedFrom)
				: null;
			if (foe is { Strength: > 0 } && turns.RealmOf(foe) != realm
				&& (best == null || Pixel(foe).DistanceTo(Pixel(army)) < Pixel(best).DistanceTo(Pixel(army))))
			{
				best = foe;
			}
		}

		return best;
	}

	/// <summary>The realm a helper goes against: whoever sits before one of the player's gates, else
	/// whoever has men standing on his land, else whoever is at war with him; empty if nobody is.</summary>
	private string PlayersFoe(TurnManager turns, FieldArmy army, string realm)
	{
		if (Besieger(turns, army, realm) is FieldArmy besieger)
		{
			return turns.RealmOf(besieger);
		}

		foreach (FieldArmy other in turns.Armies())
		{
			string theirs = turns.RealmOf(other);
			if (other.Strength > 0 && theirs != realm && theirs != turns.PlayerRealm
				&& turns.AnyProvince(other.County)?.Realm == turns.PlayerRealm)
			{
				return theirs;
			}
		}

		foreach (string rival in turns.Rivals())
		{
			if (rival != realm && turns.Diplomacy.AtWar(rival, turns.PlayerRealm))
			{
				return rival;
			}
		}

		return "";
	}

	/// <summary>A helper's company marches on the nearest siege of the player's and falls on the
	/// besiegers once it is beside them and likes its chances. False when it has no siege to lift,
	/// and goes about its lord's war as any other company does.</summary>
	private bool Relieve(TurnManager turns, FieldArmy army, string realm, GameBalance b, int skill,
		List<RivalMarch> walked, List<FiredEvent> news)
	{
		if (!IsHelping(turns, realm) || Besieger(turns, army, realm) is not FieldArmy foe)
		{
			return false;
		}

		if (Pixel(army).DistanceTo(Pixel(foe)) > _reach)
		{
			Go(turns, army, Pixel(foe), walked);
		}

		if (army.County != foe.County || Pixel(army).DistanceTo(Pixel(foe)) > _reach
			|| FieldOdds(turns, army, foe, b) < b.LordAttackOdds[skill])
		{
			return true;
		}

		string county = turns.Besieging(foe);
		int came = foe.Strength;
		Battle.Result day = turns.Engage(army, foe);
		Tell(turns, county, "siege-relieved", news, new Dictionary<string, string>
		{
			["came"] = $"{came}",
			["county"] = county,
			["fate"] = day.AttackerWon
				? "broke them. The siege is lifted"
				: "were thrown back. The siege goes on",
		});
		return true;
	}

	/// <summary>How often a company would beat another in open country, fought out on copies.</summary>
	private float FieldOdds(TurnManager turns, FieldArmy army, FieldArmy foe, GameBalance b)
	{
		float loyalty = turns.AnyProvince(foe.Home)?.Loyalty ?? 50f;
		int won = 0;
		for (int trial = 0; trial < b.LordOddsTrials; trial++)
		{
			var against = new Defenders(new Dictionary<string, int>(foe.Men), new Dictionary<string, int>(), "", loyalty,
				InOpenCountry: true);
			won += Battle.InTheField(new Dictionary<string, int>(army.Men), against, army.MarchLeft <= 0f, b, _dice).AttackerWon ? 1 : 0;
		}

		return (float)won / Mathf.Max(1, b.LordOddsTrials);
	}
}
