using System.Collections.Generic;
using Godot;

/// <summary>The rival at war: the companies he musters, the walls he raises and mans, and the
/// counties he takes.</summary>
public partial class LordCheck
{
	/// <summary>A rival raises his own men: a company out of what is in his armoury, more of them the
	/// harder he is, none from a county that has turned on him, and the surplus sent home when he has
	/// more out than his county carries.</summary>
	private void TheMuster(GameBalance b)
	{
		ProvinceDefinition def = Definition("Valmere");
		ProvinceEconomy county = County(def);
		county.Loyalty = 60f;
		county.Armoury["spear"] = 100;
		LordArms.Arm(county, b, Difficulty.Medium, _ => "north");
		Is("a lord with arms in the armoury raises a company", county.Armies.Count, 1);
		Is("  out of his own people", county.Population, def.InitialPopulation - county.FieldMen);
		Is("  and not all of them in one season",
			county.FieldMen <= Mathf.CeilToInt(def.InitialPopulation * b.LordLevyShare), true);

		Is("a hard lord keeps more men in the field than an easy one",
			LordArms.Room(County(def), b, Difficulty.Hard) > LordArms.Room(County(def), b, Difficulty.Easy), true);

		// His treasury pays for men as well as his taxes: a lord on a full chest keeps a bigger army.
		// Where wages and not his people are the limit — a lord allowed his whole county under arms.
		var unbounded = new GameBalance { LordArmyShare = new[] { 1f, 1f, 1f } };
		ProvinceEconomy poor = County(def);
		poor.Gold = 0;
		poor.Tax = 0;
		ProvinceEconomy rich = County(def);
		rich.Gold = 20000;
		rich.Tax = 0;
		Is("a full treasury pays for more men than the taxes alone",
			LordArms.Room(rich, unbounded, Difficulty.Hard) > LordArms.Room(poor, unbounded, Difficulty.Hard), true);

		// And he puts it into arms at market when his smithy has none to hand.
		ProvinceEconomy buyer = County(def);
		buyer.Loyalty = 60f;
		buyer.Gold = 20000;
		buyer.Wood = 0;
		buyer.Iron = 0;
		LordArms.Arm(buyer, b, Difficulty.Hard, _ => "north", Counter(b, Season.Summer));
		Is("a lord with gold and no smithy stores buys spears at market",
			buyer.Armoury.GetValueOrDefault("spear") > 0, true);
		Is("  and pays for them", buyer.Gold < 20000, true);

		// A band standing in his county is taken whole when the war chest will pay for it.
		ProvinceEconomy hiring = County(def);
		hiring.Loyalty = 60f;
		hiring.Gold = 20000;
		Mercenaries.Arrive(hiring, Mercenaries.Find("scottish"), 3);
		LordArms.Arm(hiring, b, Difficulty.Hard, _ => "north", Counter(b, Season.Summer));
		Is("a lord with the gold hires the band in his county", hiring.MercenaryMen, 0);
		Is("  and they stand in his field", hiring.FieldMen >= Mercenaries.Find("scottish").Men, true);
		Is("  under their own name, not as his spearmen",
			hiring.Armies.Exists(army => army.Men.GetValueOrDefault("scottish") == Mercenaries.Find("scottish").Men), true);
		Is("  a kind of their own, on the band's own bars", Units.Of("scottish").Name, "Scottish Pikemen");
		Is("  and known for hired men", Units.IsHired("scottish") && !Units.IsHired("spear"), true);

		ProvinceEconomy sullen = County(def);
		sullen.Loyalty = b.LordLevyAbove - 1f;
		sullen.Armoury["spear"] = 100;
		LordArms.Arm(sullen, b, Difficulty.Hard, _ => "north");
		Is("a county that has turned on its lord gives him no sons", sullen.Armies.Count, 0);

		ProvinceEconomy swollen = County(def);
		swollen.Realm = "north";
		swollen.Gold = 50000; // so it is the share of his people, not his purse, that sends them home
		FieldArmy host = swollen.Raise(b.MarchReach);
		host.Men["spear"] = 400;
		int people = swollen.Population;
		LordArms.Arm(swollen, b, Difficulty.Medium, _ => "north");
		Is("more out than the county carries, and the surplus goes home",
			swollen.FieldMen, Mathf.FloorToInt(people * b.LordArmyShare[(int)Difficulty.Medium]));
		Is("  back to the fields it came from", swollen.Population, people + 400 - swollen.FieldMen);

		ProvinceEconomy away = County(def);
		away.Realm = "north";
		FieldArmy campaign = away.Raise(b.MarchReach);
		campaign.Men["spear"] = 400;
		campaign.County = "Southmoor";
		LordArms.Arm(away, b, Difficulty.Medium, county => county == "Southmoor" ? "crown" : "north");
		Is("  but never from men on campaign", away.FieldMen, 400);
	}

