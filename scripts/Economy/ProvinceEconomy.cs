using System.Collections.Generic;
using Godot;

/// <summary>Runtime economic state for one province — everything that changes turn to
/// turn. Paired with a ProvinceDefinition (fixed identity) and the shared GameBalance
/// (global constants) by EconomySimulation. Plain data, no Godot node/Resource ties, so
/// it can be created, copied and saved freely.</summary>
public partial class ProvinceEconomy
{
	public string ProvinceName;

	/// <summary>Which realm holds it now, by the key the campaign's provinces.json uses. Runtime
	/// state and not authored identity, because holding is the one thing about a province that the
	/// campaign changes: it is what conquest will write, and it is saved, so a campaign reopens with
	/// the map as the player left it rather than as it was drawn.</summary>
	public string Realm = "";

	public int Population;
	public float Loyalty = OpeningLoyalty;

	/// <summary>What a county thinks of a lord it has no history with. Where every county opens, and
	/// what an unclaimed one's militia fights on: nobody has ever taxed them or fed them, so there is
	/// nothing else for them to be weighed against.</summary>
	public const float OpeningLoyalty = 70f;
	/// <summary>What the lord takes, as a percentage of what the county earns. A number and not one
	/// of five named steps, because the decision it stands for is "how much more can I ask for
	/// before they turn on me" — and that question has an answer a point at a time.
	///
	/// A county opens untaxed, as it does in Lords of the Realm; five is where the rate stops
	/// costing it goodwill (Livelihood.TaxTerm).</summary>
	[System.Text.Json.Serialization.JsonConverter(typeof(SaveGame.TaxPercentJson))]
	public int Tax = OpeningTaxPercent;

	public const int OpeningTaxPercent = 0;

	/// <summary>How well the county is, 0 to 100, read in five bands (Livelihood.BandOf): moved each
	/// season by what it was fed, and what decides how many of it die.</summary>
	public int Health = OpeningHealth;

	/// <summary>Good, as a county nobody has starved opens.</summary>
	public const int OpeningHealth = 75;
	[System.Text.Json.Serialization.JsonConverter(typeof(SaveGame.RationJson))]
	public RationLevel Ration = RationLevel.Normal;

	/// <summary>How much of the ration the lord wants served as meat rather than bread, out of a
	/// hundred. Bread by default, because that is what a county eats when nobody has thought about
	/// it — and a herd eaten down is a herd that stops giving milk, which is a decision and not a
	/// default.</summary>
	public int BeefShare;

	/// <summary>The realm's money, held by the realm rather than by the county. Every county a lord
	/// holds draws on and pays into the one purse: a tax collected in one of them buys a company in
	/// another, and a lord is broke everywhere at once or nowhere.
	///
	/// Stores are not like this and must not be. Grain rots where it was reaped, timber has to be
	/// carted, and a county starving beside a full granary next door is the whole reason a market
	/// exists. Money is the one thing a realm can move without moving anything.</summary>
	public Treasury Purse = new();

	/// <summary>What the realm has, reached through whichever county is being read. A window onto
	/// <see cref="Purse"/>, so that everything already written against a province's gold — the
	/// market, the yard, the wages, the tax — goes on working and quietly means the realm.</summary>
	public int Gold
	{
		get => Purse.Gold;
		set => Purse.Gold = value;
	}
	public int Grain;
	public int Cattle;
	public int Wood;
	public int Stone;
	public int Iron;

	/// <summary>How much of one store the province holds, by the same key the market and the smithy
	/// price things in. The six raw stores and the head count are fields; anything else is a rack in
	/// the armoury, which is keyed by name and open-ended by design — the smithy fills it from
	/// weapons.json and the market trades out of it. A name nobody has ever put anything under reads
	/// as empty rather than throwing.</summary>
	public int Stored(string store) => store switch
	{
		"gold" => Gold,
		"grain" => Grain,
		"cattle" => Cattle,
		"wood" => Wood,
		"stone" => Stone,
		"iron" => Iron,
		"people" => Population,
		_ => Armoury.GetValueOrDefault(store),
	};

