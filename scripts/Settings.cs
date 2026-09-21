using System;
using Godot;

/// <summary>Autoload. Owns persisted settings and applies them to the engine.</summary>
public partial class Settings : Node
{
	public const string MasterBus = "Master";
	public const string MusicBus = "Music";
	public const string SfxBus = "SFX";

	public static readonly Vector2I[] Resolutions =
	{
		new(1280, 720),
		new(1600, 900),
		new(1920, 1080),
		new(2560, 1440),
	};

	/// <summary>The pointer: a sword drawn pointing up and to the left, the way a cursor has to. Its
	/// own piece of art rather than the stats bar's icon turned round, and cropped to the blade —
	/// the padding and the drop shadow a picture is drawn with are a fifth of the height, and at
	/// forty-eight pixels every one of them has to be sword.</summary>
	private const string CursorIconPath = "res://assets/ui/cursor-sword.png";
	private const int CursorSize = 48;

	/// <summary>Where the point of the blade falls in that art, as a share of it: the very top, a
	/// hair in from the left. This is the hotspot — the pixel the click actually happens at — and a
	/// cursor that clicks from its middle makes every button in the game feel out of place.</summary>
	private static readonly Vector2 CursorTip = new(0.03f, 0f);

	private const string ConfigPath = "user://settings.cfg";
	private const string Section = "settings";
	private const float DefaultVolume = 0.8f;
	private const float SilenceFloor = 0.0001f;

	private static readonly string[] Buses = { MasterBus, MusicBus, SfxBus };

	/// <summary>The cursor's texture, kept so it can be handed back on the way out.</summary>
	private static ImageTexture _pointer;
	private static readonly ConfigFile Config = new();

	public static int ResolutionIndex
	{
		get => Read("resolution", LargestResolutionThatFits());
		set
		{
			Write("resolution", value);
			ApplyWindow();
		}
	}

	public static bool IsFullscreen
	{
		get => Read("fullscreen", false);
		set
		{
			Write("fullscreen", value);
			ApplyWindow();
		}
	}

	public static bool IsVsyncEnabled
	{
		get => Read("vsync", true);
		set
		{
			Write("vsync", value);
			ApplyVsync();
		}
	}

	public static float GetVolume(string bus) => Read($"volume_{bus}", DefaultVolume);

	public static void SetVolume(string bus, float level)
	{
		Write($"volume_{bus}", level);
		ApplyVolume(bus);
	}

	public override void _Ready()
	{
		Config.Load(ConfigPath);
		ApplyWindow();
		ApplyVsync();
		ApplyCursor();

		foreach (string bus in Buses)
		{
			ApplyVolume(bus);
		}
	}

	/// <summary>Handed back before the renderer goes. The engine holds the cursor texture past the
	/// point where it can free one, so leaving it in place prints a leaked-texture warning on every
	/// single quit — and a console that cries wolf at shutdown is one nobody reads on the day it has
	/// something to say.</summary>
	public override void _ExitTree()
	{
		Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
		Input.SetCustomMouseCursor(null, Input.CursorShape.PointingHand);

		// Handed back AND let go of. Clearing the cursor is not enough on its own: the texture is a
		// reference-counted resource whose last reference here is a managed object, and waiting for
		// the garbage collector to notice means the engine shuts down with it still held.
		_pointer?.Dispose();
		_pointer = null;
	}

	private static void ApplyCursor()
	{
		Image sword = GD.Load<Texture2D>(CursorIconPath).GetImage();
		sword.Resize(CursorSize, CursorSize, Image.Interpolation.Lanczos);

		_pointer = ImageTexture.CreateFromImage(sword);
		Vector2 tip = CursorTip * CursorSize;

		// Both shapes, so nothing in the game can hand the player the system arrow back: a control
		// that asks for the pointing hand gets the same sword rather than a white glove from another
		// century.
		Input.SetCustomMouseCursor(_pointer, Input.CursorShape.Arrow, tip);
		Input.SetCustomMouseCursor(_pointer, Input.CursorShape.PointingHand, tip);
	}

	private static T Read<[MustBeVariant] T>(string key, T fallback) =>
		Config.GetValue(Section, key, Variant.From(fallback)).As<T>();

	private static void Write<[MustBeVariant] T>(string key, T value)
	{
		Config.SetValue(Section, key, Variant.From(value));
		Config.Save(ConfigPath);
	}

	/// <summary>Keeps the first run from opening a window larger than the monitor.</summary>
	private static int LargestResolutionThatFits()
	{
		Vector2I screen = DisplayServer.ScreenGetSize();
		int index = Array.FindLastIndex(Resolutions, size => size.X <= screen.X && size.Y <= screen.Y);
		return index < 0 ? 0 : index;
	}

	private static void ApplyWindow()
	{
		DisplayServer.WindowSetMode(IsFullscreen
			? DisplayServer.WindowMode.Fullscreen
			: DisplayServer.WindowMode.Windowed);

		if (IsFullscreen)
		{
			return;
		}

		Vector2I size = Resolutions[Mathf.Clamp(ResolutionIndex, 0, Resolutions.Length - 1)];
		DisplayServer.WindowSetSize(size);
		DisplayServer.WindowSetPosition((DisplayServer.ScreenGetSize() - size) / 2);
	}

	private static void ApplyVsync() =>
		DisplayServer.WindowSetVsyncMode(IsVsyncEnabled
			? DisplayServer.VSyncMode.Enabled
			: DisplayServer.VSyncMode.Disabled);

	private static void ApplyVolume(string bus) =>
		AudioServer.SetBusVolumeDb(
			AudioServer.GetBusIndex(bus),
			Mathf.LinearToDb(Mathf.Max(GetVolume(bus), SilenceFloor)));
}
