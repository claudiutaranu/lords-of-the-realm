using System.Collections.Generic;
using Godot;

/// <summary>The field's scenery: its sky and sun, its ground, and the wood round it.</summary>
public partial class Battlefield
{
	/// <summary>The hour a battle is fought at, drawn by chance as the field opens (the user's call): an
	/// evening, dark and low-sunned, or a bright day. Only the light changes, never the fight.</summary>
	private readonly record struct Light(Color SkyTop, Color SkyHorizon, Color Fog, float Ambient, float Exposure,
		float Contrast, float SunEnergy, Color SunColour, float SunPitch);

	private static readonly Light Evening = new(new Color("2b4a74"), new Color("3a4240"), new Color("0c0f0b"),
		0.55f, 1.0f, 1.12f, 1.6f, new Color("ffd9b0"), -24f);

	private static readonly Light Day = new(new Color("4a80c0"), new Color("c8d4d8"), new Color("38423a"),
		1.05f, 1.6f, 1.06f, 2.5f, new Color("fff6e8"), -55f);

	private static Light _light = Day;

	private static WorldEnvironment Sky()
	{
		_light = OS.GetEnvironment("BATTLE_LIGHT") switch
		{
			"day" => Day,
			"evening" => Evening,
			_ => GD.Randf() < 0.5f ? Evening : Day,
		};
		return new WorldEnvironment { Environment = Air(_light) };
	}

	private static Godot.Environment Air(Light light) => new()
	{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky
			{
				SkyMaterial = new ProceduralSkyMaterial
				{
					SkyTopColor = light.SkyTop,
					SkyHorizonColor = light.SkyHorizon,
					GroundHorizonColor = new Color("0a0c09"),
					GroundBottomColor = new Color("060706"),
				},
			},
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			// Enough of the sky's light in the shade that a man with the sun behind him is still a man and
			// not his own silhouette.
			AmbientLightEnergy = light.Ambient,
			// Graded down toward a real summer's day, as Manor Lords is: softer colour, a little
			// more contrast, nothing glowing.
			TonemapMode = Godot.Environment.ToneMapper.Agx,
			TonemapExposure = light.Exposure,
			AdjustmentEnabled = true,
			AdjustmentSaturation = 0.95f,
			AdjustmentContrast = light.Contrast,
			FogEnabled = true,
			// A haze in the woods, the way morning lies among the trees: none over the field, where
			// the armies are, and thickening from its far edge into the treeline behind it.
			FogMode = Godot.Environment.FogModeEnum.Depth,
			FogLightColor = light.Fog,
			FogDensity = 1f,
			FogDepthBegin = 110f,
			FogDepthEnd = 420f,
			FogDepthCurve = 1.6f,
			FogSkyAffect = 0.55f,
			// Past the field the world goes into the dark, as if nothing were there to see (the user's call).
			FogAerialPerspective = 0f,
			// No screen-space occlusion: at a man's size on the field it is not shading but grain, a
			// dark speckle over every figure that darkened the whole army a shade.
			SsaoEnabled = false,
	};

	/// <summary>A low afternoon sun off the attacker's shoulder, so the shadows run toward the enemy
	/// and a squad reads by the dark it throws.</summary>
	private static DirectionalLight3D Sun()
	{
		var sun = new DirectionalLight3D
		{
			LightEnergy = _light.SunEnergy,
			LightColor = _light.SunColour,
			// Soft-edged shadows, as a sun through a little haze throws them.
			LightAngularDistance = 0f,
			ShadowEnabled = true,
			DirectionalShadowMaxDistance = 300f,
			// One map, not cascades: every man on the field is drawn again for each, and the shadow's
			// reach follows the eye (BattlefieldOrders.Look), so one is enough.
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal,
			// Clear of his own surface by the normal, not by depth: pushed off by depth far enough to
			// stop a man shadowing himself in dots, the shadow came loose from his feet and lay a pace
			// off to the side.
			ShadowBias = 0.05f,
			ShadowNormalBias = 1.4f,
			// Soft and not black: the sky lights the shade, as on a summer's day in Manor Lords.
			ShadowBlur = 1f,
			ShadowOpacity = 0.85f,
		};
		sun.RotationDegrees = new Vector3(_light.SunPitch, -30f, 0f);
		return sun;
	}

