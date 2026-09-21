using System;
using Godot;

/// <summary>Scene switching without the audio hiccup: every page carries a multi-megabyte
/// video background plus its art, and loading that on the main thread stalls the frame long
/// enough for the music buffer to run dry. This loads on a worker thread instead and only
/// swaps once the scene is ready, so the stall never happens.</summary>
public static class SceneRouter
{
	private static string _pendingPath;

	public static void GoTo(Node caller, string scenePath)
	{
		if (_pendingPath != null)
		{
			return; // a switch is already in flight; ignore the extra click
		}

		_pendingPath = scenePath;
		SceneTree tree = caller.GetTree();
		ResourceLoader.LoadThreadedRequest(scenePath);

		Action poll = null;
		poll = () =>
		{
			if (ResourceLoader.LoadThreadedGetStatus(scenePath) == ResourceLoader.ThreadLoadStatus.InProgress)
			{
				return;
			}

			tree.ProcessFrame -= poll;
			_pendingPath = null;
			var scene = ResourceLoader.LoadThreadedGet(scenePath) as PackedScene;
			if (scene == null)
			{
				GD.PushError($"SceneRouter: failed to load {scenePath}");
				return;
			}

			// A line belongs to the screen that started it: leaving cuts it off, the way it did
			// when every page carried a player of its own that died with the page.
			Narrator.Hush();
			tree.ChangeSceneToPacked(scene);
		};
		tree.ProcessFrame += poll;
	}
}
