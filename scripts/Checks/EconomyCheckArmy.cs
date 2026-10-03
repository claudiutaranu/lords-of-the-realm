using System.Collections.Generic;
using Godot;

/// <summary>The county's men under arms: the levy taken out of its people, and the companies it
/// pays and feeds.</summary>
public partial class EconomyCheck
{
	private void Levy(GameBalance b, ProvinceDefinition def)
	{
		// Hands standing about are what a levy is taken from first: the trades do not notice it.
		ProvinceEconomy spare = Province();
		spare.Population = 400;
		spare.GrainWorkers = 100;
		spare.IronWorkers = 50;
		EconomySimulation.Conscript(spare, 200);
		Is("a levy the idle can cover leaves the fields alone", spare.GrainWorkers, 100);
		Is("  and the mine too", spare.IronWorkers, 50);
		Is("  and the people are gone off the roll", spare.Population, 200);

		// One bigger than that comes off the trades, in proportion to what each of them holds.
		ProvinceEconomy pressed = Province();
		pressed.Population = 200;
		pressed.GrainWorkers = 120;
		pressed.IronWorkers = 60;
		pressed.WoodWorkers = 20;
		EconomySimulation.Conscript(pressed, 100);
		Is("a levy past the idle takes the fields", pressed.GrainWorkers, 60);
		Is("  and the mine, by what it held", pressed.IronWorkers, 30);
		Is("  and the woodyard", pressed.WoodWorkers, 10);

		// Whatever the arithmetic, the county never has more men at work than it has men.
		Is("nobody works who is not there", pressed.AllocatedWorkers <= pressed.Workers, true);

		ProvinceEconomy stripped = Province();
		stripped.Population = 50;
		stripped.GrainWorkers = 50;
		EconomySimulation.Conscript(stripped, 50);
		Is("a county emptied of people has nobody at work", stripped.AllocatedWorkers, 0);
	}

	// --- what an army costs to keep ---------------------------------------------------------------

	/// <summary>Men under arms eat and are paid. The first of those is the one that needs pinning
	/// down: a soldier is taken OUT of the population when he is raised, so for as long as he did
	/// not eat, every company a lord trained quietly made his winter cheaper — an army was a saving.
	/// That is the exact inversion this case exists to keep shut.</summary>
	private void TheArmy(GameBalance b, ProvinceDefinition def)
	{
		// Armies eat, at the county's own ration, as the original's option has it: a hundred men in
		// the field are a hundred more at the table.
		ProvinceEconomy war = Fed();
		war.Population = 700;
		war.Muster("spear", 100);
		TurnSummary raised = EconomySimulation.RunTurn(war, def, b, Season.Winter);
		Is("the men in the field eat at the county's table", raised.Needed, 800);

		// Wages, out of what the reeve just brought in.
		Is("the men are paid", raised.Wages, Mathf.CeilToInt(100 * b.WagePerSoldier));
		Is("and nobody left over it", raised.Deserted, 0);

		// And an army the county cannot carry thins itself rather than putting the treasury into a
		// negative number nothing in the game knows how to answer.
		ProvinceEconomy broke = Fed();
		broke.Population = 700;
		broke.Gold = 0;
		broke.Tax = 0; // nothing comes in, so nothing can be paid out
		broke.Muster("spear", 100);
		TurnSummary unpaid = EconomySimulation.RunTurn(broke, def, b, Season.Winter);

		Is("unpaid men walk away", unpaid.Deserted, Mathf.CeilToInt(100 * b.DesertionRate));
		Is("and are gone from the roster", broke.Soldiers, 100 - unpaid.Deserted);
		Is("the treasury is emptied, not overdrawn", broke.Gold >= 0, true);

		// A company cut in two, which is how a lord leaves a ford held and goes on with the rest. He
		// says which men walk off, kind by kind; nobody may be lost or conjured in the cut, and both
		// halves stand where the whole one did with the same season left in their legs.
		ProvinceEconomy cut = Fed();
		cut.Muster("spear", 25, 40f);
		cut.Muster("bow", 15);
		FieldArmy whole = cut.Armies[0];
		whole.County = "Elsewhere";
		FieldArmy half = cut.Split(whole, new Dictionary<string, int> { ["spear"] = 5, ["bow"] = 15 });
		Is("a company splits as the lord cut it", cut.Armies.Count, 2);
		Is("the men he sent are the ones that went", half.Men.GetValueOrDefault("bow"), 15);
		Is("  and the rest are still his", whole.Strength, 20);
		Is("  nobody is lost in the cut", half.Strength + whole.Strength, 40);
		Is("  a kind emptied is off the roster", whole.Men.ContainsKey("bow"), false);
		Is("  on the same ground", half.County, whole.County);
		Is("  with the same legs left", half.MarchLeft, whole.MarchLeft);

		// And the one rule, whatever the screen asks for: neither banner may be raised over nobody.
		ProvinceEconomy all = Fed();
		all.Muster("spear", 10);
		FieldArmy only = all.Armies[0];
		Is("a company cannot walk off entire", all.Split(only, new Dictionary<string, int> { ["spear"] = 10 }) == null, true);
		Is("  nor cut into nobody", all.Split(only, new Dictionary<string, int> { ["spear"] = 0 }) == null, true);
		FieldArmy band = all.Raise(0f);
		band.Men["swiss"] = 100;
		FieldArmy last = all.Raise(0f);
		int lastId = last.Id;
		all.Disband(last);
		Is("a company's number is never given out again", all.Raise(0f).Id, lastId + 1);
		all.Disband(all.Armies[^1]);
		Is("  and a hired band is not cut at all", all.Split(band, new Dictionary<string, int> { ["swiss"] = 40 }) == null, true);
		all.Disband(band);
		Is("  and more than he has is only what he has",
			all.Split(only, new Dictionary<string, int> { ["spear"] = 99 }) == null, true);
		Is("  so his company is untouched", all.Armies.Count, 1);
	}

	/// <summary>A province with bread in the barn, no herd to milk and nobody at work: whatever
	/// moves its granary in a winter turn is what it ate.</summary>
	private static ProvinceEconomy Fed()
	{
		ProvinceEconomy province = Province();
		province.Grain = 5000;
		province.Cattle = 0;
		province.Gold = 2000;
		return province;
	}
}
