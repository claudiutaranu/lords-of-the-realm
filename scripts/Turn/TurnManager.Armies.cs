using System.Collections.Generic;
using Godot;

/// <summary>The companies on the map: marching them, joining two, setting men on the walls, finding
/// one by its key, and a county claimed by walking into it.</summary>
public partial class TurnManager
{
	/// <summary>The player's province of that name, or null — which is what every screen that asks
	/// this actually means: a room that is not his to walk into, a ledger that is not his to read. A
	/// rival's county is in here too, and is deliberately not what comes back.</summary>
	/// <summary>Sends a county's men across country to a point on the map.
	///
	/// The WAY is the map's business and the LEDGER is this one's. Which cells are passable, what a
	/// road saves and how far a budget stretches are questions about ground, and the ground is drawn
	/// in images this class has never seen; so the map works the road out and comes here with a
	/// destination and a price. What cannot be delegated is asked here: that there are men to send,
	/// and that they can afford the walk.
	///
	/// A march is a march and not a conquest. Crossing into another lord's county puts the men on his
	/// ground and nothing more — see <see cref="Claim"/> for the only thing that moves a border, and
	/// <see cref="DefendersOf"/> for what has to be beaten first.</summary>
	public bool March(FieldArmy army, string toCounty, Vector2 at, float cost)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		if (here == null || army.Strength == 0 || cost <= 0f || cost > army.MarchLeft)
		{
			return false;
		}

		// Men who are marching are not sitting in front of anybody's gate. A siege is the army being
		// THERE, so the moment it is somewhere else there is no siege — no order to cancel and no way
		// to forget to.
		Lift(army);

		// Wherever they were sent, they are standing there now and the season is that much shorter.
		// Everything below is about whether anything CHANGED HANDS by their standing there, which is
		// a different question and mostly answered no.
		army.X = at.X;
		army.Y = at.Y;
		army.County = toCounty;
		army.MarchLeft -= cost;

		ProvinceEconomy there = _provincesByName.GetValueOrDefault(toCounty);

		// Their own county, another of the same lord's, or a rival's they are only crossing. Walking
		// over a county has never taken it and does not take it now: that is settled at the seat,
		// against whoever is standing on it. Two of a lord's own companies standing in the same field
		// stay two companies — putting them under one banner is an order he gives (see
		// <see cref="Merge"/>), not something the ground does to them.
		// Ground the map draws and no economy describes is only ground: walked across, never taken.
		// Asking to claim it failed, and a march reported refused after the men had already moved was
		// a banner the map would not walk to a place the ledger had put it.
		if (there != null || toCounty == army.Home || !CanBeTaken(toCounty))
		{
			return true;
		}

		// Nobody holds it. If its own people have taken up what hangs in the barn, they have to be
		// beaten before anything changes hands, and the march simply ends on their ground. An empty
		// county is walked into, the way it always was — and the march stands either way: the men
		// are there.
		if (DefendersOf(toCounty).Men == 0)
		{
			Claim(army, toCounty, at);
		}

