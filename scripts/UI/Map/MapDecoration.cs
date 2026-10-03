using System.Collections.Generic;
using Godot;

/// <summary>Everything standing on the terrain: woods, ploughed fields, and the working sites that
/// show what a province actually produces. (The roads are part of the ground itself — the
/// generator paints them into the surface map.)
///
/// Scattered props are drawn as MultiMesh instances — thousands of trees cost a handful of draw
/// calls — and placed from the campaign's map-props.png (R woodland, B open farmland),
/// generated alongside the terrain so vegetation lands where the ground supports it: not on cliffs,
/// not on the beach, thicker along province borders. The seed is fixed, so the same realm grows
/// the same forest every run.
///
/// Every mesh here is a primitive placeholder. Swapping in real art means changing the mesh a
/// builder returns; nothing else knows or cares what shape a tree is.</summary>
public partial class MapDecoration : Node3D
{
	private CampaignMap3D _map;
	private Image _props;
	private RandomNumberGenerator _rng;
	private readonly List<(StandardMaterial3D Material, Color[] BySeason)> _seasonal = new();
	// The broadleaves' leaves, which take the season as a shader parameter rather than a colour.
	private readonly List<ShaderMaterial> _seasonalLeaves = new();

	/// <summary>Opens the map. The roads are not laid here: they are painted into the ground itself,
	/// by the generator's surface map. The woods are not sown here either: a settlement has to
	/// be standing before them, or the trees grow through its roofs. The page calls
	/// <see cref="Sow"/> once it has put every province on the ground.</summary>
	public void Build(CampaignMap3D map)
	{
		_map = map;
		_props = GD.Load<Image>(Campaign.Asset(PropsFile));
		foreach (Vector2 point in RoadPoints())
		{
			var cell = new Vector2I(Mathf.FloorToInt(point.X / RoadCell), Mathf.FloorToInt(point.Y / RoadCell));
			if (!_roads.TryGetValue(cell, out List<Vector2> here))
			{
				here = new List<Vector2>();
				_roads[cell] = here;
			}

			here.Add(point);
		}
		_rng = new RandomNumberGenerator();
		_rng.Seed = 20260917; // fixed: the map must look the same every time it loads
	}

	/// <summary>Repaints everything that turns with the year — sown, standing, harvested, bare.
	/// The props themselves never move: a wood is the same wood in December as in June.</summary>
	public void SetSeason(float season)
	{
		int from = Mathf.FloorToInt(season) % 4;
		float along = season - Mathf.Floor(season);
		foreach ((StandardMaterial3D material, Color[] bySeason) in _seasonal)
		{
			material.AlbedoColor = bySeason[from].Lerp(bySeason[(from + 1) % 4], along);
		}

		foreach (ShaderMaterial leaves in _seasonalLeaves)
		{
			leaves.SetShaderParameter("season", season);
		}

		foreach ((ShaderMaterial look, float _) in _dressed.Values)
		{
			look.SetShaderParameter("season", season % 4f);
		}
	}
}
