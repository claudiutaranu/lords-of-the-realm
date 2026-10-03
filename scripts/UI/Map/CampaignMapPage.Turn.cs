using System.Collections.Generic;
using Godot;

/// <summary>End Turn: the rivals walked along their roads in front of the player, the season turned
/// over behind the curtain, what it cost, and the counties won or lost by it.</summary>
public partial class CampaignMapPage
{
	/// <summary>How long past the longest rival march the turn waits before it stops waiting.</summary>
	private const float DeadlineSlack = 1f;

	private const float TurnFadeInSeconds = 0.7f;
	private const float TurnHoldSeconds = 0.7f;
	private const float TurnFadeOutSeconds = 0.8f;

	/// <summary>Announces every county the lord holds now that he did not the last time this was
	/// asked. The first time, it only takes note: a campaign opening is not a conquest.</summary>
	private void Conquered()
	{
		var now = new HashSet<string>();
		foreach (ProvinceEconomy county in _turnManager.Provinces)
		{
			if (county.Realm == _playerRealm)
			{
				now.Add(county.ProvinceName);
			}
		}

		var taken = new List<string>();
		if (_held != null)
		{
			foreach (string county in now)
			{
				if (!_held.Contains(county))
				{
					taken.Add(county);
				}
			}
		}

		_held = now;
		if (taken.Count > 0)
		{
			_conquest.Announce(taken);
		}
	}

	/// <summary>True while the other lords' men are walking, after End Turn and before the season:
	/// the map can be looked around but not given orders.</summary>
	private bool _rivalsMarching;

	/// <summary>Between End Turn and dawn, or after the end of the reign. The other lords have taken
	/// their turn by then and the save has no way to say so, so a campaign saved — or carried through
	/// the options — while their banners walk would hand them a second turn when it is opened.</summary>
	private bool TurnUnderway => _turnTransition.Visible || _rivalsMarching || _fallen.Visible || _victory.Visible;

	private const string MidTurnRefusal = "Not while the season is turning";

	private FallenPanel _fallen;
	private VictoryPanel _victory;

	/// <summary>Whether the map is won — no lord but the player holds a county — and if so the
	/// congratulations said over it. Asked whenever a county can have changed hands.</summary>
	private bool Won()
	{
		if (!_turnManager.RivalsFallen || _victory.Visible)
		{
			return _victory.Visible;
		}

		int years = _turnManager.Turn / 4;
		_victory.Announce(RealmName(_playerRealm), _turnManager.CurrentSeason, _turnManager.CurrentYear, _turnManager.Turn,
			$"Won in {_turnManager.Turn} seasons{(years > 0 ? $", some {years} year{(years == 1 ? "" : "s")}" : "")}.");
		return true;
	}

	/// <summary>Whether the player has lost his last county, and if so the end of the reign said
	/// over the map. Asked after the other lords have marched and after the season, the two moments
	/// a county can change hands without the player lifting a finger.</summary>
	private bool Fell()
	{
		if (!_turnManager.PlayerFallen)
		{
			return false;
		}

		string seat = _provinces.Find(province => province.IsCapital)?.Name ?? "The realm";

		// How it ended, in the steward's words, if it ended at a gate this turn: the season will never
		// turn over to let the advisor say it.
		FiredEvent last = null;
		foreach (FiredEvent item in _turnManager.RivalNews)
		{
			if (item.Said.Id == "county-lost")
			{
				last = item;
			}
		}

		_fallen.Announce(last?.ProvinceName ?? seat, _turnManager.CurrentSeason, _turnManager.CurrentYear, _turnManager.Turn,
			last?.Said.Text);
		return true;
	}

	/// <summary>End Turn, the way Lords of the Realm plays it: the other lords take their turn in
	/// front of the player — their banners walk the roads they chose, all at once — and only when
	/// the last of them has halted does the season turn over behind the curtain. What their marches
	/// won or lost is already settled; the banners are redrawn once they have all arrived.</summary>
	private void AdvanceTurn()
	{
		if (TurnUnderway)
		{
			return; // already mid-turn, or the reign is over; a second click must not queue another season
		}

		ShowArmies(); // everybody where they stand, before anybody moves
		List<LordsCampaign.RivalMarch> marches = _turnManager.RivalsTurn();
		if (marches.Count == 0)
		{
			TurnTheSeason();
			return;
		}

		_rivalsMarching = true;
		// The lord's own orders are shut while theirs are carried out, End Turn with them.
		GetNode<Control>("%EndTurnButton").Visible = false;
		int walking = marches.Count;
		int longest = 0;
		void Halted()
		{
			if (!_rivalsMarching)
			{
				return; // already settled — by the last banner, or by the deadline below
			}

			_rivalsMarching = false;
			GetNode<Control>("%EndTurnButton").Visible = true;
			ShowArmies();

			// The season turns over next and redraws the walls, the villages and the trodden fields
			// behind its curtain; drawn here too, the whole map was built twice in a breath. Only a
			// reign that has ended here gets no curtain, and is shown the map it lost.
			if (!Fell() && !Won())
			{
				TurnTheSeason();
				return;
			}

			ShowFortifications();
			ShowSettlements();
			ShowFields();
		}

		foreach (LordsCampaign.RivalMarch march in marches)
		{
			// Stood where they set out from — a company raised this season has no banner yet — and
			// walked from there.
			FieldArmy army = _turnManager.ArmyOf(march.Army);
			if (army != null && army.Strength > 0)
			{
				_world.SetArmy(march.Army, march.From, true,
					_realms.TryGetValue(_turnManager.RealmOf(army), out RealmData lord) ? lord.Accent : Colors.White);
			}

			longest = Mathf.Max(longest, march.Road.Count);
			_world.WalkArmy(march.Army, march.Road, () =>
			{
				if (--walking == 0)
				{
					Halted();
				}
			}, MapDecoration.RivalStrideSeconds);
		}

		// And settled by the clock if a banner never reports in. One whose figure was taken off the
		// board mid-stride — merged, beaten, retired — takes its walk and its word with it, and the
		// turn sat waiting for a man who no longer existed.
		GetTree().CreateTimer(longest * MapDecoration.RivalStrideSeconds + DeadlineSlack).Timeout += Halted;
	}

