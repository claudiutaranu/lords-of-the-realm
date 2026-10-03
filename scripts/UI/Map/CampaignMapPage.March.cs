using System.Collections.Generic;
using Godot;

/// <summary>An army taken in hand and walked: the trail of footsteps laid to where the pointer is,
/// what the march costs, and the order given once the lord clicks.</summary>
public partial class CampaignMapPage
{
	/// <summary>How far apart the footsteps of a march are laid along it, in map pixels: exactly, and
	/// measured along the road, so they read as a steady pace whatever the grid underneath.</summary>
	private const float StepSpacing = 13f;

	/// <summary>Lays a road out: footsteps at an even pace along it, a numbered mark where each
	/// season's march would end — 1 where these men halt this season, 2 the next, and so on — and
	/// the destination. Footsteps are laid only as far as this season's legs reach.
	///
	/// The footsteps are laid along the road by distance and not on its grid cells: laid on the
	/// cells, they came at whatever gaps the grid gave, bunched up here and strung out there.</summary>
	private void LayTrail(List<(Vector2 At, float Spent)> road, float budget)
	{
		_trailSteps.Clear();
		_projectedFor = null; // a new trail goes on screen whether or not the camera has moved
		if (road.Count == 0)
		{
			_trail.Lay(System.Array.Empty<(Vector2, MarchTrail.Mark, int, bool)>());
			return;
		}

		float season = _balance.MarchReach;

		// A company that has walked its season out halts first where next season's walk ends, and
		// that halt is counted as its second season: this one is spent.
		bool spent = budget <= 0f;
		float nextHalt = spent ? season : budget;
		int seasons = spent ? 1 : 0;
		float carried = StepSpacing * 0.5f;
		var here = new List<(float T, Vector2 At, MarchTrail.Mark Mark, int Season, bool Reachable)>();
		for (int step = 1; step < road.Count; step++)
		{
			(Vector2 from, float spentFrom) = road[step - 1];
			(Vector2 to, float spentTo) = road[step];
			float length = from.DistanceTo(to);
			here.Clear();

			// A season ends where the road's cost passes what the men can walk in it.
			while (spentTo > nextHalt && seasons < MostSeasonsShown)
			{
				float t = Mathf.Clamp(Mathf.InverseLerp(spentFrom, spentTo, nextHalt), 0f, 1f);
				seasons++;
				here.Add((t, from.Lerp(to, t), MarchTrail.Mark.Season, seasons, seasons == 1));
				nextHalt += season;
			}

			for (carried += length; carried >= StepSpacing; carried -= StepSpacing)
			{
				// Only the steps this season's legs will take: past them the road is shown by the
				// numbered halts and the destination alone.
				float t = length <= 0f ? 1f : 1f - ((carried - StepSpacing) / length);
				if (Mathf.Lerp(spentFrom, spentTo, t) <= budget)
				{
					here.Add((t, from.Lerp(to, t), MarchTrail.Mark.Step, 0, true));
				}
			}

			// In the order they lie along the road: a footstep takes its heading from its neighbours
			// in the list, and one listed after the halt it comes before pointed back at the army.
			here.Sort((a, b) => a.T.CompareTo(b.T));
			foreach ((float _, Vector2 at, MarchTrail.Mark mark, int number, bool reachable) in here)
			{
				_trailSteps.Add((at, mark, number, reachable));
			}
		}

		_trailSteps.Add((road[^1].At, MarchTrail.Mark.Target, seasons + 1, road[^1].Spent <= budget));
	}

	/// <summary>How many season marks a trail shows before it stops counting.</summary>
	private const int MostSeasonsShown = 9;

	/// <summary>Puts the trail where the camera currently has it. Done with the pins, whenever they are,
	/// for the same reason: the map moves under them.</summary>
	private void ProjectTrail()
	{
		var beads = new List<(Vector2, MarchTrail.Mark, int, bool)>(_trailSteps.Count);
		foreach ((Vector2 at, MarchTrail.Mark mark, int season, bool reachable) in _trailSteps)
		{
			if (_world.TryScreenPosition(at, out Vector2 onScreen))
			{
				beads.Add((onScreen, mark, season, reachable));
			}
		}

		_trail.Lay(beads.ToArray());
	}

