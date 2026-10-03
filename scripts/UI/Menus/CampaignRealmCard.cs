using System;
using System.Collections.Generic;
using Godot;

/// <summary>One campaign on the selection page, on the game's painted table: its realm's view
/// between two of its lords' banners, its name, and the lords who will be met over its levels, each
/// in the gilt frame the diplomacy table hangs a lord in with his shield under him. A campaign not
/// yet made stands greyed and locked.
///
/// The lords with a film of themselves stir on it when the pointer comes to the card, once, and hold
/// their last look.</summary>
public partial class CampaignRealmCard : PanelContainer
{
	/// <summary>A lord on the card: his face, his film where he has one, his shield, and his colour.</summary>
	public record LordFace(string Face, string Film, string Shield, Color Colour);

	private const int ViewHeight = 290;
	private const int FaceSide = 128;
	private const int ShieldSide = 44;
	private const float BannerHigh = 250f;
	private static readonly Color Locked = new(0.45f, 0.45f, 0.45f);

	/// <summary>Raised when an open campaign is pressed.</summary>
	public event Action Chosen;

	private readonly List<(Control Face, string Film)> _faces = new();
	private bool _isOpen;

	public static CampaignRealmCard Make(string title, string view, IReadOnlyList<LordFace> lords,
		bool isOpen, Color left, Color right)
	{
		var card = new CampaignRealmCard { _isOpen = isOpen, CustomMinimumSize = new Vector2(680, 0) };
		card.AddThemeStyleboxOverride("panel", Chrome.PaintedStyle());
		card.MouseFilter = MouseFilterEnum.Stop;
		card.Build(title, view, lords, left, right);
		return card;
	}

	private void Build(string title, string view, IReadOnlyList<LordFace> lords, Color left, Color right)
	{
		var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		column.AddThemeConstantOverride("separation", 12);
		AddChild(column);

		// The name on the frame's ribbon, as every painted table carries its title.
		Label name = Chrome.Line(title, 30, Chrome.Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(name);
		column.AddChild(name);

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
		foreach ((bool isRight, Color colour) in new[] { (false, left), (true, right) })
		{
			Control banner = DiplomacyArt.Banner(isRight, BannerHigh, colour);
			banner.AnchorLeft = banner.AnchorRight = isRight ? 1f : 0f;
			banner.OffsetLeft = isRight ? -banner.CustomMinimumSize.X : 0f;
			banner.OffsetRight = isRight ? 0f : banner.CustomMinimumSize.X;
			banner.OffsetBottom = BannerHigh;
			scene.AddChild(banner);
		}

		column.AddChild(Chrome.Framed(scene, 2));

		if (_isOpen)
		{
			var faces = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
			faces.AddThemeConstantOverride("separation", 18);
			foreach (LordFace lord in lords)
			{
				faces.AddChild(Lord(lord));
			}

			column.AddChild(faces);
			Label count = Chrome.Line($"{lords.Count} Lords", 22, Chrome.Cream);
			count.HorizontalAlignment = HorizontalAlignment.Center;
			column.AddChild(count);
			MouseEntered += Stir;
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

		var stand = new Control { CustomMinimumSize = new Vector2(FaceSide, FaceSide + (ShieldSide / 2f)), MouseFilter = MouseFilterEnum.Ignore };
		stand.AddChild(DiplomacyArt.Framed(face, FaceSide));
		var shield = new TextureRect
		{
			Texture = Shield(lord.Shield),
			Modulate = lord.Shield.Length == 0 ? lord.Colour.Lightened(0.2f) : Colors.White,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			Position = new Vector2((FaceSide - ShieldSide) / 2f, FaceSide - (ShieldSide / 2f)),
			Size = new Vector2(ShieldSide, ShieldSide),
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
