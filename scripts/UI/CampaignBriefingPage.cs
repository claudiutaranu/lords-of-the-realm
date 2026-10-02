using Godot;

/// <summary>Shown after picking a campaign card: your lord, the rival, the contested
/// territory, and the campaign's stats/objectives, before play begins. Content here is
/// hardcoded to the Royal Crown vs. Northern Watch matchup for now, the only one with a
/// map and rival briefing text ready; generalize once the other four have theirs.</summary>
public partial class CampaignBriefingPage : Control
{
	private const string CampaignScenePath = "res://scene/campaign/campaign.tscn";
	private const string CampaignMapScenePath = "res://scene/campaign-map/campaign_map.tscn";
	private const string LoadingScenePath = "res://scene/loading/loading.tscn";
	private const string DifficultyOptionPath = "%DifficultyOption";
	private const float FadeInSeconds = 0.4f;
	private const float PanelRevealDelaySeconds = 0.3f;
	private const float PanelFadeSeconds = 0.3f;
	private const float PanelStaggerSeconds = 0.08f;
	/// <summary>Both lords' faces the same size and in the same gilt edge, the two of them squared up.</summary>
	private const int PortraitSide = 300;

	/// <summary>How far the words under each face keep from the panel's sides.</summary>
	private const int TextInset = 26;
	private const int HeadingDrop = 14;
	private const string YourLordFilm = "lord.ogv";

	public override void _Ready()
	{
		var background = GetNode<VideoStreamPlayer>("%Background");
		background.Finished += background.Play;

		// The stats bar already showed a difficulty, as text nobody could change. It is the setting
		// itself now: one place showing it and one place choosing it, rather than a second screen to
		// keep in step with a number the player thought he had already picked. Filled from the enum
		// and not from a list typed out here, so another setting is one member and not one member
		// plus a list somebody forgets.
		var difficulty = GetNode<OptionButton>(DifficultyOptionPath);
		foreach (Difficulty setting in System.Enum.GetValues<Difficulty>())
		{
			difficulty.AddItem(setting.ToString());
		}

		difficulty.Selected = (int)Campaign.Difficulty;
		difficulty.TooltipText = "How well the lords who are not you will play their own counties";
		difficulty.ItemSelected += chosen => Campaign.Difficulty = (Difficulty)chosen;

		Tally();
		ShowYours();
		ShowRival();
		Inset("MainRow/YourLordPanel/YourLordContent");
		Inset("MainRow/RivalPanel/RivalContent");

		GetNode<Button>("%BackButton").Pressed += () => SceneRouter.GoTo(this, CampaignScenePath);
		GetNode<Button>("%StartCampaignButton").Pressed += () =>
		{
			LoadingPage.TargetScenePath = CampaignMapScenePath;
			SceneRouter.GoTo(this, LoadingScenePath);
		};

		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1.0, FadeInSeconds);

		// Panels stay hidden through the page fade, then cascade in left to right.
		var panels = new Control[]
		{
			GetNode<Control>("MainRow/YourLordPanel"),
			GetNode<Control>("MainRow/MapPanel"),
			GetNode<Control>("MainRow/RivalPanel"),
			GetNode<Control>("StatsBar"),
		};
		foreach (Control panel in panels)
		{
			panel.Modulate = new Color(1, 1, 1, 0);
		}

