using System;
using Godot;

/// <summary>Vertical rail of destination buttons on the left edge of the campaign map.
/// It reports the picked section and nothing more, so it stays independent of whatever
/// screen mounts it and of how that screen chooses to navigate.</summary>
public partial class NavRail : PanelContainer
{
	// In the order they stand along the bar.
	public enum Section
	{
		Buildings,
		Chronicle,
		Trade,
		Military,
		Court,
	}

	public event Action<Section> SectionChosen;

	public override void _Ready()
	{
		Wire("%BuildingsButton", Section.Buildings);
		Wire("%ChronicleButton", Section.Chronicle);
		Wire("%TradeButton", Section.Trade);
		Wire("%MilitaryButton", Section.Military);
		Wire("%CourtButton", Section.Court);
	}

	private void Wire(string buttonPath, Section section)
	{
		GetNode<Button>(buttonPath).Pressed += () => SectionChosen?.Invoke(section);
	}
}