	/// <summary>Moves one store by a signed amount — the single place a trade, a wage or a harvest
	/// reaches into the pile. It does not ask whether the move makes sense: whether a name can be
	/// traded at all is the market's to answer, before it gets here.</summary>
	public void Add(string store, int amount)
	{
		switch (store)
		{
			case "gold": Gold += amount; break;
			case "grain": Grain += amount; break;
			case "cattle": Cattle += amount; break;
			case "wood": Wood += amount; break;
			case "stone": Stone += amount; break;
			case "iron": Iron += amount; break;
			case "people": Population += amount; break;
			default: Armoury[store] = Armoury.GetValueOrDefault(store) + amount; break;
		}
	}

	/// <summary>What stands around the province, by the key fortifications.json uses. Empty for an
	/// open village. A province holds one at a time: building a new one replaces what was there,
	/// which is why nothing here counts what it replaced.</summary>
	public string Fortification = "";

	/// <summary>What the masons are raising, and how many seasons are left on it. Empty when no
	/// work is in hand. The stores were spent when the order was placed, so a save carries only
	/// what is still owed in time — and the old wall stands until the new one is finished.</summary>
	public string Building = "";
	/// <summary>What the masons still have to do, in hand-seasons, and how many hands are on it.
	/// A wall is work rather than a wait: the same rung takes a season with the county on it and
	/// half a reign with nobody, which is where a lord puts the men he has nothing else for.</summary>
	public int BuildLeft;
	public int BuildWorkers;

	public int BuildSeasonsLeft;

	/// <summary>What the smithy makes, by weapon key: one kind, every season, for as long as the lord
	/// leaves it so (Smithy). Empty when the forge is cold, and then nobody is sent to it.</summary>
	public string Forging = "";

	/// <summary>Weapons already paid for and owed to the armoury at the end of the season. Only a
	/// save from before the forge worked by the season carries any — an order it had paid for and
	/// was still waiting on — and they are delivered the first season it is played.</summary>
	public int ForgeBatch;

	/// <summary>The hands at the anvil. A trade on the labour bar like the masons, and like them
	/// wanted only while there is work: a cold forge, or one with nothing in the stores to work, asks
	/// for nobody.</summary>
	public int SmithWorkers;

	/// <summary>Finished weapons the province holds, by the same key — what the yard arms its
	/// recruits out of.</summary>
	public Dictionary<string, int> Armoury = new();

	/// <summary>What the training yard is raising, and how many turns are left on the intake. Paid
	/// for when it is ordered, in people and in arms out of the armoury.</summary>

	/// <summary>The companies this county raised and still pays, each standing wherever it was last
	/// sent. A list and not one roster, because a lord has to be able to raise a second company
	/// without it walking into the first — see <see cref="FieldArmy"/>.
	///
	/// They are the county's for pay, food and desertion however far they have marched; where they
	/// are standing is each army's own business.</summary>
	public List<FieldArmy> Armies = new();

	/// <summary>The last company number this county gave out.</summary>
	public int LastArmyId;

	/// <summary>The men held inside the walls, by the same keys. A second roster and not a flag on
	/// the first, because a lord has to be able to do both things at once: leave men on the gate and
	/// march out with the rest. With one roster he could only ever choose between them, and every
	/// castle in the realm would empty itself the moment its county went to war.
	///
	/// They do not march, ever. Whatever stands in here when an enemy reaches the seat is what he has
	/// to take the walls off — after he has beaten whatever was standing in front of them.</summary>
	public Dictionary<string, int> Castle = new();

	/// <summary>Grain put by behind the gate, for the men who will be shut in with it. It is the
	/// county's own bread, carried up before anybody needed it — which is the whole decision: a lord
	/// who waits until an army is at his border is a lord stocking a castle he is already locked out
	/// of. What the walls can hold is the rung's own figure (see <see cref="Fortifications.Wall"/>),
	/// so stone is worth quarrying twice over: it is harder to storm and it holds more bread.</summary>
	public int CastleStores;