		return true;
	}

	/// <summary>Puts one company's men under another's banner, where the lord wants one army instead
	/// of two standing in the same field. What the season has left is the slower of the two: a
	/// company does not get its legs back by falling in with men who have walked less far.</summary>
	public bool Merge(FieldArmy into, FieldArmy from)
	{
		if (into == null || from == null || into == from || into.County != from.County
			|| RealmOf(into) != RealmOf(from) || into.KeepsItsBanner || from.KeepsItsBanner)
		{
			return false;
		}

		foreach ((string unit, int men) in from.Men)
		{
			into.Men[unit] = into.Men.GetValueOrDefault(unit) + men;
		}

		into.MarchLeft = Mathf.Min(into.MarchLeft, from.MarchLeft);
		Lift(from);
		_provincesByName.GetValueOrDefault(from.Home)?.Disband(from);
		return true;
	}

	/// <summary>Sends men off a company up onto the walls of the county it is standing in — one of
	/// its own lord's, not besieged, and with room: whoever the walls will not hold stays in the field.
	/// A company that goes up entire is gone from the map. Any of the lord's castles will take them,
	/// not only the county that raised them, and from then on it is that county that feeds and pays
	/// them, the way it does the rest of its watch. Returns how many went up.</summary>
	public int Garrison(FieldArmy army, Dictionary<string, int> going)
	{
		ProvinceEconomy walls = army == null ? null : _provincesByName.GetValueOrDefault(army.County);
		if (walls == null || walls.Realm != RealmOf(army) || walls.BesiegedFrom.Length > 0)
		{
			return 0;
		}

		int room = walls.WallRoom;
		int up = 0;
		foreach (string unit in Units.All())
		{
			int men = Mathf.Min(room - up, Mathf.Min(going.GetValueOrDefault(unit), army.Men.GetValueOrDefault(unit)));
			if (men <= 0)
			{
				continue;
			}

			army.Men[unit] -= men;
			if (army.Men[unit] == 0)
			{
				army.Men.Remove(unit);
			}

			walls.Castle[unit] = walls.Castle.GetValueOrDefault(unit) + men;
			up += men;
		}

		if (army.Strength == 0)
		{
			Lift(army);
			_provincesByName.GetValueOrDefault(army.Home)?.Disband(army);
		}

		return up;
	}

	/// <summary>Whose men these are: the realm of the county that raised them, wherever they have
	/// marched to since.</summary>
	public string RealmOf(FieldArmy army) =>
		army == null ? "" : _provincesByName.GetValueOrDefault(army.Home)?.Realm ?? "";

	/// <summary>The company that key names, or null when it has been wiped out, disbanded or merged
	/// away. Screens hold armies by key across a turn, and a turn can take one off the board.</summary>
	public FieldArmy ArmyOf(string key)
	{
		int mark = key?.LastIndexOf('#') ?? -1;
		if (mark <= 0 || !int.TryParse(key[(mark + 1)..], out int id))
		{
			return null;
		}

		return _provincesByName.GetValueOrDefault(key[..mark])?.Army(id);
	}

	/// <summary>Every company standing on the board, whoever raised it. The map draws off this: one
	/// banner per army, wherever the season has left it.</summary>
	public List<FieldArmy> Armies()
	{
		var standing = new List<FieldArmy>();
		foreach (ProvinceDefinition definition in _definitions)
		{
			standing.AddRange(_provincesByName[definition.ProvinceName].Armies);
		}

		return standing;
	}

	/// <summary>The companies of the realm that holds a county, standing in that county. Who would
	/// have to be beaten to take it, and who a lord's men find waiting when they get there.</summary>
	private List<FieldArmy> StandingIn(string county)
	{
		string realm = _provincesByName.GetValueOrDefault(county)?.Realm ?? "";
		var there = new List<FieldArmy>();
		foreach (FieldArmy army in Armies())
		{
			if (army.County == county && army.Strength > 0 && RealmOf(army) == realm)
			{
				there.Add(army);
			}
		}

		return there;
	}

	/// <summary>Puts every company the holder has in a county under one banner — every one but a
	/// hired band, which answers to its own captain and keeps its own (Merge) — because a battle for
	/// a county is one battle: men caught in the same field fight it together or they are beaten in
	/// detail by the same enemy on the same afternoon. Called before anything that can kill them, so
	/// what the fighting takes off comes off men who are really there.</summary>
	private FieldArmy Rally(string county)
	{
		List<FieldArmy> there = StandingIn(county);
		if (there.Count == 0)
		{
			return null;
		}

		for (int index = there.Count - 1; index > 0; index--)
		{
			Merge(there[0], there[index]);
		}

		return there[0];
	}

	/// <summary>Hands a county to the realm whose men are standing on its seat, with those men on it.
	/// Called when there is nobody left in the way — either because there never was anybody, or
	/// because the battle for it has just been settled.
	///
	/// The county keeps everything but its lord: its stores, its fields, its walls and its people are
	/// exactly what they were the moment before, because they are the reason anybody wanted it. What
	/// it loses is whoever was holding it, and whatever they still had standing.</summary>
	public bool Claim(FieldArmy army, string toCounty, Vector2 at)
	{
		ProvinceEconomy here = army == null ? null : _provincesByName.GetValueOrDefault(army.Home);
		if (here == null || toCounty == army.Home)
		{
			return false;
		}

		ProvinceEconomy there = _provincesByName.GetValueOrDefault(toCounty);
		if (there == null)
		{
			// Until somebody's army stands on the ground, an unclaimed county is not in the turn at
			// all — this is where it joins it.
			ProvinceDefinition taken = _unheld.GetValueOrDefault(toCounty);
			if (taken == null)
			{
				return false; // no land described there: nothing to walk into
			}

			there = ProvinceEconomy.FromDefinition(taken);
			Labour.Deal(there, taken, _balance, CurrentSeason);
			_definitions.Add(taken);
			_provincesByName[toCounty] = there;
			_unheld.Remove(toCounty);

			// What was in its coffers falls with it, into the one purse the taking realm keeps.
			// Otherwise the ground taken would sit on money the crown could see and never spend.
			here.Purse.Gold += there.Gold;
		}

		// A county taken is a county nobody is besieging any more, whichever way it fell.
		there.EndSiege();

		// Whoever was holding it is not holding it any more, and neither are the men who were
		// standing in it: they are dead, scattered or walked off by the time anybody is claiming
		// anything.
		//
		// The companies it raised that are somewhere ELSE are a different matter. They are still an
		// army in the field, three counties away, and nothing has happened to them today — so they
		// pass to another county of their own lord, which pays and feeds them from now on. Only a
		// lord with nothing left at all loses them with his last seat.
		ProvinceEconomy refuge = Refuge(there.Realm, toCounty);
		for (int index = there.Armies.Count - 1; index >= 0; index--)
		{
			FieldArmy company = there.Armies[index];
			there.Armies.RemoveAt(index);
			if (refuge != null && company.County != toCounty && company.Strength > 0)
			{
				refuge.Adopt(company);
			}
		}

		there.Castle.Clear();
		there.Realm = here.Realm;
		there.Purse = here.Purse;

		// Nobody is glad to be conquered. A county taken has to be held before it is worth having,
		// which is what stops a lord taking everything he can walk to.
		there.Loyalty = Mathf.Max(0f, there.Loyalty - _balance.ConquestResentment);

		// The men who took it are standing on it, and they are still the company that walked in:
		// their own county pays and feeds them however far they have got. What they hold, they hold
		// by being there.
		army.County = toCounty;
		army.X = at.X;
		army.Y = at.Y;
		return true;
	}

	/// <summary>Another county of the same realm, to take in the companies of one that has just
	/// fallen. Null where the lord has none left — which is the end of him.</summary>
	private ProvinceEconomy Refuge(string realm, string lost)
	{
		foreach (ProvinceDefinition definition in _definitions)
		{
			ProvinceEconomy county = _provincesByName[definition.ProvinceName];
			if (county.Realm == realm && county.ProvinceName != lost)
			{
				return county;
			}
		}

		return null;
	}
}
