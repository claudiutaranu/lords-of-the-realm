/// <summary>Which campaign is being played, and where its files live.
///
/// A campaign owns a folder and nothing outside it: data/campaigns/&lt;folder&gt; holds its provinces
/// and its map data, assets/campaigns/&lt;folder&gt; its map images and card art. Two campaigns can
/// therefore never read each other's. What the engine applies to all of them the same way stays
/// shared where it is — game-balance.tres, the shaders, the ground textures, the scenes, and every
/// script here. Adding a campaign is a folder of each kind plus one entry on CampaignPage's list,
/// with no engine code to touch.
///
/// A static handover slot rather than an autoload, the same way SaveGame.Pending and
/// LoadingPage.TargetScenePath hand state to the scene that opens next.</summary>
public static class Campaign
{
	/// <summary>Set when a campaign card is chosen, and read by the campaign-map scene while it
	/// builds. The Royal Crown by default, so that scene still opens straight from the editor.</summary>
	public static string Folder = "royal-crown";

	/// <summary>What the campaign is called on screen and in a save's header.</summary>
	public static string Name = "The Royal Crown";

	public static string Data(string file) => DataOf(Folder, file);

	public static string Asset(string file) => AssetOf(Folder, file);

	/// <summary>A campaign other than the one being played — the card list drawing all of them,
	/// or a briefing showing the rival's portrait.</summary>
	public static string AssetOf(string folder, string file) => $"res://assets/campaigns/{folder}/{file}";

	private static string DataOf(string folder, string file) => $"res://data/campaigns/{folder}/{file}";
}
