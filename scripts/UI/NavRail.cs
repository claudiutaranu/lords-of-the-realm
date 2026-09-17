using System;
using Godot;

/// <summary>Vertical rail of destination buttons on the left edge of the campaign map.
/// It reports the picked section and nothing more, so it stays independent of whatever
/// screen mounts it and of how that screen chooses to navigate.</summary>
public partial class NavRail : PanelContainer
{
	public enum Section
	{
		Chronicle,
		Military,
		Buildings,
		Court,
		Battles,
	}

	public event Action<Section> SectionChosen;

	public override void _Ready()
	{
		Wire("%ChronicleButton", Section.Chronicle);
		Wire("%MilitaryButton", Section.Military);
		Wire("%BuildingsButton", Section.Buildings);
		Wire("%CourtButton", Section.Court);
		Wire("%BattlesButton", Section.Battles);
	}

	private void Wire(string buttonPath, Section section)
	{
		GetNode<Button>(buttonPath).Pressed += () => SectionChosen?.Invoke(section);
	}
}
