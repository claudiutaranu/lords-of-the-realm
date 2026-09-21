using System.Collections.Generic;
using Godot;

/// <summary>Runtime economic state for one province — everything that changes turn to
/// turn. Paired with a ProvinceDefinition (fixed identity) and the shared GameBalance
/// (global constants) by EconomySimulation. Plain data, no Godot node/Resource ties, so
/// it can be created, copied and saved freely.</summary>
public class ProvinceEconomy
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
	/// The opening value mirrors <see cref="GameBalance.FairTaxPercent"/>: a county opens on the rate
	/// it thinks is fair. Two numbers in two files that have to agree, so a check holds them
	/// together rather than a comment asking nicely.</summary>
	[System.Text.Json.Serialization.JsonConverter(typeof(SaveGame.TaxPercentJson))]
	public int Tax = OpeningTaxPercent;

	/// <summary>The rate a county opens on, which is the one the balance calls fair.</summary>
	public const int OpeningTaxPercent = 5;
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

	/// <summary>What the smithy is forging, by weapon key, and how many turns are left on it. Empty
	/// when the forge is cold. The order is paid for when it is placed, so a save carries only what
	/// is still owed.</summary>
	public string Forging = "";
	public int ForgeTurnsLeft;

	/// <summary>How many the order delivers when it finishes — carried with the order rather than
	/// looked up later, so retuning weapons.json never changes what is already on the anvil.</summary>
	public int ForgeBatch;

	/// <summary>Finished weapons the province holds, by the same key — what the yard arms its
	/// recruits out of.</summary>
	public Dictionary<string, int> Armoury = new();

	/// <summary>What the training yard is raising, and how many turns are left on the intake. Paid
	/// for when it is ordered, in people and in arms out of the armoury.</summary>

	/// <summary>The men standing in the field, by unit key. They ARE the army piece the map draws on
	/// this county — a company is here or it is on the walls, never in two places at once — and
	/// whoever holds the ground feeds them.</summary>
	public Dictionary<string, int> Garrison = new();

	/// <summary>The men held inside the walls, by the same keys. A second roster and not a flag on
	/// the first, because a lord has to be able to do both things at once: leave men on the gate and
	/// march out with the rest. With one roster he could only ever choose between them, and every
	/// castle in the realm would empty itself the moment its county went to war.
	///
	/// They do not march, ever. Whatever stands in here when an enemy reaches the seat is what he has
	/// to take the walls off — after he has beaten whatever was standing in front of them.</summary>
	public Dictionary<string, int> Castle = new();

	/// <summary>How much ground this county's men have left in them this season, in map pixels of
	/// road. Open country costs more of it per pixel than a road does, so the same budget carries an
	/// army a long way along the stone and a short way over the hills — which is the whole of what a
	/// road is for.
	///
	/// On the province and not on a separate army object because the men are the province's: one
	/// roster, one place it lives, and a save that cannot disagree with itself about where an army
	/// is standing.</summary>
	public float MarchLeft;

	/// <summary>Where on the map the men are standing, in map pixels. Zero means they have not been
	/// put anywhere yet and belong at their county's seat — which is where they are raised.</summary>
	public float ArmyX, ArmyY;

	/// <summary>The band of foreign soldiers standing in the county this season: which company they
	/// are, how many of them are still unspoken for, and how many more seasons they will wait
	/// before walking on. An empty key is the usual state — nobody is for sale.</summary>
	/// <summary>Where the lord left the labour bar, as the share of his hands he pointed at the
	/// trades — or -1 for a county whose bar he has never touched, which reads off the allocation
	/// instead so the grip starts where the county actually stands.
	///
	/// Remembered rather than derived, because the two do not map back onto each other: with more
	/// hands than work on both sides of the bar, every position from a third to two thirds produces
	/// exactly the same allocation, and a grip that re-reads it snaps back the moment it is let go.</summary>
	public float LabourSplit = -1f;

	public string MercenaryBand = "";
	public int MercenaryMen;
	public int MercenarySeasonsLeft;

	public int GrainWorkers;
	public int CattleWorkers;
	public int WoodWorkers;
	public int StoneWorkers;
	public int IronWorkers;

	/// <summary>What each of the province's fields is under, and how much heart each of them has
	/// left. Fertility runs 0..1 and moves once a year, after the harvest: down on a field that was
	/// cropped, up on one that was rested. Two cropped to one rested comes out level.</summary>
	public FieldUse[] Fields = System.Array.Empty<FieldUse>();
	public float[] Fertility = System.Array.Empty<float>();

	/// <summary>Sacks in the ground — sown in spring, thinned by a summer nobody weeded, and reaped
	/// in autumn. Held here rather than folded into Grain because a crop standing in the field is
	/// not food: it cannot be eaten, sold, or carried off by anybody but the reapers.</summary>
	public int StandingCrop;

	/// <summary>Work left before ground the water tore up is fit to sow again, in hand-seasons.
	/// While it stands above zero the field lies turned over and the men are out on it; zero is
	/// land in good heart.</summary>
	public int FieldRepair;

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

	/// <summary>The average heart of the land under grain, which is what a harvest is scaled by. A
	/// province with no grain fields has nothing to scale and reads as bare.</summary>
	public float GrainFertility()
	{
		float total = 0f;
		int under = 0;
		for (int field = 0; field < Fields.Length; field++)
		{
			if (Fields[field] == FieldUse.Grain)
			{
				total += Fertility[field];
				under++;
			}
		}

		return under == 0 ? 0f : total / under;
	}

	// Set by EconomySimulation each turn; population growth is skipped the same turn a
	// province starves (design doc section 11).
	public bool StarvedThisTurn;

	/// <summary>Seasons the Black Death has left to run here. It kills every one of them and is
	/// announced when it arrives and again when it lifts, so it is a stretch of the campaign rather
	/// than a single bad turn.</summary>
	public int PlagueSeasonsLeft;

	/// <summary>Sons taken out of the fields for the army, still fresh in the county's memory. Set
	/// when an intake is ordered, worn down a little each season, and charged against loyalty while
	/// it lasts — which is what lets the advisor say "you took too many sons" and mean it.</summary>
	public int ConscriptedRecently;

	/// <summary>The county's average goodwill in each year it has been run, oldest first. Kept on the
	/// province rather than worked out later because it cannot be worked out later: the turn it
	/// belongs to is gone. It is what the happiness table draws its bars from, and it is saved.</summary>
	public List<float> HappinessByYear = new();

	/// <summary>How many seasons each kind of news must stay quiet for, by the kind's own name. A
	/// hard winter should be one famine warning and not four identical ones.</summary>
	public Dictionary<string, int> EventQuiet = new();

	/// <summary>Men under arms in the province. They are not part of <see cref="Population"/> — they
	/// were taken out of it when they were raised — so everything that feeds, pays or counts them
	/// has to come through here.</summary>
	public int Soldiers => FieldMen + CastleMen;

	/// <summary>The men who can be marched: the field army, and not the watch on the gate.</summary>
	public int FieldMen => Men(Garrison);

	/// <summary>The men on the walls, who go nowhere.</summary>
	public int CastleMen => Men(Castle);

	/// <summary>How many men a roster comes to. Public because whoever is counting the defenders of
	/// a county that nobody holds is counting a roster that belongs to no province.</summary>
	public static int Men(Dictionary<string, int> roster)
	{
		int men = 0;
		foreach (int company in roster.Values)
		{
			men += company;
		}

		return men;
	}

	public int AllocatedWorkers =>
		GrainWorkers + CattleWorkers + WoodWorkers + StoneWorkers + IronWorkers + BuildWorkers;

	/// <summary>Who there is to work. It is the province's whole population, the way Lords of the
	/// Realm counts it: everyone not under arms can be put to something. That is what makes raising
	/// men expensive twice over — a recruit is taken out of this number when he is taken out of the
	/// fields, and he does not come back to them.</summary>
	public int Workers => Population;

	/// <summary>A copy that shares nothing with the original, so a turn can be run over it and
	/// thrown away. That is how the province's readouts are worked out: rather than a second set of
	/// formulas that says what next season will bring — and drifts from the first one the week
	/// somebody changes a yield — the season is simply played out on a copy and the difference is
	/// read off it. The projection cannot disagree with the turn, because it is the turn.</summary>
	public ProvinceEconomy Copy()
	{
		var copy = (ProvinceEconomy)MemberwiseClone();
		// A projection must not spend the realm's actual money: the clone shares every reference it
		// is given, and the purse is the one where that would be a theft rather than a reading.
		copy.Purse = new Treasury { Gold = Purse.Gold };
		copy.Fields = (FieldUse[])Fields.Clone();
		copy.Fertility = (float[])Fertility.Clone();
		copy.Armoury = new Dictionary<string, int>(Armoury);
		copy.Garrison = new Dictionary<string, int>(Garrison);
		copy.Castle = new Dictionary<string, int>(Castle);
		copy.EventQuiet = new Dictionary<string, int>(EventQuiet);
		copy.HappinessByYear = new List<float>(HappinessByYear);
		return copy;
	}

	public static ProvinceEconomy FromDefinition(ProvinceDefinition definition)
	{
		var province = new ProvinceEconomy
		{
			ProvinceName = definition.ProvinceName,
			Population = definition.InitialPopulation,
			Gold = definition.InitialGold,
			Grain = definition.InitialGrain,
			Cattle = definition.InitialCattle,
			Wood = definition.InitialWood,
			Stone = definition.InitialStone,
			Iron = definition.InitialIron,
			Fields = new FieldUse[definition.Fields],
			Fertility = new float[definition.Fields],
			Fortification = definition.InitialFortification,
		};

		// Men a lord opens with stand where they are of most use: on the walls if his seat has any,
		// and in front of the town if it has not. A castle authored with nobody in it is a castle the
		// first army over the border walks into.
		Dictionary<string, int> opening =
			province.Fortification.Length > 0 ? province.Castle : province.Garrison;
		foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> company in definition.InitialGarrison)
		{
			opening[company.Key.AsString()] = company.Value.AsInt32();
		}

		for (int field = 0; field < province.Fields.Length; field++)
		{
			province.Fields[field] =
				field < definition.InitialGrainFields ? FieldUse.Grain
				: field < definition.InitialGrainFields + definition.InitialPastureFields ? FieldUse.Pasture
				: FieldUse.Fallow;
			province.Fertility[field] = 1f;
		}

		return province;
	}
}
