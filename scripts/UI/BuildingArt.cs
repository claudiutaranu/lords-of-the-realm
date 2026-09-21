using System;
using Godot;

/// <summary>One building standing on the province's land: its picture, whatever part of it turns,
/// and the light it wears when the player has it in hand.
///
/// A building is drawn as a still base with an optional moving piece laid over it at a fixed spot —
/// a windmill's sails, a water wheel, a crane. The moving piece is a pre-rendered loop on a sheet,
/// played by walking an atlas window across it, which keeps the whole thing inside the Control tree
/// the rest of the page is laid out in rather than dropping a Node2D into the middle of it.
///
/// Two of the same building must not turn in step, so each starts its loop somewhere of its own.</summary>
public partial class BuildingArt : Control
{
	private const string GlowShaderPath = "res://assets/shaders/building-glow.gdshader";

	/// <summary>Where a building's moving piece sits and how it is played. Read off buildings.json,
	/// in the base picture's own pixels, so the page can scale the pair together without either of
	/// them being told what scale it ended up at.</summary>
	public record Rotor(Vector2 At, Vector2 Frame, int Columns, int Frames, float Fps);

	/// <summary>How wide the building is against its own height. The page anchors a building by a
	/// fraction of the land's height, which says nothing about how wide that comes out on a window
	/// of unknown shape, so the width is worked back out from the height every time it changes.</summary>
	private float _aspect = 1f;

	/// <summary>Raised when the player presses the building itself. The plaque over it is not the
	/// only way to take a site in hand — the obvious thing to press is the thing you are looking
	/// at.</summary>
	public event Action Pressed;

