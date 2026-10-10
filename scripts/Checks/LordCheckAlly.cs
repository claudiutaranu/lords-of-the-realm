using System.Collections.Generic;
using Godot;

/// <summary>An ally at the player's call: a lord sworn to him who has said he will help comes, and
/// falls on whoever sits before the player's gate.</summary>
public partial class LordCheck
{
	/// <summary>Three villages in a row: the Watch's, the player's walled county besieged by a company
	/// of the Watch's, and an ally's with a host twice the besiegers'. The ally lifts the siege.</summary>
	private void TheirHelp()
	{
		var b = new GameBalance { WorldEventChance = 0f, MercenaryChance = 0f };
		b.LordFirstMarch = new[] { 0, 0, 0 };
		var definitions = new List<ProvinceDefinition> { Definition("North"), Definition("Middle"), Definition("South") };
		var realms = new Dictionary<string, string> { ["North"] = "north", ["Middle"] = "crown", ["South"] = "east" };
		var board = new TurnManager(b, definitions, realms, "crown", Difficulty.Medium, new List<ProvinceDefinition>());
		board.Survey(Straight, pixel => pixel.X < 200 ? "North" : pixel.X < 400 ? "Middle" : "South",
			new Dictionary<string, Vector2> { ["North"] = new(100, 100), ["Middle"] = new(300, 100), ["South"] = new(500, 100) },
			38f, null, null, null);

		ProvinceEconomy gate = board.AnyProvince("Middle");
		gate.Fortification = "small-palisade";
		gate.Castle["spear"] = 40;
		gate.CastleStores = 100_000;
		FieldArmy besieger = board.AnyProvince("North").Raise(b.MarchReach);
		besieger.Men["spear"] = 60;
		besieger.County = "Middle";
		besieger.X = 300f;
		besieger.Y = 100f;
		Is("the Watch sits down before the player's gate", board.Besiege(besieger, "Middle"), true);

		board.AnyProvince("South").Gold = 20000;
		FieldArmy helper = board.AnyProvince("South").Raise(b.MarchReach);
		helper.Men["spear"] = 300;
		board.Diplomacy.Allies["east"] = "crown";
		board.Diplomacy.Allies["crown"] = "east";
		board.Diplomacy.Errands["east"] = "";

		bool told = false;
		for (int season = 0; season < 6 && gate.BesiegedFrom.Length > 0; season++)
		{
			board.AdvanceTurn();
			told |= board.News.Exists(item => item.Said.Id == "siege-relieved");
		}

		Is("an ally who answered the call for help falls on the besiegers, and the player is told", told, true);
		Is("  and the siege is lifted", gate.BesiegedFrom.Length, 0);
		Is("  and the county is still the player's", gate.Realm, "crown");
	}
}
