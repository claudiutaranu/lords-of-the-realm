using Godot;
using Godot.Collections;

/// <summary>Transition screen between any two scenes. The caller sets
/// <see cref="TargetScenePath"/> then switches here; this page loads that scene on a
/// background thread and swaps to it once BOTH the load is done and a minimum time has
/// elapsed, so a scene that loads instantly (like today's tiny campaign map) still reads
/// as a real loading screen instead of a one-frame flash.</summary>
public partial class LoadingPage : Control
{
	private const float MinimumDisplaySeconds = 1.8f;
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";

	public static string TargetScenePath;

	private double _elapsed;

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		ResourceLoader.LoadThreadedRequest(TargetScenePath);
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;

		var progress = new Array();
		var status = ResourceLoader.LoadThreadedGetStatus(TargetScenePath, progress);
		float loadFraction = progress.Count > 0 ? (float)progress[0] : 0f;
		float timeFraction = Mathf.Clamp((float)(_elapsed / MinimumDisplaySeconds), 0f, 1f);
		GetNode<ProgressBar>("%ProgressBar").Value = Mathf.Min(loadFraction, timeFraction) * 100.0;

		// A scene that can't load used to leave this screen spinning forever; say so and go back
		// to the menu instead, where the player can at least do something.
		if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
		{
			SetProcess(false);
			GD.PushError($"LoadingPage: {TargetScenePath} failed to load ({status}).");
			SceneRouter.GoTo(this, MainMenuScenePath);
			return;
		}

		if (status == ResourceLoader.ThreadLoadStatus.Loaded && _elapsed >= MinimumDisplaySeconds)
		{
			SetProcess(false);
			var scene = (PackedScene)ResourceLoader.LoadThreadedGet(TargetScenePath);
			GetTree().ChangeSceneToPacked(scene);
		}
	}
}
