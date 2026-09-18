using System;
using System.Collections.Generic;
using Godot;

/// <summary>The province's town, seen from above: every building it has raised, standing on its own
/// ground, each one a way into the room behind it.
///
/// It is a RoomPage like the others rather than a screen of its own, because from the door it is the
/// same room — the province's stores along the top, a title, signs hung over what the town offers,
/// and a panel in the corner reading whatever is chosen. What it hangs its signs over happens to be
/// a valley rather than a forge wall.
///
/// A building stands where buildings.json puts it, and shows its own picture the day one is drawn
/// for it. Until then the sign stands on bare ground, which is the honest picture of a town nobody
/// has drawn yet — not a missing-resource error.</summary>
public partial class CityPage : RoomPage
{
	private const string DataPath = "res://data/buildings.json";

	/// <summary>One building in the town. <paramref name="Site"/> is the industry it belongs to, for
	/// the ones a province may simply not have — there is no quarry where there is no stone. A null
	/// site means every province has it.</summary>
	private record Building(string Key, string Name, string Blurb, string Icon, string Room,
		string Site, Vector2 Spot, float Scale);

	/// <summary>The frame the spots and the scales are read against — the town picture's own height,
	/// so a figure in buildings.json means the same thing whatever the window is doing.</summary>
	private const float Frame = 941f;

	/// <summary>How far below a building its sign hangs, as a fraction of the picture. The sign marks
	/// the ground the building stands on, and a sign centred on that ground covers the doorway.</summary>
	private const float SignDrop = 0.042f;

	/// <summary>Raised with a building's room when the player goes in, so the map decides what opens.
	/// The town knows the smithy is there; it does not know what a smithy is.</summary>
	public event Action<string> RoomChosen;

	private readonly List<Building> _buildings = new();
	private readonly Dictionary<string, Control> _tiles = new();
	private readonly Dictionary<string, Button> _signs = new();
	private Building _chosen;

	protected override string RoomName => "The Town";

