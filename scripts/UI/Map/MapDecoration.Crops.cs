using System.Collections.Generic;
using Godot;

/// <summary>The corn standing on a county's grain fields: which quarters of a plot are under crop,
/// how tall it has grown, and the model the season calls for.</summary>
public partial class MapDecoration
{
	/// <summary>How many patches of corn a field is drawn in, each way. Four quarters in a square.</summary>
	private const int CropQuarters = 2;

	/// <summary>How tall the corn stands, in world units of model height. Read from the map's own
	/// distance rather than from the field's size: at a quarter of a plot the model's own proportions
	/// put the ears about ankle high on a cottage, which from up here is nothing.</summary>
	private const float CropHeight = 2.6f;

	/// <summary>Two rows of standing crop inside a grain plot, each drawn where it actually stands
	/// so a field on a slope has its far row up the hill. Both the size of the crop and the space
	/// between the rows are measured off the model's own bounds against the plot's — a spacing
	/// guessed in world units fills one plot and overhangs the next the day either is retuned.</summary>
	/// <summary>Whether an interior cell falls in one of the quarters the crop was laid on. Counted
	/// the same way round as <see cref="AddCrop"/> lays its patches out, or the ground would be
	/// painted in one corner and the corn stood in another.</summary>
	private static bool UnderCrop(int x, int z, int inner, int quarters)
	{
		if (quarters <= 0)
		{
			return false;
		}

		int across = ((x - 1) >= inner / 2 ? 1 : 0) + ((z - 1) >= inner / 2 ? CropQuarters : 0);
		return across < quarters;
	}

	/// <summary>A field under grain, in quarters. Not because quarters are tidy — because the amount
	/// of a field that is actually under crop is a number the lord has to be able to read off his own
	/// land. A county that sowed half of what its fields would take, or let the weeds have half of
	/// what it sowed, has half a field of corn standing there, and the map says so before the ledger
	/// does.
	///
	/// Quarters are enough resolution and not too much: four steps a player can count at a glance
	/// beats a smooth fade he has to squint at, and it is what the game this one is copied from
	/// did.</summary>
	private void AddCrop(List<Transform3D> crop, Vector2 centre, float yaw, string model, int quarters)
	{
		Aabb bounds = Models.MeshOf(model).GetAabb();
		float inside = PlotSize * (1f - (2f * PlotBorder));
		float patch = inside / CropQuarters;
		Vector3 middle = bounds.GetCenter();

		// Stretched onto its quarter on BOTH sides rather than scaled by its longest one. The model
		// is a strip of rows half again as long as it is deep, so fitting it evenly left a third of
		// every quarter as bare earth between the rows — which is what made a sown field read as a
		// thin scatter instead of a crop.
		var spread = new Vector3(patch / bounds.Size.X, CropHeight, patch / bounds.Size.Z);

		// The height is its own number and not the ground scale. Corn stands the same height in a
		// small field as in a large one, and a field the player has to squint at is a field he does
		// not count — this is the one place where being a little taller than life is the honest
		// choice, because the map is read from further away than a man ever saw his own land.
		float roots = (-bounds.Position.Y * spread.Y) + PlotLift;

		// Laid row by row from one corner, so a quarter-full field is a quarter of the ground covered
		// and not a quarter of it thinned — a field sown thin and a field sown small are different
		// things, and the one this game has is the second.
		for (int quarter = 0; quarter < quarters; quarter++)
		{
			var step = new Vector2(
				((quarter % CropQuarters) + 0.5f - (CropQuarters * 0.5f)) * patch,
				((quarter / CropQuarters) + 0.5f - (CropQuarters * 0.5f)) * patch);

			Vector2 spot = centre + (step * _map.PixelsPerUnit).Rotated(yaw);
			// A map rotation turns the other way round the vertical than a world one does, so what
			// squares a model to its plot is the lattice's angle negated.
			// A map rotation turns the other way round the vertical than a world one does, so what
			// squares a model to its plot is the lattice's angle negated.
			var standing = new Transform3D(
				Basis.Identity.Rotated(Vector3.Up, -yaw).Scaled(spread), _map.WorldAt(spot));
			crop.Add(standing
				.TranslatedLocal(new Vector3(-middle.X, 0f, -middle.Z))
				.Translated(Vector3.Up * roots));
		}
	}

	/// <summary>How many quarters of a field are under crop, from how full the county's crop is.
	/// Rounded up, so a field with anything at all standing in it shows something standing.</summary>
	private static int Quarters(float fullness) =>
		Mathf.Clamp(Mathf.CeilToInt(CropQuarters * CropQuarters * fullness), 0, CropQuarters * CropQuarters);

	/// <summary>What stands in a grain field this season: a full crop from the sowing to the
	/// reaping, coloured green, gold or stubble by the year, and bare ploughed earth all winter —
	/// the same year the fields charge hands for. The pack's thinner sown model was honest
	/// about spring and unreadable from map height, which on a map is the same as being wrong.</summary>
	private static string CropModel(Season season) =>
		season == Season.Winter ? null : "Farm_FirstAge_Level2_Wheat";
}
