using System.Collections.Generic;
using Godot;

/// <summary>The men on the field, drawn: every man of a <see cref="FieldBattle"/> where the battle
/// says he is, with his health over his head, the fallen where they fell, and the arrows in the air.
///
/// Drawing only. The battle moves its men ten times a second; the screen walks each of them
/// between where he was and where he is, so a march is a march and not a series of hops, and turns
/// him gradually rather than snapping him round.</summary>
public partial class BattlefieldSquads : Node3D
{
	/// <summary>How long a fallen man lies before the field lets him go, and how long he takes to go,
	/// in seconds of the battle. Left lying, the dead covered the field so thickly that nobody could
	/// see the living among them.</summary>
	private const float LyingFor = 3f;
	private const float GoingFor = 1f;

	/// <summary>A baked figure's clips, by the names tools/bake_figure.py keeps them under; where in
	/// the shoot clip the arrow leaves the string, in seconds (the archer's rig, arcas_v2: frame 34 of
	/// his shoot_close at thirty a second); the paces at which he starts walking and stops; and past which pace a
	/// figure that can gallop does.</summary>
	private const string WalkClip = "walk_loop";
	private const string GallopClip = "gallop_loop";
	private const string RunClip = "run_loop";
	private const string IdleClip = "idle_loop";
	private const string ShootClip = "shoot";
	private const string AttackClip = "attack";
	private const string DeathClip = "death";
	private const float LooseInShoot = 34f / 30f;
	private const float StartsWalking = 0.6f;
	private const float StopsWalking = 0.2f;
	private const float GallopsFrom = 2.5f;

	/// <summary>Past which pace a man on foot who has a run breaks into it: his walk is made for
	/// under a metre a second, and played at a charge's two and a half it was a scurry.</summary>
	private const float RunsFrom = 1.4f;

	/// <summary>How fast each moving clip was made to cover ground, in model units a second, so the
	/// clip is run exactly as fast as the man's feet — or the horse's hooves — carry him and nothing
	/// slides. The knight's are measured off his hooves (how far one swings, over the part of the
	/// stride it is down: three fifths of a walk, two of a gallop).</summary>
	private static readonly Dictionary<(string Model, string Clip), float> ClipPaces = new()
	{
		[("units/bow", WalkClip)] = 0.93f,
		[("units/bow", RunClip)] = 2.55f,
		[("units/horse", WalkClip)] = 0.72f,
		[("units/horse", GallopClip)] = 3.2f,
		[("units/peasant", WalkClip)] = 0.89f,
		[("units/peasant", RunClip)] = 1.67f,
	};

	/// <summary>A clip's pace, where it has been measured; a man's walk where it has not.</summary>
	private static float ClipPace(SoldierFigure figure, string clip) =>
		ClipPaces.GetValueOrDefault((figure.Model, clip), ClipPaces[("units/bow", WalkClip)]);

	/// <summary>Past how far from the eye a squad is drawn with its men thinned (SoldierFigure.Far), in
	/// metres: past it a man is a finger tall on the screen, and the slips of paint thinning him
	/// leaves are not seen.</summary>
	private const float FarFrom = 70f;

	/// <summary>How long one clip takes to fade into the next, in seconds.</summary>
	private const float CrossFade = 0.25f;

	/// <summary>How quickly a man turns to face where he is going, a share a second.</summary>
	private const float Turning = 8f;

	/// <summary>How far a man steps into a blow, in metres, and how long the step and the lean
	/// into it take.</summary>
	private const float Lunge = 0.08f;
	private const float LungeSeconds = 0.4f;

	/// <summary>His gait. The pace at which he is fully striding rather than shuffling, how long his
	/// stride is at a walk and at most (radians of swing at the hip, longer the faster he goes), and
	/// how quickly he sets off and comes to a stop, a share a second.</summary>
	private const float FullStride = 0.8f;
	private const float StrideAtWalk = 0.28f;
	private const float StridePerPace = 0.06f;
	private const float StrideMost = 0.55f;
	private const float SettingOff = 6f;

	private const float ArrowArc = 0.22f;
	private const float ArrowRise = 1.5f;

	/// <summary>The bar over a man's head: how wide, how thick, and how high above his feet.</summary>
	private const float BarWide = 0.55f;
	private const float BarHigh = 0.07f;
	private const float BarEdge = 0.015f;
	private const float BarOver = 0.08f;
	private static readonly Color BarBehind = new(0.05f, 0.04f, 0.03f, 0.85f);
	private static readonly Color Dying = new("d0342c");
	private const float LowHealth = 0.4f;

	private sealed class Drawn
	{
		public FieldSquad Squad;
		public SoldierFigure Figure;
		public BattlefieldBatch Men;
		public BattlefieldBatch Fallen;
		public readonly List<(Transform3D Lying, float Fell)> Dead = new();
		public Color Colour;
		/// <summary>Whether anyone of the squad has fallen since its dead were last handed over.</summary>
		public bool HasNewDead;
	}

	private readonly List<Drawn> _drawn = new();
	private readonly Dictionary<FieldSoldier, float> _turned = new();
	private readonly Dictionary<object, Gait> _gaits = new();

