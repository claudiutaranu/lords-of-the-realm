using Godot;

/// <summary>The campaign's ground, drawn by the Terrain3D plugin instead of by one displaced plane.
///
/// What it buys, and the whole reason for it: a clipmap mesh that carries its detail where the
/// camera is rather than spreading one density over the whole map, and a material that blends its
/// surfaces by their own height channel. A linear cross-fade between grass and rock is a smear;
/// blending on height lets the stone come through where it stands proud and the grass keep the
/// hollows, which is most of the difference between ground that reads as rock and a picture of it.
///
/// Terrain3D is a GDExtension, so there are no generated C# classes for it — everything is reached
/// by name through Call and Set. That is what this wrapper is for: the untyped calls live here and
/// the rest of the map talks to it in ordinary C#.
///
/// It draws, and it does nothing else. Every question the game asks of the ground — how high is it
/// here, whose county is this, can a field be ploughed on it — is still answered from the full-size
/// height image in <see cref="CampaignMap3D"/>. There is one source of truth for the shape of the
/// land and this is not it.</summary>
public sealed class Terrain3DGround
{
	private const string ShaderPath = "res://assets/shaders/campaign-terrain.gdshader";
	private const string TextureDirectory = "res://assets/terrain/pbr";

	// Terrain3DMaterial.WorldBackground: what is drawn past the last region. NONE, because the sea
	// is its own plane and a skirt of repeated ground under it is a skirt of repeated ground.
	private const int WorldBackgroundNone = 0;

	/// <summary>World units per terrain vertex.
	///
	/// This is not a choice. Terrain3D clamps vertex spacing to a floor of 0.25, and asking for
	/// less is silently ignored — which is what drew the map at twice its size on the first
	/// attempt, with everything else still standing where it belonged. The map's world size is
	/// derived from this rather than the other way round; see <see cref="WorldWidth"/>.</summary>
	public const float VertexSpacing = 0.25f;

	/// <summary>How many height-map pixels go into one terrain vertex. Two, because one vertex per
	/// pixel at the spacing above would make a map twice as wide as this game's world and drag
	/// every distance, prop size and camera range along with it. The height image itself keeps its
	/// full size for everything that is not drawing.</summary>
	public const int PixelsPerVertex = 2;

	// Regions are square, and this is the size whose grid the map's own centre lands on. That is
	// the constraint, not tidiness: an import is snapped to the region grid, and a map centred at
	// (-96, -64) world units sits on a grid of 32 (regions of 128 vertices at this spacing) and
	// half off one of 64. At 256 the whole island was quietly shifted half a region and drawn over
	// the sea. 768x512 vertices is six by four regions of 128, with none left half empty.
	private const int RegionSize = 128;

	private const int MeshSize = 128;

	private readonly Node3D _terrain;
	private readonly GodotObject _material;

	private Terrain3DGround(Node3D terrain, GodotObject material)
	{
		_terrain = terrain;
		_material = material;
	}

	/// <summary>Whether the plugin is installed and its library loaded.</summary>
	public static bool Available => ClassDB.ClassExists("Terrain3D");

	/// <summary>How wide a world a height map of this many pixels makes, at the spacing above.</summary>
	public static float WorldWidth(int heightPixels) => heightPixels / PixelsPerVertex * VertexSpacing;

