using System.Collections.Generic;
using Godot;

/// <summary>The ground an army can cross, and what each stretch of it costs.
///
/// A county graph was not enough: a lord points at a hillside, not at a town, and the map has to
/// answer for the hillside. So the country is cut into cells, each one told whether it can be walked
/// and what walking it costs, and the way between two points is found across those cells.
///
/// Roads are the whole point of the thing. A cell a road runs through costs a fraction of one beside
/// it, so an army takes the stone when the stone goes its way and cuts across country when it does
/// not — and the lord can see, in the length of the trail, exactly what that choice cost him.
///
/// Cells and not pixels because a pixel grid is a million and a half nodes to search through for a
/// question asked every time the mouse moves. A cell is a few strides of a man and the answer is the
/// same one.</summary>
public sealed class MarchGrid
{
	/// <summary>How many map pixels a cell is across. Small enough that a road is followed rather
	/// than approximated, large enough that the whole country is a few thousand cells.</summary>
	public const int CellSize = 12;

	private const float Diagonal = 1.41421f;

	private readonly int _across;
	private readonly int _down;
	private readonly float[] _cost;		 // what crossing this cell costs, or nothing where it cannot be
	private readonly int[] _county;		 // which county owns the ground, or -1

	public MarchGrid(int width, int height)
	{
		_across = Mathf.Max(1, width / CellSize);
		_down = Mathf.Max(1, height / CellSize);
		_cost = new float[_across * _down];
		_county = new int[_across * _down];
	}

	/// <summary>Lays the ground out: what each cell costs and whose county it is. Called once, while
	/// the map is being built, off the images that already describe it.</summary>
	public void Describe(System.Func<Vector2, (bool Walkable, float Cost, int County)> ground)
	{
		for (int down = 0; down < _down; down++)
		{
			for (int across = 0; across < _across; across++)
			{
				(bool walkable, float cost, int county) = ground(Middle(across, down));
				_cost[(down * _across) + across] = walkable ? Mathf.Max(0.01f, cost) : 0f;
				_county[(down * _across) + across] = county;
			}
		}
	}

	/// <summary>Marks the cells a road runs through as road. Done from the drawn line itself, so what
	/// an army follows is what the player can see under it.</summary>
	public void LayRoad(List<Vector2> line, float cost)
	{
		for (int point = 1; point < line.Count; point++)
		{
			// Stepped along the segment: the road's own points are further apart than a cell, so
			// dropping one mark per point would leave a dotted road with gaps an army cannot use.
			float span = line[point - 1].DistanceTo(line[point]);
			for (float along = 0f; along <= span; along += CellSize * 0.5f)
			{
				Mark(line[point - 1].Lerp(line[point], span < 0.01f ? 0f : along / span), cost);
			}
		}
	}

	private void Mark(Vector2 pixel, float cost)
	{
		int cell = CellOf(pixel);
		if (cell >= 0 && _cost[cell] > 0f)
		{
			_cost[cell] = Mathf.Min(_cost[cell], cost);
		}
	}

	/// <summary>Whose county the ground under a point belongs to, or -1 for nobody's.</summary>
	public int CountyAt(Vector2 pixel)
	{
		int cell = CellOf(pixel);
		return cell < 0 ? -1 : _county[cell];
	}

	/// <summary>Shuts a county's ground to an army — a rival's land, which cannot be crossed until
	/// there is a way to fight for it.</summary>
	public void Close(int county)
	{
		for (int cell = 0; cell < _cost.Length; cell++)
		{
			if (_county[cell] == county)
			{
				_cost[cell] = 0f;
			}
		}
	}

	/// <summary>The cheapest way from one point to another: each step of it with what has been spent
	/// by the time the army stands there. Empty when there is no way at all.
	///
	/// The whole road is returned, not the part that fits this season's legs. A lord pointing at a
	/// far county is owed the answer "that way, and you would get about this far" — a trail that
	/// simply vanishes when he passes the edge of his allowance tells him nothing about which of the
	/// two is the matter, the distance or the ground.</summary>
	public List<(Vector2 At, float Spent)> Way(Vector2 from, Vector2 to, float budget)
	{
		var none = new List<(Vector2, float)>();
		int start = CellOf(from);
		int goal = CellOf(to);
		if (start < 0 || goal < 0 || _cost[start] <= 0f || _cost[goal] <= 0f)
		{
			return none;
		}

		var spent = new Dictionary<int, float> { [start] = 0f };
		var cameFrom = new Dictionary<int, int>();
		var open = new List<int> { start };
		while (open.Count > 0)
		{
			// Cheapest first, with the distance still to go as a hint. The hint is what keeps this
			// from searching the whole country to answer about the next valley.
			int at = open[0];
			float best = spent[at] + Guess(at, goal);
			foreach (int cell in open)
			{
				float reckoned = spent[cell] + Guess(cell, goal);
				if (reckoned < best)
				{
					best = reckoned;
					at = cell;
				}
			}

			if (at == goal)
			{
				return Trace(cameFrom, spent, start, goal);
			}

			open.Remove(at);
			foreach ((int next, float step) in Around(at))
			{
				float price = spent[at] + step;
				if (price > budget || (spent.TryGetValue(next, out float known) && known <= price))
				{
					continue;
				}

				spent[next] = price;
				cameFrom[next] = at;
				if (!open.Contains(next))
				{
					open.Add(next);
				}
			}
		}

		return none;
	}

	private IEnumerable<(int Cell, float Step)> Around(int cell)
	{
		int across = cell % _across;
		int down = cell / _across;
		for (int dx = -1; dx <= 1; dx++)
		{
			for (int dy = -1; dy <= 1; dy++)
			{
				if (dx == 0 && dy == 0)
				{
					continue;
				}

				int x = across + dx;
				int y = down + dy;
				if (x < 0 || y < 0 || x >= _across || y >= _down)
				{
					continue;
				}

				int next = (y * _across) + x;
				if (_cost[next] <= 0f)
				{
					continue;
				}

				// No slipping between two corners. A diagonal step passes the two cells either side of
				// it, and if either of those is ground an army cannot stand on, the step squeezes
				// through a gap that is not there — which let a line of rock, or a border ditch, one
				// cell thick be walked straight through on the slant.
				if (dx != 0 && dy != 0
					&& (_cost[(down * _across) + x] <= 0f || _cost[(y * _across) + across] <= 0f))
				{
					continue;
				}

				// The cost of the ground being entered, paid over the distance covered to enter it.
				yield return (next, _cost[next] * (dx != 0 && dy != 0 ? Diagonal : 1f) * CellSize);
			}
		}
	}

	private float Guess(int cell, int goal) =>
		Middle(cell % _across, cell / _across).DistanceTo(Middle(goal % _across, goal / _across));

	private List<(Vector2 At, float Spent)> Trace(Dictionary<int, int> cameFrom,
		Dictionary<int, float> spent, int start, int goal)
	{
		var way = new List<(Vector2, float)>();
		for (int at = goal; at != start; at = cameFrom[at])
		{
			way.Insert(0, (Middle(at % _across, at / _across), spent[at]));
		}

		return way;
	}

	private Vector2 Middle(int across, int down) =>
		new((across + 0.5f) * CellSize, (down + 0.5f) * CellSize);

	private int CellOf(Vector2 pixel)
	{
		int across = Mathf.FloorToInt(pixel.X / CellSize);
		int down = Mathf.FloorToInt(pixel.Y / CellSize);
		return across < 0 || down < 0 || across >= _across || down >= _down
			? -1
			: (down * _across) + across;
	}
}