	/// <summary>A turn passes in a night: the map darkens as if the sun went down, a veil of night
	/// comes over it with the new season's name, the season turns over at the darkest point — so the
	/// numbers never visibly jump under the player's eyes — and dawn breaks on the new season.</summary>
	private void TurnTheSeason()
	{
		_turnTransition.Visible = true;
		Season before = _turnManager.CurrentSeason;

		Tween tween = CreateTween();
		tween.TweenMethod(Callable.From<float>(_world.Nightfall), 0f, 1f, TurnFadeInSeconds);
		tween.Parallel().TweenProperty(_turnTransition, "modulate:a", 1.0, TurnFadeInSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			List<TurnSummary> summaries = _turnManager.AdvanceTurn();
			_turnNote = MenLost(summaries);
			_wallsRaised.Clear();
			foreach (TurnSummary summary in summaries)
			{
				if (summary.WallRaised.Length > 0)
				{
					_wallsRaised.Add((summary.ProvinceName, summary.WallRaised,
						_turnManager.GetProvince(summary.ProvinceName)?.CastleMen ?? 0));
				}
			}
			UpdateTurnDisplay();
			SelectProvince(_selected != null ? _markers.IndexOf(_selected) : 0);

			// A season's masons may have finished a wall; the map has to say so the moment they do,
			// and it is hidden behind the transition while this happens. The fields turn over with
			// it: what was standing gold in autumn is ploughed earth by winter.
			ShowFortifications();
			ShowSettlements(); // a rival may have taken a county: its banners change hands

			// The fields turn over with the season: what was standing gold in autumn is ploughed
			// earth by winter, and it is redrawn behind the curtain so nobody watches it change.
			ShowFields();
			ShowMercenaries();
			// Men raised, men lost and a rival's men on the march all land in the same turn; the
			// banners are redrawn once here, behind the curtain, rather than by each of the three.
			ShowArmies();

			Season season = _turnManager.CurrentSeason;

			// The country turns over to the new season through the rest of the night and the dawn,
			// not at one instant: the leaves turn, the grass withers or greens, the light changes.
			Tween turning = CreateTween();
			turning.TweenMethod(Callable.From<float>(along => _world.TurnSeason(before, season, along)),
				0f, 1f, TurnHoldSeconds + TurnFadeOutSeconds);

			// Last season's rain stops with it, and this season's is already falling when the
			// curtain lifts: over every county the river rose in, a rival's too, which the lord
			// sees rained on and is not told about. Never in winter — what falls then is snow.
			_world.StopRain();
			foreach (string county in _turnManager.Flooded)
			{
				ProvinceData flooded = _provinces.Find(province => province.Name == county);
				if (flooded != null && season != Season.Winter)
				{
					_world.Rain(flooded.TownPosition);
				}
			}
			GetNode<Label>("%TransitionSeason").Text = season.ToString();
			GetNode<Label>("%TransitionTurn").Text = $"Turn {_turnManager.Turn}";
			GetNode<Label>("%TransitionFlavor").Text = season switch
			{
				Season.Spring => "The thaw opens the roads.",
				Season.Summer => "Long days, and the fields stand full.",
				Season.Autumn => "The harvest comes in before the cold.",
				_ => "Snow closes the passes.",
			};
		}));
		tween.TweenInterval(TurnHoldSeconds);
		tween.TweenProperty(_turnTransition, "modulate:a", 0.0, TurnFadeOutSeconds);
		tween.Parallel().TweenMethod(Callable.From<float>(_world.Nightfall), 1f, 0f, TurnFadeOutSeconds);
		tween.TweenCallback(Callable.From(() =>
		{
			_turnTransition.Visible = false;

			// After the curtain, never through it: the lord is told what happened to his county with
			// the county in front of him, so he can see the flooded fields the advisor is describing.
			// A season where nothing happened tells nothing and the panel is never seen. A reign that
			// ended this season is told that, and nothing else.
			if (!Fell() && !Won())
			{
				_advisor.Tell(_turnManager.News);
			}
		}));
	}

	/// <summary>Men who walked away this season because the treasury could not pay them. Desertion
	/// is the one thing a turn does that the player would otherwise only find by counting his own
	/// garrison twice — it has no recorded line, so it is said plainly instead of not at all.</summary>
	private static string MenLost(System.Collections.Generic.List<TurnSummary> summaries)
	{
		foreach (TurnSummary summary in summaries)
		{
			if (summary.Deserted > 0)
			{
				return $"{summary.Deserted:N0} unpaid men left {summary.ProvinceName}";
			}
		}

		return "";
	}
}
