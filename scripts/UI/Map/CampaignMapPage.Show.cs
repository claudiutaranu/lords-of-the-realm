using System.Collections.Generic;
using Godot;

/// <summary>Everything standing on the map drawn from the turn: walls, villages, fields, armies,
/// the bands for hire, and a company's walls asked after.</summary>
public partial class CampaignMapPage
{
	private const string MercenaryBadgePath = "res://assets/ui/icons/mercenaries.png";

	/// <summary>Everything that stands on the map, laid once the turn is known: walls, villages, armies, the
	/// ground an army may cross, the fields, the woods and the season's colours.</summary>
	private void DrawTheMap()
	{
		// After the sites, and after any save has been restored: a loaded game's walls are whatever
		// that save built, not whatever the campaign started with. The same goes for its armies.
		ShowFortifications();
		ShowSettlements();
		ShowArmies();
		Conquered(); // takes note of what the lord opens with; nothing is announced

		// The ground an army may cross, cut once: the height of the land, the ditches dug along its
		// borders and the roads drawn on it. Who holds a county is no part of it — a conquest moves a
		// frontier on the map, not a stone of the ground — so nothing ever lays it again.
		LayGround();

		// And handed to the turn, so the other lords march over the same ground by the same rules.
		var towns = new Dictionary<string, Vector2>();
		foreach (ProvinceData province in _provinces)
		{
			if (_definitionsByName.ContainsKey(province.Name))
			{
				towns[province.Name] = province.TownPosition;
			}
		}

		_turnManager.Survey((from, to) => _ground.Way(from, to, float.MaxValue), CountyNameAt, towns,
			MapDecoration.TownRing, FieldUnder, SiteUnder, _world.GroundOf, _ground.Reaches);

		// What the counties are growing — the first thing back on the ground, because a field is not
		// decoration: it is the one thing out here a lord changes from a screen and then sees from the
		// road. The generator levels the ground round every seat for them (level_seats).
		ShowFields();

		// The woods: the baked trees (tools/decimate_meshy.py), sown last of the things that grow so
		// none is planted on a field.
		_world.Sow();

		// The rest of what stands on the map comes back one piece at a time, once each looks right:
		// ShowProvinceTrade is still here and still works.

		// A save can be loaded into a season that already has men standing about for hire.
		ShowMercenaries();

		// After the sites are placed, so a loaded save's crops open in their own season rather than
		// being sown green and repainted a frame later.
		_world.SetSeason(_turnManager.CurrentSeason);
	}

	/// <summary>Draws what a province digs: its two strongest extractive industries become visible
	/// sites on its ground. Capacity times modifier is the same product the economy pays out on, so
	/// a quarry on the map means quarry income in the ledger, not decoration.
	///
	/// Grain and the herd are not in here. They are worked on fields, and the fields are drawn as
	/// fields — see <see cref="ShowFields()"/> — off what the land is actually under this season,
	/// not off a capacity it might one day use.</summary>
	private void ShowProvinceTrade(ProvinceDefinition definition)
	{
		Vector2 seat = Vector2.Zero;
		foreach (ProvinceData province in _provinces)
		{
			if (province.Name == definition.ProvinceName)
			{
				seat = province.TownPosition;
			}
		}

		var industries = new (MapDecoration.SiteKind Kind, float Weight)[]
		{
			(MapDecoration.SiteKind.Wood, definition.WoodWorkerCapacity * definition.WoodModifier),
			(MapDecoration.SiteKind.Stone, definition.StoneWorkerCapacity * definition.StoneModifier),
			(MapDecoration.SiteKind.Iron, definition.IronWorkerCapacity * definition.IronModifier),
		};

		System.Array.Sort(industries, (left, right) => right.Weight.CompareTo(left.Weight));
		for (int i = 0; i < 2; i++)
		{
			_world.AddSite(definition.ProvinceName, seat, industries[i].Kind, industries[i].Weight);
		}
	}

	/// <summary>Lays every described province's fields on its ground — what its land is under this
	/// season, plot by plot.</summary>
	private void ShowFields()
	{
		foreach (ProvinceData province in _provinces)
		{
			ShowFields(province);
		}
	}

	/// <summary>One province's fields. Yours are worked turn by turn and drawn off the economy the
	/// lord is changing; everyone else's stand as the campaign authored them, because nothing
	/// is simulating them yet. Both read the same array, so the day an unclaimed province starts
	/// taking turns the map already draws what it does with its land.
	///
	/// A province the campaign has not described has no fields to draw, the way it has no industries
	/// to show.</summary>
	private void ShowFields(ProvinceData province)
	{
		if (!_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition))
		{
			// A county the campaign has not written an economy for (a map drawn ahead of its data). Its
			// people still farm: it is drawn as an ordinary county — the definition's own defaults —
			// until the campaign writes it one. It left those seats with a village and no fields.
			definition = new ProvinceDefinition { ProvinceName = province.Name };
		}

		ProvinceEconomy economy = _turnManager.AnyProvince(province.Name);
		if (economy == null)
		{
			// Nobody is running this county's year, so it has never had a spring and its fields
			// would stand bare for ever. It plainly feeds itself, so it is drawn as a province that
			// sowed — one number, and the day the province starts taking turns its own sowing
			// replaces it.
			economy = ProvinceEconomy.FromDefinition(definition); // opens with last winter's crop standing
		}

