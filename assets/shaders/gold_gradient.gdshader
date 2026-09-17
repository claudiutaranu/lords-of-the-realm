shader_type canvas_item;

// Vertical gold gradient for title text. top_y/height are set from C#
// (GoldTitle.cs) from the label's own screen rect, so the blend spans the
// whole word instead of restarting per glyph.
uniform vec4 top_color : source_color = vec4(1.0, 0.957, 0.655, 1.0);
uniform vec4 bottom_color : source_color = vec4(0.898, 0.639, 0.145, 1.0);
uniform float top_y = 0.0;
uniform float px_height = 100.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	// A canvas shader recolors every pass the Label draws, including the font's
	// outline (a dilated glyph tinted with font_outline_color). Tinting that too
	// erases the dark edge and smears it into the fill, so only the light fill
	// pass gets the gradient and the dark outline passes through untouched.
	if (dot(COLOR.rgb, vec3(0.299, 0.587, 0.114)) < 0.3) {
		COLOR = vec4(COLOR.rgb, tex.a);
	} else {
		float t = clamp((FRAGCOORD.y - top_y) / max(px_height, 1.0), 0.0, 1.0);
		COLOR = vec4(mix(top_color.rgb, bottom_color.rgb, t), tex.a);
	}
}
