using System;
using Godot;

/// <summary>The crest's pause menu and the corner of the minimap: save, load, options, leaving, and
/// the toast that answers them.</summary>
public partial class CampaignMapPage
{
	private const string MainMenuScenePath = "res://scene/main-menu/main_menu.tscn";
	private const string LoadGameScenePath = "res://scene/load-game/load_game.tscn";
	private const string OptionsScenePath = "res://scene/options/options.tscn";
	private const string SelfScenePath = "res://scene/campaign-map/campaign_map.tscn";
	private const string LeaveFarewellPath = "res://assets/audio/quit-farewell.mp3";

	private const string SavingVoicePath = "res://assets/audio/saving-game.mp3";

	private const string SquareButtonPath = "res://assets/ui/button-square.png";

	private const float ToastFadeInSeconds = 0.15f;
	private const float ToastHoldSeconds = 1.6f;
	private const float ToastFadeOutSeconds = 0.6f;

	/// <summary>End Turn, the nav rail and the crest's pause menu: save, load, options and leaving.</summary>
	private void WireTurnAndMenu()
	{
		_turnTransition = GetNode<Control>("%TurnTransition");
		GoldTitle.Apply(GetNode<Label>("%TransitionSeason"));
		var endTurn = GetNode<Button>("%EndTurnButton");
		// Fires on the press, not the release. The turn curtain covers the whole screen and comes down
		// under a cursor that has not moved, which takes the button's hover away; when the curtain
		// lifts, Godot does not hand the hover back until the mouse moves again. A button that fires
		// on release checks it is still hovered as it lets go, finds it is not, and does nothing — so
		// a lord who ended his turn and pressed again without touching the mouse pressed a dead button,
		// as many times as he liked. AdvanceTurn already refuses a second turn while one is running.
		endTurn.ActionMode = BaseButton.ActionModeEnum.Press;
		endTurn.Pressed += AdvanceTurn;
		var navRail = GetNode<NavRail>("%NavRail");
		navRail.SectionChosen += ShowSection;

		// The crest is the pause menu: save, load, or leave the campaign.
		var gameMenu = GetNode<Control>("%GameMenu");
		GetNode<Button>("%MenuShieldButton").Pressed += () => gameMenu.Visible = !gameMenu.Visible;
		BuildMinimapButtons(gameMenu);
		GetNode<Button>("%SaveButton").Pressed += () =>
		{
			gameMenu.Visible = false; // out of the way, so the confirmation lands on the map itself
			if (TurnUnderway)
			{
				ShowSaveToast(MidTurnRefusal);
				return;
			}

			// Spoken on the press rather than after the write: the save itself is instant, and a
			// voice that starts once the work is already done is a voice answering nothing.
			Narrator.Say(SavingVoicePath);
			ShowSaveToast(SaveGame.Write(SaveGame.Of(Campaign.Name, _turnManager))
				? $"Saved · Turn {_turnManager.Turn}"
				: "The clerk could not write the save");
		};
		GetNode<Button>("%ResumeButton").Pressed += () => gameMenu.Visible = false;
		GetNode<Button>("%LoadButton").Pressed += () => ConfirmLeave(LoadGameScenePath);
		_leaveConfirm = GetNode<Control>("%LeaveConfirm");
		GetNode<Button>("%LeaveCancelButton").Pressed += () =>
		{
			_leaveConfirm.Visible = false;
			Narrator.Hush(); // staying: he doesn't get to finish the farewell
		};
		GetNode<Button>("%LeaveConfirmButton").Pressed += () => SceneRouter.GoTo(this, _leaveTarget);
		GetNode<Button>("%OptionsButton").Pressed += () =>
		{
			if (TurnUnderway)
			{
				gameMenu.Visible = false;
				ShowSaveToast(MidTurnRefusal);
				return;
			}

			// Options is its own scene, so the running campaign rides along in memory rather
			// than through a file, and comes back when Back returns here.
			SaveGame.Pending = SaveGame.Of(Campaign.Name, _turnManager);
			OptionsPage.ReturnScenePath = SelfScenePath;
			SceneRouter.GoTo(this, OptionsScenePath);
		};
		GetNode<Button>("%QuitButton").Pressed += () => ConfirmLeave(MainMenuScenePath);
	}