	/// <summary>What the ground under the cursor would cost to reach, beside the cursor, and the road
	/// the men would take laid out in front of them. Worked out afresh whenever the mouse moves onto
	/// another cell of the march grid, because that is the question being asked.</summary>
	private void ShowMarchCost(Vector2 where)
	{
		_marchLabel.Position = where + new Vector2(18, 14);
		_marchLabel.Visible = true;

		FieldArmy army = _marchingArmy;
		if (army == null || !_world.TryMapPixel(where, out Vector2 ground))
		{
			_marchLabel.Text = "Nowhere to march";
			_marchLabel.AddThemeColorOverride("font_color", Chrome.Dim);
			LayTrail(new List<(Vector2, float)>(), 0f);
			_marchAsked = null;
			return;
		}

		var asked = (army, ArmyPixel(army), (Vector2I)(ground / MarchGrid.CellSize).Floor(), army.MarchLeft);
		if (_marchAsked == asked)
		{
			return; // the same cell as a moment ago, and the same road to it
		}

		_marchAsked = asked;
		List<(Vector2 At, float Spent)> road = _ground.Way(ArmyPixel(army), ground, Beyond(army));
		if (road.Count == 0)
		{
			_marchLabel.Text = "No ground for an army";
			_marchLabel.AddThemeColorOverride("font_color", Chrome.Dim);
			LayTrail(road, 0f);
			return;
		}

		LayTrail(road, army.MarchLeft);

		// The county named is the one he would actually END the season in, which on a long road is
		// not the one he is pointing at. Naming the far one would be a promise the season cannot
		// keep.
		(Vector2 stop, float spent) = road[Mathf.Max(0, Halting(road, army.MarchLeft))];
		int county = _world.CountyAt(stop);
		string reached = county >= 0 && county < _provinces.Count ? _provinces[county].Name : "open country";
		int left = Mathf.RoundToInt((army.MarchLeft - spent) / _balance.MarchCostByRoad);
		bool short_ = spent < road[^1].Spent;

		_marchLabel.Text = short_
			? $"{reached} — as far as this season takes them"
			: $"{reached} — {left} paces left after";
		_marchLabel.AddThemeColorOverride("font_color", short_ ? Chrome.Soft : Chrome.Bright);
	}

	/// <summary>Takes an army in hand: from here until it is sent or let go, the map is asking one
	/// question — where do these men go — and every movement of the mouse answers it.</summary>
	private void TakeUpArmy(FieldArmy army)
	{
		if (army == null || army.Strength == 0 || army.MarchLeft <= 0f)
		{
			ShowSaveToast(army == null
				? "There is nobody here with a season left in their legs"
				: $"The men of {army.Home} have no ground left this season");
			return;
		}

		_marching = true;
		_marchingArmy = army;
	}

	/// <summary>Puts the army down again, however that came about — sent, refused, or thought better
	/// of. One way out, so no trail is left burning across the map.</summary>
	private void LayDownArmy()
	{
		_marching = false;
		_marchingArmy = null;
		_marchLabel.Visible = false;
		LayTrail(new List<(Vector2, float)>(), 0f);
		_marchAsked = null;
	}

	/// <summary>How far a road is still worth working out, so the lord can be shown where it goes and
	/// how far along it he would get: four full seasons of marching, whatever is left of this one. Not
	/// what is left — an army that had spent most of its season could not be sent anywhere further
	/// than a stone's throw, and only moved again after the turn. Not unbounded either: a search told
	/// to find the far side of the world walks the whole map to say no.</summary>
	private float Beyond(FieldArmy army) => _balance.MarchReach * 4f;

	/// <summary>The last step of a road the army can actually pay for, or -1 when it cannot take a
	/// single one.</summary>
	private static int Halting(List<(Vector2 At, float Spent)> road, float budget)
	{
		int halt = -1;
		for (int step = 0; step < road.Count; step++)
		{
			if (road[step].Spent <= budget)
			{
				halt = step;
			}
		}

		return halt;
	}

	/// <summary>Sends the men where the cursor was pointing. Everything about the ground was settled
	/// when the trail was drawn; this pays for it and walks it.</summary>
	private bool March(FieldArmy army, Vector2 where) =>
		army != null && !_rivalsMarching && _world.TryMapPixel(where, out Vector2 ground) && MarchOn(army, ground, false);

