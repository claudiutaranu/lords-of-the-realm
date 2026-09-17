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

	private const string ConfigPath = "user://settings.cfg";
	private const string Section = "settings";
	private const float DefaultVolume = 0.8f;
	private const float SilenceFloor = 0.0001f;

	private static readonly string[] Buses = { MasterBus, MusicBus, SfxBus };
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

		foreach (string bus in Buses)
		{
			ApplyVolume(bus);
		}
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
