#if TOOLS
using Godot;

/// <summary>Regenerates the campaign map from its JSON, without leaving the editor.
///
/// Open this file in Godot's script editor and run it (File &gt; Run, Cmd+Shift+X on macOS). It calls
/// tools/rebuild-map.sh, prints what the generator says in the Output panel, and then rescans the
/// filesystem so the new images are re-imported straight away — after that, Cmd+R on
/// scene/campaign-map/campaign_map.tscn shows them.
///
/// This step is needed because the game never reads map.json: it reads the images the generator
/// writes from it. Saving the JSON changes nothing on its own.</summary>
[Tool]
public partial class RebuildMap : EditorScript
{
	private const string ScriptPath = "res://tools/rebuild-map.sh";

	public override void _Run()
	{
		var output = new Godot.Collections.Array();
		int exitCode = OS.Execute("bash", new[] { ProjectSettings.GlobalizePath(ScriptPath) }, output, true);

		foreach (Variant line in output)
		{
			GD.Print(line.AsString().TrimEnd());
		}

		if (exitCode != 0)
		{
			GD.PushError($"RebuildMap: the generator failed (exit {exitCode}). See the Output panel above.");
			return;
		}

		// Pick up the regenerated images now, instead of waiting for the editor to notice them.
		EditorInterface.Singleton.GetResourceFilesystem().Scan();
		GD.Print("RebuildMap: images re-imported. Cmd+R on the campaign map scene to see them.");
	}
}
#endif
