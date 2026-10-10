using System.Collections.Generic;
using Godot;

/// <summary>The pieces the castle on the field is put together from (BattlefieldWalls): its towers,
/// banners, torches, the ram and the ladders, and the stone and timber they are cut from.</summary>
public static partial class BattlefieldWalls
{
	/// <summary>A banner's width hung on a tower, and how far down from the tower's top it hangs.</summary>
	private const float BannerWide = 3.1f;
	private const float BannerDrop = 0.6f;
	private const float BannerAspect = 770f / 1497f;

	/// <summary>A torch's flame and its light.</summary>
	private const float TorchReach = 9f;
	private const float TorchEnergy = 2.2f;

	private static readonly Color Flame = new("ffb04a");

	private const string Kit = "res://assets/models/castle";

	/// <summary>The kit's tower storey, its middle storey's narrower side, and how large its engines
	/// are drawn beside a man.</summary>
	private const float KitStorey = 1.01f;
	private const float KitMidSide = 0.94f;
	private const float KitTopFloor = 0.17f;
	private const float KitStairsHigh = 0.67f;
	private const float StairsOff = 1.2f;
	private const float EngineScale = 2.8f;

	private const string BannerDirectory = "res://assets/ui/diplomacy";

	/// <summary>A square tower from the kit, its base, its middle and its crenellated top stacked to the
	/// tower's height; a timber one is a plain block.</summary>
	private static Node3D Tower(BattlefieldLand land, Vector2 at, float side, float high, Material stone, bool isTimber)
	{
		var tower = new Node3D { Position = land.On(at) + (Vector3.Up * -0.3f) };
		if (isTimber)
		{
			tower.AddChild(Block(new BoxMesh { Size = new Vector3(side, high + 0.6f, side) }, Vector3.Up * ((high / 2f) + 0.3f), stone));
			return tower;
		}

		float storey = (high + 0.3f) / (2f * KitStorey);
		var scale = Basis.FromScale(new Vector3(side, storey, side));
		tower.AddChild(Piece("tower-square-base", scale, Vector3.Zero, stone));
		tower.AddChild(Piece("tower-square-mid", Basis.FromScale(new Vector3(side / KitMidSide, storey, side / KitMidSide)),
			Vector3.Up * (KitStorey * storey), stone));
		tower.AddChild(Piece("tower-square-top", scale, Vector3.Up * (2f * KitStorey * storey), stone));
		return tower;
	}

	/// <summary>The holder's banner on a tower's face: the cloth dyed his colour, the trim and lion gold.</summary>
	private static Node3D Banner(BattlefieldLand land, Vector2 at, float top, Vector2 outward, Color holder, bool isMirrored = false)
	{
		float tall = BannerWide / BannerAspect;
		var banner = new Node3D
		{
			Position = land.On(at) + (Vector3.Up * (top - BannerDrop - (tall / 2f))),
			Rotation = new Vector3(0f, Mathf.Atan2(outward.X, outward.Y), 0f),
		};
		float lift = 0f;
		foreach ((string layer, Color tint) in new[] { ("cloth", holder), ("trim", Colors.White) })
		{
			banner.AddChild(new MeshInstance3D
			{
				Mesh = new QuadMesh { Size = new Vector2(BannerWide, tall) },
				Position = new Vector3(0f, 0f, lift),
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoTexture = GD.Load<Texture2D>($"{BannerDirectory}/banner-{layer}.png"),
					AlbedoColor = tint,
					Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
					// The one on the right of a pair hangs from its pole the other way, as a mirror.
					Uv1Scale = new Vector3(isMirrored ? -1f : 1f, 1f, 1f),
					Uv1Offset = new Vector3(isMirrored ? 1f : 0f, 0f, 0f),
					Roughness = 0.9f,
				},
			});
			lift += 0.02f;
		}

