using System.Collections.Generic;
using Godot;

/// <summary>The castle's wall drawn on the field of an assault (FieldWall): a curtain of dressed stone
/// with a walkway along its top and merlons on its outer lip, or of timber stakes for the timber
/// rungs; a tower on each corner and two either side of the gate, each hung with the holder's banner;
/// the gate shut under its portcullis, or its leaves thrown down once broken; rubble at each
/// breach; a ladder in each place to climb; and torches burning on the towers. The fight itself is on
/// the flat (FieldWall): the men the wall's line puts on its walkway are only drawn up there
/// (<see cref="Raised"/>), and so are the men half way up a ladder. Placeholder art, built in code,
/// until the castle kit is sent.</summary>
public static partial class BattlefieldWalls
{
	/// <summary>How long one length of curtain is, and how high, in metres. Stone stands high enough
	/// that a man on its walkway is seen above the men below; the timber rungs lower.</summary>
	private const float Length = 3f;

	/// <summary>How far the stone runs inward from the wall's line, which is the walkway a defender
	/// standing off the wall is drawn on (FieldWall keeps him a metre in), and how far it stands out
	/// past the line toward the attacker.</summary>
	private const float Walkway = FieldWall.Walkway;
	private const float OuterLip = 0.7f;
	private const float MerlonHigh = 0.9f;
	private const float MerlonWide = 0.9f;
	private const float MerlonEvery = 1.6f;

	/// <summary>The towers: on the corners, and either side of the gate; how far they stand above the
	/// curtain, and the gate's own height under its arch.</summary>
	private const float CornerSide = 7f;
	private const float GateTowerSide = 5.5f;
	private const float TowerOver = 2.6f;
	private const float GateHigh = 3.4f;
	private const float GateWide = 5f;

	/// <summary>How thick the planks of the gallery over a towerless gate are.</summary>
	private const float GalleryDeep = 0.3f;

	/// <summary>A ladder's lean, and how far out from its foot a man climbing it is still on it.</summary>
	private const float LadderLean = 0.36f;
	private const float ClimbReach = 2.4f;

	private static readonly Color StoneTint = new("c9bfae");
	private static readonly Color TowerTint = new("b7ad9c");
	private static readonly Color TimberBrown = new("6b4a2b");
	private static readonly Color LadderWood = new("8a6a3e");
	private static readonly Color GateWood = new("4a321d");
	private static readonly Color Portcullis = new("2b2a28");

	/// <summary>The kit's own measures: its curtain's walkway, and its portcullis's height and width.</summary>
	private const float KitWalkway = 1.18f;

	/// <summary>How far the stone pieces are sunk into the ground they stand on, so a rolling field
	/// shows no gap under them.</summary>
	private const float Sunk = 0.3f;
	private const float KitGateHigh = 0.73f;
	private const float KitGateWide = 0.7f;

	private const string StoneTexture = "res://assets/terrain/pbr/rock023_alb_ht.png";

	/// <summary>How high a man at this spot is drawn above the ground: on the walkway if the wall's
	/// line puts him on top of it (and he is not in its gate or a breach), part of the way up if he is
	/// on a ladder outside it, and on the ground anywhere else.</summary>
	public static float Raised(FieldWall wall, Vector2 at)
	{
		Rung rung = RungOf(wall);
		float high = rung.High;
		if (wall.OnTower(at) && rung.CornerTowers)
		{
			return rung.Timber ? high + TowerOver - Sunk : TowerFloor(high + TowerOver);
		}

		// The walkway: the kit's curtain is sunk a little into the ground, as it is laid; a palisade's
		// is the earth bank behind the stakes, a man's chest below their points.
		if (wall.OnWalls(at))
		{
			return rung.Timber ? BankTop(rung) : high - Sunk;
		}

		// Outside, at a ladder: part of the way up it, more the nearer the wall.
		Vector2 off = at - wall.Middle;
		float outside = Mathf.Max(Mathf.Abs(off.X), Mathf.Abs(off.Y)) - wall.Half;
		return outside > 0f && outside <= ClimbReach && wall.Climbing(at) ? high * 0.85f * (1f - (outside / ClimbReach)) : 0f;
	}