	/// <summary>Where a man is in his stride, how much he is walking at all, and where he stood last
	/// frame — his pace is what his feet covered since.</summary>
	private sealed class Gait
	{
		public float Phase;
		public float March;
		public bool IsWalking;
		public string Clip;
		public float Time;
		public string Was;
		public float WasTime;
		public float Fade = 1f;
		public Vector2 Last;
	}
	private readonly Dictionary<FieldSquad, Drawn> _bySquad = new();
	private FieldBattle _battle;
	/// <summary>The ground the men stand on.</summary>
	public BattlefieldLand Land { get; set; }

	/// <summary>The castle's wall in an assault, whose walkway and ladders lift the men on them; null in the field.</summary>
	public FieldWall Wall { get; set; }

	private MultiMeshInstance3D _marks;
	private MultiMeshInstance3D _arrows3;

	/// <summary>The caret before a company on a front being drawn: how long, how wide, how thick its
	/// bars, and how far ahead of its front rank; and the gold every mark of a drawn front is in,
	/// whoever's colours the lord flies — the gold of the march trail on the map.</summary>
	private const float ArrowLong = 2.2f;
	private const float ArrowWide = 3.4f;
	private const float CaretThick = 0.45f;
	private static readonly Color MarkGold = new("e0b95f");
	private const float ArrowAhead = 2f;
	private const int MostArrows = 40;

	/// <summary>How big the mark of a man's place on a drawn front is, and the most there can be.</summary>
	private const float MarkRadius = 0.35f;
	private const int MostMarks = 1000;
	private BattlefieldBatch _arrows;
	private BattlefieldBatch _barsBehind;
	private BattlefieldBatch _barsFilled;
	/// <summary>Stands every man of the battle on the field, in his lord's colour.</summary>
	public void Muster(FieldBattle battle, Color ours, Color theirs)
	{
		_battle = battle;
		if (SoldierFigure.Standard.Mesh == null)
		{
			return;
		}

		int figures = 0;
		foreach (FieldSquad squad in battle.Squads)
		{
			// An engine is drawn by the castle (BattlefieldCastle), not as a man.
			if (squad.Kind.IsEngine)
			{
				continue;
			}

			Color colour = squad.IsAttacking ? ours : theirs;
			SoldierFigure figure = SoldierFigure.For(squad.Unit);
			figure = figure.Mesh == null ? SoldierFigure.Standard : figure;
			var drawn = new Drawn
			{
				Squad = squad,
				Figure = figure,
				Colour = colour,
				Men = new BattlefieldBatch(Body(figure, squad.Soldiers.Count, colour).Multimesh),
				Fallen = new BattlefieldBatch(Body(figure, squad.Soldiers.Count, colour).Multimesh),
			};
			figures += squad.Soldiers.Count;
			_drawn.Add(drawn);
			_bySquad[squad] = drawn;
		}

		_arrows = new BattlefieldBatch(Many(BattlefieldStreak.Mesh(), figures, BattlefieldStreak.Look(), coloured: false).Multimesh);
		_barsBehind = new BattlefieldBatch(Many(new QuadMesh(), figures, Bar(0), coloured: true).Multimesh);
		_barsFilled = new BattlefieldBatch(Many(new QuadMesh(), figures, Bar(1), coloured: true).Multimesh);
	}

	/// <summary>Draws the field as it stands, <paramref name="between"/> of the way from the last slice
	/// of the battle to the next. <paramref name="eye"/> is the camera, which the health bars face;
	/// they are shown only when <paramref name="barsShown"/>, since from high up they are a haze.</summary>
	public void Follow(float delta, float between, ICollection<FieldSquad> chosen, Camera3D eye, bool barsShown)
	{
		if (_battle == null || _drawn.Count == 0)
		{
			return;
		}

		Guide(chosen, eye.GlobalPosition.DistanceTo(Land.On(new Vector2(eye.GlobalPosition.X, eye.GlobalPosition.Z))));
		float turn = Mathf.Min(1f, delta * Turning);
		float clock = _battle.Clock + (between * FieldBattle.Slice);
		int bars = 0;
		Basis facingUs = eye.GlobalBasis;
		foreach (Drawn drawn in _drawn)
		{
			FieldSquad squad = drawn.Squad;
			BattlefieldBatch men = drawn.Men;
			MultiMesh body = men.Mesh;
			Mesh seen = drawn.Figure.Far != null
				&& eye.GlobalPosition.DistanceTo(new Vector3(squad.At.X, 0f, squad.At.Y)) > FarFrom
					? drawn.Figure.Far
					: drawn.Figure.Mesh;
			if (body.Mesh != seen)
			{
				body.Mesh = seen;
			}

			int i = 0;
			bool inHand = chosen.Contains(squad);
			foreach (FieldSoldier man in squad.Soldiers)
			{
				SoldierFigure figure = drawn.Figure;
				float stature = figure.Stature;
				Vector3 at = Ground(man.Was.Lerp(man.At, between));
				float struck = clock - man.Struck;
				if (struck < LungeSeconds && !squad.Shoots && !figure.IsBaked)
				{
					at += new Vector3(man.Facing.X, 0f, man.Facing.Y) * (Lunge * Mathf.Sin(Mathf.Pi * struck / LungeSeconds));
				}

				float yaw = Mathf.LerpAngle(_turned.GetValueOrDefault(man, Yaw(man.Facing)), Yaw(man.Facing), turn);
				_turned[man] = yaw;
				men.Place(i, Standing(figure, at, yaw, stature));
				float lean = struck < LungeSeconds && !squad.Shoots ? Mathf.Sin(Mathf.Pi * struck / LungeSeconds) : 0f;
				men.Custom(i++, figure.IsBaked
					? Played(man, figure, man.Was.Lerp(man.At, between), delta, clock, between)
					: Stride(man, figure, man.Was.Lerp(man.At, between), delta, lean));

				// Only over the men the lord has in hand, and over anyone who has been hurt: forty
				// full bars over a squad that has not been touched said nothing and hid the men.
				if (barsShown && (inHand || man.Health < man.MostHealth))
				{
					HealthBar(bars++, at + (Vector3.Up * stature), man.Health / man.MostHealth, facingUs, drawn.Colour);
				}
			}

			men.Show(i);
		}

		_barsBehind.Show(bars);
		_barsFilled.Show(bars);
		LayDown(clock);
		Fly(clock);
	}

