using System.Collections.Generic;
using Godot;

/// <summary>Where a county's fields are laid: the plots found round its seat in blocks, kept on its
/// own level ground and clear of the roads, and found again under a pixel.</summary>
public partial class MapDecoration
{
	// What a field is drawn as: a plot this many world units on a side, with a hedge band around it,
	// laid on a lattice of cells this far apart. Sized against the cottages beside it — a plot is a
	// strip a family works, not an estate, and two of them side by side should read as two fields
	// rather than as two counties. There is no gap: neighbours share a hedge, as the original's fields
	// share a fence, and what makes a patchwork is plots that touch.
	// A field's side, in world units. Smaller than it was: at 2.6 a county's ten fields were a
	// patchwork a fifth of the way across the island, and the plots read as tiles laid on the map
	// rather than as fields in it.
	private const float PlotSize = 2.0f;

	private const float PlotGap = 0f;
	private const float PlotBorder = 0.07f;  // share of the plot its hedge band takes, each side
	private const float PlotLift = 0.12f;    // clear of the terrain
	private const int PlotRings = 7;         // lattice cells searched either way of the seat

	private const float PlotMaxDrop = 1.7f;  // fall across a plot before the ground is too steep

	/// <summary>Where each province's plots were laid, kept so a redraw puts them back exactly where
	/// they were. Searching again would not: the plots themselves are ground taken, so the second
	/// search would refuse the very cells the first one chose and walk the fields out of the county.</summary>
	private readonly Dictionary<string, List<Vector2>> _plots = new();

	/// <summary>Which of a county's fields a map pixel fell on, or -1 for the ground between them.
	/// Answered off the plot centres this class already keeps rather than by giving every plot a
	/// collision body: the plots are one mesh on purpose — a hundred and sixty little bodies to
	/// answer a question a distance check answers is how a map starts costing frames.
	///
	/// The reach is half a plot, which is the circle that fits inside the square. A click on the very
	/// corner of a field misses; a click that would have been ambiguous between two of them cannot
	/// happen, and the second is worth more than the first.</summary>
	public int PlotAt(string province, Vector2 pixel)
	{
		if (!_plots.TryGetValue(province, out List<Vector2> plots))
		{
			return -1;
		}

		float reach = PlotSize * 0.5f * _map.PixelsPerUnit;
		for (int field = 0; field < plots.Count; field++)
		{
			if (pixel.DistanceTo(plots[field]) <= reach)
			{
				return field;
			}
		}

		return -1;
	}

	/// <summary>Where a province's fields lie: blocks of plots that touch, grown outwards from the
	/// nearest workable cell of a lattice laid over the seat. Grown rather than picked, because
	/// picking the ten best cells in a ring scatters ten lonely squares across the county, and what
	/// makes ground read as farmed is fields sharing hedges with their neighbours.
	///
	/// A block stops when it runs into woods, water, a hillside or the village, and the next one
	/// starts at the nearest cell still free — so a province whose land is broken up gets two or
	/// three small patchworks rather than one impossible square.
	///
	/// The order is total — distance first, then the cell's own coordinates — so two cells equally
	/// far out never swap places between one rebuild and the next. A field that jumps across the
	/// county because the player changed what it is under reads as a bug, and would be one.
	///
	/// Two passes: the first asks for the open farmland the props mask marks out, the second takes
	/// whatever is level and dry. A county of rock still has to eat.</summary>
	private List<Vector2> PlotSites(Vector2 seat, float yaw, int wanted)
	{
		float cell = (PlotSize + PlotGap) * _map.PixelsPerUnit;
		int county = _map.CountyAt(seat);

		var cells = new List<Vector2I>();
		for (int x = -PlotRings; x <= PlotRings; x++)
		{
			for (int z = -PlotRings; z <= PlotRings; z++)
			{
				cells.Add(new Vector2I(x, z));
			}
		}

		cells.Sort((a, b) =>
		{
			int byDistance = (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y);
			return byDistance != 0 ? byDistance
				: a.X != b.X ? a.X.CompareTo(b.X)
				: a.Y.CompareTo(b.Y);
		});

		// Half a cell off the lattice's own corners, so no plot lands on the seat itself.
		Vector2 Centre(Vector2I square) => seat + new Vector2(
			(square.X + 0.5f) * cell, (square.Y + 0.5f) * cell).Rotated(yaw);

		// Whether a cell can be ploughed at all, asked once: the one-block search below asks it of
		// the same cells from many starting points.
		var ploughable = new Dictionary<Vector2I, bool>();
		bool Ploughable(Vector2I square)
		{
			if (!ploughable.TryGetValue(square, out bool can))
			{
				can = Mathf.Abs(square.X) <= PlotRings && Mathf.Abs(square.Y) <= PlotRings
					&& !OnSeatGround(seat, Centre(square)) && CanPlough(Centre(square), yaw, openGroundOnly: false, county);
				ploughable[square] = can;
			}

			return can;
		}

		// One patchwork first, as the original lays a county's fields: the nearest open cell from
		// which every field fits in one block, grown closest-to-the-first-cell first so it comes out
		// square rather than as a snake along a valley.
		foreach (bool openOnly in new[] { true, false })
		{
			foreach (Vector2I first in cells)
			{
				if (openOnly && !CanPlough(Centre(first), yaw, openGroundOnly: true, county))
				{
					continue;
				}

				List<Vector2I> block = Block(first, wanted, Ploughable);
				if (block.Count >= wanted)
				{
					return block.ConvertAll(Centre);
				}
			}
		}

		// Broken ground with no room for one block: two or three smaller ones, each grown from the
		// nearest cell still free.
		var taken = new List<Vector2I>();
		for (int pass = 0; pass < 2 && taken.Count < wanted; pass++)
		{
			var refused = new HashSet<Vector2I>();
			foreach (Vector2I first in cells)
			{
				if (taken.Count >= wanted)
				{
					break;
				}

				// Each sweep of this loop starts a new block at the nearest cell nothing has claimed,
				// and the walk below grows it as far as the ground allows.
				var frontier = new Queue<Vector2I>();
				frontier.Enqueue(first);
				while (frontier.Count > 0 && taken.Count < wanted)
				{
					Vector2I square = frontier.Dequeue();
					if (refused.Contains(square) || taken.Contains(square))
					{
						continue;
					}

					// The open-farmland mask is asked of the cell a block STARTS on and of no other.
					// Asked of every cell it fragments the block into single squares dotted across
					// the county — which is what the first map showed — because the mask thins out
					// wherever the woods begin. Where a man puts his first field, he ploughs the
					// next one beside it whatever the map thinks of the ground.
					bool seeding = square == first;
					if (Mathf.Abs(square.X) > PlotRings || Mathf.Abs(square.Y) > PlotRings
						|| OnSeatGround(seat, Centre(square))
						|| !CanPlough(Centre(square), yaw, openGroundOnly: pass == 0 && seeding, county))
					{
						refused.Add(square);
						continue;
					}

					taken.Add(square);
					foreach (Vector2I side in new[]
						{ Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up })
					{
						frontier.Enqueue(square + side);
					}
				}
			}
		}

		var sites = new List<Vector2>();
		foreach (Vector2I square in taken)
		{
			sites.Add(Centre(square));
		}

		return sites;
	}

