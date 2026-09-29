using System;
using System.Collections.Generic;
using Godot;

/// <summary>The field itself, for a lord who fights his own battle: the ground, the sky, his men and
/// the enemy's on it, and a hand on the reins.
///
/// It lays itself over the whole map in a world of its own and gives the day back as a
/// <see cref="Battle.Result"/> when it is done — the <see cref="BattlePanel"/> writes that into the
/// ledger exactly as it would the captain's reckoning, and nothing past the panel knows the
/// difference.
///
/// The orders are a real-time strategy game's, because that is what the lord's hand already knows:
/// the left button picks his squads (or draws a box round them), the right sends them — onto open
/// ground to march there, onto an enemy squad to fall on it. The keys move the eye (WASD, Q/E to
/// turn, the wheel to come closer) and Space stops the day while he thinks.</summary>
public partial class Battlefield : Control
{
	/// <summary>Raised once, when the day is over, with what it cost.</summary>
	public event Action<Battle.Result> Finished;

	private const float Field = 540f;
	private const float GroundTile = 8f;

	/// <summary>The bare earth: how fine its map is, how big its patches (a noise frequency a metre),
	/// and above what of it the ground is bare.</summary>
	private const int BareMap = 256;
	private const float BareFrequency = 0.018f;
	private const float BareFrom = 0.66f;

	/// <summary>How big the stands of tall grass are, as a noise frequency a metre.</summary>
	private const float TallGrassStands = 0.03f;
	private const string GroundAlbedo = "res://assets/terrain/pbr/ground037_alb_ht.png";
	private const string EarthAlbedo = "res://assets/terrain/pbr/path_alb_ht.png";
	private const string GroundShader = "res://assets/shaders/battlefield-ground.gdshader";

	/// <summary>The woods round the field: the campaign map's own trees (tools/decimate_meshy.py),
	/// drawn the way the map draws them, pines and oaks mixed; how tall they grow, how many, and the
	/// ring of ground they stand on — back from where anyone is fighting, so nobody's squad stands
	/// in a tree, and thick enough to be a wood rather than a scatter.</summary>
	private static readonly (string Model, bool IsEvergreen)[] Woods =
	{
		("trees/evergreen-pine", true), ("trees/cypress-tree", true), ("trees/oak-tree", false),
		("trees/whispering-oak", false), ("trees/verdant-guardian", false),
	};

	private const string FoliageShader = "res://assets/shaders/tree-foliage-close.gdshader";
	private const float TreeHeight = 17f;
	private const int Trees = 1400;
	private const float WoodsFrom = 95f;
	private const float WoodsTo = 250f;
	private const ulong WoodsSeed = 1215;

	/// <summary>How much of the wood grows in stands rather than one tree at a time, and how wide a
	/// stand is: a wood is thickets and glades, not trees dealt out evenly.</summary>
	private const int Stands = 90;
	private const float StandWide = 22f;

	private const float NearestEye = 18f;
	private const float FurthestEye = 260f;
	private const float StartingEye = 120f;
	private const float EyePitchDegrees = -40f;
	private const float PanSpeed = 0.9f;
	private const float TurnSpeed = 1.6f;
	private const float ZoomStep = 1.12f;
	private const float DragToBox = 6f;

	/// <summary>How close the eye must be before every man's health is drawn over his head. From
	/// further out the bars are a haze over the ranks and hide the men they are about.</summary>
	private const float BarsWithin = 140f;

	/// <summary>How long the field stays up once the day is decided, so the lord sees the rout
	/// before he sees the reckoning.</summary>
	private const float Lingering = 1.2f;

	/// <summary>The most slices of the fight caught up in one frame: a frame that stalls is a
	/// stutter, not a battle that jumps ahead.</summary>
	private const int MostSlicesAFrame = 5;

	private FieldBattle _battle;
	private BattlefieldSquads _squads;
	private BattlefieldBar _bar;
	private Camera3D _camera;
	private ColorRect _box;
	private readonly List<FieldSquad> _chosen = new();
	private Vector3 _focus;
	private float _yaw;
	private float _eye = StartingEye;
	private float _owed;
	private float _ending = -1f;
	private bool _isPaused;
	private Vector2? _pressedAt;
	private Vector2? _orderedAt;