	protected override string Tagline => "Everything the province has raised, and everything it works";

	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override void Load()
	{
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"{RoomName}: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["buildings"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			Godot.Collections.Array spot = fields["spot"].AsGodotArray();
			string key = fields["key"].AsString();

			_buildings.Add(new Building(
				key,
				fields["name"].AsString(),
				fields["blurb"].AsString(),
				fields["icon"].AsString(),
				fields.TryGetValue("room", out Variant room) ? room.AsString() : null,
				fields.TryGetValue("site", out Variant site) ? site.AsString() : null,
				new Vector2((float)spot[0], (float)spot[1]),
				(float)fields["scale"]));

			// Under the building, not over its door.
			SignSpots[key] = _buildings[^1].Spot + new Vector2(0f, SignDrop);
		}
	}

	protected override void BuildChoosers()
	{
		foreach (Building building in _buildings)
		{
			Building chosen = building;

			// The building itself is the button. A plaque under every roof turned the town into a
			// list of labels laid over a picture, and there are more roofs coming.
			if (CityArt.HasTile(building.Key))
			{
				_tiles[building.Key] = HangTile(building, () => Pick(chosen));
				continue;
			}

			// Nothing drawn for it yet, so a sign stands in — otherwise the building is on the page
			// with nothing on screen to press.
			Button sign = HangSign(building.Key, building.Name, building.Icon, building.Blurb,
				() => Pick(chosen));
			if (sign != null)
			{
				_signs[building.Key] = sign;
			}
		}
	}

	/// <summary>Which industries this province actually has. A site with no room for a single worker
	/// is not a site the province owns, and the town does not draw one.
	///
	/// Called after the page is open, because the capacities live on the province's definition and a
	/// room is handed only its economy.</summary>
	public void ShowSites(ProvinceDefinition definition)
	{
		foreach (Building building in _buildings)
		{
			bool stands = building.Site == null || CapacityFor(definition, building.Site) > 0;
			if (_signs.TryGetValue(building.Key, out Button sign))
			{
				sign.Visible = stands;
			}

			if (_tiles.TryGetValue(building.Key, out Control tile))
			{
				tile.Visible = stands;
			}
		}

		// The panel may be reading a quarry this province has no stone for.
		if (_chosen?.Site != null && CapacityFor(definition, _chosen.Site) <= 0)
		{
			Pick(_buildings.Count > 0 ? _buildings[0] : null);
		}
	}

	protected override void Opened() => Pick(_buildings.Count > 0 ? _buildings[0] : null);

	protected override void ShowDetail()
	{
		ClearDetail();
		if (_chosen == null)
		{
			return;
		}

		var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		heading.AddThemeConstantOverride("separation", 12);
		heading.AddChild(Icon(_chosen.Icon, 30));
		Label name = Line(_chosen.Name.ToUpperInvariant(), 24, Cream);
		name.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(name);
		Detail.AddChild(heading);

		Detail.AddChild(Chrome.Rule(360));

		// Two lines' worth of room whether the line needs them or not, so the button under it does
		// not move up and down under the cursor as the player reads along the town.
		Label blurb = Line(_chosen.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		blurb.HorizontalAlignment = HorizontalAlignment.Center;
		blurb.VerticalAlignment = VerticalAlignment.Center;
		blurb.CustomMinimumSize = new Vector2(0, 46);
		Detail.AddChild(blurb);

		if (_chosen.Room == null)
		{
			Label soon = Line("Nothing to do here yet.", 15, Dim);
			soon.HorizontalAlignment = HorizontalAlignment.Center;
			Detail.AddChild(soon);
			return;
		}

		string room = _chosen.Room;
		var enter = new Button { Text = "Enter  ›", CustomMinimumSize = new Vector2(0, 46) };
		enter.AddThemeFontSizeOverride("font_size", 19);
		enter.Pressed += () => RoomChosen?.Invoke(room);
		Detail.AddChild(enter);
	}

	private void Pick(Building building)
	{
		_chosen = building;
		LightSign(building?.Key);
		foreach ((string key, Control tile) in _tiles)
		{
			Light(tile, key == building?.Key, hovered: false);
		}

		ShowDetail();
	}

	/// <summary>How a building shows it is being read, or being pointed at. There is no frame to
	/// light the way a plaque has one, so the light is on the building: the rest of the town keeps
	/// its own colour rather than being dimmed, because a town with one lit roof and eight grey ones
	/// reads as eight buildings switched off.</summary>
	private static void Light(Control tile, bool chosen, bool hovered) =>
		tile.Modulate = chosen || hovered
			? new Color(1.22f, 1.17f, 1.04f)
			: Colors.White;

	/// <summary>A building on the town, answering to presses on its roof rather than on the box
	/// around it. The pictures are drawn on the diagonal and their corners overlap, so a building
	/// judged by its box swallows presses meant for whatever stands behind it.</summary>
	private partial class Tile : TextureButton
	{
		public Image Shape;

		public override bool _HasPoint(Vector2 point)
		{
			if (Shape == null || Size.X <= 0f || Size.Y <= 0f)
			{
				return true;
			}

			var at = new Vector2I(
				Mathf.FloorToInt(point.X / Size.X * Shape.GetWidth()),
				Mathf.FloorToInt(point.Y / Size.Y * Shape.GetHeight()));

			return at.X >= 0 && at.Y >= 0 && at.X < Shape.GetWidth() && at.Y < Shape.GetHeight()
				&& Shape.GetPixelv(at).A > 0.2f;
		}
	}

	/// <summary>The building's own picture, standing on the spot its sign hangs over. Bottom-centred
	/// on the spot rather than centred on it: a building is drawn standing on its ground, and what
	/// has to line up with the valley is the ground it stands on, not the middle of its roof.</summary>
	private Control HangTile(Building building, Action pressed)
	{
		var art = GD.Load<Texture2D>(CityArt.Tile(building.Key));
		var tile = new Tile
		{
			TextureNormal = art,
			TooltipText = building.Blurb,
			IgnoreTextureSize = true,
			StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
			Shape = art.GetImage(),
		};

		tile.Pressed += pressed;
		tile.MouseEntered += () => Light(tile, _chosen?.Key == building.Key, hovered: true);
		tile.MouseExited += () => Light(tile, _chosen?.Key == building.Key, hovered: false);

		AddChild(tile);
		// Behind the chrome, in front of the valley.
		MoveChild(tile, 1);

		Vector2 size = art.GetSize();
		float tall = building.Scale * Frame;
		tile.AnchorLeft = tile.AnchorRight = building.Spot.X;
		tile.AnchorTop = tile.AnchorBottom = building.Spot.Y;
		tile.OffsetTop = -tall;
		tile.OffsetBottom = 0;
		tile.OffsetLeft = -tall * size.X / size.Y / 2f;
		tile.OffsetRight = -tile.OffsetLeft;
		return tile;
	}

	private static int CapacityFor(ProvinceDefinition definition, string site) => site switch
	{
		"grain" => definition.GrainWorkerCapacity,
		"cattle" => definition.CattleWorkerCapacity,
		"wood" => definition.WoodWorkerCapacity,
		"stone" => definition.StoneWorkerCapacity,
		_ => definition.IronWorkerCapacity,
	};
}
