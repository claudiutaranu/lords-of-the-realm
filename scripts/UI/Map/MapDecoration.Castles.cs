using System.Collections.Generic;
using Godot;

/// <summary>The walls raised beside a seat: where the castle stands, what rung of it is built or
/// under way, and finding it again under a pixel.</summary>
public partial class MapDecoration
{
	private const float TownGap = 6f;        // a lane between the last house and the first furrow
	private const float CastleBearing = Mathf.Pi * 0.25f;
	private const float CastleDistance = 66f;
	private const float CastleReach = 34f;
	private const int CastleSides = 8;
	private const float CastleShore = 0.3f;  // height above the sea its walls' lowest corner must keep

	/// <summary>One node per province's walls, so a finished build can replace them on their own.</summary>
	private readonly Dictionary<string, Node3D> _forts = new();

	private readonly Dictionary<Vector2, Vector2> _castleSites = new();

	/// <summary>What each province's walls were last drawn from, for the same reason.</summary>
	private readonly Dictionary<string, (Vector2 Seat, string Fort, string Building, Color Lord, bool Manned)> _fortDrawn = new();

	/// <summary>The provinces whose castle ground is already among the clearings: a wall raised again
	/// stands on ground cleared the first time.</summary>
	private readonly HashSet<string> _castlesCleared = new();

	/// <summary>Whose village stands under this map pixel, or nothing. The town is walked into from
	/// its own streets: a county is a great deal of ground, and a click on the far side of its woods
	/// used to open its market square.</summary>
	/// <summary>Whether a map pixel is on the ground a county's castle stands on (CastleSite).</summary>
	public bool AtCastle(string province, Vector2 pixel) =>
		_settlementSites.TryGetValue(province, out Vector2 seat) && pixel.DistanceTo(CastleSite(seat)) <= CastleReach;

	/// <summary>The county whose castle ground this map pixel is on, or empty.</summary>
	public string CastleAt(Vector2 pixel)
	{
		foreach (string province in _settlementSites.Keys)
		{
			if (AtCastle(province, pixel))
			{
				return province;
			}
		}

		return "";
	}

	/// <summary>Puts a province's castle on the ground beside its town, and takes down whatever
	/// stood there before. Its whole job is to say, from the map and without a click, that this
	/// place has a castle and roughly how much of one — so it is the building itself and no curtain
	/// wall around it: a ring of blocks at map scale reads as a smudge, and the keep is what carries
	/// the fact.
	///
	/// A castle is the one thing on this map the player builds, so it is the one thing that has to
	/// change without the map being rebuilt around it — hence a node of its own per province rather
	/// than another scatter folded in with the trees.
	///
	/// An empty key takes it down and leaves an open village, which is what a province that has
	/// never built looks like. While the masons are at work (<paramref name="building"/>) it is the
	/// rung going up that stands there, half-raised in its scaffolding, and not the one it replaces:
	/// the old wall comes down when the new one is begun. Its flag flies only while there are men
	/// on the walls (<paramref name="manned"/>).</summary>
	public void SetFortification(string province, Vector2 seatPixel, string fort, string building, Color lord,
		bool manned)
	{
		var drawn = (seatPixel, fort, building, lord, manned);
		if (_fortDrawn.TryGetValue(province, out var was) && was == drawn)
		{
			return;
		}

		_fortDrawn[province] = drawn;
		if (_forts.TryGetValue(province, out Node3D standing))
		{
			RemoveChild(standing); // as above: gone this frame, not at the end of it
			standing.QueueFree();
			_forts.Remove(province);
		}

		Works plan = UnderWay(building) ?? PlanFor(fort);
		if (plan == null)
		{
			return; // an open village, or a key this map has no shapes for
		}

		var works = new Node3D();
		AddChild(works);
		_forts[province] = works;

		Vector2 site = CastleSite(seatPixel);
		if (_map.HeightAt(site) <= _map.WaterLine + CastleShore)
		{
			return; // nowhere to stand
		}

		if (_castlesCleared.Add(province))
		{
			_clearings.Add((site, CastleReach));
		}

		Mesh mesh = Models.MeshOf(plan.Model);
		if (mesh == null)
		{
			return;
		}

		// Through the village's own shader, so it takes the same relief and season, and its banners
		// fly the holder's colour with the wind like the village's do.
		(ShaderMaterial look, float flagRest) = Dressed(plan.Model, mesh);
		Vector2 wind = MapClouds.PrevailingWind;
		var castle = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = look,
			Transform = new Transform3D(
				Basis.Identity.Rotated(Vector3.Up, flagRest - Mathf.Atan2(wind.Y, wind.X))
					.Scaled(Vector3.One * (plan.Size * VillageYard(Settlement.Town) / Models.FootprintOf(plan.Model))),
				_map.WorldAt(site) + (Vector3.Down * SettlementSink)),
		};
		works.AddChild(castle);
		castle.SetInstanceShaderParameter("lord_color", lord);
		castle.SetInstanceShaderParameter("flag_flown", manned ? 1f : 0f);
	}

	/// <summary>The pack models about a metre tall where this map's props are two or three, so every
	/// scatter of them is grown on the way in. The transforms keep their own random spread and tilt;
	/// this only changes how big one of them is.</summary>
	private static List<Transform3D> Grown(List<Transform3D> transforms, float by)
	{
		var grown = new List<Transform3D>(transforms.Count);
		foreach (Transform3D one in transforms)
		{
			grown.Add(one.ScaledLocal(Vector3.One * by));
		}

		return grown;
	}

	/// <summary>Where a seat's castle stands: CastleDistance off the town, on whichever of CastleSides
	/// bearings has the flattest dry ground under the whole of its walls — south-east (CastleBearing)
	/// when that is as good as any. A fixed bearing stood one keep on the lip of a sea cliff and
	/// another down a hillside. Worked out from the height map alone, so the woods, the fields and
	/// the castle raised years later all agree on the spot without being told.</summary>
	private Vector2 CastleSite(Vector2 seat)
	{
		if (_castleSites.TryGetValue(seat, out Vector2 known))
		{
			return known;
		}

		Vector2 best = Clamped(seat, CastleBearing, CastleDistance);
		float flattest = float.MaxValue;
		for (int side = 0; side < CastleSides; side++)
		{
			Vector2 site = Clamped(seat, CastleBearing + (side * Mathf.Tau / CastleSides), CastleDistance);
			float low = float.MaxValue;
			float high = float.MinValue;
			for (int k = 0; k <= CastleSides; k++)
			{
				Vector2 at = k == 0 ? site : Clamped(site, k * Mathf.Tau / CastleSides, CastleReach);
				float height = _map.HeightAt(at);
				low = Mathf.Min(low, height);
				high = Mathf.Max(high, height);
			}

			if (low > _map.WaterLine + CastleShore && high - low < flattest)
			{
				(best, flattest) = (site, high - low);
			}
		}

		_castleSites[seat] = best;
		return best;
	}

	private Vector2 Clamped(Vector2 seat, float angle, float radius) => new(
		Mathf.Clamp(seat.X + Mathf.Cos(angle) * radius, 0, _props.GetWidth() - 1),
		Mathf.Clamp(seat.Y + Mathf.Sin(angle) * radius, 0, _props.GetHeight() - 1));
}
