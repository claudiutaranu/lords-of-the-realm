using Godot;

public partial class LordCheck
{
	/// <summary>A lord keeps his herd, not just his barn. Every rival used to leave his cattle on the
	/// pasture the campaign gave him: they crowded it inside two years and stood there, neither
	/// breeding nor eaten, and a county with nothing to fall back on but its grain starved in the
	/// first bad harvest. Twenty years of his own running, no weather — his herd has to have grown
	/// onto more grass without crowding it and without his fields going unrested for it, his county
	/// with it, and a lord with no beasts at all buys some.</summary>
	private void TheHerd(GameBalance b)
	{
		var def = new ProvinceDefinition
		{
			ProvinceName = "Valmere",
			InitialPopulation = 850,
			Fields = 9,
			InitialGrainFields = 4,
			InitialPastureFields = 1,
			InitialGrain = 400,
			InitialCattle = 30,
		};
		ProvinceEconomy county = County(def);
		ProvinceEconomy bare = County(def);
		bare.Cattle = 0;
		bare.Gold = 3000;

		var market = new Market(b);
		int leanest = county.Soil;
		for (int turn = 0; turn < 80; turn++)
		{
			var season = (Season)(turn % 4);
			market.Turned(season);
			foreach (ProvinceEconomy run in new[] { county, bare })
			{
				LordAI.TakeTurn(run, def, b, market, season, Difficulty.Medium);
				EconomySimulation.RunTurn(run, def, b, season);
				EconomySimulation.FitWorkforce(run);
				leanest = Mathf.Min(leanest, county.Soil);
			}
		}

		GD.Print($"  the herd after twenty years: {county.Cattle} head on {county.FieldsUnder(FieldUse.Pasture)} pastures, " +
			$"{county.FieldsUnder(FieldUse.Grain)} under grain, soil {county.Soil}; the bare county {bare.Cattle} head");
		Is("the herd has grown past what one pasture holds", county.Cattle > 40, true);
		Is("he has put more grass under it", county.FieldsUnder(FieldUse.Pasture) > 1, true);
		Is("and it is not crowded to a standstill", Husbandry.CrowdingBand(county.Cattle, county.FieldsUnder(FieldUse.Pasture)) <= 1, true);
		Is($"he never spent the soil for it (at its leanest {leanest})", leanest >= 0, true);
		Is("and his county has grown on it", county.Population > def.InitialPopulation, true);
		Is("a lord with gold and no beasts buys a herd", bare.Cattle > 0, true);
	}
}
