// A tree on the campaign map: its own relief, and its leaves turning with the year.
//
// The baked trees carry crown and trunk in one texture, so there is no separate leaf material to
// repaint the way the old pack's trees had. Instead the leaves are picked out of the texture by their
// colour — where green stands clear of red and blue it is foliage, where it does not it is bark — and
// only those texels take the season. An evergreen is drawn by this shader too, for the relief, and
// keeps its needles: see the uniform below.
shader_type spatial;
render_mode cull_back;

#include "res://assets/shaders/tree-foliage.gdshaderinc"
