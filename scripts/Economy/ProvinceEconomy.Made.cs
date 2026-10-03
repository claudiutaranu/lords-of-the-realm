using System.Collections.Generic;
using Godot;

/// <summary>A county's economy made from its authored definition when the campaign opens, and
/// copied whole for a reckoning that must not touch the real one.</summary>
public partial class ProvinceEconomy
{
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
		copy.Reclaimed = new Dictionary<int, int>(Reclaimed);
		copy.Occupied = new Dictionary<string, int>(Occupied);
		copy.Armoury = new Dictionary<string, int>(Armoury);
		copy.Shares = new Dictionary<string, int>(Shares);
		copy.Shut = new List<string>(Shut);
		copy.Efficiency = new Dictionary<string, int>(Efficiency);
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
				Raider = standing.Raider,
				RaidLeft = standing.RaidLeft,
				SpoiledTurn = standing.SpoiledTurn,
			});
		}

		copy.Castle = new Dictionary<string, int>(Castle);
		copy.EventQuiet = new Dictionary<string, int>(EventQuiet);
		copy.HappinessByYear = new List<float>(HappinessByYear);
		copy.PeopleBySeason = new List<PeopleSeason>(PeopleBySeason);
		return copy;
	}

	public static ProvinceEconomy FromDefinition(ProvinceDefinition definition)
	{
		var province = new ProvinceEconomy
		{
			ProvinceName = definition.ProvinceName,
			Population = definition.InitialPopulation,
			// Divided the way a county nobody has touched is; a save with none is read off its hands.
			Shares = Labour.OpeningShares(),
			Gold = definition.InitialGold,
			Grain = definition.InitialGrain,
			Cattle = definition.InitialCattle,
			Wood = definition.InitialWood,
			Stone = definition.InitialStone,
			Iron = definition.InitialIron,
			Fields = new FieldUse[definition.Fields],
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
		}

		// Last winter's sowing is already in the ground: a county does not come into the story in
		// the one year of its life when nobody sowed, and one that opened bare in spring would see no
		// harvest for a year and a half.
		province.SownFields = province.FieldsUnder(FieldUse.Grain);
		province.StandingCrop = province.SownFields * Husbandry.MostSacksAField * Husbandry.CropPerSack;

		return province;
	}
}