	/// <summary>Lays the field out and starts the day. <paramref name="us"/> is the lord's side,
	/// which is always the attacking one.</summary>
	public void Begin(FieldBattle battle, BattlePanel.Colours us, BattlePanel.Colours them)
	{
		_battle = battle;
		Chrome.Fill(this);
		MouseFilter = MouseFilterEnum.Stop;

		var frame = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
		Chrome.Fill(frame);
		AddChild(frame);
		var world = new SubViewport
		{
			OwnWorld3D = true,
			Msaa3D = Viewport.Msaa.Msaa4X,
			HandleInputLocally = false,
		};
		frame.AddChild(world);

		world.AddChild(Sky());
		world.AddChild(Sun());
		(MeshInstance3D ground, Image bare) = Ground();
		world.AddChild(ground);
		var grass = new BattlefieldGrass();
		world.AddChild(grass);
		grass.Sow(bare, BareFrom, Field, new FastNoiseLite { Frequency = TallGrassStands, Seed = (int)WoodsSeed + 1 }, WoodsSeed);
		world.AddChild(Wood());

		_squads = new BattlefieldSquads();
		world.AddChild(_squads);
		_squads.Muster(battle, us.Accent, them.Accent);

		_camera = new Camera3D { Far = 2000f, Fov = 50f };
		world.AddChild(_camera);
		_focus = new Vector3(0f, 0f, FieldBattle.Gap * 0.35f);
		Look();

		_box = new ColorRect { Color = new Color(0.85f, 0.7f, 0.4f, 0.18f), Visible = false, MouseFilter = MouseFilterEnum.Ignore };
		AddChild(_box);

		_bar = new BattlefieldBar();
		AddChild(_bar);
		_bar.Lay(battle, us, them, Pick, Charge, Hold, Captain, battle.Withdraw);

		foreach (FieldSquad squad in battle.Squads)
		{
			if (squad.IsAttacking)
			{
				_chosen.Add(squad);
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_battle == null)
		{
			return;
		}

		Steer((float)delta);
		if (!_isPaused)
		{
			_owed = Mathf.Min(_owed + (float)delta, FieldBattle.Slice * MostSlicesAFrame);
			while (_owed >= FieldBattle.Slice)
			{
				_battle.Step();
				_owed -= FieldBattle.Slice;
			}
		}

		_chosen.RemoveAll(squad => !squad.IsStanding);
		_squads.Follow(_isPaused ? 0f : (float)delta, _owed / FieldBattle.Slice, _chosen, _camera, _eye <= BarsWithin);
		_bar.Refresh(_chosen, _isPaused);

		if (_battle.IsOver && _ending < 0f)
		{
			_ending = Lingering;
		}
		else if (_ending >= 0f)
		{
			_ending -= (float)delta;
			if (_ending < 0f)
			{
				Battle.Result day = _battle.Result();
				_battle = null;
				Finished?.Invoke(day);
			}
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_battle == null)
		{
			return;
		}

		switch (@event)
		{
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
				Zoom(1f / ZoomStep);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
				Zoom(ZoomStep);
				break;
			// A trackpad sends gestures, not wheel buttons.
			case InputEventPanGesture pan:
				Zoom(1f + (pan.Delta.Y * 0.05f));
				break;
			case InputEventMagnifyGesture magnify:
				Zoom(1f / magnify.Factor);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
				_pressedAt = press.Position;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release:
				Choose(release.Position, release.ShiftPressed);
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } order:
				_orderedAt = order.Position;
				break;
			case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } order:
				Order(order.Position);
				break;
			case InputEventMouseMotion motion when _orderedAt is Vector2 start:
				if (start.DistanceTo(motion.Position) > DragToBox && Front(start, motion.Position) is var (near, far, facing))
				{
					List<Vector2> spots = _battle.Planned(_chosen, near, far, facing, out List<Vector2> fronts);
					_squads.Mark(spots, fronts, facing);
				}
				else
				{
					_squads.Mark(null, null, Vector2.Zero);
				}

				break;
			case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Middle) != 0:
				_yaw -= motion.Relative.X * 0.005f;
				Look();
				break;
			case InputEventMouseMotion motion when _pressedAt is Vector2 from:
				Rect2 box = new Rect2(from, motion.Position - from).Abs();
				_box.Visible = box.Size.Length() > DragToBox;
				_box.Position = box.Position;
				_box.Size = box.Size;
				break;
		}

		AcceptEvent();
	}

	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (_battle != null && @event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Space })
		{
			_isPaused = !_isPaused;
			GetViewport().SetInputAsHandled();
		}
	}

}
