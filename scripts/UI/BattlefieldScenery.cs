using Godot;

/// <summary>The field's scenery: its sky and sun, its ground, and the wood round it.</summary>
public partial class Battlefield
{
	private static WorldEnvironment Sky() => new()
	{
		Environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky
			{
				SkyMaterial = new ProceduralSkyMaterial
				{
					SkyTopColor = new Color("2b4a74"),
					SkyHorizonColor = new Color("b9c4c9"),
					GroundHorizonColor = new Color("6b7a5a"),
					GroundBottomColor = new Color("3d4a30"),
				},
			},
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			// Enough of the sky's light in the shade that a man with the sun behind him is still a man and
			// not his own silhouette.
			AmbientLightEnergy = 0.9f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			FogEnabled = true,
			// A haze in the woods, the way morning lies among the trees: none over the field, where
			// the armies are, and thickening from its far edge into the treeline behind it.
			FogMode = Godot.Environment.FogModeEnum.Depth,
			FogLightColor = new Color("aab5b0"),
			FogDensity = 0.75f,
			FogDepthBegin = 150f,
			FogDepthEnd = 420f,
			FogDepthCurve = 1.6f,
			FogSkyAffect = 0.5f,
			// No screen-space occlusion: at a man's size on the field it is not shading but grain, a
			// dark speckle over every figure that darkened the whole army a shade.
			SsaoEnabled = false,
		},
	};

	/// <summary>A low afternoon sun off the attacker's shoulder, so the shadows run toward the enemy
	/// and a squad reads by the dark it throws.</summary>
	private static DirectionalLight3D Sun()
	{
		var sun = new DirectionalLight3D
		{
			LightEnergy = 1.5f,
			LightColor = new Color("fff0cf"),
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 300f,
			// Two cascades, not four: every man on the field is drawn again for each, and four
			// hundred and eighty of them five times over held the field under thirty a second.
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
			// Clear of his own surface: at the default a man shadowed himself in a grid of dots
			// across his shirt and breeches and stood a shade darker than he should. Much past this
			// the shadows come loose from the feet that throw them.
			ShadowBias = 0.5f,
		};
		sun.RotationDegrees = new Vector3(-38f, -30f, 0f);
		return sun;
	}

	/// <summary>The ground, and the map of where its earth is bare (0..1 across the field, bare above
	/// <see cref="BareFrom"/>), which the grass is sown by.</summary>
	private static (MeshInstance3D Ground, Image Bare) Ground(BattlefieldLand land)
	{
		var earth = new FastNoiseLite { Frequency = BareFrequency, Seed = (int)WoodsSeed };
		Image bare = Image.CreateEmpty(BareMap, BareMap, false, Image.Format.R8);
		for (int x = 0; x < BareMap; x++)
		{
			for (int y = 0; y < BareMap; y++)
			{
				float metres = Field / BareMap;
				bare.SetPixel(x, y, new Color((earth.GetNoise2D(x * metres, y * metres) * 0.5f) + 0.5f, 0f, 0f));
			}
		}

		var look = new ShaderMaterial { Shader = GD.Load<Shader>(GroundShader) };
		look.SetShaderParameter("grass", GD.Load<Texture2D>(GroundAlbedo));
		look.SetShaderParameter("earth", GD.Load<Texture2D>(EarthAlbedo));
		look.SetShaderParameter("patches", new NoiseTexture2D
		{
			Width = 512,
			Height = 512,
			Seamless = true,
			Noise = new FastNoiseLite { Frequency = 0.012f, Seed = (int)WoodsSeed },
		});
		look.SetShaderParameter("tiles", Field / GroundTile);
		look.SetShaderParameter("bare_map", ImageTexture.CreateFromImage(bare));
		look.SetShaderParameter("bare_from", BareFrom);
		return (new MeshInstance3D { Mesh = land.Mesh(), MaterialOverride = look }, bare);
	}

	/// <summary>A wood round the field, so it is a place and not a board: stands of pine and oak,
	/// thicker the further out, the nearest a spear's throw past where the lines draw up.</summary>
	private static Node3D Wood(BattlefieldLand land)
	{
		var wood = new Node3D();
		var dice = new RandomNumberGenerator { Seed = WoodsSeed };
		var stands = new Vector2[Stands];
		for (int i = 0; i < Stands; i++)
		{
			float angle = dice.RandfRange(0f, Mathf.Tau);
			float far = Mathf.Lerp(WoodsFrom, WoodsTo, Mathf.Sqrt(dice.Randf()));
			stands[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * far;
		}

		foreach ((string model, bool isEvergreen) in Woods)
		{
			Mesh tree = Models.MeshOf(model);
			if (tree == null)
			{
				continue;
			}

			float scale = TreeHeight / Mathf.Max(0.01f, tree.GetAabb().Size.Y);
			var many = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = tree,
				InstanceCount = Trees / Woods.Length,
			};
			for (int i = 0; i < many.InstanceCount; i++)
			{
				Vector2 at = stands[dice.RandiRange(0, Stands - 1)]
					+ (new Vector2(dice.RandfRange(-1f, 1f), dice.RandfRange(-1f, 1f)) * StandWide);
				if (at.Length() < WoodsFrom)
				{
					at = at.Normalized() * WoodsFrom;
				}

				// Trees are never all the same height, nor as tall as they are wide.
				float grown = scale * dice.RandfRange(0.75f, 1.35f);
				var basis = new Basis(Vector3.Up, dice.RandfRange(0f, Mathf.Tau))
					.Scaled(new Vector3(grown, grown * dice.RandfRange(0.9f, 1.25f), grown));
				// Sunk a little, so a tree on a slope has no root standing in the air on the downhill side.
				many.SetInstanceTransform(i, new Transform3D(basis, land.On(at) + (Vector3.Down * 0.6f)));
			}

			wood.AddChild(new MultiMeshInstance3D { Multimesh = many, MaterialOverride = Foliage(model, tree, isEvergreen) });
		}

		return wood;
	}

	/// <summary>A tree's look, as the campaign map draws it (MapDecoration.Foliage): its baked colour
	/// and relief through tree-foliage-close.gdshader, in the leaf of high summer.</summary>
	private static ShaderMaterial Foliage(string model, Mesh tree, bool isEvergreen)
	{
		var leaves = new ShaderMaterial { Shader = GD.Load<Shader>(FoliageShader) };
		if (tree.SurfaceGetMaterial(0) is BaseMaterial3D baked)
		{
			leaves.SetShaderParameter("albedo_texture", baked.AlbedoTexture);
		}

		leaves.SetShaderParameter("relief_texture", GD.Load<Texture2D>($"res://assets/models/{model}-normal.png"));
		leaves.SetShaderParameter("evergreen", isEvergreen ? 1f : 0f);
		// Seen from a lord's height and not the map's: less of the baked relief, which up close was
		// a camouflage of black blots; sun through the leaves; and the crowns stirring.
		leaves.SetShaderParameter("relief_strength", 0.35f);
		leaves.SetShaderParameter("crown_height", tree.GetAabb().End.Y);
		return leaves;
	}
}