		_world.SetFields(province.Name, province.TownPosition, economy, _turnManager.CurrentSeason);
	}

	/// <summary>Every county's village, yours and everyone else's, flying the colour of whoever holds
	/// it now. Safe to call again: a village already standing only has its banners changed.</summary>
	private void ShowSettlements()
	{
		var holders = Image.CreateEmpty(_provinces.Count, 1, false, Image.Format.Rgba8);
		var realmOrder = new List<string>(_realms.Keys);
		for (int i = 0; i < _provinces.Count; i++)
		{
			ProvinceData province = _provinces[i];
			string holder = HolderOf(province);
			Color accent = _realms[holder].Accent;
			_definitionsByName.TryGetValue(province.Name, out ProvinceDefinition definition);
			_world.AddSettlement(province.Name, province.TownPosition, SettlementFor(definition, province.IsCapital),
				accent);
			holders.SetPixel(i, 0, new Color(accent.R, accent.G, accent.B, (realmOrder.IndexOf(holder) + 1) / 255f));
			if (i < _markers.Count)
			{
				_markers[i].SetHolder(accent);
			}
		}

		// Not only the banners on its villages: a county that falls takes its new lord's colour on
		// its ring and on the minimap the same season.
		_world.ShowHolders(holders);
	}

	private BattlePanel.Colours ColoursOf(string realmKey) =>
		_realms.TryGetValue(realmKey, out RealmData realm)
			? new BattlePanel.Colours(realm.Name, realmKey, realm.Accent)
			: new BattlePanel.Colours(realmKey, realmKey, Chrome.Dim);

	/// <summary>Who holds a county now, which after a march is not who the campaign file says. The
	/// authored realm is only the opening position; the runtime one is the answer to "whose is this".</summary>
	private string HolderOf(ProvinceData province)
	{
		string realmKey = _turnManager.AnyProvince(province.Name)?.Realm ?? province.Realm;
		return _realms.ContainsKey(realmKey) ? realmKey : province.Realm;
	}

	/// <summary>How big a place stands on a province's seat: a realm's capital is a town whatever
	/// its land, and the rest are read off the hands that land can work, because land that can work
	/// more hands has more hands living on it. So the map says what a province is worth before the
	/// sidebar does.
	///
	/// The threshold sits inside the authored spread rather than on a round number — every province
	/// on this campaign holds between 230 and 260, and a round 250 would have made them all one
	/// thing, which is no map at all.
	///
	/// Walls are not part of this. A town is what the province grew; a castle is what its lord
	/// built, and that stands beside it and changes as it is built.</summary>
	private static MapDecoration.Settlement SettlementFor(ProvinceDefinition definition, bool capital)
	{
		if (capital)
		{
			return MapDecoration.Settlement.Town;
		}

		if (definition == null)
		{
			// A province this campaign has not written an economy for. A hamlet is the honest guess:
			// it says people live there without claiming a size nobody has decided on.
			return MapDecoration.Settlement.Hamlet;
		}

		int hands = definition.GrainWorkerCapacity + definition.CattleWorkerCapacity
			+ definition.WoodWorkerCapacity + definition.StoneWorkerCapacity
			+ definition.IronWorkerCapacity;

		return hands >= 245 ? MapDecoration.Settlement.Town : MapDecoration.Settlement.Hamlet;
	}

	/// <summary>Puts a banner on every county that has men standing in it, whoever holds it. An army
	/// is the one thing on this map worth seeing from across the realm, and a rival's is worth seeing
	/// most of all.</summary>
	private void ShowArmies()
	{
		var standing = new HashSet<string>();
		foreach (FieldArmy army in _turnManager.Armies())
		{
			if (army.Strength == 0)
			{
				continue;
			}

			string realm = _turnManager.RealmOf(army);
			standing.Add(army.Key);

			// Where the men actually are, which after a march is a hillside and not a market square.
			_world.SetArmy(army.Key, BannerPixel(army), true,
				_realms.TryGetValue(realm, out RealmData lord) ? lord.Accent : Colors.White);
		}

		// A company merged into another, disbanded or killed to the last man leaves a banner behind
		// otherwise, and a banner with nobody under it is one the lord will try to give orders to.
		_world.RetireArmies(standing);
	}

	/// <summary>Hangs the hire-mark over every county with a company standing in it this season.
	/// This is the whole announcement: no panel, no voice, nothing to dismiss — a lord glancing at
	/// his realm sees where there are men to be had, and goes there if he wants them.</summary>
	private void ShowMercenaries()
	{
		var mark = GD.Load<Texture2D>(MercenaryBadgePath);
		for (int index = 0; index < _provinces.Count && index < _markers.Count; index++)
		{
			// Only his own counties: nobody is offering a company to a lord who does not hold the
			// ground, and a mark over a rival's seat would be an offer he cannot take.
			ProvinceEconomy economy = _turnManager.GetProvince(_provinces[index].Name);
			_markers[index].ShowBadge(Mercenaries.Standing(economy) != null ? mark : null);
		}
	}

	private void ShowFortifications()
	{
		foreach (ProvinceData province in _provinces)
		{
			// Everyone's walls, not just the player's: a rival raising a castle is the one thing about
			// his county a lord could hardly miss from the next valley over.
			ProvinceEconomy economy = _turnManager.AnyProvince(province.Name);
			_world.SetFortification(province.Name, province.TownPosition, economy?.Fortification ?? "",
				economy?.Building ?? "",
				_realms[HolderOf(province)].Accent, economy is { CastleMen: > 0 });
		}
	}
}