	/// <summary>Lays the ground out of the campaign's own images, under <paramref name="parent"/>.
	///
	/// The node goes into the tree before anything is set on it, and that is not a matter of taste:
	/// Terrain3D builds its material and its data store on entering the tree, and both read as null
	/// to anyone who asks first.</summary>
	/// <param name="height">The campaign height map at full size, 0..1 over the byte range.</param>
	/// <param name="tint">Regional colour as a multiplier toward white — see <see cref="AsTint"/>.</param>
	/// <param name="surface">Which ground texture covers what — see <see cref="AsControl"/>.</param>
	/// <param name="ids">The province ID map, for the borders the shader draws.</param>
	public static Terrain3DGround Build(Node parent, Image height, Image tint, Image surface, Image ids,
		float worldWidth, float worldDepth, float relief)
	{
		var terrain = ClassDB.Instantiate("Terrain3D").As<Node3D>();
		terrain.Name = "Ground";
		parent.AddChild(terrain);

		terrain.Set("region_size", RegionSize);
		terrain.Set("vertex_spacing", VertexSpacing);
		terrain.Set("cast_shadows", (int)GeometryInstance3D.ShadowCastingSetting.On);
		// How far out from the camera the clipmap keeps full detail before it starts halving it.
		// The plugin's 48 vertices is built for a player walking about on a big terrain; this camera
		// sits a hundred and thirty units off a map two hundred across, and at 48 almost the whole
		// island was drawn two or three LODs down — a sea cliff with a vertex every two units is a
		// row of slanted triangles, which cut the coast into a saw blade.
		//
		// Measured, and it has to be a power of two: 160, 192 and 224 all came in at 20-25 ms a
		// frame against 12 at 128 and 16 at 256, so the plugin clearly takes a slow path off the
		// powers of two. 256 draws every coast clean but leaves no room for anything to stand on the
		// map; 128 is used, and the coast is shaped so that it holds up at 128 — see shore_rise in
		// tools/generate_campaign_map.py.
		terrain.Set("mesh_size", MeshSize);

		var material = terrain.Get("material").As<GodotObject>();
		material.Set("world_background", WorldBackgroundNone);
		// The surfaces are painted, not guessed. The auto shader picks between two textures by slope
		// alone, which is the wrong question on this map: a mountain's broad shoulders are stone
		// however level they lie, snow belongs to a height and a beach to a distance from the water.
		// The generator already works all of that out to paint the albedo, so it writes it down and
		// the plugin is told rather than left to infer.
		material.Set("auto_shader", false);
		material.Call("enable_shader_override", true);
		material.Set("shader_override", GD.Load<Shader>(ShaderPath));

		var assets = ClassDB.Instantiate("Terrain3DAssets").As<Resource>();
		// The surfaces this island is made of, in the order the control map names them.
		// Ground037 is a bright dry pasture out of the pack (sRGB mean 156,150,92) and this island
		// is meadow, so it is brought down and turned green here — the plugin's own knob, applied
		// before its blending, rather than a correction bolted on at the end of the shader.
		assets.Call("set_texture", 0,
			Surface("Grass", "ground037", 0, 0.14f, 0.16f, new Color(0.50f, 0.66f, 0.40f)));
		// Rock023 arrives as neutral stone (140,139,137) and only wants taking off the white.
		assets.Call("set_texture", 1,
			Surface("Cliff", "rock023", 1, 0.08f, 0.31f, new Color(0.82f, 0.82f, 0.80f)));
		// Snow and sand were packed out of the older loose pair by tools/pack_terrain_textures.py,
		// which is what gave them the height and roughness channels they never had.
		assets.Call("set_texture", 2,
			Surface("Snow", "snow", 2, 0.16f, 0.22f, new Color(0.92f, 0.94f, 0.98f)));
		assets.Call("set_texture", 3,
			Surface("Sand", "sand", 3, 0.13f, 0.09f, new Color(0.86f, 0.80f, 0.66f)));
		// The roads, painted into the control map by the generator: packed dirt worn down to its
		// stones (Poly Haven's rocky_trail_02). A finer scale than the fields round it, so the stones
		// read at the width of a track, and cooled: under the warm sun and the tonemapper the dirt as
		// shipped came out brick orange.
		assets.Call("set_texture", 4,
			Surface("Path", "path", 4, 0.19f, 0.2f, new Color(0.66f, 0.70f, 0.78f)));
		terrain.Set("assets", assets);

		GodotObject data = terrain.Get("data").As<GodotObject>();
		var maps = new Godot.Collections.Array<Image> { Floor(height), AsControl(surface), Halved(tint) };
		data.Call("import_images", maps,
			new Vector3(-worldWidth * 0.5f, 0.0f, -worldDepth * 0.5f), 0.0f, relief);

		// The mesh is cut into chunks and each is culled against the screen by its bounding box. The
		// box has to know how tall the ground under it is: left at what it was before the import, a
		// chunk of cliff whose box is off the edge of the screen got culled while its top was still in
		// view, and the coast was drawn with straight-edged bites taken out of it. So the height range
		// is worked out again from what was imported, and the boxes are given a margin on top.
		data.Call("calc_height_range", true);
		terrain.Set("cull_margin", relief);

		var ground = new Terrain3DGround(terrain, material);
		ground.SetShaderParameter("campaign_id_map", ImageTexture.CreateFromImage(ids));
		ground.SetShaderParameter("campaign_map_size", new Vector2(worldWidth, worldDepth));

		return ground;
	}

