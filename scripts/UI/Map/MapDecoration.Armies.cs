using System.Collections.Generic;
using Godot;

/// <summary>The companies' banners on the map: set at their ground, walked along a smoothed road at
/// their stride, found under a pixel, and retired when they are gone.</summary>
public partial class MapDecoration
{
	/// <summary>How long a beast is drawn, nose to tail, in world units. Held as a LENGTH and not as
	/// a scale factor because a model brought in from outside arrives in whatever units it was
	/// modelled in — this is the size the map was laid out around, and the scale is worked back from
	/// the model's own footprint so a new animal cannot come in five times too big.</summary>
	/// <summary>The banner that stands for a county's men, and how tall it is drawn in world units.
	/// Taller than life on purpose: an army is the one thing on this map a lord looks for, and at a
	/// man's real height beside a cottage he would have to hunt for it.</summary>
	private const float ArmyHeight = 6.5f;

	/// <summary>The ground one whole stride of the standard-bearer's covers, left foot and right, in
	/// world units: his legs are moved by the road he walks, so his feet never slide over it.</summary>
	private const float StrideCycle = ArmyHeight * 1.4f;

	/// <summary>How near a click has to land to take hold of a banner, in map pixels. Generous: the
	/// figure is tall and thin, and a lord jabbing at his own army should not have to hit the pole.</summary>
	private const float ArmyReach = 34f;

	/// <summary>How long the banner takes to cover one bead of the road it was sent along. Slow
	/// enough to be a march and not a jump, quick enough that a lord who has ordered four of them is
	/// not waiting on the map.</summary>
	public const float StrideSeconds = 0.16f;

	/// <summary>How a banner rounds a bend: how many times the road's corners are cut, how far up the
	/// road he looks for the way he is going (world units), and how quickly he turns to it (per second,
	/// eased, so a slow frame does not leave him facing the last bend).</summary>
	private const int SmoothPasses = 2;
	private const float LookAhead = 1.2f;
	private const float TurnRate = 9f;

	/// <summary>Which way a company that has never marched stands: three-quarters on to the eye.</summary>
	private const float RestingHeading = Mathf.Pi * 0.25f;
	public const float RivalStrideSeconds = 0.05f;

	private readonly Dictionary<string, Node3D> _armies = new();

	/// <summary>The tramp of every army walking the map at once, made when the first sets off, and how
	/// loud it is heard, in decibels.</summary>
	private MarchingSound _tramp;
	private const float MarchHeard = -6f;
	private readonly Dictionary<string, float> _headings = new();
	private readonly Dictionary<string, Vector2> _armySites = new();

	/// <summary>A road as a smooth curve on the ground: from where the banner stands along the beads,
	/// the corners cut round twice over (Chaikin), at the height the banner stands above the ground.</summary>
	private List<Vector3> Smoothed(List<Vector2> road, float stands, Vector3 from)
	{
		var points = new List<Vector3> { from };
		foreach (Vector2 step in road)
		{
			points.Add(_map.WorldAt(step) + (Vector3.Up * stands));
		}

		for (int pass = 0; pass < SmoothPasses; pass++)
		{
			var cut = new List<Vector3> { points[0] };
			for (int i = 0; i < points.Count - 1; i++)
			{
				cut.Add(points[i].Lerp(points[i + 1], 0.25f));
				cut.Add(points[i].Lerp(points[i + 1], 0.75f));
			}

			cut.Add(points[^1]);
			points = cut;
		}

		// Laid back on the ground, which a cut corner may have left above or below.
		for (int i = 1; i < points.Count - 1; i++)
		{
			Vector2 pixel = _map.MapPixelOf(points[i]);
			points[i] = new Vector3(points[i].X, _map.WorldAt(pixel).Y + stands, points[i].Z);
		}

		return points;
	}

