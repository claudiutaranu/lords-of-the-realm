using System.Collections.Generic;
using Godot;

/// <summary>The castle's wall drawn on the field of an assault (FieldWall): a curtain of stone with
/// its merlons, or of timber stakes for the timber rungs, laid in short lengths over the rolls of the
/// ground; a tower either side of the gate the rams broke; rubble at each breach; and a ladder leaning
/// in each place to climb. Placeholder art, built in code, until the castle kit is sent.</summary>
public static class BattlefieldWalls
{
	/// <summary>How long one length of curtain is, how high and how thick, in metres; how high its
	/// merlons stand and how far apart. Low enough that a man behind it is seen to his shoulders.</summary>
	private const float Length = 3f;
	private const float StoneHigh = 2.0f;
	private const float TimberHigh = 1.8f;
	private const float Thick = 1.3f;
	private const float MerlonHigh = 0.5f;
	private const float MerlonEvery = 1.5f;

	/// <summary>The gate's towers, and a ladder's length and lean.</summary>
	private static readonly Vector3 Tower = new(3f, 4.5f, 3f);
	private const float LadderLong = 3.2f;
	private const float LadderLean = 0.45f;

	private static readonly Color StoneGrey = new("8c8478");
	private static readonly Color TimberBrown = new("6b4a2b");
	private static readonly Color LadderWood = new("8a6a3e");

	public static Node3D Build(FieldWall wall, BattlefieldLand land)
	{
		var built = new Node3D();
		Color colour = wall.IsTimber ? TimberBrown : StoneGrey;
		float high = wall.IsTimber ? TimberHigh : StoneHigh;
		var lengths = new List<Transform3D>();
		var merlons = new List<Transform3D>();
		foreach (FieldWall.Face face in FieldWall.Faces)
		{
			(Vector2 from, Vector2 to) = wall.Ends(face);
			Vector2 along = (to - from).Normalized();
			float yaw = Mathf.Atan2(along.Y, along.X);
			var turned = new Basis(Vector3.Up, -yaw);
			float side = from.DistanceTo(to);
			for (float d = 0f; d < side; d += Length)
			{
				float upto = Mathf.Min(side, d + Length);
				if (IsOpen(wall, face, d, upto))
				{
					continue;
				}

				Vector2 middle = from + (along * ((d + upto) / 2f));
				lengths.Add(new Transform3D(turned.Scaled(new Vector3((upto - d + 0.05f) / Length, 1f, 1f)),
					land.On(middle) + (Vector3.Up * ((high / 2f) - 0.3f))));
				if (!wall.IsTimber)
				{
					for (float m = d + 0.4f; m < upto - 0.3f; m += MerlonEvery)
					{
						merlons.Add(new Transform3D(turned, land.On(from + (along * m)) + (Vector3.Up * (high - 0.3f + (MerlonHigh / 2f)))));
					}
				}
			}

			// The corners: a tower on each, which also fills where two faces meet.
			built.AddChild(Block(new BoxMesh { Size = Tower }, land.On(from) + (Vector3.Up * ((Tower.Y / 2f) - 0.3f)), colour.Darkened(0.1f)));
		}

		built.AddChild(Many(new BoxMesh { Size = new Vector3(Length, high + 0.6f, Thick) }, lengths, colour));
		if (merlons.Count > 0)
		{
			built.AddChild(Many(new BoxMesh { Size = new Vector3(0.8f, MerlonHigh, Thick * 0.5f) }, merlons, colour));
		}

		foreach (FieldWall.Opening gap in wall.Openings)
		{
			(Vector2 from, Vector2 to) = wall.Ends(gap.On);
			Vector2 along = (to - from).Normalized();
			var turned = new Basis(Vector3.Up, -Mathf.Atan2(along.Y, along.X));
			Vector2 at = from + (along * gap.Middle);
			Vector2 outward = (at - wall.Middle).Normalized();
			outward = Mathf.Abs(outward.X) > Mathf.Abs(outward.Y) ? new Vector2(Mathf.Sign(outward.X), 0f) : new Vector2(0f, Mathf.Sign(outward.Y));
			switch (gap.Is)
			{
				case FieldWall.Kind.Gate:
					foreach (float d in new[] { gap.From - (Tower.X / 2f), gap.To + (Tower.X / 2f) })
					{
						built.AddChild(Block(new BoxMesh { Size = Tower }, land.On(from + (along * d)) + (Vector3.Up * ((Tower.Y / 2f) - 0.3f)), colour.Darkened(0.1f)));
					}

					break;
				case FieldWall.Kind.Breach:
					foreach (float d in new[] { gap.From + 0.6f, gap.To - 0.6f, gap.Middle - 1.5f, gap.Middle + 1.8f })
					{
						built.AddChild(Block(new BoxMesh { Size = new Vector3(1.2f, 0.6f, 1.6f) },
							land.On(from + (along * d) + (outward * 0.8f)) + (Vector3.Up * 0.2f), colour.Darkened(0.2f)));
					}

					break;
				case FieldWall.Kind.Ladder:
					// The wall stands whole behind a ladder: it is climbed, not walked through.
					built.AddChild(new MeshInstance3D
					{
						Mesh = new BoxMesh { Size = new Vector3(gap.To - gap.From + 0.05f, high + 0.6f, Thick) },
						Transform = new Transform3D(turned, land.On(at) + (Vector3.Up * ((high / 2f) - 0.3f))),
						MaterialOverride = Look(colour),
					});
					built.AddChild(Ladder(land.On(at + (outward * ((Thick / 2f) + 0.7f))), outward));
					break;
			}
		}

		return built;
	}

	private static bool IsOpen(FieldWall wall, FieldWall.Face face, float from, float to)
	{
		foreach (FieldWall.Opening gap in wall.Openings)
		{
			if (gap.On == face && from < gap.To && to > gap.From)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Two rails and their rungs, leaning against the wall from the attacker's side.</summary>
	private static Node3D Ladder(Vector3 foot, Vector2 outward)
	{
		// Leaning back toward the wall, whichever face it stands against.
		float yaw = Mathf.Atan2(outward.X, outward.Y);
		var ladder = new Node3D { Position = foot, Rotation = new Vector3(-LadderLean, yaw, 0f) };
		foreach (float side in new[] { -0.3f, 0.3f })
		{
			ladder.AddChild(Block(new BoxMesh { Size = new Vector3(0.07f, LadderLong, 0.07f) }, new Vector3(side, LadderLong / 2f, 0f), LadderWood));
		}

		for (float up = 0.3f; up < LadderLong; up += 0.35f)
		{
			ladder.AddChild(Block(new BoxMesh { Size = new Vector3(0.6f, 0.05f, 0.05f) }, new Vector3(0f, up, 0f), LadderWood));
		}

		return ladder;
	}

	private static MeshInstance3D Block(Mesh mesh, Vector3 at, Color colour) => new()
	{
		Mesh = mesh,
		Position = at,
		MaterialOverride = Look(colour),
	};

	private static MultiMeshInstance3D Many(Mesh mesh, List<Transform3D> where, Color colour)
	{
		var many = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = where.Count };
		for (int i = 0; i < where.Count; i++)
		{
			many.SetInstanceTransform(i, where[i]);
		}

		return new MultiMeshInstance3D { Multimesh = many, MaterialOverride = Look(colour) };
	}

	private static StandardMaterial3D Look(Color colour) => new() { AlbedoColor = colour, Roughness = 0.95f };
}
