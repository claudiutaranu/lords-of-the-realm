using Godot;

/// <summary>The tramp of men on the march, heard while anyone is walking and let go when the last of
/// them halts: faded in and out rather than started and cut, so a company that stops and starts
/// again does not stutter it. Whoever sets men walking says so (<see cref="Join"/>, <see cref="Leave"/>)
/// or says how many are (<see cref="Walking"/>), and the one sound stands for all of them.</summary>
public partial class MarchingSound : AudioStreamPlayer
{
	/// <summary>How quickly it swells and dies away, in decibels a second, and how quiet counts as
	/// gone.</summary>
	private const float FadeRate = 30f;
	private const float Silent = -60f;

	private int _walking;
	private float _hurry;

	/// <summary>How much quicker and louder the tramp is when every man moving is running rather than
	/// walking: the same feet, at a run's pace.</summary>
	private const float RunPitch = 1.35f;
	private const float RunLouder = 4f;

	/// <summary>What is heard: the field's tramp unless its owner says otherwise before it is added
	/// (the map has a march of its own, taken off a film, looped by blending its last second into
	/// its first).</summary>
	public string Sound { get; init; } = "res://assets/audio/marching.mp3";

	/// <summary>How loud it is heard while men are walking, in decibels: quiet, under whatever else is
	/// playing. Set by whoever owns it, and changed as it likes (the battlefield hushes it with its eye).</summary>
	public float Loudness { get; set; } = -14f;

	public override void _Ready()
	{
		Bus = Settings.SfxBus;
		var tramp = GD.Load<AudioStreamMP3>(Sound);
		tramp.Loop = true;
		Stream = tramp;
		VolumeDb = Silent;
	}

	/// <summary>One more company has set off.</summary>
	public void Join() => _walking++;

	/// <summary>One company has halted.</summary>
	public void Leave() => _walking = Mathf.Max(0, _walking - 1);

	/// <summary>How many are walking right now, for an owner that counts them itself every frame, and
	/// how many of those are running.</summary>
	public void Walking(int count, int running = 0)
	{
		_walking = count;
		_hurry = count > 0 ? running / (float)count : 0f;
	}

	public override void _Process(double delta)
	{
		float wanted = _walking > 0 ? Loudness + (RunLouder * _hurry) : Silent;
		VolumeDb = Mathf.MoveToward(VolumeDb, wanted, FadeRate * (float)delta);
		PitchScale = Mathf.MoveToward(PitchScale, Mathf.Lerp(1f, RunPitch, _hurry), (float)delta);
		if (VolumeDb > Silent && !Playing)
		{
			Play();
		}
		else if (VolumeDb <= Silent && Playing)
		{
			Stop();
		}
	}
}
