using System.Collections.Generic;
using Godot;

/// <summary>The lord's hand on the field: picking squads, sending them, drawing their front, and
/// moving the eye.</summary>
public partial class Battlefield
{
	/// <summary>A click picks the squad under it; a box picks every one of the lord's inside it.
	/// Shift adds to what he already holds. A click on nothing lets go of everything.</summary>
	private void Choose(Vector2 at, bool adding)
	{
		Vector2 from = _pressedAt ?? at;
		_pressedAt = null;
		_box.Visible = false;
		if (!adding)
		{
			_chosen.Clear();
		}

		if (from.DistanceTo(at) > DragToBox)
		{
			Rect2 box = new Rect2(from, at - from).Abs();
			int held = _chosen.Count;
			foreach (FieldSquad squad in _battle.Squads)
			{
				if (squad.IsAttacking && squad.IsStanding && !_chosen.Contains(squad)
					&& box.HasPoint(_camera.UnprojectPosition(_land.On(squad.At))))
				{
					_chosen.Add(squad);
				}
			}

			if (_chosen.Count > held)
			{
				Answer("select");
			}

			return;
		}

		if (OnGround(at) is Vector2 spot && _squads.At(spot) is { IsAttacking: true } ours)
		{
			Pick(ours, adding);
		}
	}

	private void Pick(FieldSquad squad, bool adding)
	{
		if (!adding)
		{
			_chosen.Clear();
		}

		if (!_chosen.Remove(squad))
		{
			_chosen.Add(squad);
			Answer("select");
		}
	}

	/// <summary>Whether the right button now being let go was the second of a double click: a march
	/// ordered once is at a walk, ordered twice quick it is at the run — the first click has already
	/// set them walking, and the second only quickens them.</summary>
	private bool _isOrderedTwice;

	/// <summary>The pace the lord's marches go at (the bar's Run and Walk): a click twice quick always runs.</summary>
	private bool _isRunning;

	private void Pace(bool running)
	{
		_isRunning = running;
		foreach (FieldSquad squad in _chosen)
		{
			squad.IsRunning = running;
		}
	}

	/// <summary>When and where the right button last went down, to tell a second click from a first.
	/// Timed here rather than read off the event's own double-click flag, which a trackpad's
	/// two-finger tap never set: the lord's double click on open ground only ever walked.</summary>
	private ulong _lastOrderTicks;
	private Vector2 _lastOrderAt;
	private const ulong TwiceWithinMsec = 400;
	private const float TwiceWithinPixels = 40f;

	private bool IsSecondClick(Vector2 at)
	{
		ulong now = Time.GetTicksMsec();
		bool isSecond = now - _lastOrderTicks <= TwiceWithinMsec && at.DistanceTo(_lastOrderAt) <= TwiceWithinPixels;
		// Counted afresh after a second click, so a third is a first again.
		_lastOrderTicks = isSecond ? 0 : now;
		_lastOrderAt = at;
		return isSecond;
	}

	/// <summary>The right button let go: a click sends the chosen squads (<see cref="Send"/>); a drag
	/// draws the front they are to stand on — along it, facing away from the lord's eye, as wide as
	/// the line he drew, which is how two ranks are made one long one, or one made two.</summary>
	private void Order(Vector2 at)
	{
		Vector2 start = _orderedAt ?? at;
		_orderedAt = null;
		_squads.Mark(null, null, Vector2.Zero);
		(Vector2 From, Vector2 To, Vector2 Facing)? front = start.DistanceTo(at) > DragToFront ? Front(at) : null;
		if (front is null)
		{
			// Clicked, or held without a real drag: they march, as they stood. The march goes to where
			// the button went down, not to wherever the hand drifted while it was held.
			_orderedFrom = null;
			Send(start);
			return;
		}

		_orderedFrom = null;
		if (_chosen.Count > 0 && front is var (from, to, facing))
		{
			_battle.IsAttackCaptained = false;
			_battle.Form(_chosen, from, to, facing, _isOrderedTwice || _isRunning);
			Answer("move");
		}
	}

	/// <summary>The front a drag draws on the field, from the ground the button went down on to the
	/// ground under the cursor now: its two ends, and the way the men on it face — square to it, away
	/// from the lord's eye. Null off the field, or too short.</summary>
	private (Vector2 From, Vector2 To, Vector2 Facing)? Front(Vector2 end)
	{
		if (_orderedFrom is not Vector2 from || OnGround(end) is not Vector2 to || from.DistanceTo(to) < ShortestFront)
		{
			return null;
		}

		Vector2 along = (to - from).Normalized();
		var facing = new Vector2(-along.Y, along.X);
		Vector3 looking = -_camera.GlobalBasis.Z;
		return (from, to, facing.Dot(new Vector2(looking.X, looking.Z)) < 0f ? -facing : facing);
	}

	/// <summary>Sends the chosen squads: onto an enemy squad to fall on it, anywhere else to march.
	/// The lord who gives an order has taken the reins back from the captain.</summary>
	private void Send(Vector2 at)
	{
		if (_chosen.Count == 0 || OnGround(at) is not Vector2 spot)
		{
			return;
		}

		_battle.IsAttackCaptained = false;
		if ((_squads.AtScreen(at, _camera, attacking: false) ?? _squads.At(spot)) is { IsAttacking: false } foe)
		{
			_battle.Charge(_chosen, foe);
			Answer("attack");
		}
		else
		{
			_battle.March(_chosen, spot, _isOrderedTwice || _isRunning);
			Answer("move");
		}
	}