	/// <summary>The wall as it stands now; BattlefieldCastle lays it again each time the engines open it.</summary>
	public static Node3D Build(FieldWall wall, BattlefieldLand land, Color holder)
	{
		var built = new Node3D();
		Rung rung = RungOf(wall);
		float high = rung.High;
		var stakes = new List<Transform3D>();
		var banks = new List<Transform3D>();
		Material stone = wall.IsTimber ? Plain(TimberBrown) : Stone(StoneTint);
		Material towerStone = wall.IsTimber ? Plain(TimberBrown.Darkened(0.15f)) : Stone(TowerTint);
		var lengths = new List<Transform3D>();
		float deep = Walkway + OuterLip;

		foreach (FieldWall.Face face in FieldWall.Faces)
		{
			(Vector2 from, Vector2 to, Vector2 along, Vector2 outward, Basis turned) = Lie(wall, face);
			float side = from.DistanceTo(to);
			foreach ((float d, float upto) in Lengths(wall, face, side, Length))
			{
				// The stone sits mostly inside the line: its outer lip just past it, its walkway behind.
				Vector2 middle = from + (along * ((d + upto) / 2f)) - (outward * ((Walkway - OuterLip) / 2f));
				if (!wall.IsTimber)
				{
					// The lord's own length of curtain, as high and as deep as the wall's walkway.
					// The kit's length of curtain, its walkway brought to the wall's height. Its merlons run
					// along its own depth, so it is turned a quarter to lay them along the face.
					built.AddChild(Piece("wall", turned * new Basis(Vector3.Up, Mathf.Pi / 2f)
						* Basis.FromScale(new Vector3(deep, high / KitWalkway, upto - d + 0.05f)),
						land.On(middle) + (Vector3.Up * -0.3f), stone));
					continue;
				}

				// A palisade: stakes along the line, an earth bank behind them for the walkway.
				Vector2 bank = from + (along * ((d + upto) / 2f)) - (outward * (Walkway / 2f));
				banks.Add(new Transform3D(turned * Basis.FromScale(new Vector3((upto - d + 0.05f) / Length, 1f, 1f)),
					land.On(bank) + (Vector3.Up * ((BankTop(rung) / 2f) - Sunk))));
				for (float s = d; s < upto; s += StakeEvery)
				{
					Vector2 at = from + (along * s) + (outward * 0.15f);
					stakes.Add(new Transform3D(new Basis(Vector3.Up, s * 1.7f), land.On(at) + (Vector3.Up * -Sunk)));
				}
			}

			// A tower on the corner the face starts from, hung with the holder's banner toward the field.
			Vector2 corner = from + ((from - wall.Middle).Normalized() * 1.2f);
			if (!rung.CornerTowers)
			{
				continue;
			}

			built.AddChild(Tower(land, corner, CornerSide * rung.TowerScale, high + TowerOver, towerStone, wall.IsTimber));
			if (face == FieldWall.Face.South || face == FieldWall.Face.East)
			{
				built.AddChild(Banner(land, corner + (outward * ((CornerSide / 2f) + 0.05f)), high + TowerOver, outward, holder,
					isMirrored: face == FieldWall.Face.East));
				built.AddChild(Torch(land, corner + (outward * ((CornerSide / 2f) + 0.3f)) + (along * 2.2f), high));
			}
		}

		if (stakes.Count > 0)
		{
			built.AddChild(Many(Stake(high), stakes, Plain(TimberBrown)));
			built.AddChild(Many(new BoxMesh { Size = new Vector3(Length, BankTop(rung), Walkway) }, banks, Plain(Earth)));
		}

		Gatehouse(built, wall, land, rung, towerStone, holder);
		Keep(built, wall, land, rung, towerStone);
		foreach (FieldWall.Opening gap in wall.Openings)
		{
			(Vector2 from, _, Vector2 along, Vector2 outward, Basis turned) = Lie(wall, gap.On);
			Vector2 at = from + (along * gap.Middle);
			switch (gap.Is)
			{
				case FieldWall.Kind.Breach:
					float spin = 0f;
					foreach (float d in new[] { gap.From + 0.6f, gap.To - 0.6f, gap.Middle - 1.5f, gap.Middle + 1.8f, gap.Middle })
					{
						spin += 1.9f;
						built.AddChild(Piece("rocks-large", turned * new Basis(Vector3.Up, spin) * Basis.FromScale(Vector3.One * 2.2f),
							land.On(from + (along * d) + (outward * 0.6f)), stone));
					}

					break;
				case FieldWall.Kind.Ladder:
					// No ladder stands against the wall until somebody climbs it (BattlefieldCastle), and the
					// garrison reach their walkway by the stairs inside (the user's call).
					if (!wall.IsTimber)
					{
						built.AddChild(Stairs(land, at - (outward * (Walkway + StairsOff)), turned, high - Sunk));
					}

					break;
			}
		}

		return built;
	}

