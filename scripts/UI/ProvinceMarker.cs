using Godot;

/// <summary>A settlement glyph drawn at a province's seat (no icon asset exists for this yet).
/// Purely visual: the province's whole colored region is what's clickable (CampaignMapPage reads
/// the click off the ID map), so this ignores mouse input and just renders the dot, the capital
/// ring, and the selection ring. Position is set by the map page from the province's pixel
/// coordinates on the source map image, converted to screen space.</summary>
public partial class ProvinceMarker : Control
{
	private const float Radius = 9f;
	private const float CapitalRingRadius = 14f;
	private const float BorderWidth = 2f;

	private static readonly Color GoldRing = new("d8b26b");
	private static readonly Color SelectedRing = new("fff3d0");

	/// <summary>How tall the mark over the dot is drawn with the camera pulled all the way out, how
	/// far its foot clears the ring, and how much bigger it is allowed to get as the lord zooms in.
	/// The dot itself is a pin and stays the size it is; the mark is a thing standing on the land
	/// and grows with it, which is the only way it reads at both ends of the wheel.</summary>
	private const float BadgeSize = 44f;
	private const float BadgeLift = 4f;
	private const float BadgeGrowth = 2.5f;

	private float _badgeScale = 1f;
	private Texture2D _badge;
	private Color _fill;
	private bool _isCapital;
	private bool _isSelected;

	public void Configure(Color fill, bool isCapital)
	{
		_fill = fill;
		_isCapital = isCapital;
		Size = new Vector2(CapitalRingRadius, CapitalRingRadius) * 2f;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	/// <summary>Hangs a mark over the county — soldiers standing in it this season, and nothing else
	/// so far. Null takes it down again. Drawn outside the control's own box on purpose: the box is
	/// the dot, and the dot is what the map pins to the ground.</summary>
	public void ShowBadge(Texture2D badge)
	{
		if (_badge == badge)
		{
			return;
		}

		_badge = badge;
		QueueRedraw();
	}

	/// <summary>How much the camera's distance grows the mark. Set every frame from the map, so it
	/// only costs a redraw when it has actually moved.</summary>
	public void SetBadgeScale(float closeness)
	{
		closeness = Mathf.Clamp(closeness, 1f, BadgeGrowth);
		if (Mathf.Abs(closeness - _badgeScale) < 0.01f)
		{
			return;
		}

		_badgeScale = closeness;
		if (_badge != null)
		{
			QueueRedraw();
		}
	}

	public void SetSelected(bool isSelected)
	{
		_isSelected = isSelected;
		QueueRedraw();
	}

	public override void _Draw()
	{
		Vector2 center = Size / 2f;
		if (_isCapital)
		{
			DrawArc(center, CapitalRingRadius, 0, Mathf.Tau, 32, GoldRing, BorderWidth);
		}

		if (_isSelected)
		{
			DrawArc(center, Radius + 4f, 0, Mathf.Tau, 32, SelectedRing, BorderWidth);
		}

		DrawCircle(center, Radius, _fill);
		DrawArc(center, Radius, 0, Mathf.Tau, 24, GoldRing, BorderWidth);

		if (_badge != null)
		{
			float size = BadgeSize * _badgeScale;
			var mark = new Rect2(
				center.X - size / 2f,
				center.Y - CapitalRingRadius - BadgeLift - size,
				size,
				size);
			DrawTextureRect(_badge, mark, tile: false);
		}
	}
}
