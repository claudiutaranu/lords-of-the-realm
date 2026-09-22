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

	/// <summary>The companies this county raised and still pays, each standing wherever it was last
	/// sent. A list and not one roster, because a lord has to be able to raise a second company
	/// without it walking into the first — see <see cref="FieldArmy"/>.
	///
	/// They are the county's for pay, food and desertion however far they have marched; where they
	/// are standing is each army's own business.</summary>
	public List<FieldArmy> Armies = new();

	/// <summary>Musters a new company at the county's seat, with the ground the caller says it has
	/// in its legs. Ids are never reused: an order given to a banner cannot land on a different
	/// company later.</summary>
	public FieldArmy Raise(float marchLeft = 0f)
	{
		var raised = new FieldArmy
		{
			Home = ProvinceName,
			Id = NextId(),
			County = ProvinceName,
			MarchLeft = marchLeft,
		};

		Armies.Add(raised);
		return raised;
	}

	/// <summary>Takes another county's company onto this county's books — what happens to a company
	/// in the field when the county that raised it falls: somebody else has to pay and feed it. It
	/// keeps its men and its legs and takes a place in its new county's line.</summary>
	public void Adopt(FieldArmy company)
	{
		company.Home = ProvinceName;
		company.Id = NextId();
		Armies.Add(company);
	}

	/// <summary>The next number this county has not used. Never reused, so an order given to a
	/// banner cannot land on a different company later.</summary>
	private int NextId()
	{
		int id = 1;
		foreach (FieldArmy standing in Armies)
		{
			id = standing.Id >= id ? standing.Id + 1 : id;
		}

		return id;
	}

	/// <summary>The army of that id, or null. Saves and screens hold on to armies by name and id
	/// rather than by reference, because the object can be gone by the time they ask again.</summary>
	public FieldArmy Army(int id) => Armies.Find(standing => standing.Id == id);

	/// <summary>Takes an army off the county — wiped out, disbanded, or merged into another.</summary>
	public void Disband(FieldArmy army) => Armies.Remove(army);

	/// <summary>Cuts a company in two: the men named in <paramref name="taken"/> fall in under a new
	/// banner on the same ground, with the same legs left under it, and the rest stay where they
	/// were. Null when that would leave a banner standing over nobody — neither side of a split may
	/// be empty, and a company that walks off entire has been renamed rather than cut.
	///
	/// What is asked for is clamped to what is there rather than trusted: a screen is a screen, and
	/// the roster is what actually decides how many of a kind there are to give away.
	///
	/// Where the two halves then go is the map's business. What is settled here is only who is
	/// whose, because that is the part a save has to carry.</summary>
	public FieldArmy Split(FieldArmy army, Dictionary<string, int> taken)
	{
		if (!Armies.Contains(army))
		{
			return null;
		}

		var goes = new Dictionary<string, int>();
		int marching = 0;
		foreach ((string kind, int asked) in taken)
		{
			int men = Mathf.Clamp(asked, 0, army.Men.GetValueOrDefault(kind));
			if (men > 0)
			{
				goes[kind] = men;
				marching += men;
			}
		}

		if (marching == 0 || marching == army.Strength)
		{
			return null;
		}

		FieldArmy half = Raise(army.MarchLeft);
		half.County = army.County;
		half.X = army.X;
		half.Y = army.Y;
		foreach ((string kind, int men) in goes)
		{
			half.Men[kind] = men;
			// A kind nobody is left carrying is gone from the roster rather than left at nought.
			if ((army.Men[kind] -= men) == 0)
			{
				army.Men.Remove(kind);
			}
		}

		return half;
	}

	/// <summary>Drops every company with nobody left in it. Called after anything that can kill men,
	/// so a banner is never left standing over an empty field.</summary>
	public void Bury()
	{
		for (int index = Armies.Count - 1; index >= 0; index--)
		{
			if (Armies[index].Strength == 0)
			{
				Armies.RemoveAt(index);
			}
		}
	}

	/// <summary>How many of a kind the county has standing in the field, whichever of its armies
	/// they are in.</summary>
	public int Mustered(string unit)
	{
		int men = 0;
		foreach (FieldArmy standing in Armies)
		{
			men += standing.Men.GetValueOrDefault(unit);
		}

		return men;
	}

	/// <summary>The company a county-wide order falls to: the biggest one it still has ground for,
	/// or null when every man it has is spent or on the walls. The sidebar's March is a county's
	/// button rather than an army's, and this is what it means by "the army".</summary>
	public FieldArmy Readiest()
	{
		FieldArmy best = null;
		foreach (FieldArmy standing in Armies)
		{
			if (standing.MarchLeft > 0f && standing.Strength > 0
				&& (best == null || standing.Strength > best.Strength))
			{
				best = standing;
			}
		}

		return best;
	}

	/// <summary>Every kind of soldier the county has anywhere — standing in the field with one of
	/// its companies, or on the gate. What the walls are manned off.</summary>
	public List<string> Companies()
	{
		var kinds = new List<string>();
		foreach (FieldArmy standing in Armies)
		{
			foreach (string unit in standing.Men.Keys)
			{
				if (!kinds.Contains(unit))
				{
					kinds.Add(unit);
				}
			}
		}

		foreach (string unit in Castle.Keys)
		{
			if (!kinds.Contains(unit))
			{
				kinds.Add(unit);
			}
		}

		return kinds;
	}

	/// <summary>Sets how many of a kind stand in the field — what the walls take and give back.
	/// Men coming down off the gate fall in with the company at the seat, and men going up are taken
	/// off the companies raised last, so a lord manning his walls empties his newest levy before he
	/// touches the army he has standing in the field.</summary>
	public void Muster(string unit, int men, float marchLeft = 0f)
	{
		for (int index = Armies.Count - 1; index >= 0 && Mustered(unit) > men; index--)
		{
			int has = Armies[index].Men.GetValueOrDefault(unit);
			int keeps = Mathf.Max(0, has - (Mustered(unit) - men));
			if (keeps > 0)
			{
				Armies[index].Men[unit] = keeps;
			}
			else
			{
				Armies[index].Men.Remove(unit);
			}
		}

		int short_ = men - Mustered(unit);
		if (short_ > 0)
		{
			FieldArmy seat = Armies.Count > 0 ? Armies[0] : Raise(marchLeft);
			seat.Men[unit] = seat.Men.GetValueOrDefault(unit) + short_;
		}

		Bury();
	}

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

	/// <summary>The company an old save is being read into, made on the first line that needs it.
	/// One with nobody in it is dropped when the save is restored (see TurnManager.Restore).</summary>
	private FieldArmy Older() => Armies.Count > 0 ? Armies[0] : Raise();

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
		copy.Armies = new List<FieldArmy>(Armies.Count);
		foreach (FieldArmy standing in Armies)
		{
			copy.Armies.Add(new FieldArmy
			{
				Home = standing.Home,
				Id = standing.Id,
				County = standing.County,
				Men = new Dictionary<string, int>(standing.Men),
				MarchLeft = standing.MarchLeft,
				X = standing.X,
				Y = standing.Y,
			});
		}

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
			province.Fortification.Length > 0 ? province.Castle : province.Raise().Men;
		foreach (System.Collections.Generic.KeyValuePair<Variant, Variant> company in definition.InitialGarrison)
		{
			opening[company.Key.AsString()] = company.Value.AsInt32();
		}

		// A county authored with nobody in the field is not given an empty banner to stand over.
		province.Bury();

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
