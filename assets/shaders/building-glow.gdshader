shader_type canvas_item;

// A rim of light around whatever the texture actually draws, for the site the player has in hand.
//
// It reads the alpha outward in rings rather than blurring the picture: a blur would have to run
// over the whole building to light its edge, and what has to light up is the edge. The texture
// needs transparent margin around it or the outermost ring clamps and smears along the border,
// which is why the art is padded when it is installed.
uniform vec4 tint : source_color = vec4(1.0, 0.85, 0.48, 1.0);
uniform float width : hint_range(0.0, 40.0) = 11.0;
uniform float strength : hint_range(0.0, 2.0) = 1.0;

void fragment() {
	vec2 step = TEXTURE_PIXEL_SIZE * width;
	float reach = 0.0;
	float counted = 0.0;

	// Averaged across the rings rather than taking the strongest of them: the strongest reading
	// gives a solid band that stops dead at its edge, which reads as a sticker cut out around the
	// building. An average falls away with distance, which reads as light.
	for (int ring = 1; ring <= 4; ring++) {
		float out_by = float(ring) / 4.0;
		float weight = 1.0 - out_by * 0.75;
		for (int i = 0; i < 12; i++) {
			float around = float(i) * 0.5235988;
			reach += texture(TEXTURE, UV + vec2(cos(around), sin(around)) * step * out_by).a * weight;
			counted += weight;
		}
	}

	COLOR = vec4(tint.rgb, pow(reach / counted, 0.7) * strength);
}
