using System;
using System.Collections.Generic;
using Godot;

/// <summary>The province's land, and the people who work it.
///
/// It is the screen the whole economy turns on, because a province has only so many people and
/// every season they can be in only one place. Three seasons of the year that is a quiet trade —
/// a few more at the woodpile, a few less in the mine. In autumn it is not: four fields of grain
/// want two hundred and forty hands for one turn, and a lord who leaves them in the quarry reaps a
/// quarter of his year and spends the winter buying bread.
///
/// Above the hands is the land itself, a strip of the province's own fields. Each one is under
/// grain, under the herd, or resting, and pressing it turns it to the next — which is the other
/// half of the same decision, because what the fields are under is what sets how many hands autumn
/// will ask for.
///
/// Every row shows the same three things: how many hands are on the job, how many it can actually
/// use, and what they will bring in. Hands above what a job can use do nothing at all, and say so.</summary>
public partial class LabourPage : RoomPage
{
	/// <summary>One thing the province's people can be put to. The blurb is what the row says when
	/// there is no work there this season — a field in winter, a herd nobody keeps.</summary>
	private record Work(ResourceType Type, string Name, string Icon, string Idle);

	private static readonly Work[] Tasks =
	{
		new(ResourceType.Grain, "Fields", "food", "Nothing grows in winter."),
		new(ResourceType.Cattle, "Herd", "livestock", "No herd to keep."),
		new(ResourceType.Wood, "Woods", "wood", "No woods worth cutting."),
		new(ResourceType.Stone, "Quarry", "stone", "No stone in the ground."),
		new(ResourceType.Iron, "Mine", "iron", "No ore in the ground."),
	};

	private ProvinceDefinition _definition;
	private GameBalance _balance;
	private Season _season;
	private VBoxContainer _rows;
	private HBoxContainer _land;

	protected override string RoomName => "The Fields";

	protected override string Tagline => "What the land is under, and where the hands are this season";

	// Nothing hangs on the valley here; the choices are rows, not signs over a room.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override void Load()
	{
	}

	/// <summary>What a task can use and what a hand is worth are both read off the province's own
	/// land and the season it stands in, neither of which a room is normally handed. This is how the
	/// map gives them over, the same way the town is told which sites the province has.</summary>
	public void Brief(ProvinceDefinition definition, GameBalance balance, Season season)
	{
		_definition = definition;
		_balance = balance;
		_season = season;
		Rebuild();
	}

	protected override void BuildChoosers()
	{
		var column = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkEnd,
		};
		column.AddThemeConstantOverride("separation", 12);
		Body.AddChild(column);
		Body.MoveChild(column, 0);

		_land = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		_land.AddThemeConstantOverride("separation", 8);
		PanelContainer acres = Framed(_land, 14);
		acres.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(acres);