	/// <summary>The ground, and the map of where its earth is bare (0..1 across the field, bare above
	/// <see cref="BareFrom"/>), which the grass is sown by.</summary>
	/// <summary>The ground's photographs (ambientCG, CC0), and the cart track across the field: where
	/// it runs, how far it wanders, how often it turns, and how wide it is worn.</summary>
	private const string GroundTextures = "res://assets/terrain/battle";
	private const float TrackAt = 105f;
	private const float TrackSwing = 22f;
	private const float TrackWanders = 0.006f;
	private const float TrackWide = 2.3f;
	private const float RutWide = 0.75f;

	private static (MeshInstance3D Ground, Image Bare) Ground(BattlefieldLand land)
	{
		var earth = new FastNoiseLite { Frequency = BareFrequency, Seed = (int)WoodsSeed };
		var wander = new FastNoiseLite { Frequency = TrackWanders, Seed = (int)WoodsSeed + 7 };
		var texels = new byte[BareMap * BareMap];
		float metres = Field / BareMap;
		for (int x = 0; x < BareMap; x++)
		{
			for (int y = 0; y < BareMap; y++)
			{
				float worn = (earth.GetNoise2D(x * metres, y * metres) * 0.5f) + 0.5f;
				// A cart track across the field, the way a real one wanders, worn to earth down its
				// middle and grassing over at its sides.
				var at = new Vector2((x * metres) - (Field / 2f), (y * metres) - (Field / 2f));
				float line = TrackAt + (wander.GetNoise1D(at.X) * TrackSwing);
				// Two ruts a cart's width apart, grass down the middle between them.
				float off = Mathf.Abs(at.Y - line);
				float rut = 1f - Mathf.SmoothStep(RutWide * 0.4f, RutWide, Mathf.Abs(off - (TrackWide / 2f)));
				float middle = (1f - Mathf.SmoothStep(0f, TrackWide / 2f, off)) * 0.62f;
				float track = Mathf.Max(rut, middle);
				texels[(y * BareMap) + x] = (byte)Mathf.RoundToInt(Mathf.Clamp(Mathf.Max(worn, track), 0f, 1f) * 255f);
			}
		}
		// Written in one piece: a pixel at a time, a million calls into the engine held up the field's opening.
		Image bare = Image.CreateFromData(BareMap, BareMap, false, Image.Format.R8, texels);


		var look = new ShaderMaterial { Shader = GD.Load<Shader>(GroundShader) };
		foreach (string layer in new[] { "meadow", "worn", "dirt" })
		{
			look.SetShaderParameter(layer, GD.Load<Texture2D>($"{GroundTextures}/{layer}.jpg"));
			look.SetShaderParameter($"{layer}_normal", GD.Load<Texture2D>($"{GroundTextures}/{layer}-normal.jpg"));
		}

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
	/// <summary>How far out the trees still throw a shadow.</summary>
	private const float ShadedWithin = WoodsFrom + 35f;
	private const float WoodShade = 0.62f;

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
			var near = new List<Transform3D>();
			var far = new List<Transform3D>();
			for (int i = 0; i < Trees / Woods.Length; i++)
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
				(at.Length() < ShadedWithin ? near : far).Add(new Transform3D(basis, land.On(at) + (Vector3.Down * 0.6f)));
			}

			// Only the edge of the wood throws its shadow onto the field: the trees behind it throw
			// theirs onto other trees, in the haze, and drawn again for the sun they halved the frame rate.
			ShaderMaterial leaves = Foliage(model, tree, isEvergreen);
			foreach ((List<Transform3D> placed, bool casts) in new[] { (near, true), (far, false) })
			{
				var many = new MultiMesh
				{
					TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
					Mesh = tree,
					InstanceCount = placed.Count,
				};
				for (int i = 0; i < placed.Count; i++)
				{
					many.SetInstanceTransform(i, placed[i]);
				}

				wood.AddChild(new MultiMeshInstance3D
				{
					Multimesh = many,
					MaterialOverride = leaves,
					CastShadow = casts ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
				});
			}
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
		leaves.SetShaderParameter("shade", WoodShade);
		leaves.SetShaderParameter("edge_from", 150f);
		leaves.SetShaderParameter("edge_to", 255f);
		leaves.SetShaderParameter("leaf_glow", 0.3f);
		leaves.SetShaderParameter("crown_height", tree.GetAabb().End.Y);
		return leaves;
	}
}
