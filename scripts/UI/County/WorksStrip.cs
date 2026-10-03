using Godot;

/// <summary>The work in hand that takes seasons, under the county's stores as Lords of the Realm
/// keeps it: the masons on the wall and the reclaimers on ruined ground, each with its hands and
/// how many seasons it has left at them. Hidden when neither is under way.</summary>
public partial class WorksStrip : VBoxContainer
{
	private Label _castle;
	private Label _reclaim;
	private Control _castleRow;
	private Control _reclaimRow;

	public override void _Ready()
	{
		AddThemeConstantOverride("separation", 4);
		(_castleRow, _castle) = Row("castle");
		(_reclaimRow, _reclaim) = Row("pitchfork");
	}

	public void Show(ProvinceEconomy p, GameBalance b)
	{
		bool isBuilding = p != null && p.Building.Length > 0;
		bool isReclaiming = p != null && Husbandry.ReclaimLeft(p, b) > 0;
		Visible = isBuilding || isReclaiming;
		_castleRow.Visible = isBuilding;
		_reclaimRow.Visible = isReclaiming;

		if (isBuilding)
		{
			// At the masons on it now, not the gang the order was quoted for: a lord who moves men off
			// the scaffolding should see the wall go further off.
			int seasons = p.BuildWorkers > 0 ? Mathf.CeilToInt((float)p.BuildLeft / p.BuildWorkers) : -1;
			Fill(_castle, $"{p.BuildWorkers:N0} masons", seasons);
		}

		if (isReclaiming)
		{
			Fill(_reclaim, $"{p.ReclaimWorkers:N0} reclaiming", Husbandry.SeasonsToReclaim(p, b));
		}
	}

	/// <summary>Seasons left in green, or in red when nobody is on the work and it will never end.</summary>
	private static void Fill(Label line, string hands, int seasons)
	{
		line.Text = seasons < 0 ? $"{hands} · nobody on it"
			: seasons == 1 ? $"{hands} · 1 season" : $"{hands} · {seasons} seasons";
		line.AddThemeColorOverride("font_color", seasons < 0 ? Stroke.Wanting : Chrome.Gain);
	}

	private (Control, Label) Row(string icon)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(Chrome.Icon(icon, 24));
		Label line = Chrome.Line("", 15, Chrome.Gain);
		line.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(line);
		AddChild(row);
		return (row, line);
	}
}
