using Godot;

/// <summary>The film the game opens on, before the main menu. It plays once, at launch, and any
/// click or key cuts it short — a lord who has seen it before should not have to sit through it
/// again to reach his realm.</summary>
public partial class IntroVideo : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";

	public override void _Ready()
	{
		var film = GetNode<VideoStreamPlayer>("Film");
		film.Finished += Leave;
		film.Play();
	}

	public override void _Input(InputEvent @event)
	{
		bool isSkip = @event is InputEventMouseButton { Pressed: true } or InputEventKey { Pressed: true, Echo: false };
		if (isSkip)
		{
			GetViewport().SetInputAsHandled();
			Leave();
		}
	}

	/// <summary>SceneRouter ignores a second call while the menu is loading, so a click on the last
	/// frame and the film's own end do not change scene twice.</summary>
	private void Leave()
	{
		GetNode<VideoStreamPlayer>("Film").Stop();
		SceneRouter.GoTo(this, MainMenuScenePath);
	}
}
