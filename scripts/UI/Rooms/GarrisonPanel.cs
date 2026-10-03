using System.Collections.Generic;
using Godot;

/// <summary>The walls of a county, asked about with the right button the way a company is: what
/// stands there, how many are on it of how many it holds, every kind of man by his portrait, and how
/// long the stores behind the gate would feed them — in the painted frame every modal wears.</summary>
public partial class GarrisonPanel : Control
{
	private const float FadeSeconds = 0.18f;
	private static readonly Vector2 PortraitSize = new(64, 64);

	private Label _title;
	private Label _wall;
	private Label _count;
	private VBoxContainer _roster;
	private Label _larder;

	public override void _Ready()
	{
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;
		Visible = false;
		Modulate = new Color(1, 1, 1, 0);

		AddChild(new ColorRect
		{
			Color = new Color(0, 0, 0, 0.5f),
			MouseFilter = MouseFilterEnum.Ignore,
			AnchorRight = 1,
			AnchorBottom = 1,
		});

		var centred = new CenterContainer();
		Chrome.Fill(centred);
		centred.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(centred);

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(560, 0) };
		column.AddThemeConstantOverride("separation", 12);
		centred.AddChild(Chrome.Painted(column));

		_title = Chrome.Line("", 32, Chrome.Cream);
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_title);
		column.AddChild(_title);

		_wall = Chrome.Line("", 17, Chrome.Dim);
		_wall.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_wall);

		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(rule);

		_count = Chrome.Line("", 22, Chrome.Bright);
		_count.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_count);

		_roster = new VBoxContainer();
		_roster.AddThemeConstantOverride("separation", 8);
		column.AddChild(_roster);

		_larder = Chrome.Line("", 16, Chrome.Soft);
		_larder.HorizontalAlignment = HorizontalAlignment.Center;
		_larder.AutowrapMode = TextServer.AutowrapMode.Word;
		column.AddChild(_larder);

		var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(220, 46), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		done.AddThemeFontSizeOverride("font_size", 18);
		done.Pressed += Close;
		column.AddChild(done);
	}

	/// <summary>Opens on a county's walls. <paramref name="realm"/> is whose they are, as the map
	/// names him; a county with no walls has nothing to show and nothing opens.</summary>
	public void Open(ProvinceEconomy county, string realm, GameBalance balance)
	{
		if (county == null || county.Fortification.Length == 0)
		{
			return;
		}

		Fortifications.Wall wall = Fortifications.Of(county.Fortification);
		_title.Text = county.ProvinceName;
		_wall.Text = county.BesiegedFrom.Length > 0 ? $"{wall.Name} of {realm} — under siege" : $"{wall.Name} of {realm}";
		_count.Text = $"{county.CastleMen:N0} of {wall.Garrison:N0} men on the walls";

		foreach (Node old in _roster.GetChildren())
		{
			old.QueueFree();
		}

		var units = new List<string>(county.Castle.Keys);
		units.Sort(System.StringComparer.Ordinal);
		foreach (string unit in units)
		{
			int men = county.Castle[unit];
			if (men > 0)
			{
				_roster.AddChild(Row(unit, men));
			}
		}

		if (county.CastleMen == 0)
		{
			Label nobody = Chrome.Line("Nobody stands on them.", 18, Chrome.Short);
			nobody.HorizontalAlignment = HorizontalAlignment.Center;
			_roster.AddChild(nobody);
		}

		int eaten = Mathf.CeilToInt(county.CastleMen / balance.PeoplePerGrain * balance.SoldierAppetite);
		_larder.Text = county.CastleMen == 0 ? $"{county.CastleStores:N0} grain in the stores behind the gate."
			: eaten <= 0 ? ""
			: $"{county.CastleStores:N0} grain behind the gate: {county.CastleStores / eaten} season{(county.CastleStores / eaten == 1 ? "" : "s")} of bread for them if the gate is shut.";

		MoveToFront();
		Visible = true;
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
	}

	private static Control Row(string unit, int men)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 16);
		row.AddChild(UnitArt.Tile(unit, PortraitSize));

		Units.Unit kind = Units.Of(unit);
		Label name = Chrome.Line(kind.Name, 20, Chrome.Cream);
		name.VerticalAlignment = VerticalAlignment.Center;
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		row.AddChild(name);

		Label count = Chrome.Line(men.ToString("N0"), 22, Chrome.Bright);
		count.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(count);
		return row;
	}

	private void Close()
	{
		Tween tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
		tween.TweenCallback(Callable.From(() => Visible = false));
	}

	/// <summary>A press on the dimmed map behind closes it, and so does Escape.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Visible && @event is InputEventMouseButton { Pressed: true })
		{
			Close();
			AcceptEvent();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			Close();
			GetViewport().SetInputAsHandled();
		}
	}
}
