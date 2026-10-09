using Godot;

/// <summary>Lists the saves written from the campaign's crest menu, newest first. Picking one
/// hands it to <see cref="SaveGame.Pending"/> and goes to the map, which rebuilds its state
/// from it.</summary>
public partial class LoadGamePage : Control
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string CampaignMapScenePath = "res://scene/campaign-map/campaign_map.tscn";
	private const string LoadingScenePath = "res://scene/loading/loading.tscn";
	// A map with no card of its own is shown on the first map's.
	private static readonly string FallbackPreviewPath = Campaign.AssetOf("england/maps/royal-crown", "card.png");

	private TextureRect _preview;
	private Label _detailName;
	private Label _detailMeta;

	public override void _Ready()
	{
		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, MainMenuScenePath);

		GoldTitle.Apply(GetNode<Label>("Center/Panel/Margin/Content/Title"));

		_preview = GetNode<TextureRect>("%Preview");
		_detailName = GetNode<Label>("%DetailName");
		_detailMeta = GetNode<Label>("%DetailMeta");

		var list = GetNode<VBoxContainer>("%SaveList");
		var saves = SaveGame.List();
		if (saves.Count == 0)
		{
			list.AddChild(new Label { Text = "No saved campaigns yet.", HorizontalAlignment = HorizontalAlignment.Center });
			_preview.Texture = null;
			_detailName.Text = "Nothing to load";
			_detailMeta.Text = "Save from the crest menu while a campaign is running.";
			return;
		}

		Button firstSlot = null;
		foreach (SaveGame save in saves)
		{
			var slot = new Button { Text = $"{save.CampaignName} · Turn {save.Turn}", Alignment = HorizontalAlignment.Left };
			slot.FocusEntered += () => ShowDetail(save);
			slot.Pressed += () => Load(save);
			list.AddChild(slot);

			// Must run after AddChild: outside the tree GetThemeStylebox resolves against
			// Godot's default theme, not the project's, and would replace the ornate button art.
			// Left-aligned text sits against that art's decorative edge, so widen the padding.
			foreach (string styleName in new[] { "normal", "hover", "pressed", "focus" })
			{
				var style = (StyleBox)slot.GetThemeStylebox(styleName).Duplicate();
				style.ContentMarginLeft += 24f;
				slot.AddThemeStyleboxOverride(styleName, style);
			}

			firstSlot ??= slot;
		}

		firstSlot.GrabFocus();
		ShowDetail(saves[0]);
	}

	private void ShowDetail(SaveGame save)
	{
		string card = Campaign.AssetOf(save.CampaignFolder, "card.png");
		_preview.Texture = GD.Load<Texture2D>(ResourceLoader.Exists(card) ? card : FallbackPreviewPath);
		_detailName.Text = save.CampaignName;
		_detailMeta.Text = $"Turn {save.Turn} · {save.SavedAtDisplay}";
	}

	private void Load(SaveGame save)
	{
		SaveGame.Pending = save;
		Campaign.Folder = save.CampaignFolder;
		Campaign.Name = save.CampaignName;
		Campaign.Player = save.PlayerLord;
		LoadingPage.TargetScenePath = CampaignMapScenePath;
		SceneRouter.GoTo(this, LoadingScenePath);
	}
}
