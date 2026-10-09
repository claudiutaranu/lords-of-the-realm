using Godot;

/// <summary>A lord's face, moving: assets/video/lords/&lt;key&gt;.ogv, played once, silent, and held on
/// its last look (the user's call: a face that never stops moving is a face nobody reads), in a gold edge.
/// Lords are the game's and not a campaign's, so a portrait is keyed by the lord. A lord nobody has
/// painted yet has no portrait, and the card goes on without one; one painted but not yet filmed
/// shows his painting (assets/video/lords/&lt;key&gt;.png), still.</summary>
public static partial class LordPortrait
{
	private static string PathOf(string lord) => $"res://assets/video/lords/{lord}.ogv";
	private static string StillOf(string lord) => $"res://assets/video/lords/{lord}.png";

	/// <summary>The portrait <paramref name="side"/> pixels square, or null where there is none.</summary>
	public static Control Of(Lord lord, int side)
	{
		return Moving(lord, side) is Control face ? Framed(face) : null;
	}

	/// <summary>A face in the gilt edge every portrait wears.</summary>
	public static Control Framed(Control face)
	{
		var frame = new PanelContainer
		{
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		var edge = new StyleBoxFlat { BgColor = Colors.Black, BorderColor = new Color(0.78f, 0.62f, 0.32f) };
		edge.SetBorderWidthAll(3);
		frame.AddThemeStyleboxOverride("panel", edge);
		frame.AddChild(face);
		return frame;
	}

	/// <summary>The face alone, unframed, for a table that hangs its own frame round it; null where
	/// there is none.</summary>
	public static Control Moving(Lord lord, int side) =>
		lord == null ? null : Moving(PathOf(lord.Key), side) ?? Still(StillOf(lord.Key), side);

	private static Control Still(string painting, int side) => !ResourceLoader.Exists(painting) ? null : new TextureRect
	{
		Texture = GD.Load<Texture2D>(painting),
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		CustomMinimumSize = new Vector2(side, side),
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};

	/// <summary>Any face, by its film; null where there is no such film.</summary>
	public static Control Moving(string film, int side)
	{
		if (!ResourceLoader.Exists(film))
		{
			return null;
		}

		return new Once
		{
			Stream = GD.Load<VideoStream>(film),
			Expand = true,
			Autoplay = true,
			VolumeDb = -80f,
			CustomMinimumSize = new Vector2(side, side),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
	}

	/// <summary>Plays to its last frame and stops there. Left to finish, a player shows nothing; so it
	/// pauses itself a frame short of the end instead.</summary>
	private partial class Once : VideoStreamPlayer
	{
		private const double LastFrame = 0.06;

		public override void _Process(double delta)
		{
			double length = GetStreamLength();
			if (length > 0 && IsPlaying() && !Paused && StreamPosition >= length - LastFrame)
			{
				Paused = true;
			}
		}
	}
}
