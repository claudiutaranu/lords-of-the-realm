using System.Collections.Generic;
using Godot;

/// <summary>Lords of the Realm's way of pointing at something without a word: the peasant on the
/// labour bar ringed in blue while men stand idle, and the cow or the loaf ringed in red while its
/// work is short of hands. The ring is drawn once into a copy of the picture and kept.</summary>
public static class Stroke
{
	public static readonly Color Idle = new("5a9cff");
	public static readonly Color Wanting = new("e0402e");

	/// <summary>How thick the ring reads on screen, whatever size the picture was painted at.</summary>
	private const int ScreenPixels = 2;

	private static readonly Dictionary<(ulong, Color), Texture2D> Kept = new();

	/// <summary>The picture with a ring round everything painted in it. The copy is padded by the
	/// ring's width, since a figure painted to the edge of its square would lose it there.
	/// <paramref name="shownAt"/> is the side it is drawn at, so a 128-pixel icon shown at 30 gets a
	/// ring thick enough to survive the shrinking.</summary>
	public static Texture2D Ringed(Texture2D picture, Color colour, int shownAt)
	{
		if (Kept.TryGetValue((picture.GetRid().Id, colour), out Texture2D kept))
		{
			return kept;
		}

		Image source = picture.GetImage();
		if (source.IsCompressed())
		{
			source.Decompress();
		}

		source.Convert(Image.Format.Rgba8);
		int width = Mathf.Max(1, Mathf.CeilToInt((float)ScreenPixels * source.GetWidth() / Mathf.Max(1, shownAt)));
		int w = source.GetWidth() + 2 * width;
		int h = source.GetHeight() + 2 * width;
		Image ringed = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
		ringed.BlitRect(source, new Rect2I(0, 0, source.GetWidth(), source.GetHeight()), new Vector2I(width, width));

		bool Solid(int x, int y) =>
			x >= width && y >= width && x < w - width && y < h - width && source.GetPixel(x - width, y - width).A >= 0.5f;

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				if (Solid(x, y))
				{
					continue;
				}

				bool near = false;
				for (int dy = -width; dy <= width && !near; dy++)
				{
					for (int dx = -width; dx <= width && !near; dx++)
					{
						near = dx * dx + dy * dy <= width * width && Solid(x + dx, y + dy);
					}
				}

				if (near)
				{
					ringed.SetPixel(x, y, colour);
				}
			}
		}

		Texture2D made = ImageTexture.CreateFromImage(ringed);
		Kept[(picture.GetRid().Id, colour)] = made;
		return made;
	}
}
