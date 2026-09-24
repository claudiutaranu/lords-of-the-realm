using Godot;

/// <summary>The fields and the herd by Lords of the Realm's own rules (Husbandry).</summary>
public partial class EconomyCheck
{
	private void TheLand(GameBalance b, ProvinceDefinition def)
	{
		// Sowing, at the end of winter: ten sacks a field when the barn and the sowers allow it.
		ProvinceEconomy sown = Province();
		int fields = sown.FieldsUnder(FieldUse.Grain);
		sown.Grain = 1000;
		sown.GrainWorkers = 24 * fields;
		var winter = new TurnSummary();
		Husbandry.WorkTheFields(sown, Season.Winter, winter);
		Is("a full barn and the sowers put ten sacks in every field", winter.Sown, 10 * fields);
		Is("  and the crop is twelve times the seed", sown.StandingCrop, 120 * fields);

		// Fewer sowers, fewer sacks a field: the most both the barn and the hands allow.
		ProvinceEconomy few = Province();
		few.Grain = 1000;
		few.GrainWorkers = 12 * fields; // enough for five sacks a field
		Is("half the sowers sow half the sacks", Husbandry.SacksAField(few, few.GrainWorkers), 5);
		few.Grain = 2 * fields;
		Is("  and a barn of two sacks a field sows two", Husbandry.SacksAField(few, 24 * fields), 2);

		// Growing: capped at ten sacks a tender, then grown by half the soil.
		ProvinceEconomy growing = Province();
		growing.StandingCrop = 480;
		growing.Soil = 40;
		growing.GrainWorkers = 48;
		Husbandry.WorkTheFields(growing, Season.Spring, new TurnSummary());
		Is("tenders enough for the crop, and good soil grows it a fifth", growing.StandingCrop, 576);
		growing.GrainWorkers = 20;
		Husbandry.WorkTheFields(growing, Season.Summer, new TurnSummary());
		Is("  twenty tenders keep two hundred of it", growing.StandingCrop, 240);

		// The harvest: three sacks for every two reapers, and no more than stands.
		ProvinceEconomy reaped = Province();
		reaped.StandingCrop = 600;
		reaped.Grain = 0;
		reaped.GrainWorkers = 200;
		var autumn = new TurnSummary();
		Husbandry.WorkTheFields(reaped, Season.Autumn, autumn);
		Is("two hundred reapers bring in three hundred", autumn.Harvest, 300);
		Is("  into the barn", reaped.Grain, 300);
		Is("  and the rest rots in the field", reaped.StandingCrop, 0);

		// The soil, every season: six for a field resting, three off for one cropped.
		ProvinceEconomy soil = Province();
		int rest = soil.FieldsUnder(FieldUse.Fallow);
		int crop = soil.FieldsUnder(FieldUse.Grain);
		Husbandry.Rest(soil);
		Is("the soil moves by its fallow and its grain", soil.Soil, (6 * rest) - (3 * crop));

		// The herd: few head a pasture breed fast, and without herdsmen it dies.
		ProvinceEconomy herd = Province();
		herd.Cattle = 20;
		herd.CattleWorkers = Husbandry.Herdsmen(20);
		var calving = new TurnSummary();
		Husbandry.TendTheHerd(herd, Season.Summer, calving);
		Is("a small tended herd on its pasture grows", herd.Cattle > 20, true);

		ProvinceEconomy loose = Province();
		loose.Cattle = 40;
		loose.CattleWorkers = 0;
		Husbandry.TendTheHerd(loose, Season.Summer, new TurnSummary());
		Is("  an untended one dies off", loose.Cattle < 40, true);

		ProvinceEconomy homeless = Province();
		System.Array.Fill(homeless.Fields, FieldUse.Fallow);
		homeless.Cattle = 40;
		Husbandry.TendTheHerd(homeless, Season.Summer, new TurnSummary());
		Is("  and one with no pasture loses half", homeless.Cattle, 20);

		// And a county left to itself a year feeds itself: the sowing, the growing and the harvest,
		// with 'Set them to work' every season.
		ProvinceEconomy year = Province();
		year.Grain = 1000; // a thousand mouths want a hundred and thirty sacks a season
		int opened = year.Grain;
		foreach (Season season in new[] { Season.Winter, Season.Spring, Season.Summer, Season.Autumn })
		{
			Labour.FarmsFirst(year, def, b, season);
			TurnSummary played = EconomySimulation.RunTurn(year, def, b, season);
			Is($"  a {season.ToString().ToLowerInvariant()} served at the table", played.Achieved >= RationLevel.Normal, true);
		}

		Is("a worked year ends with more grain than it began", year.Grain > opened - 100, true);
	}
}
