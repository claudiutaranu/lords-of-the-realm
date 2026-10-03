using Godot;

/// <summary>The lord's eye over the map: pitched and pulled back by the wheel, panned by keys and
/// drag, held over the land, and placed again whenever it moves.</summary>
public partial class CampaignMap3D
{
	// The pitch follows the zoom: from high up the map is read like a map, looking well down on it;
	// come in close and the eye drops toward the horizon, so the hills stand up against the sky and
	// the far country goes into the haze — the way a lord on a ridge sees his land.
	private const float CameraPitchFar = -55.0f;
	private const float CameraPitchNear = -30.0f;
	private float CameraPitchDegrees => Mathf.Lerp(CameraPitchNear, CameraPitchFar, ZoomedOut);

	/// <summary>How far out the camera is, 0 at its closest and 1 at its furthest.</summary>
	private float ZoomedOut => Mathf.Clamp((_distance - MinDistance) / (MaxDistance - MinDistance), 0f, 1f);
	private const float CameraFov = 48.0f;
	// A fifth closer than it was (54): near enough to see a field's rows and a village's roofs.
	private const float MinDistance = 45.0f;
	private const float MaxDistance = 152.0f;

	private const float ZoomStep = 9.5f;
	// Trackpad gestures carry continuous deltas, not the wheel's discrete clicks, so they need their
	// own scale: how many world units one unit of two-finger scroll, and one of pinch, are worth.
	// Tune by feel — these are not comparable to ZoomStep.
	private const float PanGestureZoomStep = 18.0f;
	private const float MagnifyZoomStep = 120.0f;
	private const float PanSpeed = 0.13f;
	private const float KeyPanSpeed = 74.0f; // world units per second, at full zoom-out

	// Physical key positions, not letters, so WASD stays under the same fingers on a non-QWERTY
	// layout. Polled rather than handled as events: holding a key has to pan every frame, and the
	// UI over the map would otherwise eat the repeats.
	public override void _Process(double delta)
	{
		var move = new Vector3(
			(IsHeld(Key.D) || IsHeld(Key.Right) ? 1 : 0) - (IsHeld(Key.A) || IsHeld(Key.Left) ? 1 : 0),
			0,
			(IsHeld(Key.S) || IsHeld(Key.Down) ? 1 : 0) - (IsHeld(Key.W) || IsHeld(Key.Up) ? 1 : 0));
		if (move == Vector3.Zero)
		{
			return;
		}

		// Close in, the same key press should cover less ground, or the map bolts away from you.
		float speed = KeyPanSpeed * Mathf.Max(_distance / MaxDistance, 0.35f);
		_focus += move.Normalized() * speed * (float)delta;
		ClampFocus();
		UpdateCamera();
	}

	private static bool IsHeld(Key key) => Input.IsPhysicalKeyPressed(key);

