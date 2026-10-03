using System.Collections.Generic;
using Godot;

/// <summary>What stands on the land, asked for through the map and handed on to MapDecoration:
/// armies, towns, castles, fields, sites and the woods.</summary>
public partial class CampaignMap3D
{
	/// <summary>Whose banner stands under a map pixel, or nothing.</summary>
	public string ArmyAt(Vector2 mapPixel) => _decoration.ArmyAt(mapPixel);

	/// <summary>Walks a county's banner along a road, and says when it has arrived.</summary>
	public void WalkArmy(string army, List<Vector2> road, System.Action arrived,
		float stride = MapDecoration.StrideSeconds) =>
		_decoration.WalkArmy(army, road, arrived, stride);

	/// <summary>Takes down the banners of companies that are not standing any more.</summary>
	public void RetireArmies(System.Collections.Generic.ICollection<string> standing) =>
		_decoration.RetireArmies(standing);

	/// <summary>Puts a company on the ground under its lord's colour, or takes it off it.</summary>
	public void SetArmy(string army, Vector2 seatPixel, bool standing, Color lord) =>
		_decoration.SetArmy(army, seatPixel, standing, lord);

	/// <summary>Whose village stands under a map pixel, or nothing.</summary>
	public string TownAt(Vector2 mapPixel) => _decoration.TownAt(mapPixel);

	public string CastleAt(Vector2 mapPixel) => _decoration.CastleAt(mapPixel);

	public bool AtCastle(string province, Vector2 mapPixel) => _decoration.AtCastle(province, mapPixel);

	/// <summary>Which of a county's fields sits under a map pixel, or -1.</summary>
	public int PlotAt(string province, Vector2 mapPixel) => _decoration.PlotAt(province, mapPixel);

	/// <summary>Puts a working site (quarry, pasture, lumber camp) on a province's ground.</summary>
	public void AddSite(string province, Vector2 seatPixel, MapDecoration.SiteKind kind, float weight) =>
		_decoration.AddSite(province, seatPixel, kind, weight);

	public (string Province, MapDecoration.SiteKind Kind)? SiteAt(Vector2 mapPixel) => _decoration.SiteAt(mapPixel);

	public System.Collections.Generic.List<Vector2> GroundOf(string province) => _decoration.GroundOf(province);

	/// <summary>Raises a province's village on its seat — what the map pin points at — flying its
	/// lord's colour, or hands an existing one's banners to a new lord.</summary>
	public void AddSettlement(string province, Vector2 seatPixel, MapDecoration.Settlement kind, Color lord) =>
		_decoration.AddSettlement(province, seatPixel, kind, lord);

	/// <summary>Puts a province's walls on the ground beside its town, taking down whatever stood
	/// there before. Called again whenever a build finishes, so the map keeps up with the ledger.</summary>
	public void SetFortification(string province, Vector2 seatPixel, string fort, string building, Color lord,
		bool manned) =>
		_decoration.SetFortification(province, seatPixel, fort, building, lord, manned);

	/// <summary>Lays a province's fields on its ground — one plot per field, under what the province
	/// has it under. Called again whenever the land or the season changes, so the map keeps up with
	/// the ledger the same way the walls do.</summary>
	public void SetFields(string name, Vector2 seatPixel, ProvinceEconomy province, Season season) =>
		_decoration.SetFields(name, seatPixel, province, season);

	/// <summary>Sows the woods, once everything built is standing. Last, so they grow around it.</summary>
	public void Sow() => _decoration.Sow();
}