	/// <summary>A lord builds up his walls a rung at a time out of his own stores, no higher than the
	/// campaign lets him, holds back the timber for the next rung instead of selling it, and puts
	/// men on a wall once it stands.</summary>
	private void TheWalls()
	{
		var b = new GameBalance { LordBuildChance = new[] { 1f, 1f, 1f } };
		ProvinceDefinition def = Definition("Valmere");
		var dice = new RandomNumberGenerator { Seed = 1268 };

		ProvinceEconomy rich = County(def);
		rich.Wood = 1000;
		rich.Stone = 200;
		LordAI.TakeTurn(rich, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		Is("a lord with the timber orders the first palisade", rich.Building, "small-palisade");
		Is("  and pays for it on the order", rich.Wood <= 1000 - 200, true);

		ProvinceEconomy topped = County(def);
		topped.Fortification = "medium-fort";
		topped.Wood = 5000;
		topped.Stone = 5000;
		topped.Iron = 5000;
		LordAI.TakeTurn(topped, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Hard, dice, "medium-fort");
		Is("  but builds no higher than the campaign lets him", topped.Building, "");

		ProvinceEconomy unasked = County(def);
		unasked.Wood = 1000;
		LordAI.TakeTurn(unasked, def, b, Counter(b, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("  and without the dice to decide the day, never builds at all", unasked.Building, "");

		// A town with no wall at all does not wait on the dice: the palisade goes up the first season
		// the timber is there — which is what a county he has just taken looks like.
		var never = new GameBalance { LordBuildChance = new[] { 0f, 0f, 0f } };
		ProvinceEconomy taken = County(def);
		taken.Wood = 300;
		LordAI.TakeTurn(taken, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		Is("an open town is palisaded the season it has the timber", taken.Building, "small-palisade");

		ProvinceEconomy bare = County(def);
		bare.Wood = 0;
		bare.Gold = 3000;
		LordAI.TakeTurn(bare, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		Is("  and one without the timber buys it, if the purse runs to it", bare.Building, "small-palisade");

		// Saving for the next rung: the timber a motte wants stays in the yard rather than going to
		// market while the stone for it is still to come.
		ProvinceEconomy saving = County(def);
		saving.Fortification = "small-palisade";
		saving.Wood = 450;
		LordAI.TakeTurn(saving, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium, dice, "medium-fort");
		ProvinceEconomy selling = County(def);
		selling.Fortification = "small-palisade";
		selling.Wood = 450;
		LordAI.TakeTurn(selling, def, never, Counter(never, Season.Summer), Season.Summer, Difficulty.Medium);
		Is("a lord saving for a motte keeps the timber for it", saving.Wood > selling.Wood, true);

		ProvinceEconomy manned = County(def);
		manned.Fortification = "small-palisade";
		manned.Raise(0f).Men["spear"] = 100;
		LordWalls.ManTheWalls(manned, b);
		int watch = Mathf.FloorToInt(Fortifications.Of("small-palisade").Garrison * b.LordWatchShare);
		Is("a wall that stands gets its share of what it holds", manned.CastleMen, watch);
		Is("  out of the company at home", manned.FieldMen, 100 - watch);

		ProvinceEconomy shut = County(def);
		shut.Fortification = "small-palisade";
		shut.BesiegedFrom = "Kingsreach#1";
		shut.Raise(0f).Men["spear"] = 100;
		LordWalls.ManTheWalls(shut, b);
		Is("  but nobody goes up through a gate an army is sitting before", shut.CastleMen, 0);
	}

	/// <summary>The rival marches, over ground handed to him the way the map hands it over, and takes
	/// what his difficulty lets him want. A straight road here instead of the map's: three villages
	/// in a row, a neutral one between his and the player's.</summary>
	private void TheirWar()
	{
		(TurnManager turns, FieldArmy host) Board(Difficulty skill, bool surveyed, int menInHost, int settles = 0,
			bool raids = false)
		{
			var b = new GameBalance { WorldEventChance = 0f, MercenaryChance = 0f };
			b.LordFirstMarch = new[] { 0, 0, 0 };
			b.LordSettles = new[] { settles, settles, settles };
			var definitions = new List<ProvinceDefinition> { Definition("North"), Definition("South") };
			var middle = Definition("Middle");
			middle.InitialPopulation = 300; // a militia of forty-odd farmhands
			var realms = new Dictionary<string, string> { ["North"] = "north", ["South"] = "crown" };
			var board = new TurnManager(b, definitions, realms, "crown", skill, new List<ProvinceDefinition> { middle });
			var towns = new Dictionary<string, Vector2>
			{
				["North"] = new(100, 100),
				["Middle"] = new(300, 100),
				["South"] = new(500, 100),
			};
			if (surveyed)
			{
				// The player's one field lies forty paces off his town, which is where a raid goes.
				var southField = new Vector2(500, 140);
				board.Survey(Straight, pixel => pixel.X < 200 ? "North" : pixel.X < 400 ? "Middle" : "South",
					towns, 38f,
					raids ? pixel => pixel.DistanceTo(southField) < 15f ? ("South", 0) : ("", -1) : null,
					null,
					raids ? county => county == "South" ? new List<Vector2> { southField } : new List<Vector2>() : null);
			}

			// A chest that pays for the host, so it is his orders being tested and not his wages.
			board.AnyProvince("North").Gold = 20000;
			FieldArmy army = board.AnyProvince("North").Raise(b.MarchReach);
			army.Men["spear"] = menInHost;
			return (board, army);
		}

		(TurnManager war, FieldArmy _) = Board(Difficulty.Medium, surveyed: true, menInHost: 200);
		for (int season = 0; season < 3; season++)
		{
			war.AdvanceTurn();
		}

		Is("a lord marches on the empty country next to him and takes it", war.AnyProvince("Middle")?.Realm, "north");

		(TurnManager mapless, FieldArmy idle) = Board(Difficulty.Medium, surveyed: false, menInHost: 200);
		mapless.AdvanceTurn();
		Is("  and with no ground handed to him, nobody marches", idle.County, "North");

		(TurnManager gentle, FieldArmy _) = Board(Difficulty.Easy, surveyed: true, menInHost: 400);
		for (int season = 0; season < 8; season++)
		{
			gentle.AdvanceTurn();
		}

		Is("an easy lord takes the neutral county", gentle.AnyProvince("Middle")?.Realm, "north");
		Is("  and leaves the player's alone", gentle.AnyProvince("South")?.Realm, "crown");

		(TurnManager hard, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		Is("a lord with a county to his name has not fallen", hard.PlayerFallen, false);
		bool told = false;
		for (int season = 0; season < 8 && hard.AnyProvince("South")?.Realm == "crown"; season++)
		{
			hard.AdvanceTurn();
			told |= hard.News.Exists(item => item.Said.Id == "county-lost" && item.ProvinceName == "South");
		}

		Is("a hard lord comes for the player's county and takes it", hard.AnyProvince("South")?.Realm, "north");
		Is("  and the player is told", told, true);
		Is("  and with his only county gone, his reign is over", hard.PlayerFallen, true);

		// Diplomacy is what he marches by: sworn to the player he leaves him be, however hard a lord he
		// is; at war with him, even an easy lord comes for him.
		(TurnManager sworn, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		sworn.Diplomacy.Allies["north"] = "crown";
		sworn.Diplomacy.Allies["crown"] = "north";
		for (int season = 0; season < 8; season++)
		{
			sworn.AdvanceTurn();
		}

		Is("a lord sworn to the player takes the empty country", sworn.AnyProvince("Middle")?.Realm, "north");
		Is("  and leaves his ally's county alone", sworn.AnyProvince("South")?.Realm, "crown");

		(TurnManager feud, FieldArmy _) = Board(Difficulty.Easy, surveyed: true, menInHost: 400);
		feud.Diplomacy.Wars.Add(Diplomacy.Pair("north", "crown"));
		for (int season = 0; season < 10 && feud.AnyProvince("South")?.Realm == "crown"; season++)
		{
			feud.AdvanceTurn();
		}

		Is("an easy lord at war with the player comes for him", feud.AnyProvince("South")?.Realm, "north");

		// Manned walls are not stormed off the march, his any more than the player's: he sits down
		// and builds his engines first.
		(TurnManager walled, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		ProvinceEconomy gate = walled.AnyProvince("South");
		gate.Fortification = "small-palisade";
		gate.Castle["spear"] = 40;
		gate.CastleStores = 100_000;
		for (int season = 0; season < 10 && gate.BesiegedFrom.Length == 0 && gate.Realm == "crown"; season++)
		{
			walled.AdvanceTurn();
		}

		Is("a lord at the player's manned walls sits down before them", gate.BesiegedFrom.Length > 0, true);
		Is("  and has not stormed them off the march", gate.Realm, "crown");
		Is("  and builds his engines", walled.SiegeSeasonsLeft("South") > 0, true);

		// A lord who has just taken a county settles it before he marches on the next — and the
		// player hears of it, because the race for the country is his too.
		(TurnManager settling, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400, settles: 20);
		bool heard = false;
		for (int season = 0; season < 8; season++)
		{
			settling.AdvanceTurn();
			heard |= settling.News.Exists(item => item.Said.Id == "rival-took" && item.ProvinceName == "Middle");
		}

		Is("a lord takes the empty county", settling.AnyProvince("Middle")?.Realm, "north");
		Is("  and the player hears his rival has grown", heard, true);
		Is("  and he settles it before he comes for the player", settling.AnyProvince("South")?.Realm, "crown");

		// A lord sends a raid of peasants over the player's land — the original's step 10 — which
		// treads his corn, and comes home when its seasons are up.
		(TurnManager raided, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 10, settles: 100, raids: true);
		ProvinceEconomy south = raided.AnyProvince("South");
		south.Fields[0] = FieldUse.Grain;
		south.StandingCrop = 100 * south.FieldsUnder(FieldUse.Grain);
		int standing = south.StandingCrop;
		raided.AdvanceTurn();
		FieldArmy raid = raided.Armies().Find(army => army.Raider);
		// Two counties off is more than a season's walk: it is on his land by the next.
		bool trodden = raided.News.Exists(item => item.Said.Id == "fields-trampled");
		raided.AdvanceTurn();
		trodden |= raided.News.Exists(item => item.Said.Id == "fields-trampled");
		Is("a lord sends a raid over the player's land", raid?.Men.GetValueOrDefault("peasant"), new GameBalance().LordRaidMen);
		Is("  under its own banner", raided.AnyProvince("North").Armies.FindAll(army => army.Raider).Count, 1);
		Is("  and it treads his corn", south.Fields[0] == FieldUse.Waste || south.StandingCrop < standing || trodden, true);
		Is("  and he is told", trodden, true);
		for (int season = 0; season < new GameBalance().LordRaidSeasons + 3; season++)
		{
			raided.AdvanceTurn();
		}

		Is("  and it goes home when its seasons are up", raided.Armies().Contains(raid), false);

		// And the other way a reign ends: every county in revolt and not a man under arms.
		ProvinceEconomy risen = hard.Provinces.Find(county => county.Realm != "crown");
		string was = risen.Realm;
		risen.Realm = "crown";
		risen.Loyalty = 0f;
		risen.Castle.Clear();
		risen.Armies.Clear();
		Is("  or with every county risen and no army left", hard.PlayerFallen, true);
		risen.Castle["spear"] = 10;
		Is("  though ten men on a wall still hold it", hard.PlayerFallen, false);
		risen.Realm = was;

		// Defended this time, by thirty spears of his own standing at the gate: he is told what came,
		// what stood, and what it cost him, by kind — and no blank left in the line.
		(TurnManager held, FieldArmy _) = Board(Difficulty.Hard, surveyed: true, menInHost: 400);
		held.AnyProvince("South").Raise(0f).Men["spear"] = 30;
		string report = "";
		for (int season = 0; season < 8 && report.Length == 0; season++)
		{
			held.AdvanceTurn();
			FiredEvent said = held.News.Find(item => item.ProvinceName == "South"
				&& (item.Said.Id == "county-lost" || item.Said.Id == "invaders-repelled" || item.Said.Id == "field-lost"));
			report = said?.Said.Text ?? "";
		}

		Is("the player hears how the fight at his gate went", report.Length > 0, true);
		Is("  with every number written in", report.Contains('{'), false);
		Is("  how many of his stood", report.Contains("our 30 "), true);
		Is("  and what he lost, by kind", report.Contains("spearmen"), true);
	}
}
