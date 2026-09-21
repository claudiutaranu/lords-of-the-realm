using System.Collections.Generic;
using Godot;

/// <summary>What happens when one lord's men reach another's. The whole of the fighting, and none
/// of the screen it is watched on.
///
/// It is an auto-calculation and says so: the old game this one is copied from let a lord fight his
/// battles himself on a tactical field or hand them to his captain, and this is the captain. So it
/// has to be legible rather than clever — a lord looks at two armies, at the walls between them and
/// at what the season has done to his own men, and forms an expectation. A battle that then goes
/// the other way for reasons he could not have weighed is a battle that teaches him nothing.
///
/// Every number it decides by is one he has already been shown somewhere else: the four bars on the
/// cards in his own barracks (<see cref="Units"/>), the rung of wall standing on the map
/// (<see cref="Fortifications"/>), the county's goodwill, and how much road is left in his men's
/// legs. Nothing is rolled that he could not have seen coming, except the day itself.
///
/// TWO FIGHTS, IN ORDER, and that is the shape of the thing. Whatever is standing in the open is
/// beaten in the open, where numbers tell. Whatever falls back behind the walls is beaten
/// afterwards and on far worse terms — and the county does not change hands until both are done,
/// which is what a castle is FOR. See <see cref="Defenders"/> for the two rosters that make the two
/// halves.</summary>
public static class Battle
{
	/// <summary>The range at which a man shoots rather than closes. Bows and crossbows are over it
	/// and everything else is under it, which is how a volley knows who is in it without anybody
	/// keeping a second list of who counts as an archer.</summary>
	private const int ShootsBeyond = 5;

	/// <summary>How a fight came out: who held the ground at the end of it, what it cost each side
	/// company by company, and how long it took. The losses are what is subtracted from the real
	/// rosters afterwards — the fight itself is done on copies, so a battle that is only being
	/// LOOKED at cannot kill anybody.</summary>
	public readonly record struct Result(
		bool AttackerWon,
		Dictionary<string, int> AttackerLosses,
		Dictionary<string, int> DefenderLosses,
		int Rounds)
	{
		public int AttackerFell => ProvinceEconomy.Men(AttackerLosses);

		public int DefenderFell => ProvinceEconomy.Men(DefenderLosses);
	}

	/// <summary>The terms one side is fighting on: what its ground is worth, how many of the other
	/// side can reach it at once, what a volley is worth into it, and how much is left in the legs
	/// of the men who walked here.</summary>
	private readonly record struct Terms(
		float Defence, int Frontage, float Volley, float Vigour, float Exposure, float Bite,
		bool Assault);

	/// <summary>The open field in front of the town. Nothing shortens the line an attacker can bring
	/// up, so this is the fight where numbers tell — and the reason a lord who cannot win it puts
	/// his men behind stone instead.</summary>
	public static Result InTheField(Dictionary<string, int> attacker, Defenders against, bool marched,
		GameBalance balance, RandomNumberGenerator rng) =>
		Fight(attacker, against.Field, Open(against, marched, balance), balance, rng);

	/// <summary>The walls. Everything is against the man coming up them: he can only bring so many
	/// at once, he cannot bring his horses at all, and the men he is shooting at are behind stone.</summary>
	public static Result OnTheWalls(Dictionary<string, int> attacker, Defenders against, bool marched,
		GameBalance balance, RandomNumberGenerator rng) =>
		Fight(attacker, against.Castle, Walls(against, marched, balance), balance, rng);

	/// <summary>What the two sides weigh against each other before anybody moves — the attacker's
	/// weight of blows against what the defender can take. No dice: this is what a captain can see
	/// from where he is standing, and it is what the bar on the pre-battle panel is drawn from.
	///
	/// The same two figures the fight itself divides one by the other, so the bar cannot flatter a
	/// side the battle is about to ruin.</summary>
	public static (float Attacker, float Defender) Weighed(Dictionary<string, int> attacker,
		Defenders against, bool assault, bool marched, GameBalance balance)
	{
		Terms terms = assault ? Walls(against, marched, balance) : Open(against, marched, balance);
		return (Offence(attacker, terms),
			Resilience(assault ? against.Castle : against.Field, terms.Defence));
	}

	private static Terms Open(Defenders against, bool marched, GameBalance b) =>
		new(b.TownDefence * Heart(against.Loyalty, b), int.MaxValue, 1f, Legs(marched, b), 1f,
			b.BattleBite, false);