	private Image _shape;
	private Node2D _life;
	private TextureRect _glow;
	private TextureRect _rotor;
	private AtlasTexture _window;
	private Rotor _turning;
	private float _at;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Stop;
	}

	/// <summary>Answers to presses on the building rather than on the box around it. The plots are
	/// drawn on the diagonal and their corners overlap, so a plot judged by its box swallows presses
	/// meant for whatever stands in front of it.</summary>
	public override bool _HasPoint(Vector2 point)
	{
		if (_shape == null || Size.X <= 0f || Size.Y <= 0f)
		{
			return false;
		}

		var at = new Vector2I(
			Mathf.FloorToInt(point.X / Size.X * _shape.GetWidth()),
			Mathf.FloorToInt(point.Y / Size.Y * _shape.GetHeight()));

		return at.X >= 0 && at.Y >= 0 && at.X < _shape.GetWidth() && at.Y < _shape.GetHeight()
			&& _shape.GetPixelv(at).A > 0.2f;
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			AcceptEvent();
			Pressed?.Invoke();
		}
	}

	/// <summary>Builds the layers: the light underneath, the building, whatever turns on it, and
	/// whatever lives on it.</summary>
	public void Show(Texture2D baseArt, Texture2D rotorSheet, Rotor turning, PackedScene life)
	{
		_aspect = baseArt.GetSize().X / baseArt.GetSize().Y;
		_shape = baseArt.GetImage();

		// Under the building rather than over it, so the rim reads as light coming from behind the
		// thing rather than as a line drawn on top of it.
		_glow = Layer(baseArt);
		var light = new ShaderMaterial { Shader = GD.Load<Shader>(GlowShaderPath) };
		// In the picture's own pixels, so a rim that reads at one size reads at every size. The art
		// is installed with transparent margin around it to give the outermost ring somewhere to go.
		light.SetShaderParameter("width", 62f);
		light.SetShaderParameter("strength", 0.62f);
		_glow.Material = light;
		_glow.Visible = false;

		TextureRect painted = Layer(baseArt);

		// Whatever lives on the ground goes on last, over everything painted. It is a Node2D in a
		// Control tree on purpose: each animal keeps its own clock and its own place on the plot, and
		// laying them out with containers would mean laying out a herd.
		if (life != null)
		{
			_life = life.Instantiate<Node2D>();
			AddChild(_life);

			// Some grounds are not still: a banner over a building site waves, so the site itself is
			// painted inside the living scene rather than under it. The still picture is still
			// wanted — it is what the light behind the building is cut from — so it stays as the
			// glow and only the painted copy of it goes.
			painted.Visible = _life.GetNodeOrNull("Ground") == null;
		}

		if (rotorSheet == null || turning == null)
		{
			return;
		}

		_turning = turning;
		_window = new AtlasTexture
		{
			Atlas = rotorSheet,
			Region = new Rect2(Vector2.Zero, turning.Frame),
		};

		_rotor = Layer(_window);
		_rotor.SetAnchorsPreset(LayoutPreset.TopLeft);

		// Two windmills side by side turning in lockstep read as one picture drawn twice.
		_at = GD.Randf() * turning.Frames;
		SetProcess(true);
	}

	/// <summary>How much of the plot's gang is out on it, from nobody to all of them.
	///
	/// Lords of the Realm draws the peasants themselves on each piece of ground, and that is the
	/// whole reading of the county at a glance: a quarry with three men in it and one with thirty
	/// are different counties, and you should not have to find the number to know which you are
	/// looking at. The ground stays whatever the season made it; only the men come and go.</summary>
	public void Crew(float share)
	{
		Node gang = _life?.GetNodeOrNull("Workers") ?? _life?.GetNodeOrNull("People");
		if (gang == null)
		{
			return;
		}

		int hands = gang.GetChildCount();

		// Rounded up, so one man put on a plot is one man seen on it: a plot that is being worked at
		// all must not read as empty just because it is barely worked.
		int out_ = Mathf.Clamp(Mathf.CeilToInt(hands * share), 0, hands);
		for (int man = 0; man < hands; man++)
		{
			if (gang.GetChild(man) is CanvasItem figure)
			{
				figure.Visible = man < out_;
			}
		}
	}

	/// <summary>Lights the building, or puts it out.</summary>
	public void Light(bool lit)
	{
		if (_glow != null)
		{
			_glow.Visible = lit;
		}
	}

	public override void _Process(double delta)
	{
		if (_turning == null)
		{
			return;
		}

		_at = Mathf.PosMod(_at + (float)delta * _turning.Fps, _turning.Frames);
		int frame = Mathf.FloorToInt(_at);
		_window.Region = new Rect2(
			new Vector2(frame % _turning.Columns, frame / _turning.Columns) * _turning.Frame,
			_turning.Frame);
	}

	/// <summary>Squares the building's width to its height, and keeps the moving piece on its own
	/// roof — both of which change whenever the window does.</summary>
	public override void _Notification(int what)
	{
		if (what != NotificationResized || _glow == null)
		{
			return;
		}

		float half = Size.Y * _aspect / 2f;
		if (Mathf.Abs(OffsetLeft + half) > 0.5f)
		{
			OffsetLeft = -half;
			OffsetRight = half;
			return; // the change resizes us again, and the rest is done on that pass
		}

		Place(((TextureRect)GetChild(1)).Texture.GetSize());
	}

	private void Place(Vector2 baseSize)
	{
		Vector2 scale = Size / baseSize;
		if (_rotor != null && _turning != null)
		{
			_rotor.Position = _turning.At * scale;
			_rotor.Size = _turning.Frame * scale;
		}

		// Everything living on the plot is placed in the base picture's own pixels, so one scale
		// puts the whole herd where it belongs however large the plot is drawn.
		if (_life != null)
		{
			_life.Scale = scale;
		}
	}

	private TextureRect Layer(Texture2D texture)
	{
		var layer = new TextureRect
		{
			Texture = texture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.Scale,
			MouseFilter = MouseFilterEnum.Ignore,
		};

		AddChild(layer);
		Chrome.Fill(layer);
		return layer;
	}
}
