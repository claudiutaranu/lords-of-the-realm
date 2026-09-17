using Godot;

/// <summary>Autoload. Shuffles the game's music tracks back-to-back on the Music bus, independent of scene changes.</summary>
public partial class MusicPlayer : AudioStreamPlayer
{
	private static readonly string[] TrackPaths =
	{
		"res://assets/audio/dawn-over-the-kingdom.mp3",
		"res://assets/audio/dawn-over-the-wall.mp3",
		"res://assets/audio/march-of-the-brave.mp3",
	};

	private readonly RandomNumberGenerator _rng = new();
	private int[] _playOrder;
	private int _playIndex;

	public override void _Ready()
	{
		Bus = Settings.MusicBus;
		Finished += PlayNextTrack;
		ShufflePlayOrder();
		LoadTrackAt(_playIndex);
	}

	/// <summary>Starts the playlist if it isn't already running, so re-entering the menu doesn't restart it.</summary>
	public void EnsurePlaying()
	{
		if (!Playing)
		{
			Play();
		}
	}

	private void PlayNextTrack()
	{
		_playIndex++;
		if (_playIndex >= _playOrder.Length)
		{
			ShufflePlayOrder();
		}

		LoadTrackAt(_playIndex);
		Play();
	}

	private void LoadTrackAt(int index)
	{
		var track = GD.Load<AudioStreamMP3>(TrackPaths[_playOrder[index]]);
		track.Loop = false;
		Stream = track;
	}

	private void ShufflePlayOrder()
	{
		_playOrder ??= new int[TrackPaths.Length];
		for (int i = 0; i < _playOrder.Length; i++)
		{
			_playOrder[i] = i;
		}

		for (int i = _playOrder.Length - 1; i > 0; i--)
		{
			int j = _rng.RandiRange(0, i);
			(_playOrder[i], _playOrder[j]) = (_playOrder[j], _playOrder[i]);
		}

		_playIndex = 0;
	}
}