	/// <summary>The clipmap is laid out around the camera, so it has to be told which one. Terrain3D
	/// stops processing altogether if it cannot find one.</summary>
	public void Watch(Camera3D camera) => _terrain.Call("set_camera", camera);

	public void SetShaderParameter(string name, Variant value) => _material.Call("set_shader_param", name, value);

	/// <summary>The height map as the plugin wants it: one channel of float, at one vertex per
	/// <see cref="PixelsPerVertex"/> pixels.
	///
	/// The averaging is the point, not the shrinking. A PNG holds a byte per pixel, and at this
	/// map's relief one byte is a tenth of a world unit — which on any gentle slope is a terrace
	/// every few pixels, and a hillside drawn as a flight of steps. Shrinking the image first and
	/// converting after keeps those steps, because the average of two equal bytes is that byte.
	/// Averaging into floats instead puts real values between them: four samples per vertex give
	/// quarter-byte precision, and the terracing goes with it.
	///
	/// Done on the raw buffer for speed — this is a million and a half samples.</summary>
	private static Image Floor(Image height)
	{
		Image source = height.Duplicate() as Image;
		if (source.IsCompressed())
		{
			source.Decompress();
		}

		source.ClearMipmaps();
		source.Convert(Image.Format.L8);
		byte[] bytes = source.GetData();

		int wide = source.GetWidth();
		int deep = source.GetHeight();
		int across = wide / PixelsPerVertex;
		int down = deep / PixelsPerVertex;
		byte[] floors = new byte[across * down * sizeof(float)];

		for (int z = 0; z < down; z++)
		{
			for (int x = 0; x < across; x++)
			{
				int total = 0;
				for (int dz = 0; dz < PixelsPerVertex; dz++)
				{
					int row = (z * PixelsPerVertex + dz) * wide + x * PixelsPerVertex;
					for (int dx = 0; dx < PixelsPerVertex; dx++)
					{
						total += bytes[row + dx];
					}
				}

				float level = total / (255.0f * PixelsPerVertex * PixelsPerVertex);
				System.BitConverter.GetBytes(level).CopyTo(floors, (z * across + x) * sizeof(float));
			}
		}

		return Image.CreateFromData(across, down, false, Image.Format.Rf, floors);
	}

	/// <summary>Turns a painted biome colour into something a terrain material can be multiplied by.
	///
	/// The generated albedo is an absolute colour — sage, slate, pine — averaging a third of full
	/// brightness. Handed over as a colour map it multiplies the ground down to a third and every
	/// surface comes out the same dark sludge, which is exactly what the first attempt drew. What is
	/// wanted from it is only which way the region leans, so each pixel is divided by its own
	/// strongest channel: the hue survives, white means "no opinion", and nothing is darkened more
	/// than the lean itself asks for. The map cannot brighten in any case — a byte tops out at 1.0.
	///
	/// Alpha is the plugin's roughness offset about half grey, not opacity, so it is left at neutral.
	///
	/// Done on the raw buffer rather than through GetPixel: a million and a half pixels of per-pixel
	/// calls is a visible pause on a loading screen.</summary>
	public static Image AsTint(Image albedo)
	{
		Image source = albedo.Duplicate() as Image;
		// It arrives off a Texture2D, so it may be VRAM-compressed and it certainly has mipmaps —
		// and GetData hands back every level end to end, a third more bytes than the image
		// CreateFromData is then asked to build out of them.
		if (source.IsCompressed())
		{
			source.Decompress();
		}

		source.ClearMipmaps();
		source.Convert(Image.Format.Rgba8);
		byte[] pixels = source.GetData();

		for (int at = 0; at < pixels.Length; at += 4)
		{
			int strongest = Mathf.Max(pixels[at], Mathf.Max(pixels[at + 1], pixels[at + 2]));
			if (strongest < 1)
			{
				pixels[at] = pixels[at + 1] = pixels[at + 2] = 255;
			}
			else
			{
				for (int channel = 0; channel < 3; channel++)
				{
					pixels[at + channel] = (byte)(pixels[at + channel] * 255 / strongest);
				}
			}

			pixels[at + 3] = 128;
		}

		return Image.CreateFromData(source.GetWidth(), source.GetHeight(), false, Image.Format.Rgba8, pixels);
	}

