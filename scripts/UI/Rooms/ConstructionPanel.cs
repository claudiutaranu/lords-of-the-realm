using System.Collections.Generic;
using Godot;

/// <summary>The masons have finished: the new wall in its picture, the county it stands in, what its
/// walls will hold and what it is worth to the county's taxes, read out in the painted frame every
/// modal wears (Chrome.Painted). One season can finish more than one wall; they are shown one after
/// another, and the herald speaks once.</summary>
public partial class ConstructionPanel : Control
{
	private const float FadeSeconds = 0.18f;
	private const string CompleteVoicePath = "res://assets/audio/castle-complete.mp3";
	private static readonly Vector2 PictureSize = new(340, 290);

	private readonly Queue<(string County, string Fort, int OnTheWalls)> _waiting = new();
	private TextureRect _picture;
	private Label _county;
	private Label _built;
	private Label _garrison;
	private Label _worth;

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

		var column = new VBoxContainer { CustomMinimumSize = new Vector2(820, 0) };
		column.AddThemeConstantOverride("separation", 16);
		centred.AddChild(Chrome.Painted(column));

		Label title = Chrome.Line("Construction Complete", 34, Chrome.Cream);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(title);
		column.AddChild(title);

		var body = new HBoxContainer();
		body.AddThemeConstantOverride("separation", 28);
		column.AddChild(body);

		_picture = new TextureRect
		{
			CustomMinimumSize = PictureSize,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
			ClipContents = true,
		};
		body.AddChild(Chrome.Framed(_picture, 4));

		var reading = new VBoxContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
		};
		reading.AddThemeConstantOverride("separation", 14);
		body.AddChild(reading);

		reading.AddChild(Rule());
		_county = Chrome.Line("", 32, Chrome.Cream);
		_county.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_county);
		reading.AddChild(_county);
		reading.AddChild(Rule());

		_built = Said(reading);
		_garrison = Said(reading);
		_worth = Said(reading);

		var done = new Button { Text = "Done", CustomMinimumSize = new Vector2(260, 48), SizeFlagsHorizontal = SizeFlags.ShrinkCenter };
		done.AddThemeFontSizeOverride("font_size", 20);
		done.Pressed += Next;
		column.AddChild(done);
	}

	/// <summary>Walls the season finished, in the player's counties, each with the men standing on it.
	/// Nothing to show, and nothing happens.</summary>
	public void Announce(List<(string County, string Fort, int OnTheWalls)> raised)
	{
		if (raised.Count == 0)
		{
			return;
		}

		foreach ((string, string, int) wall in raised)
		{
			_waiting.Enqueue(wall);
		}

		if (!Visible)
		{
			Narrator.Say(CompleteVoicePath);
			Next();
		}
	}

	private void Next()
	{
		if (_waiting.Count == 0)
		{
			Tween tween = CreateTween();
			tween.TweenProperty(this, "modulate:a", 0.0, FadeSeconds);
			tween.TweenCallback(Callable.From(() => Visible = false));
			return;
		}

		(string county, string fort, int onTheWalls) = _waiting.Dequeue();
		Fortifications.Wall wall = Fortifications.Of(fort);
		_picture.Texture = FortArt.HasScene(fort) ? GD.Load<Texture2D>(FortArt.Scene(fort))
			: FortArt.Has(fort) ? GD.Load<Texture2D>(FortArt.Picture(fort)) : null;
		_county.Text = county;
		_built.Text = $"Work on the new {wall.Name.ToLowerInvariant()} in this county has been completed.";
		_garrison.Text = onTheWalls > 0
			? $"Its walls hold {wall.Garrison:N0} men, and {onTheWalls:N0} stand on them."
			: $"Its walls hold {wall.Garrison:N0} men. Nobody stands on them yet.";
		int raised = (wall.TaxBase - Fortifications.OpenGroundTaxBase) * 100 / Fortifications.OpenGroundTaxBase;
		_worth.Text = raised > 0
			? $"It will strengthen the county, and it pays {raised}% more tax than open ground at any rate."
			: "It will strengthen the county against whoever comes for it.";

		if (!Visible)
		{
			MoveToFront();
			Visible = true;
			CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeSeconds);
		}
	}

	private static Label Said(Container into)
	{
		Label line = Chrome.Line("", 19, Chrome.Soft);
		line.AutowrapMode = TextServer.AutowrapMode.Word;
		line.HorizontalAlignment = HorizontalAlignment.Center;
		into.AddChild(line);
		return line;
	}

	private static Control Rule()
	{
		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		return rule;
	}

	/// <summary>Escape moves on as Done does; a press on the dimmed map behind as well.</summary>
	public override void _GuiInput(InputEvent @event)
	{
		if (Visible && @event is InputEventMouseButton { Pressed: true })
		{
			Next();
			AcceptEvent();
		}
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (Visible && @event.IsActionPressed("ui_cancel"))
		{
			Next();
			GetViewport().SetInputAsHandled();
		}
	}
}
