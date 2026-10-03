using System.Collections.Generic;
using Godot;

/// <summary>The woods: which trees grow where, sown in stands and chunks over the ground nothing
/// else has taken, with foliage that turns with the season.</summary>
public partial class MapDecoration
{
	// Both belong to the played campaign, generated alongside its map images.
	private const string PropsFile = "map-props.png";

	// Attempts, not instances: each one rolls against the density at a random pixel, so the count
	// that lands is whatever the mask supports — ~3,800 trees on this map. The
	// woods have been thinned twice from where they started: a forest that covers the ground reads
	// as a texture, and what the map wants is stands of trees with country between them.
	private const int ScatterAttempts = 345000;
	private const float TreeChance = 0.150f;

	// How big the two kinds of tree stand, and the largest chance makes one of them. Named rather
	// than written into the scatter, because the fields have to be able to ask: ground cleared to
	// the width of a trunk is still ground with a crown hanging over it.
	// The pack ships five trees and this map used two of them, which is how a wood comes out as one
	// trunk printed four thousand times. The "Group" models are a whole stand on a single mesh —
	// five or six trees for the price of one instance — so they carry the bulk of the canopy and
	// the single trees fill in around them.
	// The baked trees (tools/decimate_meshy.py): each a few thousand triangles cut down from a
	// multi-million-triangle original, with its look baked into its own texture. Evergreens keep
	// their own material; the broadleaves are drawn through tree-foliage.gdshader so their leaves
	// turn with the year.
	private static readonly string[] Conifers = { "trees/evergreen-pine", "trees/evergreen-pine", "trees/cypress-tree" };
	private static readonly string[] Broadleaves = { "trees/oak-tree", "trees/whispering-oak", "trees/verdant-guardian" };
	private const string FoliageShaderPath = "res://assets/shaders/tree-foliage.gdshader";
	/// <summary>What the file holding a model's baked normals is called, beside the model itself.</summary>
	private const string ReliefSuffix = "-normal.png";
	// How many of the trees that land are a whole stand rather than one trunk.
	private const float StandShare = 0.24f;
	// The baked trees stand about 1.9 units tall in their own space; these bring them to the height
	// the old pack's trees stood at on this map. A "stand" is an old, big tree rather than a clump.
	private const float ConiferSize = 1.45f;
	private const float BroadleafSize = 1.35f;
	private const float StandSize = 1.3f;

	// The scatter is drawn in square chunks of this many world units rather than as one piece per
	// kind of thing. One MultiMesh over the whole island is culled and given its level of detail as
	// a single object — by its nearest point, so every tree on the map was drawn at full detail even
	// when the camera was across the sea from it. In chunks, the far woods drop to their coarse LODs
	// and the woods off the edge of the screen are not drawn at all.
	private const float ChunkSize = 24f;
	private const float TreeSpread = 1.4f;
	// Above this share of the map's relief conifers take over from broadleaf, the way a real
	// treeline works. A share and not a height: the map can be flattened or raised without the
	// treeline staying behind at an altitude nothing reaches any more.
	private const float ConiferLine = 0.375f;

	/// <summary>Sows every wood on the map, keeping clear of whatever has already been built. Called
	/// after the settlements, which is the whole point of it being its own call — a forest sown first
	/// grows straight through the villages.</summary>
	public void Sow()
	{
		var conifers = new List<Transform3D>[Conifers.Length];
		var broadleaves = new List<Transform3D>[Broadleaves.Length];
		for (int kind = 0; kind < Conifers.Length; kind++)
		{
			conifers[kind] = new List<Transform3D>();
		}

		for (int kind = 0; kind < Broadleaves.Length; kind++)
		{
			broadleaves[kind] = new List<Transform3D>();
		}

		for (int i = 0; i < ScatterAttempts; i++)
		{
			var pixel = new Vector2(_rng.RandfRange(0, _props.GetWidth() - 1), _rng.RandfRange(0, _props.GetHeight() - 1));
			if (IsBuiltOn(pixel))
			{
				continue; // somebody's village, or the ground their castle stands on
			}

			Color density = _props.GetPixel((int)pixel.X, (int)pixel.Y);
			if (density.R > 0.05f && _rng.Randf() < density.R * TreeChance)
			{
				Transform3D tree = PropTransform(pixel, _rng.RandfRange(TreeSpread * 0.5f, TreeSpread), tiltDegrees: 4f);
				bool isHigh = _map.HeightAt(pixel) > _map.Relief * ConiferLine;
				// Mixed woods, weighted by altitude, rather than one species per region.
				bool conifer = isHigh ? _rng.Randf() < 0.92f : _rng.Randf() < 0.55f;
				// Now and then an old tree, bigger than the ones round it: a wood of one size of tree
				// reads as a plantation.
				if (_rng.Randf() < StandShare)
				{
					tree = tree.ScaledLocal(Vector3.One * StandSize);
				}

				// Three silhouettes of each, so no two neighbouring trees are the same drawing.
				if (conifer)
				{
					conifers[_rng.RandiRange(0, Conifers.Length - 1)].Add(tree);
				}
				else
				{
					broadleaves[_rng.RandiRange(0, Broadleaves.Length - 1)].Add(tree);
				}
			}
		}

		// One mesh per tree: the model carries its trunk and its crown as one drawing, so a wood costs
		// a third of the draw calls it did when a tree was three meshes over one transform list.
		//
		// Every tree goes through the foliage shader — the evergreens for their relief, the broadleaves
		// for that and for the year: it picks the leaves out of the baked texture by their colour and
		// gives those the season, and leaves the bark alone.
		for (int kind = 0; kind < Conifers.Length; kind++)
		{
			AddScatter(Models.MeshOf(Conifers[kind]), null, Grown(conifers[kind], ConiferSize),
				overrideMaterial: Foliage(Conifers[kind], evergreen: true));
		}

		for (int kind = 0; kind < Broadleaves.Length; kind++)
		{
			AddScatter(Models.MeshOf(Broadleaves[kind]), null, Grown(broadleaves[kind], BroadleafSize),
				overrideMaterial: Foliage(Broadleaves[kind], evergreen: false));
		}
	}

	/// <summary>How a tree is drawn: its baked colour, the relief baked out of the original beside it
	/// (tools/decimate_meshy.py writes both), and whether it keeps its leaves through the year.</summary>
	private ShaderMaterial Foliage(string model, bool evergreen)
	{
		var leaves = new ShaderMaterial { Shader = GD.Load<Shader>(FoliageShaderPath) };
		if (Models.MeshOf(model)?.SurfaceGetMaterial(0) is BaseMaterial3D baked)
		{
			leaves.SetShaderParameter("albedo_texture", baked.AlbedoTexture);
		}

		leaves.SetShaderParameter("relief_texture", GD.Load<Texture2D>($"res://assets/models/{model}{ReliefSuffix}"));
		leaves.SetShaderParameter("evergreen", evergreen ? 1f : 0f);
		_seasonalLeaves.Add(leaves);
		return leaves;
	}
}
