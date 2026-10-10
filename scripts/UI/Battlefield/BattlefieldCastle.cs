using System.Collections.Generic;
using Godot;

/// <summary>The castle on the field of an assault as the day goes: laid again whenever the engines
/// bring a stretch of it down (FieldWall.Version), and over every stretch a bar of what it has left,
/// in the holder's colour while the engines are at it, so the lord can see how near the breach is; and the besiegers' engines,
/// each where its company of one stands and facing the way it goes, broken once it is.</summary>
public partial class BattlefieldCastle : Node3D
{
	private const float BarWide = 6f;
	private const float BarLingers = 3f;
	private const float BarHigh = 0.45f;
	private const float BarOver = 3.2f;
	private static readonly Color Spent = new(0.12f, 0.1f, 0.08f, 0.85f);

	private readonly FieldWall _wall;
	private readonly BattlefieldLand _land;
	private readonly Color _holder;
	private readonly Dictionary<FieldWall.Target, MeshInstance3D> _bars = new();
	private readonly Dictionary<FieldSquad, (Node3D Model, bool IsBroken)> _engines = new();
	private readonly IReadOnlyList<FieldSquad> _squads;
	private readonly Dictionary<FieldSquad, float> _yaws = new();
	private readonly Dictionary<FieldWall.Opening, Node3D> _ladders = new();

	/// <summary>How quickly a drawn engine catches up with where it stands, and how much slower it turns.</summary>
	private const float EnginesFollow = 6f;
	private const float EnginesTurn = 0.35f;
	private Node3D _built;
	private int _laid = -1;

	public BattlefieldCastle(FieldWall wall, BattlefieldLand land, Color holder, IReadOnlyList<FieldSquad> squads)
	{
		_wall = wall;
		_land = land;
		_holder = holder;
		_squads = squads;
	}

	public BattlefieldCastle()
	{
	}

	public override void _Process(double delta)
	{
		if (_wall == null)
		{
			return;
		}

		if (_laid != _wall.Version)
		{
			_laid = _wall.Version;
			_built?.QueueFree();
			_built = BattlefieldWalls.Build(_wall, _land, _holder);
			AddChild(_built);
		}

		Ladders();
		Engines((float)delta);
		Throw((float)delta);
		foreach (FieldWall.Target target in _wall.Battered)
		{
			if (!_bars.TryGetValue(target, out MeshInstance3D bar))
			{
				bar = Bar(target);
				_bars[target] = bar;
			}

			// Every stretch has its health, but its bar shows only while the engines are at it.
			bar.Visible = !target.IsDown && _wall.Clock - target.StruckAt < BarLingers;
			float share = Mathf.Clamp(target.Health / target.Full, 0f, 1f);
			// Shrinking to its middle: a billboard turns with the eye, so an offset to one end would not.
			bar.GetChild<MeshInstance3D>(0).Scale = new Vector3(Mathf.Max(0.001f, share), 1f, 1f);
		}
	}

	/// <summary>A ladder stands against the wall only while one of the attackers is on it or at its foot.</summary>
	private void Ladders()
	{
		foreach (FieldWall.Opening gap in _wall.Openings)
		{
			if (gap.Is != FieldWall.Kind.Ladder)
			{
				continue;
			}

			bool isClimbed = false;
			foreach (FieldSquad squad in _squads)
			{
				if (squad.IsAttacking && squad.IsStanding && !squad.Kind.IsEngine)
				{
					isClimbed = squad.Soldiers.Exists(man => _wall.Climbing(man.At) && _wall.PointOf(gap).DistanceTo(man.At) < 3f);
				}

				if (isClimbed)
				{
					break;
				}
			}

			if (isClimbed && !_ladders.ContainsKey(gap))
			{
				_ladders[gap] = BattlefieldWalls.LadderAt(_wall, _land, gap);
				AddChild(_ladders[gap]);
			}
		}
	}

	/// <summary>Every engine where its man stands, turned to his facing; swapped for its wreck when it falls.</summary>
	private void Engines(float delta)
	{
		// Eased toward where the engine stands and the way it faces: the field moves in tenths of a
		// second, and an engine set straight onto each slice's spot went forward and round in jerks.
		float ease = 1f - Mathf.Exp(-EnginesFollow * delta);
		foreach (FieldSquad squad in _squads)
		{
			if (!squad.Kind.IsEngine)
			{
				continue;
			}

			bool isBroken = !squad.IsStanding;
			if (!_engines.TryGetValue(squad, out (Node3D Model, bool IsBroken) drawn) || drawn.IsBroken != isBroken)
			{
				// The wreck lies where the engine stood.
				Transform3D stood = drawn.Model?.Transform ?? Transform3D.Identity;
				drawn.Model?.QueueFree();
				drawn = (BattlefieldWalls.Engine(squad.Unit, isBroken), isBroken);
				drawn.Model.Transform = stood;
				AddChild(drawn.Model);
				_engines[squad] = drawn;
			}

			FieldSoldier man = squad.Soldiers.Count > 0 ? squad.Soldiers[0] : null;
			if (man != null && !isBroken)
			{
				float yaw = BattlefieldWalls.EngineYaw(man.Facing);
				bool isNew = !_yaws.ContainsKey(squad);
				float turned = isNew ? yaw : Mathf.LerpAngle(_yaws[squad], yaw, ease * EnginesTurn);
				_yaws[squad] = turned;
				Vector3 there = _land.On(man.At);
				Vector3 now = isNew ? there : drawn.Model.Position.Lerp(there, ease);
				drawn.Model.Transform = new Transform3D(new Basis(Vector3.Up, turned), now);
			}
		}
	}

	/// <summary>A bar over a stretch, facing the eye: dark for what is spent, the holder's colour over it.</summary>
	private MeshInstance3D Bar(FieldWall.Target target)
	{
		(Vector2 from, Vector2 to) = _wall.Ends(target.Gap.On);
		Vector2 at = from + ((to - from).Normalized() * target.Gap.Middle);
		float high = BattlefieldWalls.Raised(_wall, at - ((at - _wall.Middle).Normalized() * 1f));
		var back = new MeshInstance3D
		{
			Mesh = new QuadMesh { Size = new Vector2(BarWide, BarHigh) },
			Position = _land.On(at) + (Vector3.Up * (high + BarOver)),
			MaterialOverride = Flat(Spent, 0),
		};
		back.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(BarWide, BarHigh) }, MaterialOverride = Flat(_holder, 1) });
		AddChild(back);
		return back;
	}

	private static StandardMaterial3D Flat(Color colour, int over) => new()
	{
		AlbedoColor = colour,
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
		BillboardKeepScale = true,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		RenderPriority = over,
		NoDepthTest = true,
	};
}