	/// <summary>The squad drawn nearest a spot on the ground, if the spot is inside its ranks.</summary>
	public FieldSquad At(Vector2 spot)
	{
		FieldSquad best = null;
		float bestFar = float.MaxValue;
		foreach (FieldSquad squad in _battle.Squads)
		{
			float far = squad.At.DistanceTo(spot);
			if (squad.IsStanding && far <= squad.Reach + 1.5f && far < bestFar)
			{
				best = squad;
				bestFar = far;
			}
		}

		return best;
	}

	/// <summary>The squad one of whose men is drawn under a point of the screen, wherever he stands —
	/// on a wall's walkway or half way up a ladder as much as on the ground: picked by the spot on the
	/// ground, a man on the walls could never be clicked, since the ground under the cursor there is
	/// the bailey behind him.</summary>
	public FieldSquad AtScreen(Vector2 screen, Camera3D eye, bool? attacking = null)
	{
		FieldSquad best = null;
		float bestFar = PickPixels * PickPixels;
		foreach (FieldSquad squad in _battle.Squads)
		{
			if (!squad.IsStanding || (attacking is bool side && squad.IsAttacking != side))
			{
				continue;
			}

			foreach (FieldSoldier man in squad.Soldiers)
			{
				Vector3 chest = Ground(man.At) + (Vector3.Up * ChestHigh);
				if (eye.IsPositionBehind(chest))
				{
					continue;
				}

				float far = eye.UnprojectPosition(chest).DistanceSquaredTo(screen);
				if (far < bestFar)
				{
					best = squad;
					bestFar = far;
				}
			}
		}

		return best;
	}

	private const float PickPixels = 28f;
	private const float ChestHigh = 1.1f;

	private MultiMeshInstance3D Body(SoldierFigure figure, int figures, Color colour)
	{
		var body = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				// Each man's march is his own: soldier.gdshader reads it from here, where the map's
				// single figures read it from their own instance parameter.
				UseCustomData = true,
				Mesh = figure.Mesh,
				InstanceCount = Mathf.Max(1, figures),
				VisibleInstanceCount = 0,
			},
			MaterialOverride = figure.Material,
		};
		AddChild(body);

		// Nobody in the ranks carries a banner: the placeholder figure is the map's standard-bearer, and
		// on the field his cloth would be a flag over every man.
		body.SetInstanceShaderParameter("lord_color", colour);
		body.SetInstanceShaderParameter("banner", 0f);
		body.SetInstanceShaderParameter("walking", 0f);
		return body;
	}

	/// <summary>A man standing with his feet on a spot, turned to a heading, drawn this tall.</summary>
	private static Transform3D Standing(SoldierFigure figure, Vector3 at, float yaw, float stature)
	{
		float scale = ScaleOf(figure, stature);
		Basis turned = new Basis(Vector3.Up, yaw).Scaled(Vector3.One * scale);
		Vector3 feet = turned * new Vector3(figure.Feet.X, 0f, figure.Feet.Y);
		return new Transform3D(turned, at - feet + (Vector3.Up * (-figure.Bounds.Position.Y * scale)));
	}

	/// <summary>What a figure's model is scaled by to stand this tall.</summary>
	private static float ScaleOf(SoldierFigure figure, float stature) =>
		stature / Mathf.Max(0.01f, figure.Bounds.Size.Y);

	/// <summary>The turn about the vertical that faces the model — who looks down +Z — along a
	/// direction on the field.</summary>
	private static float Yaw(Vector2 facing) => Mathf.Atan2(facing.X, facing.Y);

	private Vector3 Ground(Vector2 at) =>
		Wall == null ? Land.On(at) : Land.On(at) + (Vector3.Up * BattlefieldWalls.Raised(Wall, at));
}
