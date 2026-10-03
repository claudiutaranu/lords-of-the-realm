using Godot;

/// <summary>The sidebar minimap. Sits on its own node and needs nothing from the page around it:
/// it listens for who holds what (CampaignMap3D.HoldersChanged) and for the selection, so a rewrite
/// of the map page cannot quietly leave it unwired.
///
/// Province shapes come from the campaign's ID map (R = province index + 1) — the same image the
/// terrain shader and the click raycast use, so the minimap can never disagree with the map. Who
/// holds what is the same strip the ground is washed from: the holder's colour in RGB, his realm's
/// number + 1 in alpha, so a whole realm can be lit when one of its provinces is chosen.</summary>
public partial class MapMinimap : TextureRect
{
	private const string MinimapShaderPath = "res://assets/shaders/minimap.gdshader";
	private const string IdMapFile = "map-ids.png";
	private const string BaseMapFile = "map-albedo.png";

	private ShaderMaterial _material;
	private Image _holders;

	public override void _Ready()
	{
		// The terrain's own colour map, not the flat drawn one: mountains, woods and coastline read
		// as terrain at this size, where flat province fills just looked like a chart.
		Texture = GD.Load<Texture2D>(Campaign.Asset(BaseMapFile));

		_material = new ShaderMaterial { Shader = GD.Load<Shader>(MinimapShaderPath) };
		_material.SetShaderParameter("id_map", ImageTexture.CreateFromImage(GD.Load<Image>(Campaign.Asset(IdMapFile))));
		Material = _material;

		CampaignMap3D.ProvinceHighlighted += ShowSelection;
		CampaignMap3D.HoldersChanged += ShowHolders;
	}

	public override void _ExitTree()
	{
		CampaignMap3D.ProvinceHighlighted -= ShowSelection;
		CampaignMap3D.HoldersChanged -= ShowHolders;
	}

	private void ShowHolders(Image holders)
	{
		_holders = holders;
		_material.SetShaderParameter("owner_colors", ImageTexture.CreateFromImage(holders));
	}

	private void ShowSelection(int index)
	{
		_material.SetShaderParameter("selected_index", index + 1);
		_material.SetShaderParameter("selected_realm",
			_holders != null && index >= 0 && index < _holders.GetWidth()
				? Mathf.RoundToInt(_holders.GetPixel(index, 0).A * 255f) - 1
				: -1);
	}
}