	/// <summary>The column of buttons beside the minimap: the places a lord returns to most, and the
	/// crest menu. Each wears the square button plate, with the glyph inset inside its frame.</summary>
	private void BuildMinimapButtons(Control gameMenu)
	{
		var column = GetNode<VBoxContainer>("%MinimapButtons");

		// Art where there is art, the drawn glyph where there is not yet: an icon file name here is
		// all it takes to replace one.
		(NavRailIcon.Glyph Glyph, string Art, string Tip, Action Open)[] entries =
		{
			(NavRailIcon.Glyph.Crown, null, "Court", () => ShowSection(NavRail.Section.Court)),
			(NavRailIcon.Glyph.Book, "castle", "Fortifications", () => ShowSection(NavRail.Section.Fortifications)),
			(NavRailIcon.Glyph.Helmet, "shield", "Military", () => ShowSection(NavRail.Section.Military)),
			(NavRailIcon.Glyph.Gear, null, "Menu", () => gameMenu.Visible = !gameMenu.Visible),
		};

		foreach ((NavRailIcon.Glyph glyph, string art, string tip, Action open) in entries)
		{
			// Square, touching, and sized so four of them stand as tall as the square map beside
			// them at its smallest; it grows to fill the frame, they stay centred on it.
			var button = new Button
			{
				CustomMinimumSize = new Vector2(57, 57),
				SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
				SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, // against the frame's right edge
				TooltipText = tip,
			};
			button.AddThemeStyleboxOverride("normal", SquareButtonPlate(Colors.White));
			button.AddThemeStyleboxOverride("hover", SquareButtonPlate(new Color(1.3f, 1.2f, 1.05f)));
			button.AddThemeStyleboxOverride("focus", SquareButtonPlate(new Color(1.3f, 1.2f, 1.05f)));
			button.AddThemeStyleboxOverride("pressed", SquareButtonPlate(new Color(0.78f, 0.76f, 0.72f)));

			button.Pressed += () => open();
			column.AddChild(button);

			Control icon = art == null
				? new NavRailIcon { Kind = glyph, MouseFilter = Control.MouseFilterEnum.Ignore }
				: new TextureRect
				{
					Texture = GD.Load<Texture2D>($"res://assets/ui/icons/{art}.png"),
					ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
					StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
					MouseFilter = Control.MouseFilterEnum.Ignore,
				};
			button.AddChild(icon);
			// An even inset all round, so the glyph sits centred in the button with room to breathe.
			icon.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			icon.OffsetLeft = 13;
			icon.OffsetTop = 13;
			icon.OffsetRight = -13;
			icon.OffsetBottom = -13;
		}
	}

	/// <summary>The square button plate. The art is square and so is the button, so it simply
	/// stretches — no nine-slice, nothing to keep in step with the button's size. The tint is what
	/// separates resting from hovered and pressed.</summary>
	private static StyleBoxTexture SquareButtonPlate(Color tint) => new()
	{
		Texture = GD.Load<Texture2D>(SquareButtonPath),
		ModulateColor = tint,
	};

	// Both ways out of a campaign drop everything since the last save, so neither goes through
	// on a single click. Options isn't in here: it comes back to this same run.
	private void ConfirmLeave(string scenePath)
	{
		_leaveTarget = scenePath;
		_leaveConfirm.Visible = true;
		// The old man asks it out loud while the panel asks it in writing. He speaks over the
		// campaign he is being left, so the line starts with the panel rather than after it.
		Narrator.Say(LeaveFarewellPath);
	}

	// A save is instant and silent otherwise: the toast holds long enough to be read, then
	// clears itself so nothing stays parked over the map.
	private void ShowSaveToast(string message)
	{
		var toast = GetNode<Control>("%SaveToast");
		var label = GetNode<Label>("%SaveToastLabel");
		label.Text = message;

		Tween tween = CreateTween();
		tween.TweenProperty(toast, "modulate:a", 1.0, ToastFadeInSeconds);
		tween.TweenInterval(ToastHoldSeconds);
		tween.TweenProperty(toast, "modulate:a", 0.0, ToastFadeOutSeconds);
	}
}