	/// <summary>Which county's army is sitting in front of the gate, or nothing. Sitting there is
	/// the other way to take a place — the one its own blurb says the royal castle falls to — and it
	/// is a commitment rather than a click: the men have to stay, so they are holding nothing else
	/// while they do it.</summary>
	public string BesiegedFrom = "";

	/// <summary>How long they have been sitting there, and how many of those seasons the garrison
	/// has gone without. The second is what actually ends it: men hold out on an empty larder for a
	/// while and then they open the gate.</summary>
	public int SiegeSeasons;

	/// <summary>The turn the town's own militia was last beaten in the field. A beaten militia does
	/// not turn out again the same season (TurnManager.DefendersOf), which is what lets the victor go
	/// on to the walls or sit down before them.</summary>
	public int MilitiaRoutedTurn;
	public int HungrySeasons;

	/// <summary>The men the county's own granary has to feed. Everybody, until somebody shuts the
	/// gate: from then on the garrison is eating what was carried up before the siege, and counting
	/// them twice would feed a besieged castle out of the fields its besiegers are standing on.</summary>
	public int Fed => BesiegedFrom.Length > 0 ? FieldMen : Soldiers;

	/// <summary>What a save written before a county could have more than one army carries: one
	/// roster, one budget of ground and one place its men were standing. Read into the one company
	/// that file describes, so a campaign saved then opens with its army where it left it.
	///
	/// Set-only on purpose — this is how the old shape is READ, and nothing writes it again.</summary>
	[System.Text.Json.Serialization.JsonInclude]
	[System.Text.Json.Serialization.JsonIgnore(
		Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, int> Garrison
	{
		// Reads as nothing and is never written: a file in the new shape carries companies instead,
		// and the getter is here only because System.Text.Json will not fill a collection it cannot
		// also read — without it the old roster was quietly skipped and a saved army came back empty.
		get => null;
		set => Older().Men = value ?? new Dictionary<string, int>();
	}

	[System.Text.Json.Serialization.JsonInclude]
	public float MarchLeft { set => Older().MarchLeft = value; }

	[System.Text.Json.Serialization.JsonInclude]
	public float ArmyX { set => Older().X = value; }

	[System.Text.Json.Serialization.JsonInclude]
	public float ArmyY { set => Older().Y = value; }

	/// <summary>The proportions the county is dealt by (Labour): the percent of it that goes to
	/// industry — a quarter in a county nobody has divided — and, within each half, each job's part
	/// of that half in hundredths of a percent (Labour.Whole). The hands themselves are dealt from
	/// these, twice a season.</summary>
	public double IndustryShare = 25;

	public Dictionary<string, int> Shares = new();

	/// <summary>The sites the lord has shut (Labour.Sites): nobody is dealt to them.</summary>
	public List<string> Shut = new();

	/// <summary>How practised each site's people are at it, in percent (Labour.Practise). A site
	/// nobody has written down for is one the county has always worked, at its best.</summary>
	public Dictionary<string, int> Efficiency = new();

	public bool IsShut(string site) => Shut.Contains(site);

	public int EfficiencyOf(string site) => Efficiency.TryGetValue(site, out int at) ? at : 100;

	/// <summary>The band of foreign soldiers standing in the county this season: which company they
	/// are, how many of them are still unspoken for, and how many more seasons they will wait
	/// before walking on. An empty key is the usual state — nobody is for sale.</summary>
	public string MercenaryBand = "";
	public int MercenaryMen;
	public int MercenarySeasonsLeft;

	public int GrainWorkers;
	public int CattleWorkers;
	public int WoodWorkers;
	public int StoneWorkers;
	public int IronWorkers;

	/// <summary>The hands putting torn ground right (FieldRepair), a job of their own as in the
	/// original: men mending a field are not reaping one.</summary>
	public int ReclaimWorkers;

	/// <summary>What each of the province's fields is under.</summary>
	public FieldUse[] Fields = System.Array.Empty<FieldUse>();

	/// <summary>The heart of the county's land, −100 to 100, as Lords of the Realm keeps it: one
	/// figure for the whole county, fed by its fallow and spent by its grain (Husbandry.Rest), and
	/// worth half itself in percent to the crop each growing season.</summary>
	public int Soil;

	/// <summary>The crop standing in the fields, in sacks it will give — twelve to a sack sown at the
	/// end of winter, capped and grown through spring and summer, reaped in autumn. Not food until
	/// then: it cannot be eaten, sold, or carried off by anybody but the reapers.</summary>
	public int StandingCrop;

	/// <summary>How many fields were under grain when it was sown; a field lost since takes its share
	/// of the harvest.</summary>
	public int SownFields;

	/// <summary>Hand-seasons put into each waste field so far, by field (Husbandry.Reclaim): eight
	/// hundred and it is land again.</summary>
	public Dictionary<int, int> Reclaimed = new();

	/// <summary>How dry the county has been (Climate): the season's weather is read off it.</summary>
	public int Dryness = Climate.Opening;

	/// <summary>The season's weather over the county.</summary>
	public Weather Weather = Weather.Cloudy;

	/// <summary>The field this season's flood or drought ruined, or -1: a field just struck takes no
	/// order until the season has turned.</summary>
	public int Weathered = -1;

	/// <summary>Sites an enemy army has stood on, and the seasons each stays shut (Labour.Sites).</summary>
	public Dictionary<string, int> Occupied = new();

	public int FieldsUnder(FieldUse use)
	{
		int count = 0;
		foreach (FieldUse field in Fields)
		{
			if (field == use)
			{
				count++;
			}
		}

		return count;
	}

	/// <summary>Seasons the Black Death has left to run here. It kills every one of them and is
	/// announced when it arrives and again when it lifts, so it is a stretch of the campaign rather
	/// than a single bad turn.</summary>
	public int PlagueSeasonsLeft;

	/// <summary>The county's average goodwill in each year it has been run, oldest first. Kept on the
	/// province rather than worked out later because it cannot be worked out later: the turn it
	/// belongs to is gone. It is what the happiness table draws its bars from, and it is saved.</summary>
	public List<float> HappinessByYear = new();

	/// <summary>The county's people season by season, oldest first (<see cref="PeopleSeason"/>): the
	/// people table's record, saved, for the same reason as the years above.</summary>
	public List<PeopleSeason> PeopleBySeason = new();

	/// <summary>How many seasons each kind of news must stay quiet for, by the kind's own name. A
	/// hard winter should be one famine warning and not four identical ones.</summary>
	public Dictionary<string, int> EventQuiet = new();

	/// <summary>Men under arms in the province. They are not part of <see cref="Population"/> — they
	/// were taken out of it when they were raised — so everything that feeds, pays or counts them
	/// has to come through here.</summary>
	public int Soldiers => FieldMen + CastleMen;

	/// <summary>The men who can be marched: every company the county has in the field, and not the
	/// watch on the gate.</summary>
	public int FieldMen
	{
		get
		{
			int men = 0;
			foreach (FieldArmy standing in Armies)
			{
				men += standing.Strength;
			}

			return men;
		}
	}

	/// <summary>The men on the walls, who go nowhere.</summary>
	public int CastleMen => Men(Castle);

	/// <summary>How many more men the walls have room for. Open ground has room for nobody.</summary>
	public int WallRoom => Mathf.Max(0, Fortifications.Of(Fortification).Garrison - CastleMen);

	public int AllocatedWorkers => System.Linq.Enumerable.Sum(Labour.Jobs, job => Labour.Hands(this, job));

	/// <summary>Who there is to work. It is the province's whole population, the way Lords of the
	/// Realm counts it: everyone not under arms can be put to something. That is what makes raising
	/// men expensive twice over — a recruit is taken out of this number when he is taken out of the
	/// fields, and he does not come back to them.</summary>
	public int Workers => Population;
}