	/// <summary>Turns the generator's readable surface map into the bit field Terrain3D stores.
	///
	/// map-surface.png says it plainly — R is the texture underneath, G the one laid over it, B how
	/// much of the overlay there is — so it can be opened and looked at. The plugin wants those
	/// three packed into one 32-bit word per pixel, and the word handed over as a float with the
	/// same bits, which is a thing no image file can hold. So the packing happens here, once, on the
	/// way in.
	///
	/// The layout is the plugin's, read off its own shader: base texture in bits 27-31, overlay in
	/// 22-26, blend in 14-21.</summary>
	public static Image AsControl(Image surface)
	{
		// The three channels cannot be shrunk the same way. Which texture is which must go through
		// untouched — halfway between grass and snow is a texture that does not exist — but how much
		// of it there is wants filtering, or every hillside arrives with a staircase on it.
		Image named = Halved(surface, Image.Interpolation.Nearest);
		named.Convert(Image.Format.Rgb8);
		byte[] painted = named.GetData();

		Image amount = Halved(surface, Image.Interpolation.Bilinear);
		amount.Convert(Image.Format.Rgb8);
		byte[] blended = amount.GetData();

		Image source = named;

		int width = source.GetWidth();
		int height = source.GetHeight();
		byte[] control = new byte[width * height * sizeof(float)];
		for (int pixel = 0; pixel < width * height; pixel++)
		{
			uint packed = ((uint)painted[pixel * 3] & 0x1Fu) << 27
				| ((uint)painted[pixel * 3 + 1] & 0x1Fu) << 22
				| ((uint)blended[pixel * 3 + 2] & 0xFFu) << 14;
			System.BitConverter.GetBytes(packed).CopyTo(control, pixel * sizeof(float));
		}

		return Image.CreateFromData(width, height, false, Image.Format.Rf, control);
	}

	private static Image Halved(Image full, Image.Interpolation how = Image.Interpolation.Lanczos)
	{
		Image small = full.Duplicate() as Image;
		if (small.IsCompressed())
		{
			small.Decompress();
		}

		small.ClearMipmaps();
		small.Resize(full.GetWidth() / PixelsPerVertex, full.GetHeight() / PixelsPerVertex, how);
		return small;
	}

	/// <summary>One ground surface. The pack's maps are packed the way Terrain3D wants them — albedo
	/// with its height in the alpha, normal with its roughness — which is the pair our own loose
	/// textures never had, and the reason they could only ever look painted.</summary>
	private static Resource Surface(string name, string stem, int slot, float uvScale, float detiling,
		Color cast)
	{
		var asset = ClassDB.Instantiate("Terrain3DTextureAsset").As<Resource>();
		asset.Set("name", name);
		asset.Set("id", slot);
		asset.Set("albedo_color", cast);
		asset.Set("albedo_texture", GD.Load<Texture2D>($"{TextureDirectory}/{stem}_alb_ht.png"));
		asset.Set("normal_texture", GD.Load<Texture2D>($"{TextureDirectory}/{stem}_nrm_rgh.png"));
		asset.Set("normal_depth", 1.0f);
		asset.Set("ao_strength", 2.0f);
		asset.Set("uv_scale", uvScale);
		asset.Set("detiling_rotation", detiling);
		return asset;
	}
}