	/// <summary>The march itself, to a point on the ground. One that would halt at another lord's gate
	/// asks first (<paramref name="sure"/> is the lord's yes): it is the one march that starts a battle
	/// nobody can call off.</summary>
	private bool MarchOn(FieldArmy army, Vector2 ground, bool sure)
	{
		List<(Vector2 At, float Spent)> road = _ground.Way(ArmyPixel(army), ground, Beyond(army));
		int halt = Halting(road, army.MarchLeft);
		if (road.Count == 0)
		{
			ShowSaveToast("There is no way there");
			return false;
		}

		if (halt <= 0)
		{
			ShowSaveToast($"The men of {army.Home} have no ground left this season");
			return false;
		}

		// As far as the season carries them along that road, and no further. An order to a far county
		// is not refused — it is obeyed for as long as there are legs for it, which is what a lord
		// pointing at the horizon actually means.
		road = road.GetRange(0, halt + 1);

		// And no further than the first of another lord's fields on the way: the men stop to tread it
		// bare, and that is their season.
		int spoil = _turnManager.FirstSpoil(army, road.ConvertAll(step => step.At));
		if (spoil > 0)
		{
			road = road.GetRange(0, spoil + 1);
		}

		int county = _world.CountyAt(road[^1].At);
		string into = county >= 0 && county < _provinces.Count ? _provinces[county].Name : "";

		// A gate is attacked only when the lord pointed at it. Men sent somewhere else who run out of
		// road beside a town, or whose road runs past one, are passing it, not storming it.
		bool storms = _world.TownAt(ground) == into && Contested(army, into, road[^1].At);
		if (!sure && storms)
		{
			string holder = _realms[HolderOf(_provinces[county])].Name;
			_ask.Ask($"March on {into}?",
				$"{army.Strength:N0} men of {army.Home} will halt at the gate of {into}, held by {holder}, and "
				+ "the battle for it is fought there and then: whoever stands in the open, then whoever is "
				+ "on the walls, to the last man.",
				"Attack", "Not now", () => MarchOn(army, ground, true));
			return true;
		}

		if (into.Length == 0 || !_turnManager.March(army, into, road[^1].At, road[^1].Spent))
		{
			// A rival's border is no longer what stops a march — his ground is walked into like
			// anybody's — so what is left to refuse is the sea, and men with nothing in their legs.
			ShowSaveToast(into.Length == 0
				? "Nobody's land lies that way"
				: $"The men of {army.Home} cannot make that march");
			return false;
		}

		// The banner walks it while the board waits. Everything that has to be redrawn is redrawn
		// when it arrives — a county changing hands moves its walls, its fields and its crest, and
		// doing that at the first step would have the map jump ahead of the man walking across it.
		var strides = new List<Vector2>(road.Count);
		foreach ((Vector2 step, float _) in road)
		{
			strides.Add(step);
		}

		// Over another lord's fields they tread his corn and scatter his herd as they go.
		_turnManager.Trample(army, strides);

		_world.WalkArmy(army.Key, strides, () =>
		{
			ShowArmies();
			ShowFortifications();
			ShowSettlements();
			ShowFields(); // and every field the road crossed, trodden black if it was another lord's
			SelectProvince(county);
			Conquered();

			// A county is taken at its own gate and nowhere else. Men who have halted on another
			// lord's seat — his town, or the walls raised on it — are standing where the thing worth
			// taking is, and that is the one place the fighting happens.
			if (storms)
			{
				_battle.Open(_turnManager, _balance, army, into, strides[^1],
					ColoursOf(_turnManager.RealmOf(army)), ColoursOf(HolderOf(_provinces[county])));
				return;
			}

			// Halted beside another lord's company in open country: that is a fight too, if he wants it.
			FieldArmy foe = Foe(army);
			if (foe != null)
			{
				_battle.OpenAgainst(_turnManager, _balance, army, foe,
					ColoursOf(_turnManager.RealmOf(army)), ColoursOf(_turnManager.RealmOf(foe)));
				return;
			}

			// Halted at his own walls, the question is whether they go up onto them. Asked, not done:
			// a company marched home to the castle may only be passing through.
			if ((_world.TownAt(strides[^1]) == into || _world.AtCastle(into, strides[^1])) && WallsFor(army) != null)
			{
				AskWalls(army);
				return;
			}

			// Where they have halted beside another of the lord's companies, the two of them are one
			// army only if he says so. Nothing has joined by the time this is asked.
			FieldArmy beside = Beside(army);
			if (beside != null)
			{
				_ask.Ask("Two banners, one field", JoinReading(army, beside), "Put them under one banner",
					"Leave them as they are", () =>
				{
					_turnManager.Merge(beside, army);
					ShowArmies();
					_sidebar.Refresh();
				});
			}
		});

		return true;
	}
}
