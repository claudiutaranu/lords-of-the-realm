using Godot;

/// <summary>Autoload. The one voice in the realm: the narrator over the map, the advisor with his
/// bad news, the steward reading a rung of wall. Everything spoken goes through here, so a new line
/// cuts the one before it instead of talking over it — two men reading different sentences at once
/// is not two lines heard, it is neither.
///
/// One player rather than one per screen, because the screens do not know about each other: the
/// advisor speaks from over whatever the lord has open, and the panel underneath has no way to know
/// it should shut up.</summary>
public partial class Narrator : AudioStreamPlayer
{
	private static Narrator _him;

	public override void _Ready()
	{
		_him = this;
		Bus = Settings.SfxBus;
	}

	/// <summary>Says a line, cutting whatever was being said. A line with no recording behind it is
	/// not spoken and not complained about: the writing arrives before the voice work does, and a
	/// screen that reads itself is the normal state of a line nobody has recorded yet.</summary>
	public static void Say(string path)
	{
		if (_him == null || path == null || path.Length == 0 || !ResourceLoader.Exists(path))
		{
			return;
		}

		_him.Stop();
		_him.Stream = GD.Load<AudioStream>(path);
		_him.Play();
	}

	/// <summary>Stops him mid-sentence.</summary>
	public static void Hush() => _him?.Stop();
}