	private static Terms Walls(Defenders against, bool marched, GameBalance b)
	{
		Fortifications.Wall wall = Fortifications.Of(against.Fortification);
		return new(wall.Defence * b.TownDefence * Heart(against.Loyalty, b), wall.Frontage,
			b.AssaultVolley, Legs(marched, b), b.AssaultExposure, b.BattleBite, true);
	}

	/// <summary>A defender hitting back fights on nobody's terms but his own: his own walls do not
	/// shorten his line and he is not the one climbing anything.</summary>
	private static readonly Terms Standing = new(1f, int.MaxValue, 1f, 1f, 1f, 0f, false);

	private static float Legs(bool marched, GameBalance b) => marched ? b.MarchedOutOffence : 1f;

	/// <summary>What the county's goodwill is worth to the men holding it, above or below one.</summary>
	private static float Heart(float loyalty, GameBalance b) =>
		1f + (Mathf.Clamp(loyalty, 0f, 100f) - 50f) / 50f * b.LoyaltyDefence;

	private static Result Fight(Dictionary<string, int> attacker, Dictionary<string, int> defender,
		Terms terms, GameBalance b, RandomNumberGenerator rng)
	{
		// Copies. The rosters handed in are the real ones standing in a county, and a battle being
		// weighed on a panel that nobody has pressed Attack on yet must not kill a single man.
		var storming = new Dictionary<string, int>(attacker);
		var holding = new Dictionary<string, int>(defender);
		var stormingLost = new Dictionary<string, int>();
		var holdingLost = new Dictionary<string, int>();
		int broughtUp = ProvinceEconomy.Men(storming);
		int stoodThere = ProvinceEconomy.Men(holding);

		if (broughtUp == 0 || stoodThere == 0)
		{
			// Nobody to fight. Whoever brought men to an empty field is standing in it, and an
			// attacker with nobody left has not taken anything.
			return new Result(broughtUp > 0 && stoodThere == 0, stormingLost, holdingLost, 0);
		}

		// The day each side got. One roll apiece, here and never again — see GameBalance.BattleLuck
		// for why a die thrown every round would be no uncertainty at all.
		float ourDay = Day(rng, b);
		float theirDay = Day(rng, b);

		// The volley, before the lines meet. Both sides loose at once: the defender is shooting down
		// at men in the open, and the men in the open are shooting at whatever shows above the
		// parapet, which is what the terms of an assault say about it.
		Exchange(storming, holding, Shot(storming) * terms.Volley * ourDay, Shot(holding) * theirDay,
			terms, stormingLost, holdingLost);

		int round = 0;
		while (round < b.BattleMostRounds
			&& ProvinceEconomy.Men(storming) > 0
			&& ProvinceEconomy.Men(holding) > 0
			&& !Broken(stormingLost, broughtUp, b)
			&& !Broken(holdingLost, stoodThere, b))
		{
			Exchange(storming, holding, Offence(storming, terms) * ourDay,
				Offence(holding, Standing) * theirDay, terms, stormingLost, holdingLost);
			round++;
		}

		bool attackBroke = Broken(stormingLost, broughtUp, b) || ProvinceEconomy.Men(storming) == 0;
		bool defenceBroke = Broken(holdingLost, stoodThere, b) || ProvinceEconomy.Men(holding) == 0;

		// The ground stays with whoever was standing on it unless he was actually driven off it.
		// Both sides breaking at once, or neither breaking in the time there was, is not a victory
		// for the man who arrived — it is the evening he has to withdraw in.
		return new Result(defenceBroke && !attackBroke, stormingLost, holdingLost, round);
	}

	private static float Day(RandomNumberGenerator rng, GameBalance b) =>
		1f + rng.RandfRange(-b.BattleLuck, b.BattleLuck);

	private static void Exchange(Dictionary<string, int> storming, Dictionary<string, int> holding,
		float stormPower, float holdPower, Terms terms,
		Dictionary<string, int> stormingLost, Dictionary<string, int> holdingLost)
	{
		// Both sides' blows are weighed against the strength each had at the START of the exchange,
		// so who is written down first in this method decides nothing. Fought in sequence, the side
		// the code happened to run first would be killing men who had already been killed.
		int holdingFell = Bite(ProvinceEconomy.Men(holding), stormPower,
			Resilience(holding, terms.Defence), terms.Bite);
		int stormingFell = Bite(ProvinceEconomy.Men(storming), holdPower,
			Resilience(storming, terms.Exposure), terms.Bite);

		Take(holding, holdingFell, holdingLost);
		Take(storming, stormingFell, stormingLost);
	}