		_rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_rows.AddThemeConstantOverride("separation", 10);
		PanelContainer hands = Framed(_rows, 16);
		hands.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(hands);
	}

	protected override void Opened()
	{
	}

	protected override void ShowDetail()
	{
		ClearDetail();
		if (_definition == null)
		{
			return;
		}

		Label heading = Line(_season.ToString().ToUpperInvariant(), 24, Cream);
		heading.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(heading);
		Detail.AddChild(Chrome.Rule(360));

		int hands = Province.Workers;
		int idle = EconomySimulation.Idle(Province, _definition, _balance, _season);
		Detail.AddChild(Reading("Hands", hands.ToString("N0"), Bright));
		Detail.AddChild(Reading("At work", (hands - idle).ToString("N0"), Bright));
		Detail.AddChild(Reading("Idle", idle.ToString("N0"), idle > 0 ? Short : Soft));

		// Three lines' worth of room whether the note needs them or not, so the button under it
		// does not walk up and down the panel as the season changes.
		Label note = Line(Note(), 15, Soft);
		note.AutowrapMode = TextServer.AutowrapMode.Word;
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.VerticalAlignment = VerticalAlignment.Center;
		note.CustomMinimumSize = new Vector2(0, 62);
		Detail.AddChild(note);

		var spread = new Button { Text = "Set them to work", CustomMinimumSize = new Vector2(0, 46) };
		spread.AddThemeFontSizeOverride("font_size", 18);
		spread.TooltipText = "Enough on the farm for every field and beast this season, the rest to the industry.";
		spread.Pressed += () =>
		{
			Labour.FarmsFirst(Province, _definition, _balance, _season);
			Rebuild();
		};

		Detail.AddChild(spread);
	}

	/// <summary>What this season is asking of the province, in one line. Autumn gets its own
	/// warning because it is the one season where the right answer is "everybody".</summary>
	private string Note() => _season switch
	{
		Season.Spring => "Seed goes into the ground now, out of your own granary. What is not sown is not reaped.",
		Season.Summer => "A few hands keep the weeds down. The rest are yours to send anywhere.",
		Season.Autumn => "The whole year comes in over one turn. Every hand you leave elsewhere is grain left standing in the field.",
		_ => "Nothing grows. The woods and the diggings are all there is to work.",
	};

	private void Rebuild()
	{
		foreach (Node old in _rows.GetChildren())
		{
			old.QueueFree();
		}

		foreach (Node old in _land.GetChildren())
		{
			old.QueueFree();
		}

		for (int field = 0; field < Province.Fields.Length; field++)
		{
			_land.AddChild(Acre(field));
		}

		foreach (Work work in Tasks)
		{
			_rows.AddChild(Row(work));
		}

		ShowDetail();
	}

	/// <summary>One of the province's fields: what it is under, and how much heart it has left.
	/// Pressing it turns it to the next use.</summary>
	private Control Acre(int field)
	{
		FieldUse use = Province.Fields[field];
		float heart = (Province.Soil + 100) / 200f; // the county's soil, as the original keeps it

		var acre = new Button
		{
			CustomMinimumSize = new Vector2(76, 88),
			TooltipText = $"{Name(use)} — {Mathf.RoundToInt(heart * 100)} in a hundred.\n{Habit(use)}",
		};

		acre.Pressed += () => Turn(field);
		Chrome.DressPlaque(acre, lit: use != FieldUse.Fallow);

		var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		stack.AddThemeConstantOverride("separation", 4);
		stack.Alignment = BoxContainer.AlignmentMode.Center;
		acre.AddChild(stack);
		Chrome.Fill(stack);

		var centred = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
		centred.AddChild(Icon(Glyph(use), 34));
		stack.AddChild(centred);

		Label name = Line(Name(use).ToUpperInvariant(), 11, use == FieldUse.Fallow ? Dim : Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(name);

		stack.AddChild(Heart(heart));
		return acre;
	}

	/// <summary>How much the field has left in it, drawn rather than written: a strip of the ten
	/// fields is read at a glance, and ten numbers are not.</summary>
	private static Control Heart(float fertility)
	{
		var bar = new ProgressBar
		{
			MinValue = 0,
			MaxValue = 1,
			Value = fertility,
			ShowPercentage = false,
			CustomMinimumSize = new Vector2(52, 5),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
			MouseFilter = MouseFilterEnum.Ignore,
		};

		bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f) });
		bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
		{
			// Green where the land is in heart, red where it has been cropped out.
			BgColor = new Color(0.42f, 0.75f, 0.37f).Lerp(new Color(0.78f, 0.35f, 0.26f), 1f - fertility),
		});

		return bar;
	}

	/// <summary>Turns one field to the next use. What that costs — a standing crop ploughed under —
	/// is the engine's to say, not the page's.</summary>
	private void Turn(int field)
	{
		EconomySimulation.SetField(Province, field, Province.Fields[field] switch
		{
			FieldUse.Grain => FieldUse.Pasture,
			FieldUse.Pasture => FieldUse.Fallow,
			_ => FieldUse.Grain,
		});

		// The land decides how many hands autumn will ask for, so an allocation that was full a
		// moment ago may now be over what the fields can use.
		Rebuild();
	}

	private static string Glyph(FieldUse use) => use switch
	{
		FieldUse.Grain => "food",
		FieldUse.Pasture => "livestock",
		FieldUse.Waste => "pitchfork",
		_ => "laurel",
	};

	private static string Name(FieldUse use) => use switch
	{
		FieldUse.Grain => "Grain",
		FieldUse.Pasture => "Pasture",
		FieldUse.Waste => "Flooded",
		_ => "Fallow",
	};

	private static string Habit(FieldUse use) => use switch
	{
		FieldUse.Grain => "Sown in spring, reaped in autumn. Takes heart out of the land.",
		FieldUse.Pasture => "Room for twenty head. Gives back half of what grain takes.",
		FieldUse.Waste => "Torn up by the flood. Nothing will grow on it until the reclaimers have mended it.",
		_ => "Resting. Two fields under grain to one at rest comes out level.",
	};

	/// <summary>One task: what it is, how many hands are on it, and what they are worth. The stepper
	/// asks for hands (Labour.Ask) and runs to what the task can use or to its half of the county,
	/// whichever comes first — there is no way to put a hundred men on a job with work for twenty.</summary>
	private Control Row(Work work)
	{
		string job = Labour.JobOf(work.Type);
		int demand = Labour.Most(Province, _definition, _balance, _season, job);
		int on = Allocated(work.Type);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);

		var named = new HBoxContainer { CustomMinimumSize = new Vector2(170, 0) };
		named.AddThemeConstantOverride("separation", 12);
		TextureRect glyph = Icon(work.Icon, 30);
		glyph.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		named.AddChild(glyph);
		Label name = Line(work.Name, 20, Cream);
		name.VerticalAlignment = VerticalAlignment.Center;
		named.AddChild(name);
		row.AddChild(named);

		if (demand <= 0)
		{
			Label none = Line(work.Idle, 16, Dim);
			none.VerticalAlignment = VerticalAlignment.Center;
			none.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(none);
			return row;
		}

		// The slider runs to what the job can use, so its position reads as "how much of this job is
		// manned" — which is the thing worth knowing.
		// A figure at a time, as on the province screen (WorkerFigures).
		Control stepper = Stepper(null, on, WorkerFigures.Size(Province.Workers), demand, hands =>
		{
			Labour.Ask(Province, _definition, _balance, _season, job, hands);
			Rebuild();
		}, floor: 0);

		stepper.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(stepper);

		// What it wants, and what those hands will bring in. Two readings, because either one alone
		// leaves the player guessing: the demand without the yield says nothing about whether it is
		// worth filling, and the yield without the demand hides how much is being left on the table.
		var readings = new VBoxContainer { CustomMinimumSize = new Vector2(150, 0) };
		readings.AddThemeConstantOverride("separation", 0);
		readings.SizeFlagsVertical = SizeFlags.ShrinkCenter;

		Label wants = Line($"of {demand:N0}", 14, on >= demand ? Soft : Short);
		wants.HorizontalAlignment = HorizontalAlignment.Center;
		readings.AddChild(wants);

		int yield = EconomySimulation.ProjectedYield(work.Type, on, Province, _definition, _balance, _season);
		Label brings = Line(yield > 0 ? $"+{yield:N0}" : "—", 19, yield > 0 ? Gain() : Dim);
		brings.HorizontalAlignment = HorizontalAlignment.Center;
		readings.AddChild(brings);

		row.AddChild(readings);
		return row;
	}

	private Control Reading(string what, string value, Color colour)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		Label name = Line(what, 16, Soft);
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(name);
		Label reading = Line(value, 18, colour);
		row.AddChild(reading);
		return row;
	}

	private static Color Gain() => Chrome.Gain;

	private int Allocated(ResourceType type) => type switch
	{
		ResourceType.Grain => Province.GrainWorkers,
		ResourceType.Cattle => Province.CattleWorkers,
		ResourceType.Wood => Province.WoodWorkers,
		ResourceType.Stone => Province.StoneWorkers,
		_ => Province.IronWorkers,
	};
}
