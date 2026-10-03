using System.Collections.Generic;
using Godot;

/// <summary>The fortifications a province can raise around itself: a ladder in timber and a longer
/// one in stone, laid out as two counters of cards with the chosen one read in the panel beside
/// them.
///
/// A province holds one fortification at a time, and raises one at a time. The stores are spent
/// when the order is placed, but the masons take seasons over it and the old wall stands until the
/// new one is done — so a province is never left open by its own building work.
///
/// Raising a new one replaces what stood there and costs the whole thing, not the difference: a
/// castle is not a palisade with stone poured over it.
///
/// It waits on turns the way a smith's commission does, but it is not a ProductionPage: what goes
/// up here has no attack, no range and no batch, and bending an order record with four combat stats
/// around a wall would cost more than the counter it saves.</summary>
public partial class FortificationsPage : RoomPage
{
	private const string DataPath = "res://data/fortifications.json";

	/// <summary>Where a rung's name is read aloud from, keyed the way everything else here is. A
	/// recording that has not been made yet simply does not play — the page must not wait on a
	/// voice actor to be usable.</summary>
	private const string VoiceDirectory = "res://assets/audio/forts";

	/// <summary>One rung of the ladder. <paramref name="Material"/> is the counter it is shown under,
	/// not the whole of what it costs: the timber rungs are timber alone, but a stone wall wants timber
	/// for its floors and scaffolding as well.</summary>
	private record Fort(string Key, string Name, string Short, string Blurb, string Material,
		int Seasons, Dictionary<string, int> Cost);

	/// <summary>One counter: the material, and everything that can be raised out of it.</summary>
	private record Ladder(string Key, string Name, string Icon, List<Fort> Forts);

	private readonly List<Ladder> _ladders = new();
	private readonly Dictionary<string, Button> _cards = new();
	private readonly Dictionary<string, Label> _prices = new();
	private Fort _chosen;

	protected override string RoomName => "Fortifications";

	protected override string Tagline => "Raise the walls your lands stand behind";

	// The yard lays its choices out on cards along the foot of the page, not on signs over the room.
	protected override Dictionary<string, Vector2> SignSpots { get; } = new();

	protected override void Load()
	{
		var file = GD.Load<Json>(DataPath);
		if (file?.Data.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"{RoomName}: no readable {DataPath}");
			return;
		}

		foreach (Variant entry in file.Data.AsGodotDictionary()["materials"].AsGodotArray())
		{
			Godot.Collections.Dictionary fields = entry.AsGodotDictionary();
			string material = fields["key"].AsString();

			var forts = new List<Fort>();
			foreach (Variant rung in fields["forts"].AsGodotArray())
			{
				Godot.Collections.Dictionary fort = rung.AsGodotDictionary();
				var cost = new Dictionary<string, int>();
				foreach (KeyValuePair<Variant, Variant> line in fort["cost"].AsGodotDictionary())
				{
					cost[line.Key.AsString()] = line.Value.AsInt32();
				}

				forts.Add(new Fort(
					fort["key"].AsString(),
					fort["name"].AsString(),
					fort["short"].AsString(),
					fort["blurb"].AsString(),
					material,
					fort["seasons"].AsInt32(),
					cost));
			}

			_ladders.Add(new Ladder(
				material,
				fields["name"].AsString(),
				fields.TryGetValue("icon", out Variant icon) ? icon.AsString() : material,
				forts));
		}
	}

	/// <summary>Opens on whatever the province already stands behind, or on the first rung of the
	/// timber ladder if it stands behind nothing.</summary>
	protected override void Opened()
	{
		// Asked for on the way in, on a worker thread, so that stepping down the ladder never waits
		// on a disk. By the time the first card is pressed they are usually already in hand — and
		// the rung already standing is among them, or the valley would open without its own walls.
		foreach (Ladder ladder in _ladders)
		{
			foreach (Fort rung in ladder.Forts)
			{
				Want(FortArt.Scene(rung.Key));
				Want(FortArt.Cloth(rung.Key));
				Want(FortArt.Life(rung.Key));
			}
		}

		Fort standing = Find(Province.Fortification);
		if (standing != null)
		{
			Choose(standing, standing.Key);
			return;
		}

		// Nothing raised here. The panel still opens on the first rung, so there is something to
		// read the moment the page appears — but the valley behind it stays empty, because an open
		// village is the true picture until the player asks to see one.
		Fort first = _ladders.Count > 0 && _ladders[0].Forts.Count > 0 ? _ladders[0].Forts[0] : null;
		Choose(first, showing: null);
	}

	// --- the counters --------------------------------------------------------------------------

	/// <summary>Two counters along the foot of the page, one per material, each with its own heading
	/// so a player reads "these cost wood, those cost stone" before reading any price.</summary>
	protected override void BuildChoosers()
	{
		BuildWalls();

		// The walls' own card stands over the two counters, so the counters keep the whole width and
		// their cards stay wide enough to read.
		var stack = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkEnd,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		stack.AddThemeConstantOverride("separation", 12);
		Body.AddChild(stack);
		Body.MoveChild(stack, 0);

		var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		row.AddThemeConstantOverride("separation", 16);

		foreach (Ladder ladder in _ladders)
		{
			var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			column.AddThemeConstantOverride("separation", 10);

			var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			heading.AddThemeConstantOverride("separation", 10);
			heading.AddChild(Icon(ladder.Icon, 28));
			Label name = Line(ladder.Name.ToUpperInvariant(), 20, Cream);
			name.VerticalAlignment = VerticalAlignment.Center;
			heading.AddChild(name);
			column.AddChild(heading);

			var cards = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cards.AddThemeConstantOverride("separation", 8);
			column.AddChild(cards);

			foreach (Fort fort in ladder.Forts)
			{
				cards.AddChild(BuildCard(fort));
			}

			// The frame has to be told to expand, not just the row holding it: a container that only
			// fills takes its minimum width and leaves the counter huddled in the corner. Stone has
			// four rungs to wood's three, so the two counters split the page in that proportion and
			// every card comes out the same width.
			PanelContainer frame = Framed(column, 12);
			frame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			frame.SizeFlagsStretchRatio = ladder.Forts.Count;
			row.AddChild(frame);
		}

		_garrison = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_garrison.AddThemeConstantOverride("separation", 8);
		PanelContainer watch = Framed(_garrison, 12);
		watch.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
		watch.CustomMinimumSize = new Vector2(560, 0);
		stack.AddChild(watch);
		stack.AddChild(row);
	}

	private string NameOf(string key) => Find(key)?.Name ?? key;

	/// <summary>Over what the page already re-reads: every card's price is read again, because a
	/// build spends the store the others were priced against, and the new wall takes its green
	/// ring.</summary>
	public override void Refresh()
	{
		base.Refresh();
		ShowGarrison();
		foreach (Ladder ladder in _ladders)
		{
			foreach (Fort fort in ladder.Forts)
			{
				foreach ((string store, int amount) in fort.Cost)
				{
					// Red on the store the province is short of: the reason the button is dead, read
					// off the card without having to open it.
					_prices[$"{fort.Key}/{store}"].AddThemeColorOverride("font_color",
						Province.Stored(store) >= amount ? Bright : Short);
				}

				DressCard(fort.Key);
			}
		}
	}
}
