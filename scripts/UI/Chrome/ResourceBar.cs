using Godot;

/// <summary>The province's stockpiles in one small strip: gold, grain, cattle, wood, stone, iron.
///
/// A page that spends a province's goods shows this so the player can check a price against what is
/// actually in the store. It knows nothing about the page around it — hand it an economy and it
/// draws it — so the smithy, the market and whatever the city gains next all use the same strip
/// rather than each drawing its own.</summary>
public partial class ResourceBar : PanelContainer
{
	private const string IconDirectory = "res://assets/ui/icons";

	private static readonly Color Cream = new("d9cdb4");

	// The stockpiles a province keeps, and the icon each is read by.
	private static readonly (string Key, string Icon)[] Stores =
	{
		("gold", "gold"),
		("grain", "food"),
		("cattle", "livestock"),
		("wood", "wood"),
		("stone", "stone"),
		("iron", "iron"),
		// The people are not a stockpile, but they are the one number every other one comes out of:
		// they grow the grain, cut the wood and fill the ranks, and a lord reading his stores wants
		// to see what he has to work them with in the same breath.
		("people", "population"),
	};

	private HBoxContainer _row;

	public override void _Ready()
	{
		AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.045f, 0.04f, 0.88f),
			BorderWidthLeft = 2,
			BorderWidthTop = 2,
			BorderWidthRight = 2,
			BorderWidthBottom = 2,
			BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.85f),
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomRight = 6,
			CornerRadiusBottomLeft = 6,
			ContentMarginLeft = 34,
			ContentMarginRight = 34,
			ContentMarginTop = 10,
			ContentMarginBottom = 10,
		});

		_row = new HBoxContainer();
		_row.AddThemeConstantOverride("separation", 34);
		AddChild(_row);
	}

	/// <summary>Draws what the province holds. Call it again whenever those numbers change.</summary>
	public void Show(ProvinceEconomy province) =>
		Show(province == null ? null : province.Stored); // an unclaimed province keeps no stores

	/// <summary>Draws whatever answers for the stores, which is not always one province: the hall of
	/// lords reads the whole realm across every province the crown holds. Null draws an empty
	/// strip.</summary>
	public void Show(System.Func<string, int> held)
	{
		foreach (Node cell in _row.GetChildren())
		{
			cell.QueueFree();
		}

		if (held == null)
		{
			return;
		}

		foreach ((string key, string icon) in Stores)
		{
			var cell = new HBoxContainer();
			cell.AddThemeConstantOverride("separation", 11);

			cell.AddChild(new TextureRect
			{
				Texture = GD.Load<Texture2D>($"{IconDirectory}/{icon}.png"),
				CustomMinimumSize = new Vector2(42, 42),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				MouseFilter = MouseFilterEnum.Ignore,
			});

			var amount = new Label { Text = held(key).ToString("N0"), VerticalAlignment = VerticalAlignment.Center };
			amount.AddThemeFontSizeOverride("font_size", 24);
			amount.AddThemeColorOverride("font_color", Cream);
			cell.AddChild(amount);

			_row.AddChild(cell);
		}
	}
}
