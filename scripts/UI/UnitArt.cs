using Godot;

/// <summary>Where a soldier's pictures are, and which ground he stands on.
///
/// Soldiers belong to the engine rather than to a campaign — every realm musters the same six — so
/// all of this lives under assets/units and is keyed by the weapon the man carries, the same key
/// weapons.json and the weapon glyphs use. One key names his portrait, his backdrop and his icon.
///
/// The backdrops are shared: three scenes for six soldiers, because an archer and a crossbowman
/// train on the same ground.</summary>
public static class UnitArt
{
	private const string Directory = "res://assets/units";

	/// <summary>The one yard every man on the roster stands in, and how he is cut to stand in it. One
	/// ground for all of them on purpose — the roster is a list of who is here, and seven different
	/// skies behind seven rows makes it a gallery instead.
	///
	/// The tile's shape is the crop: the portraits are full-length, and as much of one as a tile
	/// this wide can hold is the man from the waist up. A row three fingers high has no business
	/// showing a soldier's boots.</summary>
	private const string RosterGround = $"{Directory}/backgrounds/courtyard.jpg";

	/// <summary>How much sky is left over a man's head when the tile is cut.</summary>
	private const int HeadRoom = 24;

	public static string Portrait(string unit) => $"{Directory}/{Drawn(unit)}.png";

	public static string Backdrop(string unit) => $"{Directory}/backgrounds/{Ground(Drawn(unit))}.jpg";

	/// <summary>Whose picture a kind wears: a hired band is painted as the kind it fights as, since
	/// nobody has painted a Swiss.</summary>
	private static string Drawn(string unit) => Mercenaries.Find(unit)?.Unit ?? unit;

	private static string Ground(string unit) => unit switch
	{
		"bow" or "crossbow" => "training-ground",  // the butts they shoot at
		"horse" or "peasant" => "meadow",          // the field: one charges over it, one works it
		_ => "courtyard",                          // foot soldiers muster inside the walls
	};

	/// <summary>A soldier, standing on ground rather than on nothing: the yard behind him and the
	/// man himself over it, cut off at the belt so the row shows a face and not a full-length figure
	/// three fingers high. The portraits are cut-outs, so without the yard the row would be a man
	/// hanging in the dark.</summary>
	public static Control Tile(string unit, Vector2 size)
	{
		var tile = new PanelContainer { ClipContents = true, CustomMinimumSize = size };
		tile.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		tile.AddChild(Fill(GD.Load<Texture2D>(RosterGround)));

		string painted = Portrait(unit);
		if (ResourceLoader.Exists(painted))
		{
			var man = GD.Load<Texture2D>(painted);
			float tall = man.GetWidth() * size.Y / size.X;
			tile.AddChild(Fill(new AtlasTexture
			{
				Atlas = man,
				Region = new Rect2(0, Mathf.Min(HeadTop(man), man.GetHeight() - tall), man.GetWidth(), tall),
			}));
		}

		return Chrome.Framed(tile, 2);
	}

	/// <summary>How far down the man's head begins, so the tile is cut just above it instead of at
	/// the top of the picture. He stands at a different height in every portrait — a rider's head is
	/// near the top edge, a peasant's a fifth of the way down — so this is read off the picture
	/// rather than written down beside it, and art that is redrawn cannot drift from a number nobody
	/// remembers.
	///
	/// Only the middle of the frame is looked at, and coarsely: a spear held upright is not a head,
	/// and every fiftieth pixel says where a man starts as well as all of them would.</summary>
	private static int HeadTop(Texture2D portrait)
	{
		Image drawn = portrait.GetImage();
		int from = drawn.GetWidth() * 35 / 100;
		int to = drawn.GetWidth() * 68 / 100;
		for (int y = 0; y < drawn.GetHeight(); y += 2)
		{
			int solid = 0;
			for (int x = from; x < to; x += 3)
			{
				solid += drawn.GetPixel(x, y).A > 0.25f ? 1 : 0;
			}

			// A sixth of the band covered is a head and not the fringe of a banner behind him.
			if (solid * 18 > to - from)
			{
				return Mathf.Max(0, y - HeadRoom);
			}
		}

		return 0;
	}

	/// <summary>One layer of the tile. A PanelContainer lays every child over the same rect, so the
	/// ground goes in first and the man stands on it.</summary>
	private static TextureRect Fill(Texture2D texture) => new()
	{
		Texture = texture,
		ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
		StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		MouseFilter = Control.MouseFilterEnum.Ignore,
	};
}
