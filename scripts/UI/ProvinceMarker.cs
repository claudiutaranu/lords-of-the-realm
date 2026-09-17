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
	}
}
