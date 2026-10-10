using Godot;

/// <summary>Shown before a map is played — after picking a campaign, and after winning the map
/// before it: your lord, every rival lord on it, the contested territory, and its stats and
/// objectives, all read off the map's own provinces.json and the lords' roster.</summary>
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
	private const string BriefingArt = "briefing-map.png";

	/// <summary>How large each rival's face is when more than one shares the column.</summary>
	private const int SharedPortraitSide = 170;
	private const int CompactPortraitSide = 112;
	private const string RivalColumn = "MainRow/RivalPanel/RivalContent";

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

		Godot.Collections.Dictionary data = GD.Load<Json>(Campaign.Data("provinces.json")).Data.AsGodotDictionary();
		Tally(data);
		ShowYours();
		ShowRivals(Campaign.Seated(data["realms"].AsGodotDictionary(), data["player"].AsString()));
		ShowMap();
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

	/// <summary>The lord the player chose, moving, under his realm's name and his motto.</summary>
	private void ShowYours()
	{
		if (Lords.Find(Campaign.Player) is not Lord yours)
		{
			return;
		}

		const string Column = "MainRow/YourLordPanel/YourLordContent";
		GetNode<Label>($"{Column}/RealmName").Text = yours.Realm;
		GetNode<Label>($"{Column}/Tagline").Text = yours.Motto;
		if (LordPortrait.Of(yours, PortraitSide) is Control face)
		{
			var still = GetNode<Control>($"{Column}/Portrait");
			still.AddSibling(face);
			still.Visible = false;
		}
	}

	/// <summary>The map's own painting where it has one; the scene's is the first map's.</summary>
	private void ShowMap()
	{
		if (ResourceLoader.Exists(Campaign.Asset(BriefingArt)))
		{
			GetNode<TextureRect>("MainRow/MapPanel/Map").Texture = GD.Load<Texture2D>(Campaign.Asset(BriefingArt));
		}
	}

	/// <summary>Every lord who holds a realm on the map, in the order the map lists them: his face
	/// (moving where he has been filmed), his name, his realm, his motto and what the map says of
	/// him. The scene lays out one; each lord after the first is given a copy of it, and the faces
	/// shrink so two still fit the column.</summary>
	private void ShowRivals(Godot.Collections.Dictionary realms)
	{
		var rivals = new System.Collections.Generic.List<(Lord Lord, Godot.Collections.Dictionary Realm)>();
		foreach (Variant realm in realms.Values)
		{
			Godot.Collections.Dictionary fields = realm.AsGodotDictionary();
			if (fields.TryGetValue("lord", out Variant key) && Lords.Find(key.AsString()) is Lord lord)
			{
				rivals.Add((lord, fields));
			}
		}

		// Three lords or more do not fit one under another: they stand two to a row, each his face, his
		// name and his realm, without the motto and the word on him.
		bool isCompact = rivals.Count > 2;
		string[] parts = isCompact
			? new[] { "Portrait", "RivalName", "RivalRealm" }
			: new[] { "Portrait", "RivalName", "RivalRealm", "RivalQuote", "RivalDescription" };
		Node column = GetNode(RivalColumn);
		GridContainer grid = null;
		if (isCompact)
		{
			GetNode<Control>($"{RivalColumn}/RivalQuote").Visible = false;
			GetNode<Control>($"{RivalColumn}/RivalDescription").Visible = false;
			grid = new GridContainer { Columns = 2 };
			grid.AddThemeConstantOverride("h_separation", 10);
			grid.AddThemeConstantOverride("v_separation", 12);
			column.AddChild(grid);
		}

		int side = isCompact ? CompactPortraitSide : rivals.Count > 1 ? SharedPortraitSide : PortraitSide;
		var originals = new System.Collections.Generic.Dictionary<string, Control>();
		foreach (string part in parts)
		{
			originals[part] = GetNode<Control>($"{RivalColumn}/{part}");
		}

		for (int i = 0; i < rivals.Count; i++)
		{
			var block = new System.Collections.Generic.Dictionary<string, Control>();
			var cell = isCompact ? new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill } : null;
			grid?.AddChild(cell);
			foreach (string part in parts)
			{
				Control original = originals[part];
				block[part] = i == 0 ? original : (Control)original.Duplicate();
				if (cell != null)
				{
					if (i == 0)
					{
						original.Reparent(cell, false);
					}
					else
					{
						cell.AddChild(block[part]);
					}

					if (block[part] is Label words)
					{
						words.AddThemeFontSizeOverride("font_size", 14);
					}
				}
				else if (i > 0)
				{
					column.AddChild(block[part]);
				}
			}

			Introduce(block, rivals[i].Lord, rivals[i].Realm, side);
		}
	}

	private static void Introduce(System.Collections.Generic.Dictionary<string, Control> block, Lord lord,
		Godot.Collections.Dictionary realm, int side)
	{
		var name = (Label)block["RivalName"];
		var realmName = (Label)block["RivalRealm"];
		name.Text = lord.Name.Length > 0 ? lord.Name : lord.Title;
		realmName.Text = realm["name"].AsString();
		if (block.TryGetValue("RivalQuote", out Control quote) && block.TryGetValue("RivalDescription", out Control said))
		{
			var motto = (Label)quote;
			var about = (Label)said;
			motto.Text = $"\"{lord.Motto}\"";
			motto.Visible = lord.Motto.Length > 0;
			about.Text = realm.TryGetValue("about", out Variant word) ? word.AsString() : "";
			about.Visible = about.Text.Length > 0;
		}

		Control still = block["Portrait"];
		still.CustomMinimumSize = new Vector2(0, side);
		if (LordPortrait.Of(lord, side) is Control face)
		{
			still.AddSibling(face);
			still.Visible = false;
		}
	}

	/// <summary>The stats bar read off the campaign itself, not typed into the scene: the lords who
	/// hold land, the counties that can change hands, the gold the player's seat opens with, and
	/// the seats the rivals rule from.</summary>
	private void Tally(Godot.Collections.Dictionary data)
	{
		string player = data["player"].AsString();
		string unclaimed = data["unclaimed"].AsString();
		var lords = new System.Collections.Generic.HashSet<string>();
		int counties = 0;
		int gold = 0;
		var rivalSeats = new System.Collections.Generic.List<string>();
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
			else if (realm != unclaimed && county.TryGetValue("capital", out Variant seat) && seat.AsBool())
			{
				rivalSeats.Add(county["name"].AsString());
			}
		}

		const string Stats = "StatsBar/Center/StatsRow";
		GetNode<Label>($"{Stats}/Lords/Value").Text = lords.Count.ToString();
		GetNode<Label>($"{Stats}/EnemyLords/Value").Text = (lords.Count - 1).ToString();
		GetNode<Label>($"{Stats}/Provinces/Value").Text = counties.ToString();
		GetNode<Label>($"{Stats}/Gold/Text/Value").Text = gold.ToString("N0");
		// One rival is named with his seat, as the first map always was; more are only their seats,
		// or the line runs off the bar.
		string take = rivalSeats.Count == 1 && RivalNamed(Campaign.Seated(data["realms"].AsGodotDictionary(), player)) is { Length: > 0 } rival
			? $"{rivalSeats[0]}, {rival}'s seat"
			: string.Join(" and ", rivalSeats);
		GetNode<Label>($"{Stats}/Objectives/Value").Text = $"Take {take}  ·  Hold all {counties} counties";
	}

	/// <summary>The name of the first lord the map seats, or nothing if he has none.</summary>
	private static string RivalNamed(Godot.Collections.Dictionary realms)
	{
		foreach (Variant realm in realms.Values)
		{
			if (realm.AsGodotDictionary().TryGetValue("lord", out Variant key) && Lords.Find(key.AsString()) is Lord lord)
			{
				return lord.Name;
			}
		}

		return "";
	}
}