		Tween panelTween = CreateTween();
		panelTween.TweenInterval(PanelRevealDelaySeconds);
		panelTween.TweenProperty(panels[0], "modulate:a", 1.0, PanelFadeSeconds);
		for (int i = 1; i < panels.Length; i++)
		{
			panelTween.Parallel().TweenProperty(panels[i], "modulate:a", 1.0, PanelFadeSeconds).SetDelay(PanelStaggerSeconds * i);
		}
	}

	/// <summary>Gives the words in a lord's column room at the sides (wrapped to the panel's edge they
	/// ran to its border) and its heading room above.</summary>
	private void Inset(string column)
	{
		// And a little air over the heading, which sat on the panel's top edge.
		var air = new Control { CustomMinimumSize = new Vector2(0, HeadingDrop) };
		GetNode(column).AddChild(air);
		GetNode(column).MoveChild(air, 0);
		foreach (Node child in GetNode(column).GetChildren())
		{
			if (child is Label { AutowrapMode: not TextServer.AutowrapMode.Off } words)
			{
				var margin = new MarginContainer();
				margin.AddThemeConstantOverride("margin_left", TextInset);
				margin.AddThemeConstantOverride("margin_right", TextInset);
				words.AddSibling(margin);
				words.Reparent(margin);
			}
		}
	}

	/// <summary>The player's own lord, moving, where the campaign has a film of him.</summary>
	private void ShowYours()
	{
		if (LordPortrait.Moving(Campaign.Asset(YourLordFilm), PortraitSide) is Control face)
		{
			var still = GetNode<Control>("MainRow/YourLordPanel/YourLordContent/Portrait");
			still.AddSibling(LordPortrait.Framed(face));
			still.Visible = false;
		}
	}

	/// <summary>The rival's face, moving, where his lord has been painted: the first realm on the
	/// campaign that names a lord. The still stays where there is no portrait of him yet.</summary>
	private void ShowRival()
	{
		Godot.Collections.Dictionary realms = GD.Load<Json>(Campaign.Data("provinces.json")).Data
			.AsGodotDictionary()["realms"].AsGodotDictionary();
		foreach (Variant realm in realms.Values)
		{
			if (realm.AsGodotDictionary().TryGetValue("lord", out Variant key)
				&& LordPortrait.Of(Lords.Find(key.AsString()), PortraitSide) is Control face)
			{
				var still = GetNode<Control>("MainRow/RivalPanel/RivalContent/Portrait");
				still.AddSibling(face);
				still.Visible = false;
				return;
			}
		}
	}

	/// <summary>The stats bar read off the campaign itself, not typed into the scene: the lords who
	/// hold land, the counties that can change hands, the gold the player's seat opens with, and
	/// the seat the rival rules from.</summary>
	private void Tally()
	{
		Godot.Collections.Dictionary data = GD.Load<Json>(Campaign.Data("provinces.json")).Data.AsGodotDictionary();
		string player = data["player"].AsString();
		string unclaimed = data["unclaimed"].AsString();
		var lords = new System.Collections.Generic.HashSet<string>();
		int counties = 0;
		int gold = 0;
		string rivalSeat = "";
		foreach (Variant entry in data["provinces"].AsGodotArray())
		{
			Godot.Collections.Dictionary county = entry.AsGodotDictionary();
			string realm = county["realm"].AsString();
			string economy = county.TryGetValue("economy", out Variant file) ? file.AsString() : "";
			if (economy.Length == 0)
			{
				continue; // scenery: drawn on the map, not a county anybody can hold
			}

			counties++;
			if (realm != unclaimed)
			{
				lords.Add(realm);
			}

			if (realm == player)
			{
				gold += GD.Load<ProvinceDefinition>(Campaign.Data($"provinces/{economy}.tres")).InitialGold;
			}
			else if (realm != unclaimed && rivalSeat.Length == 0)
			{
				rivalSeat = county["name"].AsString();
			}
		}

		const string Stats = "StatsBar/Center/StatsRow";
		GetNode<Label>($"{Stats}/Lords/Value").Text = lords.Count.ToString();
		GetNode<Label>($"{Stats}/EnemyLords/Value").Text = (lords.Count - 1).ToString();
		GetNode<Label>($"{Stats}/Provinces/Value").Text = counties.ToString();
		GetNode<Label>($"{Stats}/Gold/Text/Value").Text = gold.ToString("N0");
		GetNode<Label>($"{Stats}/Objectives/Value").Text =
			$"Take {rivalSeat}, Lord Alaric's seat  ·  Hold all {counties} counties";
	}
}
