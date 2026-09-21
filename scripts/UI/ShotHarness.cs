using Godot;

/// <summary>Throwaway: the campaign map, stripped back.</summary>
public partial class ShotHarness : Control
{
	public override async void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(GD.Load<PackedScene>("res://scene/campaign-map/campaign_map.tscn").Instantiate());
		await Settle(120);
		RenderingServer.Singleton.ForceDraw();
		GetViewport().GetTexture().GetImage().SavePng("user://harta.png");
		GD.Print("gata");
		GetTree().Quit();
	}

	private async System.Threading.Tasks.Task Settle(int frames)
	{
		for (int frame = 0; frame < frames; frame++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}

		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
	}
}
