using System.Collections.Generic;
using Godot;

/// <summary>The one runnable check behind the lords the player is not. Two things are worth pinning
/// down and neither of them shows on screen as wrong: that a rival county is actually being RUN —
/// the quiet failure here is an enemy whose numbers never move and whose difficulty setting does
/// nothing — and that what belongs to him stays his side of the line. A rival's granary counted into
/// the crown's total, or his rats reported as the player's news, is the kind of bug a player never
/// reports because he cannot tell it from a game that is simply hard.
///
/// Run it: Godot --headless --path . res://scene/checks/lord-check.tscn
/// It prints a line per case and leaves a non-zero exit code if any of them failed.</summary>
public partial class LordCheck : Node
{
	private int _failed;

	public override void _Ready()
	{
		var b = new GameBalance();
		ProvinceDefinition def = Definition("Valmere");

		Hands(b, def);
		Bread(b, def);
		TheTax(b, def);
		TheCounter(b, def);
		TheLine(b);
		WordGetsAround(b);
		TheLarder(b, def);
		TheMarch(b);
		FiftyYears(b);
		TheHerd(b);
		TheMuster(b);
		TheWalls();
		TheirWar();

		GD.Print(_failed == 0
			? "\nthe other lords: all checks passed"
			: $"\nthe other lords: {_failed} FAILED");
		GetTree().Quit(_failed == 0 ? 0 : 1);
	}

	// --- scaffolding -------------------------------------------------------------------------------

	/// <summary>A lord left alone with a poor county for fifty years has to still have a county at
	/// the end of them. Every rival used to starve his own land to death without a blow struck: he
	/// never rested a field, ploughed up more than he could reap, sold his iron only once the barn
	/// was empty and kept a garrison his shrinking people would not stand. No weather and no plague
	/// here — only his own running of it, which is the part this check is about.</summary>
	private void FiftyYears(GameBalance b)
	{
		var def = new ProvinceDefinition
		{
			ProvinceName = "Valmere",
			InitialPopulation = 850,
			Fields = 9,
			InitialGrainFields = 4,
			InitialPastureFields = 2,
			InitialGrain = 360,
			InitialCattle = 50,
			GrainModifier = 0.85f,
			IronWorkerCapacity = 160,
			IronModifier = 1.4f,
			InitialFortification = "large-fort",
		};
		ProvinceEconomy county = County(def);
		county.Castle["spear"] = 60;
		Labour.FarmsFirst(county, def, b, Season.Spring);
		var market = new Market(b);
		int hungry = 0;
		float goodwill = 0f;
		for (int turn = 0; turn < 200; turn++)
		{
			var season = (Season)(turn % 4);
			market.Turned(season);
			LordAI.TakeTurn(county, def, b, market, season, Difficulty.Medium);
			hungry += EconomySimulation.RunTurn(county, def, b, season).Achieved < RationLevel.Normal ? 1 : 0;
			EconomySimulation.FitWorkforce(county);
			goodwill += county.Loyalty / 200f;
		}

		Is("fifty years of a poor county under a middling lord, and it is still a county",
			county.Population >= def.InitialPopulation / 2, true);
		// Over the fifty years and not on the last day of them: a county that outgrows its fields and
		// has a lean year is a county being run, not one being lost.
		Is("  that has not turned on him", goodwill > b.EmigrationBelow, true);
		Is("  and has gone hungry in fewer than one season in ten", hungry < 20, true);
	}

	private static ProvinceDefinition Definition(string name) => new()
	{
		ProvinceName = name,
		InitialPopulation = 850,
		Fields = 9,
		InitialGrainFields = 4,
		InitialPastureFields = 2,
		InitialGrain = 400,
		InitialCattle = 40,
	};

	/// <summary>A rival's county as he runs it: at the rate a lord calls fair, where a county opens at
	/// nothing.</summary>
	private static ProvinceEconomy County(ProvinceDefinition def)
	{
		ProvinceEconomy county = ProvinceEconomy.FromDefinition(def);
		county.Tax = new GameBalance().FairTaxPercent;
		return county;
	}

	private static Market Counter(GameBalance b, Season season)
	{
		var market = new Market(b);
		market.Turned(season);
		return market;
	}

	private void Is<T>(string what, T got, T wanted)
	{
		if (EqualityComparer<T>.Default.Equals(got, wanted))
		{
			GD.Print($"ok	{what}");
			return;
		}

		_failed++;
		GD.PrintErr($"FAIL {what}: got {got}, expected {wanted}");
	}
}
