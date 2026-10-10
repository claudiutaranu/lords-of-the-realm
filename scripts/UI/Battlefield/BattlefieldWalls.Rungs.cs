using Godot;

/// <summary>How each rung of the fortifications ladder is drawn on the field (BattlefieldWalls), so
/// the place the lord storms is the place his county has built (the user's call): a palisade of
/// stakes on an earth bank; a motte and bailey, with its tower on a mound; a wooden keep among
/// wooden towers; and the stone rungs, their walls rising and their keep growing up to the royal
/// castle's. The fight is the same square of wall for all of them (FieldWall); only its look and its
/// height change.</summary>
public static partial class BattlefieldWalls
{
	private enum KeepKind { None, Motte, Wooden, Stone }

	private readonly record struct Rung(float High, bool Timber, bool CornerTowers, bool GateTowers, KeepKind Keep,
		float KeepSide, float KeepHigh, float TowerScale);

	private static Rung RungOf(FieldWall wall) => wall.Fortification switch
	{
		"small-palisade" => new(3.0f, true, false, false, KeepKind.None, 0f, 0f, 1f),
		"medium-fort" => new(3.0f, true, false, false, KeepKind.Motte, 5f, 6f, 1f),
		"large-fort" => new(3.4f, true, true, true, KeepKind.Wooden, 8f, 8f, 0.8f),
		"small-castle" => new(3.8f, false, true, true, KeepKind.Stone, 8f, 9f, 0.85f),
		"medium-castle" => new(4.2f, false, true, true, KeepKind.Stone, 10f, 11f, 0.95f),
		"grand-castle" => new(5.4f, false, true, true, KeepKind.Stone, 15f, 16f, 1.2f),
		_ => new(4.6f, false, true, true, KeepKind.Stone, 12f, 13f, 1f),
	};

	/// <summary>How far apart a palisade's stakes stand, and how far below their points the bank behind
	/// them comes up, which is where its defenders stand.</summary>
	private const float StakeEvery = 0.42f;
	private const float StakeOverBank = 1.3f;
	private static readonly Color Earth = new("5e4a32");
	private static readonly Color MotteGrass = new("3d5420");
	private static readonly Color Slate = new("4c525c");

	private static float BankTop(Rung rung) => rung.High - StakeOverBank;

	/// <summary>A stake, sharpened: a log as tall as the palisade with its point on top.</summary>
	private static Mesh Stake(float high)
	{
		var log = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.19f, Height = high, RadialSegments = 6 };
		var point = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.17f, Height = 0.45f, RadialSegments = 6 };
		var surface = new SurfaceTool();
		surface.AppendFrom(log, 0, new Transform3D(Basis.Identity, Vector3.Up * (high / 2f)));
		surface.AppendFrom(point, 0, new Transform3D(Basis.Identity, Vector3.Up * (high + 0.22f)));
		return surface.Commit();
	}

	/// <summary>The keep in the back of the bailey, or the motte and its tower.</summary>
	private static void Keep(Node3D built, FieldWall wall, BattlefieldLand land, Rung rung, Material stone)
	{
		if (rung.Keep == KeepKind.None)
		{
			return;
		}

		Vector2 at = wall.Middle + new Vector2(0f, -wall.Half * 0.45f);
		float rise = 0f;
		if (rung.Keep == KeepKind.Motte)
		{
			// The mound: earth heaped and grassed over, the tower on its flat top.
			float reach = wall.Half * 0.42f;
			rise = 3.5f;
			built.AddChild(new MeshInstance3D
			{
				Mesh = new SphereMesh { Radius = reach, Height = rise * 2f, RadialSegments = 24, Rings = 8 },
				Position = land.On(at),
				MaterialOverride = Plain(MotteGrass),
			});
		}

		bool isWood = rung.Keep != KeepKind.Stone;
		var keep = new Node3D { Position = Vector3.Up * rise };
		Node3D tower = Tower(land, at, rung.KeepSide, rung.KeepHigh, isWood ? Plain(TimberBrown.Darkened(0.1f)) : stone, isWood);
		keep.AddChild(tower);
		// A roof over it: the kit's pyramid for stone, a plank cone for timber.
		float top = isWood ? rung.KeepHigh + 0.3f : TowerFloor(rung.KeepHigh) + 0.4f;
		Vector3 roofAt = land.On(at) + (Vector3.Up * top);
		if (isWood)
		{
			keep.AddChild(Block(new CylinderMesh { TopRadius = 0f, BottomRadius = rung.KeepSide * 0.78f, Height = rung.KeepSide * 0.7f, RadialSegments = 4 },
				roofAt + (Vector3.Up * rung.KeepSide * 0.35f), Plain(TimberBrown.Darkened(0.35f)), new Basis(Vector3.Up, Mathf.Pi / 4f)));
		}
		else
		{
			keep.AddChild(Piece("tower-square-roof", Basis.FromScale(new Vector3(rung.KeepSide, rung.KeepSide * 0.45f, rung.KeepSide)),
				roofAt, Plain(Slate)));
		}

		built.AddChild(keep);
	}
}