	/// <summary>Walks a county's banner along a road to where it was sent, and says when it has got
	/// there. The piece is not moved and then the map redrawn — it is the same figure, carried along
	/// the same line of beads the lord was shown, so what he ordered and what he watches are plainly
	/// the same thing.
	///
	/// The men have already moved in the ledger by the time this runs. This is the walk, not the
	/// march: if it were the other way round, a lord could close the game mid-stride and find his
	/// army had never left.</summary>
	/// <param name="stride">Seconds a stride takes: a lord's own company walks at StrideSeconds, the
	/// rivals' at RivalStrideSeconds — watching another lord's whole turn at a marching pace was most
	/// of the wait between seasons.</param>
	public void WalkArmy(string army, List<Vector2> road, System.Action arrived, float stride = StrideSeconds)
	{
		if (!_armies.TryGetValue(army, out Node3D piece) || piece.GetChildCount() == 0
			|| road.Count == 0)
		{
			arrived();
			return;
		}

		float stands = piece.Position.Y - _map.WorldAt(_armySites.GetValueOrDefault(army)).Y;
		foreach (Node rank in piece.GetChildren())
		{
			// soldier.gdshader swings their legs while this stands.
			((GeometryInstance3D)rank).SetInstanceShaderParameter("walking", 1f);
		}

		// The map may redraw its banners while this one is still on the road — another march, a
		// battle settled — and the piece walking here is freed under the tween. Then the walk is
		// over: it stops, and the arrival is still reported, once.
		if (_tramp == null)
		{
			// Heard over the music: at the field's quiet it was under it, and the lord never heard his
			// men set off.
			_tramp = new MarchingSound { Sound = "res://assets/audio/map-marching.mp3", Loudness = MarchHeard };
			AddChild(_tramp);
		}

		_tramp.Join();
		Tween walk = CreateTween();
		bool over = false;
		void Arrive()
		{
			if (over)
			{
				return;
			}

			over = true;
			walk.Kill();
			_tramp.Leave();
			arrived();
		}

		// Walked along the road smoothed into a curve, at an even pace, turning gradually to face
		// the way it goes: stepped from bead to bead, the banner walked a zigzag at a hitching pace and
		// snapped round at every corner.
		List<Vector3> path = Smoothed(road, stands, piece.Position);
		var lengths = new float[path.Count];
		for (int i = 1; i < path.Count; i++)
		{
			lengths[i] = lengths[i - 1] + path[i - 1].DistanceTo(path[i]);
		}

		float total = lengths[^1];
		int at = 0;
		walk.TweenMethod(Callable.From<float>(along =>
		{
			if (!IsInstanceValid(piece))
			{
				Arrive();
				return;
			}

			piece.Position = PointAlong(path, lengths, along, ref at);
			foreach (Node rank in piece.GetChildren())
			{
				((GeometryInstance3D)rank).SetInstanceShaderParameter("map_gait", Mathf.Tau * along / StrideCycle);
			}

			// Facing a point a little up the road, not the bead he is on: the heading swings round a
			// bend before he reaches it instead of snapping at every cut corner.
			int ahead = at;
			Vector3 toward = PointAlong(path, lengths, Mathf.Min(along + LookAhead, total), ref ahead) - piece.Position;
			if (toward.X * toward.X + toward.Z * toward.Z > 0.0001f)
			{
				float facing = Mathf.Atan2(toward.X, toward.Z);
				float ease = 1f - Mathf.Exp(-TurnRate * (float)GetProcessDeltaTime());
				piece.Rotation = new Vector3(0f, Mathf.LerpAngle(piece.Rotation.Y, facing, ease), 0f);
			}
		}), 0f, total, stride * road.Count);

		walk.TweenCallback(Callable.From(() =>
		{
			if (IsInstanceValid(piece))
			{
				_headings[army] = piece.Rotation.Y;
				foreach (Node rank in piece.GetChildren())
				{
					((GeometryInstance3D)rank).SetInstanceShaderParameter("walking", 0f);
					((GeometryInstance3D)rank).SetInstanceShaderParameter("map_gait", -1f);
				}
			}

			Arrive();
		}));
	}

	/// <summary>The point so far along a path, walking <paramref name="at"/> on to the segment it lies in.</summary>
	private static Vector3 PointAlong(List<Vector3> path, float[] lengths, float along, ref int at)
	{
		while (at < path.Count - 2 && lengths[at + 1] < along)
		{
			at++;
		}

		float span = Mathf.Max(0.0001f, lengths[at + 1] - lengths[at]);
		return path[at].Lerp(path[at + 1], Mathf.Clamp((along - lengths[at]) / span, 0f, 1f));
	}