		return banner;
	}

	/// <summary>The top of a kit tower built this tall (Tower): its floor inside the crenellation.</summary>
	private static float TowerFloor(float high) => -0.3f + ((2f * KitStorey) + KitTopFloor) * ((high + 0.3f) / (2f * KitStorey));

	/// <summary>A flight of stone steps up the inside of the wall to its walkway, along the face.</summary>
	private static Node3D Stairs(BattlefieldLand land, Vector2 at, Basis turned, float high)
	{
		float grow = high / KitStairsHigh;
		return Piece("stairs-stone", turned * new Basis(Vector3.Up, Mathf.Pi / 2f) * Basis.FromScale(Vector3.One * grow),
			land.On(at) + (Vector3.Up * -0.3f), Stone(new Color("b7ad9c")));
	}

	/// <summary>A siege ladder against the wall, for BattlefieldCastle to stand up while it is climbed.</summary>
	public static Node3D LadderAt(FieldWall wall, BattlefieldLand land, FieldWall.Opening gap)
	{
		(Vector2 from, _, Vector2 along, Vector2 outward, _) = Lie(wall, gap.On);
		Vector2 at = from + (along * gap.Middle);
		return Ladder(land.On(at + (outward * (OuterLip + 1.2f))), outward, wall.IsTimber ? TimberHigh : StoneHigh);
	}

	/// <summary>A torch on its bracket, burning, and the light it throws on the stone.</summary>
	private static Node3D Torch(BattlefieldLand land, Vector2 at, float high)
	{
		var torch = new Node3D { Position = land.On(at) + (Vector3.Up * high * 0.75f) };
		torch.AddChild(Block(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.04f, Height = 0.7f }, Vector3.Zero, Plain(GateWood)));
		torch.AddChild(new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = 0.16f, Height = 0.42f },
			Position = Vector3.Up * 0.45f,
			MaterialOverride = new StandardMaterial3D
			{
				AlbedoColor = Flame,
				EmissionEnabled = true,
				Emission = Flame,
				EmissionEnergyMultiplier = 4f,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			},
		});
		torch.AddChild(new OmniLight3D { Position = Vector3.Up * 0.6f, LightColor = Flame, OmniRange = TorchReach, LightEnergy = TorchEnergy });
		return torch;
	}

	/// <summary>A siege engine from the kit, whole or broken, its foot at the node's origin; the kit's
	/// engines face along their own x, and the caller turns the node to face where it is going.</summary>
	public static Node3D Engine(string kind, bool isBroken)
	{
		string piece = (kind == SiegeEngines.Ram ? "siege-ram" : "siege-catapult") + (isBroken ? "-demolished" : "");
		var engine = new Node3D();
		engine.AddChild(Piece(piece, Basis.FromScale(Vector3.One * EngineScale), Vector3.Zero, Engines()));
		return engine;
	}

	/// <summary>The turn about the vertical that points a kit engine along a direction on the field.</summary>
	public static float EngineYaw(Vector2 facing) => Mathf.Atan2(-facing.Y, facing.X);

	/// <summary>Two rails and their rungs, leaning against the wall from the attacker's side, as long
	/// as the wall is high.</summary>
	private static Node3D Ladder(Vector3 foot, Vector2 outward, float high)
	{
		float longest = (high + 0.6f) / Mathf.Cos(LadderLean);
		float yaw = Mathf.Atan2(outward.X, outward.Y);
		var ladder = new Node3D { Position = foot, Rotation = new Vector3(-LadderLean, yaw, 0f) };
		Material wood = Plain(LadderWood);
		foreach (float side in new[] { -0.32f, 0.32f })
		{
			ladder.AddChild(Block(new BoxMesh { Size = new Vector3(0.08f, longest, 0.08f) }, new Vector3(side, longest / 2f, 0f), wood));
		}

		for (float up = 0.3f; up < longest; up += 0.38f)
		{
			ladder.AddChild(Block(new BoxMesh { Size = new Vector3(0.64f, 0.05f, 0.05f) }, new Vector3(0f, up, 0f), wood));
		}

		return ladder;
	}

	/// <summary>A piece of the castle kit (Kenney's Castle Kit, CC0: assets/models/castle), laid where
	/// it goes, its own paint replaced by <paramref name="look"/>.</summary>
	private static Node3D Piece(string name, Basis shape, Vector3 at, Material look)
	{
		Node3D piece = GD.Load<PackedScene>($"{Kit}/{name}.glb").Instantiate<Node3D>();
		piece.Transform = new Transform3D(shape, at);
		Dress(piece, look);
		return piece;
	}

	private static void Dress(Node node, Material look)
	{
		if (node is MeshInstance3D mesh && look != null)
		{
			mesh.MaterialOverride = look;
		}

		foreach (Node child in node.GetChildren())
		{
			Dress(child, look);
		}
	}

	/// <summary>The engines keep the kit's own paint, which assets/models/castle/Textures/colormap.png
	/// has turned from its toy peach and blue to timber and iron.</summary>
	private static Material Engines() => null;

	private static MeshInstance3D Block(Mesh mesh, Vector3 at, Material look, Basis? turned = null) => new()
	{
		Mesh = mesh,
		Transform = new Transform3D(turned ?? Basis.Identity, at),
		MaterialOverride = look,
	};

	private static MultiMeshInstance3D Many(Mesh mesh, List<Transform3D> where, Material look)
	{
		var many = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = where.Count };
		for (int i = 0; i < where.Count; i++)
		{
			many.SetInstanceTransform(i, where[i]);
		}

		return new MultiMeshInstance3D { Multimesh = many, MaterialOverride = look };
	}

	/// <summary>Dressed stone: the ground's own rock, laid on in world space so a long curtain and a
	/// small merlon show the same size of block.</summary>
	private static StandardMaterial3D Stone(Color tint) => new()
	{
		AlbedoTexture = GD.Load<Texture2D>(StoneTexture),
		AlbedoColor = tint,
		Uv1Triplanar = true,
		Uv1WorldTriplanar = true,
		Uv1Scale = new Vector3(0.22f, 0.22f, 0.22f),
		Roughness = 0.95f,
	};

	private static StandardMaterial3D Plain(Color colour) => new() { AlbedoColor = colour, Roughness = 0.95f };
}
