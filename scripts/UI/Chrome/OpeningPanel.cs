using Godot;

/// <summary>The page a campaign opens on: the king on his balcony over the valley, in the royal
/// frame, the season on its ribbon and the realm's own shield in the crest, and under it the
/// briefing. Everything is laid out in the frame painting's own pixels and the whole is scaled to
/// the window, so the words stay on the cloth they were measured for at any size.</summary>
public partial class OpeningPanel : Control
{
	private const string FramePath = "res://assets/ui/opening-frame.png";
	private const string ViewPath = "res://assets/ui/opening-view.png";

	/// <summary>The frame painting's size, and where its parts lie in it.</summary>
	private static readonly Vector2 Painting = new(1448, 1086);
	private static readonly Rect2 Window = new(45, 120, 1360, 528);
	private static readonly Rect2 Crest = new(668, 50, 112, 126);
	private static readonly Rect2 Ribbon = new(380, 170, 690, 66);
	private static readonly Rect2 Heading = new(260, 676, 928, 48);
	private static readonly Rect2 Text = new(270, 728, 908, 220);
	private static readonly Rect2 Begin = new(554, 954, 340, 54);

	/// <summary>How much of the window the frame may take: room left round it for the map to show.</summary>
	private const float MostOfWidth = 0.72f;
	private const float MostOfHeight = 0.72f;

	private static readonly Color Gold = new("e8c873");

	private Control _painting;

	/// <summary>The sidebar's width down the right of the window: the frame stands over the map, not
	/// under the sidebar.</summary>
	private float _besides;

	public void Open(string season, string welcome, string body, Texture2D crest, float besides)
	{
		_besides = besides;
		SetAnchorsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop; // the map waits until he has read it

		var dusk = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = MouseFilterEnum.Ignore };
		dusk.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(dusk);

		_painting = new Control { Size = Painting, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_painting);

		Place(new TextureRect
		{
			Texture = GD.Load<Texture2D>(ViewPath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		}, Window);
		Place(new TextureRect
		{
			Texture = GD.Load<Texture2D>(FramePath),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		}, new Rect2(Vector2.Zero, Painting));
		Place(new TextureRect
		{
			Texture = crest,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		}, Crest);

		Label title = Words(season, 44, Gold, Ribbon);
		GoldTitle.Apply(title);
		Words(welcome.ToUpperInvariant(), 30, Gold, Heading);
		Label told = Words(body, 19, Chrome.Bright, Text, wraps: true);
		told.VerticalAlignment = VerticalAlignment.Top;

		Button begin = Chrome.Order("Begin", "", QueueFree, out _);
		Place(begin, Begin);

		Fit();
		GetViewport().SizeChanged += Fit;
	}

	public override void _ExitTree()
	{
		if (_painting != null)
		{
			GetViewport().SizeChanged -= Fit;
		}
	}

	/// <summary>Scales the painting to the window and stands it in the middle.</summary>
	private void Fit()
	{
		Vector2 window = GetViewportRect().Size - new Vector2(_besides, 0);
		float scale = Mathf.Min(window.X * MostOfWidth / Painting.X, window.Y * MostOfHeight / Painting.Y);
		_painting.Scale = new Vector2(scale, scale);
		_painting.Position = (window - (Painting * scale)) / 2f;
	}

	/// <summary>A wrapping label is told so before it is sized: sized first, it takes the whole text
	/// on one line as its width and keeps it.</summary>
	private Label Words(string text, int size, Color colour, Rect2 where, bool wraps = false)
	{
		Label label = Chrome.Line(text, size, colour);
		label.AutowrapMode = wraps ? TextServer.AutowrapMode.Word : TextServer.AutowrapMode.Off;
		label.HorizontalAlignment = HorizontalAlignment.Center;
		label.VerticalAlignment = VerticalAlignment.Center;
		Place(label, where);
		return label;
	}

	private void Place(Control part, Rect2 where)
	{
		part.Position = where.Position;
		part.Size = where.Size;
		_painting.AddChild(part);
	}
}
