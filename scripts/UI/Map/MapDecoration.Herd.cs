using System.Collections.Generic;
using Godot;

/// <summary>The cattle on a county's pastures: how many beasts a herd is drawn as, and where they
/// stand and lie.</summary>
public partial class MapDecoration
{
	private const float CattleLength = 1.25f;

	/// <summary>Every so many beasts lies down. A field of animals all standing in the same attitude
	/// reads as a row of ornaments; one of them resting is what makes the rest look alive.</summary>
	private const int RestingEveryNth = 3;
	// The herd is drawn, not summarised: one beast stands for this many head, up to what fits inside
	// a hedge. Four beasts is a pasture at its twenty-head capacity, so a field of cattle looks full
	// exactly when it is full, and a lord who has sold his herd looks out on empty grass.
	private const int HeadPerBeast = 5;
	private const int MostBeasts = 4;

	/// <summary>One kind of beast, ready to be set out: its mesh, the scale that makes it the length
	/// this map draws cattle at, and how far off the ground its own feet sit.</summary>
	private readonly record struct Beast(Mesh Mesh, float Scale, float Stands);

	private static Beast BeastOf(string name)
	{
		Mesh mesh = Models.MeshOf(name);
		float scale = CattleLength / Mathf.Max(0.01f, Models.FootprintOf(name));

		// Its own underside, not half its height: a model built around its middle and one built on
		// its feet both land on the grass this way.
		return new Beast(mesh, scale, (-mesh.GetAabb().Position.Y * scale) + PlotLift);
	}

	/// <summary>Beasts on a pasture — as many as the province keeps there, one drawn to every few
	/// head, up to what stands inside a hedge. Counted rather than decorative: in the game this is
	/// copied from, a lord reads his herd off the map by looking at it, and a fixed two cows on
	/// every green square would make selling half the herd invisible.
	///
	/// A beast is a body and a head over one transform, the way a tree used to be a trunk and a
	/// crown: one box at this size is a crate, and the knob on the front of it is the whole
	/// difference between a crate and an animal.</summary>
	private void AddHerd(List<Transform3D> herd, List<Transform3D> resting, Vector2 centre, float yaw,
		Beast afoot, Beast lying, int head)
	{
		int beasts = Mathf.Min(head / HeadPerBeast, MostBeasts);
		if (head > 0 && beasts == 0)
		{
			beasts = 1; // a handful of cattle is still cattle standing in the field
		}

		// Far enough apart to read as four animals, near enough that the outermost one does not put
		// its head through the hedge: a beast standing half outside its own field is the one thing on
		// this map that looks like a mistake rather than like husbandry.
		float reach = PlotSize * (0.5f - PlotBorder) * 0.52f * _map.PixelsPerUnit;
		var corners = new[] { new Vector2(-1, -1), new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, 1) };
		for (int beast = 0; beast < beasts; beast++)
		{
			// Four corners of the square, in the same order and at the same spots on every pasture in
			// the realm, with no jitter on top. A pasture is a fenced field and not a wilderness: the
			// eye reads a repeated arrangement as husbandry and a scattered one as an accident, and
			// the count is the thing the player is meant to read off the ground anyway.
			//
			// Opposite corners first, so a herd of two stands as a diagonal rather than as a pair.
			Vector2 spot = corners[beast % corners.Length] * reach;
			Beast kind = beast % RestingEveryNth == RestingEveryNth - 1 ? lying : afoot;

			// Squared to the plot, the way the corn rows are, and every beast the same way. Left to
			// itself the transform picks a random heading, which is what had them facing four
			// different ways in the same field.
			(kind.Mesh == lying.Mesh ? resting : herd).Add(
				BuildingTransform(centre + spot.Rotated(yaw), kind.Scale, -yaw)
					.Translated(Vector3.Up * kind.Stands));
		}
	}
}