	/// <summary>Pan with a right/middle drag, zoom on the wheel, a two-finger scroll or a pinch.
	/// Left clicks are the page's, for selecting provinces.</summary>
	public void HandleInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton button && button.Pressed)
		{
			if (button.ButtonIndex == MouseButton.WheelUp)
			{
				Zoom(-ZoomStep);
			}
			else if (button.ButtonIndex == MouseButton.WheelDown)
			{
				Zoom(ZoomStep);
			}
		}
		// A trackpad sends no wheel buttons at all: macOS gives anything with a gesture phase to
		// Godot as a pan or magnify event instead, which is why the wheel branch above never fires
		// on a laptop. Both are already the inverse of the finger movement, so scrolling up zooms in
		// exactly as the wheel does.
		else if (@event is InputEventPanGesture pan)
		{
			Zoom(pan.Delta.Y * PanGestureZoomStep);
		}
		else if (@event is InputEventMagnifyGesture magnify)
		{
			Zoom((1.0f - magnify.Factor) * MagnifyZoomStep);
		}
		else if (@event is InputEventMouseMotion motion &&
			(motion.ButtonMask & (MouseButtonMask.Right | MouseButtonMask.Middle)) != 0)
		{
			// Drag distance scales with zoom so the ground keeps up with the cursor at any height.
			float scale = PanSpeed * (_distance / MaxDistance) * 2.0f;
			_focus += new Vector3(-motion.Relative.X, 0, -motion.Relative.Y) * scale;
			ClampFocus();
			UpdateCamera();
		}
	}

	/// <summary>How close the camera is standing, as a multiple of how close it stands when pulled
	/// all the way out: 1 at the far end and near three at the near one. What is pinned to the
	/// ground rather than painted on it grows by this, so a mark over a county reads at the same
	/// size against the land whatever the lord has done with the wheel.</summary>
	public float Closeness => MaxDistance / _distance;

	// --- camera ---------------------------------------------------------------------------

	private void Zoom(float amount)
	{
		_distance = Mathf.Clamp(_distance + amount, MinDistance, MaxDistance);
		ClampFocus(); // a wider view needs more room around it
		UpdateCamera();
	}

	/// <summary>Keeps what the camera SEES on the map, not only the point it looks at. The camera
	/// leans in from the south, so the bottom of the screen is ground a little south of the focus and
	/// the top is ground a long way north of it: stopping the focus at the map's edge let the whole
	/// lower half of the screen hang out over open water. So the view's own reach — near edge, far
	/// edge and half its width at the near edge — is measured off the camera's height and tilt, and
	/// the focus is held where all of it stays over the map. A view bigger than the map is centred.</summary>
	/// <summary>The share of the screen's width the page's own furniture covers down the right-hand
	/// side (the sidebar). The view may go that much further east, or the counties along the east
	/// coast sit under the sidebar for good.</summary>
	public float CoveredRight { get; set; }

	private void ClampFocus()
	{
		float pitch = Mathf.DegToRad(-CameraPitchDegrees);
		float half = Mathf.DegToRad(_camera?.Fov ?? CameraFov) / 2f;
		float height = Mathf.Sin(pitch) * _distance;
		float back = Mathf.Cos(pitch) * _distance;
		float south = back - (height / Mathf.Tan(pitch + half));
		float north = pitch - half > 0.01f ? (height / Mathf.Tan(pitch - half)) - back : MapDepth;
		Vector2 screen = IsInsideTree() ? GetViewport().GetVisibleRect().Size : new Vector2(16, 9);
		float side = height / Mathf.Sin(pitch + half) * Mathf.Tan(half) * (screen.X / Mathf.Max(1f, screen.Y));

		_focus.X = Fit(_focus.X, (-MapWidth / 2) + side, (MapWidth / 2) - side + (2f * side * CoveredRight));

		// Taller than the map, the view is laid on its southern coast rather than centred: the near
		// ground fills the bottom half of the screen, so the sea past the south edge was most of what
		// the opening view showed. The north is let go further: what spills past the north edge is
		// far off and small, and holding all of it over land kept the northern counties at the top
		// of the screen, under the resource bar, however far the lord scrolled.
		float least = (-MapDepth / 2) + Mathf.Min(north, back * 0.25f);
		float most = (MapDepth / 2) - south;
		_focus.Z = least > most ? most : Mathf.Clamp(_focus.Z, least, most);
	}

	private static float Fit(float value, float least, float most) =>
		least > most ? (least + most) / 2f : Mathf.Clamp(value, least, most);

	/// <summary>Bumped whenever the camera moves. What is pinned to the ground over the viewport only
	/// has to be put back where the camera has it when this has changed.</summary>
	public int CameraMoved { get; private set; }

	private void UpdateCamera()
	{
		CameraMoved++;
		float pitch = Mathf.DegToRad(CameraPitchDegrees);
		_camera.Position = _focus + new Vector3(0, -Mathf.Sin(pitch) * _distance, Mathf.Cos(pitch) * _distance);
		_camera.RotationDegrees = new Vector3(CameraPitchDegrees, 0, 0);
		// Low down, the far edge of the screen is several camera distances off, not one and a half:
		// the shadows reach as far as the eye does, or they stop halfway up the screen.
		_sun.DirectionalShadowMaxDistance = _distance * Mathf.Lerp(ShadowReachLow, ShadowReach, ZoomedOut);
		_environment.FogDepthBegin = _distance * HazeFrom;
		_environment.FogDepthEnd = _distance * HazeTo;
		_clouds?.SetFocus(_focus);
		_rain?.SetZoom(_distance);

		int looking = _idImage == null ? -1 : ProvinceAtWorld(_focus);
		if (looking != _lookingAt)
		{
			_lookingAt = looking;
			LookedAt?.Invoke(looking);
		}
	}
}