	/// <summary>Whether a plot would touch the ground the seat keeps for its town or its castle.
	/// Judged on the plot's reach, half its diagonal, so no corner of a field clips a roof.</summary>
	/// <summary>A block of ploughable cells grown from one, always taking next the free neighbour
	/// nearest the first cell (ties by the cell's own coordinates, so a rebuild lays the same block).</summary>
	private static List<Vector2I> Block(Vector2I first, int wanted, System.Func<Vector2I, bool> ploughable)
	{
		var block = new List<Vector2I>();
		var seen = new HashSet<Vector2I> { first };
		var frontier = new List<Vector2I> { first };
		while (frontier.Count > 0 && block.Count < wanted)
		{
			int best = 0;
			for (int i = 1; i < frontier.Count; i++)
			{
				if (Closer(frontier[i], frontier[best], first))
				{
					best = i;
				}
			}

			Vector2I square = frontier[best];
			frontier.RemoveAt(best);
			if (!ploughable(square))
			{
				continue;
			}

			block.Add(square);
			foreach (Vector2I side in new[] { Vector2I.Right, Vector2I.Down, Vector2I.Left, Vector2I.Up })
			{
				if (seen.Add(square + side))
				{
					frontier.Add(square + side);
				}
			}
		}

		return block;
	}

	private static bool Closer(Vector2I a, Vector2I b, Vector2I to)
	{
		int da = (a - to).LengthSquared();
		int db = (b - to).LengthSquared();
		return da != db ? da < db : a.X != b.X ? a.X < b.X : a.Y < b.Y;
	}

	private bool OnSeatGround(Vector2 seat, Vector2 plot)
	{
		float reach = PlotSize * 0.71f * _map.PixelsPerUnit;
		if (plot.DistanceTo(seat) < TownRing + TownGap + reach)
		{
			return true;
		}

		return plot.DistanceTo(CastleSite(seat)) < CastleReach + reach;
	}

	/// <summary>How far the crown of the widest tree this map sows reaches from its trunk, in map
	/// pixels. Measured off the models and the scatter's own sizes rather than guessed, so retuning
	/// how big the woods stand cannot quietly leave the fields under them again.</summary>
	private float CrownReach
	{
		get
		{
			float widest = 0f;
			foreach (string tree in Conifers)
			{
				widest = Mathf.Max(widest, Models.FootprintOf(tree) * ConiferSize);
			}

			foreach (string tree in Broadleaves)
			{
				widest = Mathf.Max(widest, Models.FootprintOf(tree) * BroadleafSize);
			}

			// An old tree is grown past the rest; its crown is the one that reaches furthest.
			return 0.5f * _map.PixelsPerUnit * TreeSpread * widest * StandSize;
		}
	}

	/// <summary>A fixed angle per province, so its fields lie square to each other but not to the
	/// map — and the same angle every run, which a string's own hash code does not promise.</summary>
	private static float PlotYaw(string province)
	{
		int hash = 0;
		foreach (char letter in province)
		{
			hash = (hash * 31) + letter;
		}

		return Mathf.Tau * ((hash & 0xFF) / 256f);
	}
}
