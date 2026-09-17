using Godot;

/// <summary>Invisible hit-region over the realm crest baked into the top bar's own art:
/// brightens the separate %ShieldGlowOverlay sprite on hover. What the click opens is wired
/// by the page that owns the button (CampaignMapPage opens the game menu).</summary>
public partial class ShieldMenuButton : Button
{
	private const float GlowFadeSeconds = 0.15f;

	public override void _Ready()
	{
		MouseEntered += () => SetGlow(true);
		MouseExited += () => SetGlow(false);
	}

	private void SetGlow(bool isOn)
	{
		var glow = GetNode<TextureRect>("%ShieldGlowOverlay");
		glow.CreateTween().TweenProperty(glow, "modulate:a", isOn ? 0.6 : 0.0, GlowFadeSeconds);
	}
}
