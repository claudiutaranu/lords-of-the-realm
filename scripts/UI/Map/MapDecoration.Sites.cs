using System.Collections.Generic;
using Godot;

/// <summary>The diggings and the woodcutters' sites: placed on a county's ground, found again under
/// a pixel, and the meshes they are drawn with.</summary>
public partial class MapDecoration
{
	/// <summary>What a province digs out of its ground. Grain and the herd are not in here: those
	/// are worked on fields, and a field is a plot on the map rather than a scattered prop.</summary>
	public enum SiteKind { Wood, Stone, Iron }

	/// <summary>Drops a working site — a quarry, a mine, a lumber camp — on the ground around a
	/// province's seat. The page calls this from each province's authored capacities, so what the
	/// map shows and what the economy simulates are the same fact.
	///
	/// Each one takes its own footprint of ground with it, which is what keeps the fields laid after
	/// it from being ploughed straight through the diggings. A bite the size of the prop and no
	/// more: pushing the sites out past the farmland instead would put half of them in the next
	/// county, which on a map of eight provinces is a lie about who owns the ore.</summary>
	public void AddSite(string province, Vector2 seatPixel, SiteKind kind, float weight)
	{
		// A handful, never a field of them: these mark what a province works, they are not the
		// industry itself.
		int count = 3 + (weight > 70f ? 2 : 0);

		var transforms = new List<Transform3D>();
		for (int attempt = 0; attempt < count * 14 && transforms.Count < count; attempt++)
		{
			// Ringed around the seat, not on top of it: the stronghold marker has to stay readable.
			float angle = _rng.RandfRange(0, Mathf.Tau);
			float radius = _rng.RandfRange(38f, 88f);
			var pixel = new Vector2(
				Mathf.Clamp(seatPixel.X + Mathf.Cos(angle) * radius, 0, _props.GetWidth() - 1),
				Mathf.Clamp(seatPixel.Y + Mathf.Sin(angle) * radius, 0, _props.GetHeight() - 1));

			if (_map.HeightAt(pixel) <= _map.WaterLine + 0.2f)
			{
				continue; // out at sea or below the waterline
			}

			transforms.Add(PropTransform(pixel, _rng.RandfRange(0.9f, 1.15f)));
			_clearings.Add((pixel, 9f)); // its own footprint: a plot may abut a quarry, never cover it
			_sites.Add((province, pixel, kind));
		}

		(Mesh mesh, Color color) = kind switch
		{
			SiteKind.Wood => (LogPileMesh(), new Color("6b4c2f")),
			SiteKind.Stone => (QuarryMesh(), new Color("9d9891")),
			_ => (MineMesh(), new Color("4a4440")),
		};
		AddScatter(mesh, color, transforms, colorJitter: 0.1f);
	}

	// --- the land ------------------------------------------------------------------------------

	/// <summary>Lays a province's fields on the ground around its seat: one enclosed plot per field,
	/// under grain, under the herd, or resting, in the season it stands in.
	///
	/// This is the province's own <see cref="ProvinceEconomy.Fields"/> drawn at map scale, not a
	/// decoration of it — turn a field over on its panel and the plot out here changes with
	/// it, which is the whole point: what the land is under is a decision, and a decision the player
	/// cannot see from the map is a decision he makes in a menu with his eyes shut.
	///
	/// Rebuilt whole rather than patched, because the cheapest thing that can be right is one that
	/// only draws the present state: ten plots is nothing to build, and a patched one would have to
	/// know which field changed and what season it changed in.</summary>
	/// <summary>Every working site laid, with the county it is worked for: an army halted on one
	/// shuts it (TurnManager.Trample).</summary>
	private readonly List<(string Province, Vector2 At, SiteKind Kind)> _sites = new();

	/// <summary>How near a halt counts as standing on a site: its own footprint and a little over.</summary>
	private const float SiteReach = 14f;

	/// <summary>Every field and diggings a county has on the map: where a raid goes to do harm.</summary>
	public List<Vector2> GroundOf(string province)
	{
		var ground = new List<Vector2>(_plots.GetValueOrDefault(province) ?? new List<Vector2>());
		foreach ((string owner, Vector2 at, SiteKind _) in _sites)
		{
			if (owner == province)
			{
				ground.Add(at);
			}
		}

		return ground;
	}

	/// <summary>The site a map pixel is standing on, and whose it is, or nothing.</summary>
	public (string Province, SiteKind Kind)? SiteAt(Vector2 pixel)
	{
		foreach ((string province, Vector2 at, SiteKind kind) in _sites)
		{
			if (pixel.DistanceTo(at) <= SiteReach)
			{
				return (province, kind);
			}
		}

		return null;
	}

	// --- the shapes still built here -------------------------------------------------------------
	// Everything a lord built — cottages, halls, towers, and the trees around them — is a model out
	// of the pack now. What is left in here is the handful of things too small to be worth a model:
	// a beast on a pasture, a heap of cut logs, a working face of stone. The box-and-cone kit the
	// map was first built out of went with the models that replaced it.

	private static Mesh LogPileMesh() =>
		new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.25f, Height = 1.1f, RadialSegments = 6, Rings = 1 };

	private static Mesh QuarryMesh() =>
		new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.4f) };

	private static Mesh MineMesh() =>
		new BoxMesh { Size = new Vector3(0.9f, 0.9f, 0.9f) };
}
