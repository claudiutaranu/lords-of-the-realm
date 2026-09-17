using System;
using Godot;

/// <summary>One campaign entry in the selection grid. The title is plain outlined
/// text overlaid on the portrait (no backing plate), so it reads over any of the
/// baked (frame + character art + map) portraits regardless of their art. A
/// two-layer sandstorm (slow hazy far layer, faster grit near layer) rises behind
/// the card while hovered, tinted to the realm's accent, and the same accent trims
/// the info panel so each card reads as its own realm.</summary>
public partial class CampaignCard : PanelContainer
{
	private const float GlowFadeSeconds = 0.1f;
	private const float NearAlpha = 0.85f;
	private const float FarAlpha = 0.55f;

	public event Action Selected;

	public override void _Ready()
	{
		MouseEntered += () => SetGlow(true);
		MouseExited += () => SetGlow(false);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
		{
			Selected?.Invoke();
		}
	}

	public void SetData(Texture2D portrait, string title, int lords, Color accent)
	{
		GetNode<TextureRect>("%Portrait").Texture = portrait;
		GetNode<Label>("%TitleOverlay").Text = title;
		GetNode<Label>("%Lords").Text = $"Lords: {lords}";
		ApplyAccent(accent);
	}

	private void ApplyAccent(Color accent)
	{
		GetNode<CpuParticles2D>("%Near").Color = new Color(accent, NearAlpha);
		GetNode<CpuParticles2D>("%Far").Color = new Color(accent, FarAlpha);

		var infoPanel = GetNode<PanelContainer>("Content/InfoPanel");
		var infoStyle = (StyleBoxFlat)infoPanel.GetThemeStylebox("panel").Duplicate();
		infoStyle.BorderWidthTop = 3;
		infoStyle.BorderColor = accent;
		infoPanel.AddThemeStyleboxOverride("panel", infoStyle);
	}

	private void SetGlow(bool isOn)
	{
		GetNode<CpuParticles2D>("%Near").Emitting = isOn;
		GetNode<CpuParticles2D>("%Far").Emitting = isOn;

		var glow = GetNode<Node2D>("%Glow");
		glow.CreateTween().TweenProperty(glow, "modulate:a", isOn ? 1.0 : 0.0, GlowFadeSeconds);
	}
}
