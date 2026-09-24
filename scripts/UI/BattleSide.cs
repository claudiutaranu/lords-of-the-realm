using System.Collections.Generic;
using Godot;

/// <summary>One army's half of the battle table: its banner, its captain, whose men they are, and
/// every kind of man a realm can raise with how many of him are standing here.
///
/// Only the kinds that are standing here: a column of noughts is seven rows to read past for the two
/// that matter.</summary>
public sealed class BattleSide
{
	private const string CaptainPath = "res://assets/ui/army-captain.png";
	private static readonly Rect2 CrestRegion = new(174, 94, 908, 1070);
	private static readonly Vector2 BannerSize = new(74, 100);
	private static readonly Vector2 PortraitTile = new(64, 30);
	private const int CaptainSide = 100;
	private const int RowHigh = 31;

	public Control Root { get; }

	private readonly StyleBoxFlat _cloth;
	private readonly TextureRect _crest;
	private readonly Label _name;
	private readonly Label _under;
	private readonly Dictionary<string, (Control Row, Label Count)> _roster = new();
	private readonly Label _total;

	/// <summary><paramref name="far"/> is the other side of the table: its banner hangs on the far
	/// edge and its captain looks back across at ours.</summary>
	public BattleSide(bool far)
	{
		var side = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		side.AddThemeConstantOverride("separation", 4);

		var head = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		head.AddThemeConstantOverride("separation", 16);
		side.AddChild(head);

		_cloth = Chrome.CardStyle(Colors.Black);
		_cloth.BorderColor = new Color(0.86f, 0.69f, 0.36f);
		var banner = new PanelContainer
		{
			CustomMinimumSize = BannerSize,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
		};
		banner.AddThemeStyleboxOverride("panel", _cloth);
		_crest = new TextureRect
		{
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		banner.AddChild(_crest);

		Control captain = Chrome.Framed(new TextureRect
		{
			Texture = GD.Load<Texture2D>(CaptainPath),
			CustomMinimumSize = new Vector2(CaptainSide, CaptainSide),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
			FlipH = far,
		}, 3);

		head.AddChild(far ? captain : banner);
		head.AddChild(far ? banner : captain);

		_name = Chrome.Line("", 27, Chrome.Cream);
		_name.HorizontalAlignment = HorizontalAlignment.Center;
		GoldTitle.Apply(_name);
		side.AddChild(_name);

		_under = Chrome.Line("", 16, Chrome.Soft);
		_under.HorizontalAlignment = HorizontalAlignment.Center;
		side.AddChild(_under);

		var rows = new VBoxContainer();
		rows.AddThemeConstantOverride("separation", 2);
		foreach (string kind in Units.All())
		{
			Units.Unit unit = Units.Of(kind);
			var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, RowHigh) };
			row.AddThemeConstantOverride("separation", 12);
			row.AddChild(UnitArt.Tile(kind, PortraitTile));

			Label name = Chrome.Line(unit.Name, 17, Chrome.Bright);
			name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			name.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(name);

			TextureRect glyph = Chrome.Icon(unit.Icon, 24);
			glyph.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			row.AddChild(glyph);

			Label count = Chrome.Line("0", 20, Chrome.Bright);
			count.HorizontalAlignment = HorizontalAlignment.Right;
			count.VerticalAlignment = VerticalAlignment.Center;
			count.CustomMinimumSize = new Vector2(56, 0);
			row.AddChild(count);

			// The row and the rule under it come and go together.
			var line = new VBoxContainer();
			line.AddThemeConstantOverride("separation", 2);
			line.AddChild(row);
			line.AddChild(Hairline());
			rows.AddChild(line);
			_roster[kind] = (line, count);
		}

		var tally = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		tally.AddThemeConstantOverride("separation", 14);
		tally.AddChild(Chrome.Icon("men", 30));
		Label all = Chrome.Line("Total Men", 20, Chrome.Soft);
		all.CustomMinimumSize = new Vector2(150, 0);
		tally.AddChild(all);
		_total = Chrome.Line("0", 24, Chrome.Bright);
		tally.AddChild(_total);
		rows.AddChild(tally);

		side.AddChild(Chrome.Framed(rows, 8));
		Root = side;
	}

	private static Control Hairline()
	{
		Control rule = Chrome.Rule(0);
		rule.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		return rule;
	}

	/// <summary>Hangs this side's colours and counts its men. <paramref name="realmKey"/> finds its
	/// crest where somebody has drawn one; the cloth is the realm's own colour either way, so a side
	/// with no crest is still somebody's. Says how many kinds of man it listed, and how tall one line
	/// of them is, so the table can be cut to the longer of its two sides.</summary>
	public (int Kinds, float LineHigh) Show(string name, string under, string realmKey, Color accent, Dictionary<string, int> men)
	{
		_name.Text = name;
		_under.Text = under;
		_cloth.BgColor = accent.Darkened(0.35f);

		string crest = $"{Chrome.IconDirectory}/shield-{realmKey}.png";
		_crest.Texture = ResourceLoader.Exists(crest)
			? new AtlasTexture { Atlas = GD.Load<Texture2D>(crest), Region = CrestRegion }
			: null;

		int kinds = 0;
		float high = 0f;
		foreach ((string kind, (Control line, Label count)) in _roster)
		{
			int many = men.GetValueOrDefault(kind);
			count.Text = many.ToString("N0");
			line.Visible = many > 0;
			kinds += many > 0 ? 1 : 0;
			high = line.GetCombinedMinimumSize().Y;
		}

		_total.Text = ProvinceEconomy.Men(men).ToString("N0");
		return (kinds, high);
	}
}
