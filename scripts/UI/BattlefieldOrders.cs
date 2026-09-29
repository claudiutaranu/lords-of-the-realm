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
			foreach (FieldSquad squad in _battle.Squads)
			{
				if (squad.IsAttacking && squad.IsStanding && !_chosen.Contains(squad)
					&& box.HasPoint(_camera.UnprojectPosition(new Vector3(squad.At.X, 0f, squad.At.Y))))
				{
					_chosen.Add(squad);
				}
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
		}
	}

	/// <summary>The right button let go: a click sends the chosen squads (<see cref="Send"/>); a drag
	/// draws the front they are to stand on — along it, facing away from the lord's eye, as wide as
	/// the line he drew, which is how two ranks are made one long one, or one made two.</summary>
	private void Order(Vector2 at)
	{
		Vector2 start = _orderedAt ?? at;
		_orderedAt = null;
		_squads.Mark(null, null, Vector2.Zero);
		if (start.DistanceTo(at) <= DragToBox)
		{
			Send(at);
			return;
		}

		if (_chosen.Count > 0 && Front(start, at) is var (from, to, facing))
		{
			_battle.IsAttackCaptained = false;
			_battle.Form(_chosen, from, to, facing);
		}
	}

	/// <summary>The front a drag across the screen draws on the field: its two ends, and the way
	/// the men on it face — square to it, away from the lord's eye. Null off the field, or too short.</summary>
	private (Vector2 From, Vector2 To, Vector2 Facing)? Front(Vector2 start, Vector2 end)
	{
		if (OnGround(start) is not Vector2 from || OnGround(end) is not Vector2 to || from.DistanceTo(to) < 1f)
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
		if (_squads.At(spot) is { IsAttacking: false } foe)
		{
			_battle.Charge(_chosen, foe);
		}
		else
		{
			_battle.March(_chosen, spot);
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
	}

	private void Captain(bool handing) => _battle.IsAttackCaptained = handing;

	/// <summary>Where on the field a point on the screen falls. The field is flat, so this is the
	/// ray from the eye meeting the ground.</summary>
	private Vector2? OnGround(Vector2 screen)
	{
		Vector3 origin = _camera.ProjectRayOrigin(screen);
		Vector3 ray = _camera.ProjectRayNormal(screen);
		if (ray.Y >= -0.001f)
		{
			return null;
		}

		Vector3 hit = origin + (ray * (-origin.Y / ray.Y));
		return new Vector2(hit.X, hit.Z);
	}

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

	private void Zoom(float by)
	{
		_eye = Mathf.Clamp(_eye * by, NearestEye, FurthestEye);
		Look();
	}

	private void Look()
	{
		Basis turned = Basis.FromEuler(new Vector3(Mathf.DegToRad(EyePitchDegrees), _yaw, 0f));
		_camera.Transform = new Transform3D(turned, _focus + (turned * new Vector3(0f, 0f, _eye)));
	}
}
