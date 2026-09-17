using System.Collections.Generic;
using Godot;

/// <summary>The sidebar minimap. Sits on its own node and needs nothing from the page around it:
/// it reads the campaign's own files and listens for the selection, so a rewrite of the map page
/// cannot quietly leave it unwired.
///
/// Province shapes come from the campaign's ID map (R = province index + 1) — the same image the
/// terrain shader and the click raycast use, so the minimap can never disagree with the map. Who
/// holds what comes from provinces.json, handed to the shader as one texel per province: the realm's
/// colour in RGB, the realm's index in alpha, so a whole realm can be lit when one of its provinces
/// is chosen. After a conquest, call Refresh().</summary>
public partial class MapMinimap : TextureRect
{
	private const string MinimapShaderPath = "res://assets/shaders/minimap.gdshader";
	private const string IdMapFile = "map-ids.png";
	private const string BaseMapFile = "map-albedo.png";
	private const string ProvincesFile = "provinces.json";

	private ShaderMaterial _material;
	private readonly List<string> _provinceRealms = new();
	private readonly Dictionary<string, Color> _realmColors = new();
	private readonly Dictionary<string, int> _realmIndex = new();

	public override void _Ready()
	{
		// The terrain's own colour map, not the flat drawn one: mountains, woods and coastline read
		// as terrain at this size, where flat province fills just looked like a chart.
		Texture = GD.Load<Texture2D>(Campaign.Asset(BaseMapFile));

		_material = new ShaderMaterial { Shader = GD.Load<Shader>(MinimapShaderPath) };
		_material.SetShaderParameter("id_map", ImageTexture.CreateFromImage(GD.Load<Image>(Campaign.Asset(IdMapFile))));
		Material = _material;

		LoadRealms();
		Refresh();

		CampaignMap3D.ProvinceHighlighted += ShowSelection;
	}

	public override void _ExitTree()
	{
		CampaignMap3D.ProvinceHighlighted -= ShowSelection;
	}

	/// <summary>Rebuilds the ownership strip — after a province changes hands.</summary>
	public void Refresh()
	{
		var strip = Image.CreateEmpty(Mathf.Max(_provinceRealms.Count, 1), 1, false, Image.Format.Rgba8);
		for (int i = 0; i < _provinceRealms.Count; i++)
		{
			string realm = _provinceRealms[i];
			Color accent = _realmColors.TryGetValue(realm, out Color color) ? color : new Color("8b8b86");
			strip.SetPixel(i, 0, new Color(accent.R, accent.G, accent.B, _realmIndex[realm] / 255f));
		}

		_material.SetShaderParameter("owner_colors", ImageTexture.CreateFromImage(strip));
	}

	private void ShowSelection(int index)
	{
		_material.SetShaderParameter("selected_index", index + 1);
		_material.SetShaderParameter("selected_realm",
			index >= 0 && index < _provinceRealms.Count ? _realmIndex[_provinceRealms[index]] : -1);
	}

	private void LoadRealms()
	{
		var file = GD.Load<Json>(Campaign.Data(ProvincesFile));
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"MapMinimap: campaign '{Campaign.Folder}' has no readable {ProvincesFile}");
			return;
		}

		Godot.Collections.Dictionary data = file.Data.AsGodotDictionary();
		foreach (KeyValuePair<Variant, Variant> realm in data["realms"].AsGodotDictionary())
		{
			string name = realm.Key.AsString();
			_realmColors[name] = new Color(realm.Value.AsGodotDictionary()["accent"].AsString());
			_realmIndex[name] = _realmIndex.Count;
		}

		foreach (Variant entry in data["provinces"].AsGodotArray())
		{
			_provinceRealms.Add(entry.AsGodotDictionary()["realm"].AsString());
		}
	}
}
