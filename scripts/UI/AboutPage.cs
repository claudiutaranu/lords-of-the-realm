using Godot;

public partial class AboutPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const float AutoScrollPixelsPerSecond = 35f;

	private ScrollContainer _scrollArea;
	private bool _isHovering;

	public override void _Ready()
	{
		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, MainMenuScenePath);
		GetNode<Button>("%BackButton").GrabFocus();

		GoldTitle.Apply(GetNode<Label>("Center/Panel/Margin/Content/ScrollArea/ScrollContent/Title"));

		_scrollArea = GetNode<ScrollContainer>("%ScrollArea");
		_scrollArea.MouseEntered += () => _isHovering = true;
		_scrollArea.MouseExited += () => _isHovering = false;
	}

	// ScrollVertical clamps itself to the content's scroll range, so no bounds-checking here.
	public override void _Process(double delta)
	{
		if (_isHovering)
		{
			return;
		}

		_scrollArea.ScrollVertical += Mathf.RoundToInt(AutoScrollPixelsPerSecond * (float)delta);
	}
}
