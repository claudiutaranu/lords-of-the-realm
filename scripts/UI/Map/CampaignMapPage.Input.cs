using System.Collections.Generic;
using Godot;

/// <summary>The lord's hand on the map: clicks and the pointer over it, a county chosen, the
/// scouts' word on it, and the pins kept on the ground every frame.</summary>
public partial class CampaignMapPage
{
	private void OnMapGuiInput(InputEvent @event)
	{
		_world.HandleInput(@event); // pan and zoom; selection is the left button, below
		if (_rivalsMarching)
		{
			return; // the other lords are moving: look, but give no orders until they have
		}

		if (@event is InputEventMouseMotion motion)
		{
			int hovered = _world.ProvinceAt(motion.Position);
			if (hovered != _hovered)
			{
				_hovered = hovered;
				_world.SetHighlight(_selected != null ? _markers.IndexOf(_selected) : -1, _hovered);
			}

			if (_marching)
			{
				ShowMarchCost(motion.Position);
			}

			return;
		}

		// The right button asks about a thing rather than doing something with it: a company under
		// the pointer is opened and told in full, whoever it belongs to; with none there, the walls of
		// the seat under it.
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: false } asked
			&& !_marching && _world.TryMapPixel(asked.Position, out Vector2 under))
		{
			FieldArmy company = _turnManager.ArmyOf(_world.ArmyAt(under));
			if (company is { Strength: > 0 })
			{
				string realmKey = _turnManager.RealmOf(company);
				_army.Show(company, _turnManager.AnyProvince(company.Home), _balance,
					_realms.TryGetValue(realmKey, out RealmData lord) ? lord.Name : realmKey, realmKey,
					realmKey == _playerRealm);
				_army.OfferWalls(WallsFor(company) != null);
				return;
			}

			// No company there: a seat with walls on it is asked about its garrison.
			string seat = _world.CastleAt(under) is { Length: > 0 } castle ? castle : _world.TownAt(under);
			ProvinceEconomy walled = seat.Length == 0 ? null : _turnManager.AnyProvince(seat);
			if (walled is { Fortification.Length: > 0 })
			{
				_garrison.Open(walled, _realms.TryGetValue(walled.Realm, out RealmData holder) ? holder.Name : walled.Realm,
					_balance);
			}

			return;
		}

		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } click)
		{
			int index = _world.ProvinceAt(click.Position);
			if (_marching)
			{
				FieldArmy sent = _marchingArmy;
				LayDownArmy();
				if (March(sent, click.Position))
				{
					return;
				}
			}

			// A banner is taken hold of before the county under it: a lord jabbing at his own army
			// means the army, not the ground it happens to be standing on.
			if (!_marching && _world.TryMapPixel(click.Position, out Vector2 ground))
			{
				FieldArmy banner = _turnManager.ArmyOf(_world.ArmyAt(ground));
				if (banner != null && _turnManager.RealmOf(banner) == _playerRealm)
				{
					int stands = _provinces.FindIndex(province => province.Name == banner.County);
					SelectProvince(stands >= 0
						? stands
						: _provinces.FindIndex(province => province.Name == banner.Home));
					TakeUpArmy(banner);
					return;
				}
			}

			if (index >= 0 && index < _provinces.Count)
			{
				// The first press is how you look a province over. After that the ground answers for
				// itself: a press on one of the county's own fields opens that field, and a press on
				// the village opens the town. Everywhere else — its woods, its hills, the road between
				// them — is ground, and a lord pointing at it is not asking to be taken indoors.
				if (_selected != null && _markers.IndexOf(_selected) == index)
				{
					if (!OpenField(index, click.Position) && _world.TryMapPixel(click.Position, out Vector2 streets)
						&& _world.TownAt(streets) == _provinces[index].Name)
					{
						OpenCity();
					}
				}
				else
				{
					SelectProvince(index);
				}
			}
		}
	}

	private void SelectProvince(int index)
	{
		_selected?.SetSelected(false);
		ProvinceMarker marker = _markers[index];
		marker.SetSelected(true);
		_selected = marker;

		_world.SetHighlight(index, _hovered);

		ProvinceData province = _provinces[index];

		string realmKey = HolderOf(province);

		string realmName = _realms[realmKey].Name;
		_markers[index].Configure(_realms[realmKey].Accent, province.IsCapital);

		ProvinceEconomy economy = _turnManager.GetProvince(province.Name);
		if (economy != null)
		{
			UpdateResourceBar(economy);
		}

		// The sidebar takes the province whether or not anyone runs it: an unclaimed one still has a
		// name, a crest and the land under it, it just has no numbers of its own to show.
		_sidebar.ShowHeader(province.Name, realmName, realmKey, _realms[realmKey].Accent);
		ProvinceDefinition definition = _definitionsByName.GetValueOrDefault(province.Name);
		_sidebar.ShowEconomy(economy, definition, _balance, _turnManager.CurrentSeason);
		if (economy == null)
		{
			_sidebar.ShowWord(Word(province.Name, definition));
		}
	}

	/// <summary>The scouts' word on a county not yours: its people, what its hills give, its herd and
	/// the men who would meet you at its gate — rounded, because it is hearsay, not its books. A
	/// county worth marching on is one you can tell apart from the others.</summary>
	private string Word(string county, ProvinceDefinition definition)
	{
		if (definition == null)
		{
			return "No word reaches you of what it holds.";
		}

		ProvinceEconomy theirs = _turnManager.AnyProvince(county);
		int people = theirs?.Population ?? definition.InitialPopulation;
		int herd = theirs?.Cattle ?? definition.InitialCattle;
		string hills = definition.IronWorkerCapacity > 0 ? "Iron in its hills."
			: definition.StoneWorkerCapacity > 0 ? "Stone in its hills."
			: "Nothing in its hills but sheep.";
		return $"Some {Rounded(people)} souls, and {Rounded(herd)} head of cattle. {hills}\n"
			+ $"About {Rounded(_turnManager.DefendersOf(county).Men)} men would meet you at its gate.";
	}

	/// <summary>A number as a scout brings it back: to the nearest ten, or five under fifty.</summary>
	private static int Rounded(int count) =>
		count < ScoutsRoundSmall ? Mathf.RoundToInt(count / 5f) * 5 : Mathf.RoundToInt(count / 10f) * 10;

	private const int ScoutsRoundSmall = 50;

	// Markers are 2D art pinned to 3D ground, so every frame the camera moves — or the window is
	// resized under it — they have to be re-projected. A frame where neither happened leaves them
	// where they are.
	public override void _Process(double delta)
	{
		var view = (_world.CameraMoved, _world.GetViewport().GetVisibleRect().Size);
		if (_projectedFor == view)
		{
			return;
		}

		_projectedFor = view;
		if (_trailSteps.Count > 0)
		{
			ProjectTrail();
		}

		for (int i = 0; i < _provinces.Count; i++)
		{
			bool onScreen = _world.TryScreenPosition(_provinces[i].MapPosition, out Vector2 screenPosition);
			_markers[i].Visible = onScreen;
			if (onScreen)
			{
				_markers[i].Position = screenPosition - _markers[i].Size / 2f;
				_markers[i].SetBadgeScale(_world.Closeness);
			}
		}
	}
}