	/// <summary>How many of a side one exchange takes off: a share of what it brought, scaled by how
	/// its enemy's weight of blows compares with what it can take.</summary>
	private static int Bite(int men, float offence, float resilience, float bite)
	{
		if (men <= 0 || offence <= 0f)
		{
			return 0;
		}

		float share = offence / (offence + Mathf.Max(1f, resilience));
		return Mathf.Clamp(Mathf.RoundToInt(men * bite * share), 0, men);
	}

	/// <summary>The weight of blows a roster can land under the terms it is fighting on.</summary>
	private static float Offence(Dictionary<string, int> roster, Terms terms)
	{
		float power = 0f;
		int men = 0;
		foreach (string unit in Order(roster))
		{
			Units.Unit kind = Units.Of(unit);
			men += roster[unit];

			// A horse is worth nothing against a wall. He cannot take it up a ladder and he cannot
			// ride through a gate that is shut, and a lord who brought cavalry to a siege has
			// brought the most expensive men in his realm to stand and watch.
			power += terms.Assault && kind.Mounted ? 0f : roster[unit] * kind.Attack;
		}

		// Only so many men can come at a wall at once; the rest are behind them waiting their turn.
		// THIS is what a castle does. A multiplier can always be answered by bringing more men, and
		// a wall that could be answered that way would not be worth the stone — which is why the
		// royal castle's own blurb can say it is taken by starvation and mean it.
		float engaged = men <= terms.Frontage ? 1f : terms.Frontage / (float)men;
		return power * engaged * terms.Vigour;
	}

	/// <summary>What a roster can take, on ground worth what it is worth.</summary>
	private static float Resilience(Dictionary<string, int> roster, float ground)
	{
		float held = 0f;
		foreach (string unit in Order(roster))
		{
			held += roster[unit] * Units.Of(unit).Defence;
		}

		return held * ground;
	}

	/// <summary>What the men who can shoot are worth, before anybody is close enough to swing.</summary>
	private static float Shot(Dictionary<string, int> roster)
	{
		float power = 0f;
		foreach (string unit in Order(roster))
		{
			Units.Unit kind = Units.Of(unit);
			power += kind.Range >= ShootsBeyond ? roster[unit] * kind.Attack : 0f;
		}

		return power;
	}

	private static bool Broken(Dictionary<string, int> lost, int brought, GameBalance b) =>
		ProvinceEconomy.Men(lost) >= brought * b.BattleBreakPoint;

	/// <summary>Spreads a round's dead across the companies that took them, and writes them into the
	/// butcher's bill the caller is keeping.
	///
	/// The ill-armed fall first: a peasant in the line has a fraction of a swordsman's chance of
	/// walking off the field, so a lord who filled his ranks out with farmhands is told exactly what
	/// that was worth by who is left standing at the end of it.</summary>
	private static void Take(Dictionary<string, int> roster, int count, Dictionary<string, int> into)
	{
		if (count <= 0)
		{
			return;
		}

		List<string> order = Order(roster);
		float total = 0f;
		foreach (string unit in order)
		{
			total += Exposure(roster, unit);
		}

		int taken = 0;
		foreach (string unit in order)
		{
			int fell = Mathf.Min(roster[unit],
				Mathf.FloorToInt(count * (total <= 0f ? 0f : Exposure(roster, unit) / total)));
			taken += Fall(roster, into, unit, fell);
		}

		// What the rounding left over falls on whoever is still standing, walked in the same order
		// every time so the same battle fought twice comes out the same way.
		foreach (string unit in order)
		{
			while (taken < count && roster.GetValueOrDefault(unit) > 0)
			{
				taken += Fall(roster, into, unit, 1);
			}
		}
	}

	private static float Exposure(Dictionary<string, int> roster, string unit) =>
		roster.GetValueOrDefault(unit) / (float)Mathf.Max(1, Units.Of(unit).Defence);

	private static int Fall(Dictionary<string, int> roster, Dictionary<string, int> into, string unit,
		int men)
	{
		if (men <= 0)
		{
			return 0;
		}

		roster[unit] -= men;
		if (roster[unit] <= 0)
		{
			roster.Remove(unit);
		}

		into[unit] = into.GetValueOrDefault(unit) + men;
		return men;
	}

	/// <summary>The companies of a roster in one fixed order. Dictionary order is not promised to be
	/// the same twice, and everything in here adds floats up across companies — the same battle has
	/// to come out the same way whichever order the men happened to be written down in.</summary>
	private static List<string> Order(Dictionary<string, int> roster)
	{
		var order = new List<string>(roster.Keys);
		order.Sort(System.StringComparer.Ordinal);
		return order;
	}
}
