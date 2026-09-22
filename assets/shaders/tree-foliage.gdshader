// A broadleaf tree's leaves, turning with the year.
//
// The baked trees carry crown and trunk in one texture, so there is no separate leaf material to
// repaint the way the old pack's trees had. Instead the leaves are picked out of the texture by their
// colour — where green stands clear of red and blue it is foliage, where it does not it is bark — and
// only those texels take the season. Evergreens do not use this; a pine keeps its needles.
shader_type spatial;
render_mode cull_back;

uniform sampler2D albedo_texture : source_color, filter_linear_mipmap_anisotropic;

// 0 spring, 1 summer, 2 autumn, 3 winter — MapDecoration.SetSeason.
uniform float season = 1.0;

void fragment() {
	vec3 colour = texture(albedo_texture, UV).rgb;

	// How much of this texel is leaf. The bark in these textures is brown and grey; the leaves are
	// green by a clear margin. The ramp is soft so the edge of a leaf mass does not show as a line.
	float leaf = smoothstep(0.004, 0.035, colour.g - max(colour.r, colour.b));

	float spring = clamp(1.0 - abs(season), 0.0, 1.0);
	float summer = clamp(1.0 - abs(season - 1.0), 0.0, 1.0);
	float autumn = clamp(1.0 - abs(season - 2.0), 0.0, 1.0);
	float winter = clamp(1.0 - abs(season - 3.0), 0.0, 1.0);

	// The leaf keeps its own light and shade and takes the season's colour — the same way the
	// meadow does, and for the same reason: brightness is squashed by the tonemapper, hue is not.
	vec3 tone = spring * vec3(0.78, 1.40, 0.36)
		+ summer * vec3(0.52, 1.10, 0.40)
		+ autumn * vec3(2.10, 1.05, 0.22)
		+ winter * vec3(1.15, 0.95, 0.72);
	float light = dot(colour, vec3(0.2126, 0.7152, 0.0722));
	// Winter leaves are mostly gone: what is left of the crown is thin and brown, and darker.
	float strength = mix(0.8, 0.9, winter);
	vec3 seasoned = light * tone * mix(1.0, 0.72, winter);
	colour = mix(colour, seasoned, leaf * strength);

	ALBEDO = colour;
	ROUGHNESS = 0.9;
	SPECULAR = 0.25;
}