	/// <summary>The south face's gate in its two towers: shut under its portcullis while it stands,
	/// the planks thrown down and the ram still in the arch once it has been broken.</summary>
	private static void Gatehouse(Node3D built, FieldWall wall, BattlefieldLand land, Rung rung, Material stone,
		Color holder)
	{
		float high = rung.High;
		(Vector2 from, _, Vector2 along, Vector2 outward, Basis turned) = Lie(wall, FieldWall.Face.South);
		Vector2 gate = from + (along * wall.Half);
		foreach (float side in rung.GateTowers ? new[] { -1f, 1f } : System.Array.Empty<float>())
		{
			Vector2 tower = gate + (along * side * ((GateWide / 2f) + (GateTowerSide / 2f))) - (outward * 0.6f);
			built.AddChild(Tower(land, tower, GateTowerSide, high + TowerOver, stone, wall.IsTimber));
			built.AddChild(Banner(land, tower + (outward * ((GateTowerSide / 2f) + 0.05f)), high + TowerOver, outward, holder,
				isMirrored: side > 0f));
			built.AddChild(Torch(land, tower + (outward * ((GateTowerSide / 2f) + 0.3f)) - (along * side * 2f), GateHigh));
		}

		// The span over the arch, from tower to tower, at the walkway's height.
		float over = high - GateHigh;
		if (rung.GateTowers)
		{
			built.AddChild(Block(new BoxMesh { Size = new Vector3(GateWide + 0.4f, over, Walkway + OuterLip) },
			land.On(gate - (outward * ((Walkway - OuterLip) / 2f))) + (Vector3.Up * (GateHigh + (over / 2f))), stone, turned));
		}

		bool isBroken = wall.Openings.Exists(gap => gap.Is == FieldWall.Kind.Gate);
		if (!rung.GateTowers && !isBroken)
		{
			// With no towers to carry a span, a gallery of planks over the gate on two posts, at the
			// walkway's level: the walkway runs on across a shut gate (FieldWall.OnWalls), and with
			// nothing drawn there the men walking it trod the air.
			float deck = (rung.Timber ? BankTop(rung) : high) - Sunk;
			Vector2 behind = gate - (outward * (Walkway / 2f));
			built.AddChild(Block(new BoxMesh { Size = new Vector3(GateWide + 0.4f, GalleryDeep, Walkway) },
				land.On(behind) + (Vector3.Up * (deck - (GalleryDeep / 2f))), Plain(TimberBrown), turned));
			foreach (float side in new[] { -1f, 1f })
			{
				Vector2 post = gate + (along * side * (GateWide / 2f)) - (outward * (Walkway - 0.3f));
				built.AddChild(Block(new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.18f, Height = deck, RadialSegments = 6 },
					land.On(post) + (Vector3.Up * (deck / 2f)), Plain(GateWood)));
			}
		}

		if (!isBroken)
		{
			// The kit's portcullis lies along its own depth: turned a quarter to stand across the arch.
			built.AddChild(Piece("metal-gate", turned * new Basis(Vector3.Up, Mathf.Pi / 2f)
				* Basis.FromScale(new Vector3(3f, Mathf.Min(GateHigh, high) / KitGateHigh, GateWide / KitGateWide)),
				land.On(gate + (outward * OuterLip)), Plain(rung.Timber ? GateWood : Portcullis)));
			return;
		}

		// The broken leaves thrown down either side.
		foreach (float side in new[] { -1f, 1f })
		{
			var leaf = Block(new BoxMesh { Size = new Vector3(GateWide / 2f, 0.25f, GateHigh * 0.8f) },
				land.On(gate + (along * side * 1.4f) + (outward * 1.6f)) + (Vector3.Up * 0.15f), Plain(GateWood), turned);
			leaf.RotateObjectLocal(Vector3.Forward, side * 0.12f);
			built.AddChild(leaf);
		}

	}

	/// <summary>A face's two ends, which way it runs, which way is out of the castle across it, and the
	/// turn that lays a length of it along the face.</summary>
	private static (Vector2 From, Vector2 To, Vector2 Along, Vector2 Outward, Basis Turned) Lie(FieldWall wall, FieldWall.Face face)
	{
		(Vector2 from, Vector2 to) = wall.Ends(face);
		Vector2 along = (to - from).Normalized();
		Vector2 middle = (from + to) / 2f;
		Vector2 outward = (middle - wall.Middle).Normalized();
		return (from, to, along, outward, new Basis(Vector3.Up, -Mathf.Atan2(along.Y, along.X)));
	}

	/// <summary>The lengths of curtain a face is laid in: the whole face but its gate's span and its
	/// breaches, each stretch of stone cut into lengths of no more than <paramref name="length"/>, the last
	/// of them shorter, so the curtain runs on unbroken to every tower and every opening. A ladder is
	/// leant against the wall, not through it.</summary>
	private static IEnumerable<(float From, float To)> Lengths(FieldWall wall, FieldWall.Face face, float side, float length)
	{
		var open = new List<(float From, float To)>();
		foreach (FieldWall.Opening gap in wall.Openings)
		{
			if (gap.On == face && gap.Is != FieldWall.Kind.Ladder)
			{
				open.Add((gap.From, gap.To));
			}
		}

		if (face == FieldWall.Face.South)
		{
			open.Add((wall.Half - (GateWide / 2f), wall.Half + (GateWide / 2f)));
		}

		open.Sort();
		float at = 0f;
		foreach ((float from, float to) in open)
		{
			for (float d = at; d < from - 0.05f; d += length)
			{
				yield return (d, Mathf.Min(from, d + length));
			}

			at = Mathf.Max(at, to);
		}

		for (float d = at; d < side - 0.05f; d += length)
		{
			yield return (d, Mathf.Min(side, d + length));
		}
	}
}
