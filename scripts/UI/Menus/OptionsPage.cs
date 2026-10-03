using Godot;

public partial class OptionsPage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";

	/// <summary>Where Back leads. The campaign map sets it so options opened mid-run returns to
	/// the run; anyone else gets the main menu.</summary>
	public static string ReturnScenePath = MainMenuScenePath;

	public override void _Ready()
	{
		GoldTitle.Apply(GetNode<Label>("Center/Panel/Margin/Content/Title"));

		SetUpWindowMode();
		SetUpResolution();
		SetUpVsync();
		SetUpVolume(Settings.MasterBus, "%MasterSlider", "%MasterValue");
		SetUpVolume(Settings.MusicBus, "%MusicSlider", "%MusicValue");
		SetUpVolume(Settings.SfxBus, "%SfxSlider", "%SfxValue");

		string returnPath = ReturnScenePath;
		ReturnScenePath = MainMenuScenePath; // consumed, so the next visit isn't sent back into a campaign

		Button back = GetNode<Button>("%BackButton");
		back.Pressed += () => SceneRouter.GoTo(this, returnPath);
		back.GrabFocus();
	}

	private void SetUpWindowMode()
	{
		OptionButton option = GetNode<OptionButton>("%WindowModeOption");
		option.AddItem("Windowed");
		option.AddItem("Fullscreen");
		option.Selected = Settings.IsFullscreen ? 1 : 0;
		option.ItemSelected += index =>
		{
			Settings.IsFullscreen = index == 1;
			GetNode<OptionButton>("%ResolutionOption").Disabled = Settings.IsFullscreen;
		};
	}

	private void SetUpResolution()
	{
		OptionButton option = GetNode<OptionButton>("%ResolutionOption");
		foreach (Vector2I size in Settings.Resolutions)
		{
			option.AddItem($"{size.X} x {size.Y}");
		}

		option.Selected = Settings.ResolutionIndex;
		option.Disabled = Settings.IsFullscreen;
		option.ItemSelected += index => Settings.ResolutionIndex = (int)index;
	}

	private void SetUpVsync()
	{
		Button toggle = GetNode<Button>("%VsyncButton");
		toggle.ButtonPressed = Settings.IsVsyncEnabled;
		toggle.Text = Label(Settings.IsVsyncEnabled);
		toggle.Toggled += isEnabled =>
		{
			Settings.IsVsyncEnabled = isEnabled;
			toggle.Text = Label(isEnabled);
		};

		static string Label(bool isEnabled) => isEnabled ? "On" : "Off";
	}

	private void SetUpVolume(string bus, string sliderPath, string valuePath)
	{
		HSlider slider = GetNode<HSlider>(sliderPath);
		Label readout = GetNode<Label>(valuePath);

		slider.Value = Settings.GetVolume(bus);
		readout.Text = Percent(slider.Value);
		slider.ValueChanged += level =>
		{
			Settings.SetVolume(bus, (float)level);
			readout.Text = Percent(level);
		};

		static string Percent(double level) => $"{Mathf.RoundToInt(level * 100)}%";
	}
}
