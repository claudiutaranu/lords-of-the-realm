using System.Collections.Generic;
using Godot;

/// <summary>The watch on the walls that stand: who mans them, what the larder carries for them, and
/// the men posted up or taken down.</summary>
public partial class FortificationsPage
{
	/// <summary>The column that says who is standing on the walls, and the line under it that counts
	/// them.</summary>
	private VBoxContainer _garrison;
	private Label _manned;
	private Label _larder;

	/// <summary>What the column was last built for. The steppers hold a snapshot of their own value
	/// and ceiling, so they are rebuilt whenever either could have moved — and a man going up onto
	/// the walls moves both, since he takes one of their places. A slider only settles when it is
	/// let go, so a rebuild never frees it out from under the finger still holding it.</summary>
	private string _standing = "";

	/// <summary>Who stands on the walls, company by company.
	///
	/// It belongs in this room and nowhere else: manning a gate is meaningless without a gate, and
	/// this is the only screen that knows what the province has raised. The decision it asks is the
	/// one a castle exists to ask — a lord can leave archers on the parapet and ride out with the
	/// rest, and the men he leaves are the ones who still hold the county after he has lost
	/// everything he took into the field.
	///
	/// Companies and not one number, because WHICH men is the whole question. Bows are worth twice
	/// as much behind a parapet as in front of one, and cavalry on a wall are the most expensive men
	/// in the realm standing about watching.</summary>
	private void ShowGarrison()
	{
		string signature = Signature();
		if (signature == _standing)
		{
			Counted();
			return;
		}

		_standing = signature;
		foreach (Node old in _garrison.GetChildren())
		{
			old.QueueFree();
		}

		var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		heading.AddThemeConstantOverride("separation", 10);
		heading.AddChild(Icon("castle", 28));
		Label title = Line("THE WALLS", 20, Cream);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		_garrison.AddChild(heading);

		List<string> companies = Province.Companies();
		companies.Sort(System.StringComparer.Ordinal);

		if (Province.Fortification.Length == 0)
		{
			_garrison.AddChild(Line("There is nothing here yet for a man to stand on.", 16, Dim));
			return;
		}

		if (companies.Count == 0)
		{
			Label nobody = Line("No men in the county to put on them.", 16, Bright);
			nobody.HorizontalAlignment = HorizontalAlignment.Center;
			_garrison.AddChild(nobody);
			return;
		}

		foreach (string unit in companies)
		{
			int held = Province.Castle.GetValueOrDefault(unit);
			int most = held + Mathf.Min(Province.Mustered(unit), Province.WallRoom);
			string name = unit;
			_garrison.AddChild(Stepper(Units.Of(unit).Name, held, 5, most, men => Man(name, men), floor: 0));
		}

		_manned = Line("", 16, Soft);
		_manned.HorizontalAlignment = HorizontalAlignment.Center;
		_garrison.AddChild(_manned);

		// The larder. A siege is lost or won on this line and on nothing else, and it has to be
		// carried up BEFORE anybody is at the border — a lord stocking a castle he is already shut
		// out of is a lord who has understood the mechanism a season too late.
		_garrison.AddChild(Chrome.Rule(0));
		int room = Fortifications.Of(Province.Fortification).Stores;
		_garrison.AddChild(Stepper("Grain behind the gate", Province.CastleStores, 5,
			Mathf.Min(room, Province.CastleStores + Province.Grain), Stock, floor: 0));

		_larder = Line("", 16, Soft);
		_larder.HorizontalAlignment = HorizontalAlignment.Center;
		_garrison.AddChild(_larder);
		Counted();
	}

	/// <summary>Everything about the column that decides how it is BUILT, as against what it
	/// reads.</summary>
	private string Signature()
	{
		var mark = new System.Text.StringBuilder(Province.Fortification);
		mark.Append('|').Append(Province.CastleStores + Province.Grain).Append('|').Append(Province.WallRoom);
		List<string> companies = Province.Companies();
		companies.Sort(System.StringComparer.Ordinal);
		foreach (string unit in companies)
		{
			mark.Append('|').Append(unit).Append(':')
				.Append(Province.Mustered(unit) + Province.Castle.GetValueOrDefault(unit));
		}

		return mark.ToString();
	}

	/// <summary>Carries grain up behind the gate, or brings it back down. It is the same grain
	/// either way: nothing is spent, and what is up there is simply not in the granary.</summary>
	private void Stock(int carried)
	{
		if (Province.BesiegedFrom.Length > 0)
		{
			return; // nothing goes in or out through a siege line
		}

		int all = Province.CastleStores + Province.Grain;
		carried = Mathf.Clamp(carried, 0, Mathf.Min(all, Fortifications.Of(Province.Fortification).Stores));
		Province.CastleStores = carried;
		Province.Grain = all - carried;
		Refresh();
	}

	/// <summary>Moves men of one kind between the field and the gate, no more than the walls have room
	/// for. Nothing is raised or spent: these are the same men either way, and the only question is
	/// where they are standing when somebody comes for the county.</summary>
	private void Man(string unit, int onTheWalls)
	{
		int all = Province.Castle.GetValueOrDefault(unit) + Province.Mustered(unit);
		int held = Province.Castle.GetValueOrDefault(unit);
		onTheWalls = Mathf.Clamp(onTheWalls, 0, Mathf.Min(all, held + Province.WallRoom));
		Post(Province.Castle, unit, onTheWalls);

		// Men coming down off the gate fall in with the county's own company — raised for them if
		// every one of its companies has marched off — and they have the season's ground in their
		// legs like anybody who has not walked anywhere yet.
		Province.Muster(unit, all - onTheWalls, GameBalance.Engine.MarchReach);
		Refresh();
	}

	private static void Post(Dictionary<string, int> roster, string unit, int men)
	{
		if (men > 0)
		{
			roster[unit] = men;
		}
		else
		{
			roster.Remove(unit);
		}
	}

	private void Counted()
	{
		if (_manned == null)
		{
			return;
		}

		_manned.Text = $"{Province.CastleMen:N0} men on the walls, room for "
			+ $"{Fortifications.Of(Province.Fortification).Garrison:N0}";

		// In seasons rather than in sacks, because seasons is the question. Sacks is how it is
		// carried; how long they hold out is what a lord is deciding.
		int eaten = Mathf.CeilToInt(
			Province.CastleMen / GameBalance.Engine.PeoplePerGrain * GameBalance.Engine.SoldierAppetite);
		string holds = Province.BesiegedFrom.Length > 0
			? "and nothing gets in while they are out there"
			: Province.CastleMen == 0
			? "nobody up there to eat it"
			: eaten <= 0 ? "as long as you like"
			: $"{Province.CastleStores / eaten:N0} seasons of bread for them";
		_larder.Text = $"{Province.CastleStores:N0} of {Fortifications.Of(Province.Fortification).Stores:N0} sacks — {holds}";
	}
}
