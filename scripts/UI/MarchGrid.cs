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
	private readonly int[] _region;		 // which stretch of walkable ground the cell is on, or -1

	// The search's working, kept between searches so the mouse can ask on every movement without
	// the country being allocated again each time. A cell's figures are this search's only when it
	// carries this search's mark (_search).
	private readonly float[] _spent;
	private readonly int[] _cameFrom;
	private readonly int[] _reckoned;	 // the search that last priced the cell
	private readonly int[] _opened;		 // the search the cell is waiting in, 0 once it has been settled
	private readonly int[] _joined;		 // when it joined the waiting, which settles a tie
	private readonly PriorityQueue<int, (float Reckoned, int Joined)> _open = new();
	private int _search;

	public MarchGrid(int width, int height)
	{
		_across = Mathf.Max(1, width / CellSize);
		_down = Mathf.Max(1, height / CellSize);
		_cost = new float[_across * _down];
		_county = new int[_across * _down];
		_region = new int[_across * _down];
		_spent = new float[_across * _down];
		_cameFrom = new int[_across * _down];
		_reckoned = new int[_across * _down];
		_opened = new int[_across * _down];
		_joined = new int[_across * _down];
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

		Region();
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

	/// <summary>The cheapest way from one point to another: each step of it with what has been spent
	/// by the time the army stands there. Empty when there is no way at all.
	///
	/// The whole road is returned, not the part that fits this season's legs. A lord pointing at a
	/// far county is owed the answer "that way, and you would get about this far" — a trail that
	/// simply vanishes when he passes the edge of his allowance tells him nothing about which of the
	/// two is the matter, the distance or the ground.</summary>
	public List<(Vector2 At, float Spent)> Way(Vector2 from, Vector2 to, float budget)
	{
		// An army can come to rest a step off the walkable ground — on the shoulder of a mountain road —
		// and a click can land on a crag beside the path. Both are read as the nearest ground a man
		// can stand on, or an army halted there could never be moved again.
		int start = Footing(CellOf(from));
		int goal = Footing(CellOf(to));
		if (start < 0 || goal < 0 || _cost[start] <= 0f || _cost[goal] <= 0f || _region[start] != _region[goal])
		{
			// Across the sea or behind a ditch with no ford is known before a step is taken; searching
			// for it would walk every cell on this side of the water to say no.
			return new List<(Vector2, float)>();
		}

		// A fresh mark for this search: whatever an older one left in the arrays is not this one's.
		_search++;
		_open.Clear();
		int joined = 0;
		_spent[start] = 0f;
		_reckoned[start] = _search;
		_opened[start] = _search;
		_joined[start] = joined++;
		_open.Enqueue(start, (Guess(start, goal), _joined[start]));
		while (_open.TryDequeue(out int at, out (float Reckoned, int Joined) entry))
		{
			// Cheapest first, with the distance still to go as a hint — the hint is what keeps this
			// from searching the whole country to answer about the next valley — and of two as cheap,
			// the one that joined the search first. A cell made cheaper while waiting is queued again
			// in its old place in that order; the dearer copy it leaves behind is passed over.
			if (_opened[at] != _search || _joined[at] != entry.Joined)
			{
				continue;
			}

			if (at == goal)
			{
				return Trace(start, goal);
			}

			_opened[at] = 0;
			int across = at % _across;
			int down = at / _across;
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					if (!Step(across, down, dx, dy, out int next))
					{
						continue;
					}

					// The cost of the ground being entered, paid over the distance covered to enter it.
					float price = _spent[at] + (_cost[next] * (dx != 0 && dy != 0 ? Diagonal : 1f) * CellSize);
					if (price > budget || (_reckoned[next] == _search && _spent[next] <= price))
					{
						continue;
					}

					_spent[next] = price;
					_reckoned[next] = _search;
					_cameFrom[next] = at;
					if (_opened[next] != _search)
					{
						_opened[next] = _search;
						_joined[next] = joined++;
					}

					_open.Enqueue(next, (price + Guess(next, goal), _joined[next]));
				}
			}
		}

		return new List<(Vector2, float)>();
	}

	/// <summary>Whether <see cref="Way"/> with no limit on what may be spent would find a road, without
	/// walking it: the two points stand on the same stretch of ground and are not the same cell. A
	/// company already standing where it is going has no road to take.</summary>
	public bool Reaches(Vector2 from, Vector2 to)
	{
		int start = Footing(CellOf(from));
		int goal = Footing(CellOf(to));
		return start >= 0 && goal >= 0 && start != goal && _cost[start] > 0f && _cost[goal] > 0f
			&& _region[start] == _region[goal];
	}

	/// <summary>The cell one step from (across, down), if a man can take that step.</summary>
	private bool Step(int across, int down, int dx, int dy, out int next)
	{
		next = -1;
		int x = across + dx;
		int y = down + dy;
		if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= _across || y >= _down)
		{
			return false;
		}

		next = (y * _across) + x;
		// No slipping between two corners. A diagonal step passes the two cells either side of it, and
		// if either of those is ground an army cannot stand on, the step squeezes through a gap that
		// is not there — which let a line of rock, or a border ditch, one cell thick be walked straight
		// through on the slant.
		return _cost[next] > 0f
			&& (dx == 0 || dy == 0 || (_cost[(down * _across) + x] > 0f && _cost[(y * _across) + across] > 0f));
	}

	/// <summary>Numbers every stretch of ground a man can walk from end to end without a boat or a ford
	/// that is not there, by the same steps <see cref="Way"/> takes. Roads only make ground cheaper,
	/// never walkable, so this is settled once the ground is described.</summary>
	private void Region()
	{
		System.Array.Fill(_region, -1);
		var reached = new Stack<int>();
		int regions = 0;
		for (int cell = 0; cell < _cost.Length; cell++)
		{
			if (_cost[cell] <= 0f || _region[cell] >= 0)
			{
				continue;
			}

			_region[cell] = regions;
			reached.Push(cell);
			while (reached.TryPop(out int at))
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					for (int dy = -1; dy <= 1; dy++)
					{
						if (Step(at % _across, at / _across, dx, dy, out int next) && _region[next] < 0)
						{
							_region[next] = regions;
							reached.Push(next);
						}
					}
				}
			}

			regions++;
		}
	}

	private float Guess(int cell, int goal) =>
		Middle(cell % _across, cell / _across).DistanceTo(Middle(goal % _across, goal / _across));

	private List<(Vector2 At, float Spent)> Trace(int start, int goal)
	{
		var way = new List<(Vector2, float)>();
		for (int at = goal; at != start; at = _cameFrom[at])
		{
			way.Add((Middle(at % _across, at / _across), _spent[at]));
		}

		way.Reverse();
		return way;
	}

	private Vector2 Middle(int across, int down) =>
		new((across + 0.5f) * CellSize, (down + 0.5f) * CellSize);

	/// <summary>The cell itself if it can be stood on, else the nearest one within two cells that can.</summary>
	private int Footing(int cell)
	{
		if (cell < 0 || _cost[cell] > 0f)
		{
			return cell;
		}

		int across = cell % _across;
		int down = cell / _across;
		for (int reach = 1; reach <= 2; reach++)
		{
			for (int dy = -reach; dy <= reach; dy++)
			{
				for (int dx = -reach; dx <= reach; dx++)
				{
					int x = across + dx;
					int y = down + dy;
					if (x >= 0 && y >= 0 && x < _across && y < _down && _cost[(y * _across) + x] > 0f)
					{
						return (y * _across) + x;
					}
				}
			}
		}

		return cell;
	}

	private int CellOf(Vector2 pixel)
	{
		int across = Mathf.FloorToInt(pixel.X / CellSize);
		int down = Mathf.FloorToInt(pixel.Y / CellSize);
		return across < 0 || down < 0 || across >= _across || down >= _down
			? -1
			: (down * _across) + across;
	}
}
