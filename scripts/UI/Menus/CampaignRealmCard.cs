using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

/// <summary>One campaign on the selection page, on the game's painted table: its realm's view
/// between two of its lords' banners, its name, and the lords who will be met over its levels, each
/// in the gilt frame the diplomacy table hangs a lord in with his shield under him. A campaign not
/// yet made stands greyed and locked.
///
/// The lords with a film of themselves stir on it when the pointer comes to the card, once, and hold
/// their last look, and embers rise behind the card while it stays there.</summary>
public partial class CampaignRealmCard : PanelContainer
{
	/// <summary>A lord on the card: his face, his film where he has one, his shield, and his colour.
	/// The player's own lord stands first and larger than the lords he will fight.</summary>
	public record LordFace(string Key, string Face, string Film, string Shield, Color Colour, bool IsYours = false);

	// Wide enough for five lords in a row under the view.
	private const int CardWidth = 738;
	private const int ViewHeight = 288;
	private const int FaceSide = 108;
	private const int ShieldSide = 43;
	private const int YourSide = 151;
	private const int YourShieldSide = 56;
	private const int YoursApart = 10;
	private const float BannerHigh = 225f;
	private static readonly Color Locked = new(0.45f, 0.45f, 0.45f);
	private static readonly Color Ember = new("ff8a2a");

	/// <summary>Raised when an open campaign is pressed.</summary>
	public event Action Chosen;

	private readonly List<(Control Face, string Film)> _faces = new();
	private bool _isOpen;
	private string _tale = "";
	private int _maps;

	public static CampaignRealmCard Make(string title, string view, IReadOnlyList<LordFace> lords,
		bool isOpen, (Color Colour, string Lord) left, (Color Colour, string Lord) right, string tale = "", int maps = 0)
	{
		var card = new CampaignRealmCard { _isOpen = isOpen, CustomMinimumSize = new Vector2(CardWidth, 0) };
		card.AddThemeStyleboxOverride("panel", Chrome.PaintedStyle());
		card.MouseFilter = MouseFilterEnum.Stop;
		card._tale = tale;
		card._maps = maps;
		card.Build(title, view, lords, left, right);
		return card;
	}

	private const int TitleSize = 24;
	private const int TitleDrop = 7;

	private void Build(string title, string view, IReadOnlyList<LordFace> lords, (Color Colour, string Lord) left, (Color Colour, string Lord) right)
	{
		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 12);
		AddChild(column);

