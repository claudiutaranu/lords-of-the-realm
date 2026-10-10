// The body of tree-foliage.gdshader and tree-foliage-close.gdshader: see those.
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;

// The normals of the original eight-million-triangle model, baked into the three thousand this one
// has (tools/decimate_meshy.py). Held in the model's own space rather than in the surface's, because
// the wrapped surface these were baked onto has no tangents worth the name — its UV charts are cut
// wherever the packer found room, and a tangent-space map would have to agree with them.
uniform sampler2D relief_texture : filter_linear_mipmap_anisotropic;
// How much of the original's relief is taken over the wrap's own soft shading, 0..1. All of it makes
// a crown of separate leaves at arm's length and a boiling mess at map height.
uniform float relief_strength = 0.7;

// 1 for a pine or a cypress: it keeps its needles, and only the snow touches it.
uniform float evergreen = 0.0;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapDecoration.SetSeason.
uniform float season = 1.0;

#ifdef CLOSE
// Seen close — on the battlefield, not from the map's height — a crown wants what a crown has: sun
// coming through the leaves on its shaded side (leaf_glow) and a stir in the wind (sway, metres at
// the top of the tree, which is `crown_height` up). Only in the close shader: written at all, the
// light through the leaves is paid for by every tree, and the map has thousands.
uniform float leaf_glow = 0.55;
// How dark the crowns are drawn: a shade deeper than the map's, as a real wood is beside a sunlit
// meadow.
uniform float shade = 1.0;
// The battlefield's fog of war (battlefield-ground.gdshader's own edge).
uniform float edge_from = 0.0;
uniform float edge_to = 0.0;
varying float edge_dark;
uniform float sway = 0.06;
uniform float crown_height = 1.9;

void vertex() {
	vec3 edge_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	edge_dark = edge_to > 0.0 ? smoothstep(edge_from, edge_to, length(edge_world.xz)) : 0.0;
	{
		vec3 base = MODEL_MATRIX[3].xyz;
		float up = clamp(VERTEX.y / crown_height, 0.0, 1.0);
		float t = TIME * 0.9 + dot(base.xz, vec2(0.071, 0.053));
		vec2 lean = vec2(sin(t) + 0.35 * sin(t * 2.3 + 1.7), 0.5 * cos(t * 0.8)) * sway * up * up;
		// The world lean, in the model's own space: through the inverse of its basis, scale and all.
		// MODEL_NORMAL_MATRIX carries the instance's scale the wrong way round for this — a tree grown
		// to seventeen metres leaned eighty times its sway, and every crown on the field swam.
		VERTEX += inverse(mat3(MODEL_MATRIX)) * vec3(lean.x, 0.0, lean.y);
	}
}
#endif

void fragment() {
	vec3 colour = texture(albedo_texture, UV).rgb;

	// The baked relief, laid over the shading the wrap itself has.
	vec3 relief = texture(relief_texture, UV).rgb * 2.0 - 1.0;
	if (dot(relief, relief) > 0.01) {
		vec3 modelled = normalize((VIEW_MATRIX * MODEL_MATRIX * vec4(relief, 0.0)).xyz);
		NORMAL = normalize(mix(NORMAL, modelled, relief_strength));
	}

	// How much of this texel is leaf. The bark in these textures is brown and grey; the leaves are
	// green by a clear margin. The ramp is soft so the edge of a leaf mass does not show as a line.
	float leaf = smoothstep(0.004, 0.035, colour.g - max(colour.r, colour.b)) * (1.0 - evergreen);

	// A season in between is a blend of its two neighbours, and the year goes round: 3.5 is half
	// winter and half the spring after it, which is season 4 as much as season 0.
	float spring = clamp(1.0 - min(abs(season), abs(season - 4.0)), 0.0, 1.0);
	float summer = clamp(1.0 - abs(season - 1.0), 0.0, 1.0);
	float autumn = clamp(1.0 - abs(season - 2.0), 0.0, 1.0);
	float winter = clamp(1.0 - abs(season - 3.0), 0.0, 1.0);

	// The leaf keeps its own light and shade and takes the season's colour — the same way the
	// meadow does, and for the same reason: brightness is squashed by the tonemapper, hue is not.
	vec3 tone = spring * vec3(0.78, 1.40, 0.36)
		+ summer * vec3(0.52, 1.10, 0.40)
		+ autumn * vec3(1.75, 1.02, 0.30)
		+ winter * vec3(1.15, 0.95, 0.72);
	float light = dot(colour, vec3(0.2126, 0.7152, 0.0722));
	// Winter leaves are mostly gone: what is left of the crown is thin and brown, and darker.
	float strength = mix(0.8, 0.9, winter);
	vec3 seasoned = light * tone * mix(1.0, 0.72, winter);
	colour = mix(colour, seasoned, leaf * strength);

#ifdef CLOSE
	ALBEDO = colour * shade * (1.0 - edge_dark);
	BACKLIGHT = colour * leaf_glow * (leaf + evergreen * 0.6) * (1.0 - edge_dark);
#else
	// The map's trees have no shade and no fog of war to come down over them; written here as on the
	// battlefield, the map's shader would not compile, and every tree on it was drawn white.
	ALBEDO = colour;
#endif
	ROUGHNESS = 0.9;
	SPECULAR = 0.25;
}