	/// <summary>Which company is standing under this map pixel, by the key the page named it with, or
	/// nothing. The banner is picked off its own position rather than off a collision body, the same
	/// way the fields are: one distance against a handful of armies costs nothing, and a body on
	/// every figure would have to be built and thrown away every time a county raised or lost a man.
	///
	/// The nearest one wins, so two companies mustered beside the same town are told apart by which
	/// of them the lord actually pointed at.</summary>
	public string ArmyAt(Vector2 pixel)
	{
		string nearest = "";
		float reach = ArmyReach;
		foreach ((string army, Vector2 site) in _armySites)
		{
			float off = pixel.DistanceTo(site);
			if (_armies.ContainsKey(army) && off <= reach)
			{
				nearest = army;
				reach = off;
			}
		}

		return nearest;
	}

	/// <summary>Takes down every banner that is not in the list — a company merged into another,
	/// disbanded, or killed to the last man. Called with everything that IS standing, so a banner
	/// nobody claims cannot be left on the map.</summary>
	public void RetireArmies(System.Collections.Generic.ICollection<string> standing)
	{
		foreach (string army in new List<string>(_armies.Keys))
		{
			if (!standing.Contains(army))
			{
				SetArmy(army, Vector2.Zero, false, Colors.White);
			}
		}
	}

	/// <summary>One company, standing where it is. A banner to an army and not to a county: a lord
	/// who raises a second company sees a second banner, and can take hold of either of them.
	///
	/// One standard-bearer, however many men: a company drawn two and three figures strong walked
	/// as a clump of men sliding over one another (the user's call). How big it is, the panel behind
	/// the right button says.</summary>
	public void SetArmy(string army, Vector2 seatPixel, bool standing, Color lord)
	{
		if (_armies.TryGetValue(army, out Node3D piece))
		{
			RemoveChild(piece);
			piece.QueueFree();
			_armies.Remove(army);
		}

		_armySites.Remove(army);
		if (!standing)
		{
			return;
		}

		// Exactly where the men are, and nowhere near it. This used to stand them a little north-west
		// of the town, back when a banner meant "this county has men in it" — now it means "the men
		// are HERE", and an army drawn fifty paces from where it was sent is a map that argues with
		// the order the lord just gave.
		Vector2 site = seatPixel;
		if (_map.HeightAt(site) <= _map.WaterLine + 0.3f)
		{
			return;
		}

		if (SoldierFigure.Standard.Mesh == null)
		{
			return;
		}

		// Scaled by his own height to the figure this map draws a man at, and stood on his feet
		// rather than sunk to the waist — the same rule the cattle are placed by.
		float scale = ArmyHeight / Mathf.Max(0.01f, Models.HeightOf(SoldierFigure.Standard.Model));
		Aabb bounds = Models.MeshOf(SoldierFigure.Standard.Model).GetAabb();

		// The company stands on one node: it is one army, it marches as one, and the map moves it as
		// one.
		var held = new Node3D { Position = _map.WorldAt(site) + (Vector3.Up * (-bounds.Position.Y * scale)) };
		AddChild(held);
		_armies[army] = held;

		// Facing the way it last marched: redrawn after the walk, it used to turn back to face the
		// same corner of the map every company faces before it has gone anywhere.
		held.Rotation = new Vector3(0f, _headings.GetValueOrDefault(army, RestingHeading), 0f);
		_armySites[army] = site;

		var man = new MeshInstance3D { Mesh = SoldierFigure.Standard.Mesh, MaterialOverride = SoldierFigure.Standard.Material };
		held.AddChild(man);
		man.SetInstanceShaderParameter("lord_color", lord);
		man.SetInstanceShaderParameter("walking", 0f);
		man.SetInstanceShaderParameter("banner", 1f);
		// Facing the way the company faces: turned a quarter off it, he walked every road crabwise.
		man.Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), Vector3.Zero);
	}
}