	/// <summary>The chosen squads stand where they are.</summary>
	private void Hold()
	{
		_battle.IsAttackCaptained = false;
		_battle.Hold(_chosen);
	}

	/// <summary>The chosen squads fall on whichever enemy is nearest each of them.</summary>
	private void Charge()
	{
		_battle.IsAttackCaptained = false;
		foreach (FieldSquad squad in _chosen)
		{
			FieldSquad nearest = null;
			foreach (FieldSquad foe in _battle.Squads)
			{
				if (!foe.IsAttacking && foe.IsStanding
					&& (nearest == null || squad.At.DistanceTo(foe.At) < squad.At.DistanceTo(nearest.At)))
				{
					nearest = foe;
				}
			}

			if (nearest != null)
			{
				_battle.Charge(new[] { squad }, nearest);
			}
		}

		Answer("attack");
	}

	private void Captain(bool handing) => _battle.IsAttackCaptained = handing;

	/// <summary>The lord leaves the rest of the day to the captains of both sides, and it is fought to
	/// its end at once, by the same field and the same dice (FieldBattle.Fought): what he hands over
	/// is the fighting, not the reckoning.</summary>
	private void AutoResolve()
	{
		_battle.Fought();
		_chosen.Clear();
	}

	/// <summary>Where on the field a point on the screen falls: the ray from the eye meeting the
	/// ground, over the hills as they lie.</summary>
	private Vector2? OnGround(Vector2 screen) =>
		_land.Hit(_camera.ProjectRayOrigin(screen), _camera.ProjectRayNormal(screen), Field);

	/// <summary>The keys that move the eye, polled so a held key keeps moving it.</summary>
	private void Steer(float delta)
	{
		float turn = (Held(Key.E) ? 1f : 0f) - (Held(Key.Q) ? 1f : 0f);
		var pan = new Vector3(
			(Held(Key.D) || Held(Key.Right) ? 1f : 0f) - (Held(Key.A) || Held(Key.Left) ? 1f : 0f),
			0f,
			(Held(Key.S) || Held(Key.Down) ? 1f : 0f) - (Held(Key.W) || Held(Key.Up) ? 1f : 0f));
		if (turn == 0f && pan == Vector3.Zero)
		{
			return;
		}

		_yaw -= turn * TurnSpeed * delta;
		_focus += new Basis(Vector3.Up, _yaw) * pan.Normalized() * _eye * PanSpeed * delta;
		float edge = Field / 3f;
		_focus = new Vector3(Mathf.Clamp(_focus.X, -edge, edge), 0f, Mathf.Clamp(_focus.Z, -edge, edge));
		Look();
	}

	private static bool Held(Key key) => Input.IsPhysicalKeyPressed(key);

	/// <summary>Closer or further, toward the ground under the cursor: the eye closes on what the lord
	/// is pointing at, so a wheel turned over a company brings him down among its men and not onto
	/// the empty middle of the field.</summary>
	private void Zoom(float by, Vector2 toward)
	{
		float was = _eye;
		_eye = Mathf.Clamp(_eye * by, NearestEye, FurthestEye);
		if (_eye < was && OnGround(toward) is Vector2 spot)
		{
			var focus = new Vector2(_focus.X, _focus.Z);
			focus += (spot - focus) * (1f - (_eye / was));
			float edge = Field / 3f;
			_focus = new Vector3(Mathf.Clamp(focus.X, -edge, edge), 0f, Mathf.Clamp(focus.Y, -edge, edge));
		}

		Look();
	}

	/// <summary>The eye, looking at the ground under the focus: steeply from far off, nearly level
	/// close in, and never inside a hill between the two.</summary>
	private void Look()
	{
		float close = Mathf.SmoothStep(NearestEye, LevelsBelow, _eye);
		float pitch = Mathf.Lerp(CloseEyePitchDegrees, EyePitchDegrees, close);
		Basis turned = Basis.FromEuler(new Vector3(Mathf.DegToRad(pitch), _yaw, 0f));
		Vector3 at = _land.On(new Vector2(_focus.X, _focus.Z)) + (Vector3.Up * EyeClearance * (1f - close));
		Vector3 eye = at + (turned * new Vector3(0f, 0f, _eye));
		eye.Y = Mathf.Max(eye.Y, _land.Rise(new Vector2(eye.X, eye.Z)) + EyeClearance);
		_camera.Transform = new Transform3D(turned, eye);

		// The shadows reach as far as the eye looks and no further: spread over the whole field
		// whatever the eye, they were too coarse to show a man's from any height but a man's.
		_sun.DirectionalShadowMaxDistance = Mathf.Clamp(_eye * ShadowReach, ShadowNearest, ShadowFurthest);

		// The haze begins past what the eye is looking at, however high it is: fixed at a distance,
		// from high up the whole field lay in it, washed out to grey.
		_sky.Environment.FogDepthBegin = _eye + HazeBeyond;
		_sky.Environment.FogDepthEnd = _eye + HazeBeyond + HazeEnd;
	}

	private const float ShadowReach = 2.6f;
	private const float HazeBeyond = 110f;
	private const float HazeEnd = 200f;
	private const float ShadowNearest = 40f;
	private const float ShadowFurthest = 520f;
}