		// The name on the frame's ribbon, as every painted table carries its title — set a little
		// small and a little lower than the frame's own margin puts it, so it sits in the middle of the
		// blue rather than crowding its lower gilt.
		Label name = Chrome.Line(title, TitleSize, Chrome.Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(name);
		var onRibbon = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		onRibbon.AddThemeConstantOverride("margin_top", TitleDrop);
		onRibbon.AddChild(name);
		column.AddChild(onRibbon);

		// The realm itself, its lords' banners hung either side of it.
		var scene = new Control { CustomMinimumSize = new Vector2(0, ViewHeight), ClipContents = true, MouseFilter = MouseFilterEnum.Ignore };
		var picture = new TextureRect
		{
			Texture = GD.Load<Texture2D>(view),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		picture.SetAnchorsPreset(LayoutPreset.FullRect);
		scene.AddChild(picture);
		foreach ((bool isRight, (Color colour, string lord)) in new[] { (false, left), (true, right) })
		{
			Control banner = DiplomacyArt.Banner(isRight, BannerHigh, colour, lord);
			banner.AnchorLeft = banner.AnchorRight = isRight ? 1f : 0f;
			banner.OffsetLeft = isRight ? -banner.CustomMinimumSize.X : 0f;
			banner.OffsetRight = isRight ? 0f : banner.CustomMinimumSize.X;
			banner.OffsetBottom = BannerHigh;
			scene.AddChild(banner);
		}

		// The frame and its inset stop the mouse by default, and a pointer on the picture would then
		// have left the card: its lords stirring and its embers going out halfway across it.
		PanelContainer framed = Chrome.Framed(scene, 2);
		framed.MouseFilter = MouseFilterEnum.Ignore;
		framed.GetChild<Control>(0).MouseFilter = MouseFilterEnum.Ignore;
		column.AddChild(framed);

		if (_isOpen)
		{
			var faces = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
			faces.AddThemeConstantOverride("separation", 12);
			foreach (LordFace lord in lords)
			{
				faces.AddChild(Lord(lord));
				if (lord.IsYours)
				{
					// A little more room between you and those you will fight.
					faces.AddChild(new Control { CustomMinimumSize = new Vector2(YoursApart, 0), MouseFilter = MouseFilterEnum.Ignore });
				}
			}

			column.AddChild(faces);

			// What the campaign is, in a line, and how long: so the card says more than who is in it.
			if (_tale.Length > 0)
			{
				Label tale = Chrome.Line(_tale, 17, Chrome.Soft);
				tale.HorizontalAlignment = HorizontalAlignment.Center;
				tale.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				tale.CustomMinimumSize = new Vector2(560, 0);
				var told = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
				told.AddChild(tale);
				column.AddChild(told);
			}

			var tally = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
			tally.AddThemeConstantOverride("separation", 28);
			if (_maps > 0)
			{
				tally.AddChild(Tally("castle", $"{_maps} Maps"));
			}

			tally.AddChild(Tally("helmet", $"{lords.Count(lord => !lord.IsYours)} Lords"));
			column.AddChild(tally);
			MouseEntered += Stir;
			Embers embers = Embers.Behind(this, Ember);
			MouseEntered += () => embers.Glow(true);
			MouseExited += () => embers.Glow(false);
			return;
		}

		// Not made yet: greyed, and said so where its lords would stand.
		Modulate = Locked;
		Label locked = Chrome.Line("Locked", 28, Chrome.Cream);
		locked.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(locked);
		Label soon = Chrome.Line("Coming Soon", 18, Chrome.Soft);
		soon.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(soon);
	}

	private static Control Tally(string icon, string text)
	{
		var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		line.AddThemeConstantOverride("separation", 8);
		line.AddChild(Chrome.Icon(icon, 26));
		Label said = Chrome.Line(text, 22, Chrome.Cream);
		said.VerticalAlignment = VerticalAlignment.Center;
		line.AddChild(said);
		return line;
	}

	/// <summary>A lord in his gilt frame, his shield over the foot of it.</summary>
	private Control Lord(LordFace lord)
	{
		var face = new Control { MouseFilter = MouseFilterEnum.Ignore };
		var still = new TextureRect
		{
			Texture = GD.Load<Texture2D>(lord.Face),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		still.SetAnchorsPreset(LayoutPreset.FullRect);
		face.AddChild(still);
		_faces.Add((face, lord.Film));

		int side = lord.IsYours ? YourSide : FaceSide;
		int shieldSide = lord.IsYours ? YourShieldSide : ShieldSide;
		// Stood on one line, the foot of every frame level with the others, so the larger face of the
		// player's own lord rises above the row rather than hanging below it.
		var stand = new Control
		{
			CustomMinimumSize = new Vector2(side, side + (shieldSide / 2f)),
			SizeFlagsVertical = SizeFlags.ShrinkEnd,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		stand.AddChild(DiplomacyArt.Framed(face, side));
		var shield = new TextureRect
		{
			Texture = Shield(lord.Shield),
			Modulate = lord.Shield.Length == 0 ? lord.Colour.Lightened(0.2f) : Colors.White,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Position = new Vector2((side - shieldSide) / 2f, side - (shieldSide / 2f)),
			Size = new Vector2(shieldSide, shieldSide),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		stand.AddChild(shield);
		return stand;
	}

	/// <summary>A realm's painted crest, cut the way the sidebar cuts it, or the plain shield.</summary>
	private static Texture2D Shield(string painted) => painted.Length > 0 && ResourceLoader.Exists(painted)
		? new AtlasTexture { Atlas = GD.Load<Texture2D>(painted), Region = ProvinceSidebar.CrestRegion }
		: GD.Load<Texture2D>($"{Chrome.IconDirectory}/shield.png");

	/// <summary>Each lord with a film plays it once over his painting: a fresh player every time, since
	/// a stopped one will not start the same film again.</summary>
	private void Stir()
	{
		foreach ((Control face, string film) in _faces)
		{
			if (face.GetChildCount() > 1)
			{
				face.GetChild(1).QueueFree();
			}

			if (film.Length > 0 && LordPortrait.Moving(film, 1) is Control moving)
			{
				moving.CustomMinimumSize = Vector2.Zero;
				moving.SetAnchorsPreset(LayoutPreset.FullRect);
				face.AddChild(moving);
			}
		}
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (_isOpen && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false })
		{
			Chosen?.Invoke();
		}
	}
}
