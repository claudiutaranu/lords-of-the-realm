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
	/// not the whole of what it costs: timber alone raises a palisade, but nothing above a fort goes
	/// up without stone, and nothing above a small castle without iron.</summary>
	private record Fort(string Key, string Name, string Short, string Blurb, string Material,
		int Seasons, Dictionary<string, int> Cost);

	/// <summary>One counter: the material, and everything that can be raised out of it.</summary>
	private record Ladder(string Key, string Name, string Icon, List<Fort> Forts);

	/// <summary>How long one fort takes to dissolve into the next. Long enough to read as the valley
	/// changing rather than as a picture being swapped, short enough not to lag the press.</summary>
	private const float FadeSeconds = 0.45f;

	/// <summary>The two layers the valley's fort is drawn on. One holds what is showing, the other
	/// takes what is coming, and they trade places every time the choice changes — a single layer
	/// could only blink to black and back.</summary>
	private readonly TextureRect[] _walls = new TextureRect[2];

	/// <summary>What moves on each wall layer beyond its own cloth, or null where the rung showing
	/// there is a still picture. One per layer, so a dissolve carries the old rung's cranes out
	/// while the new rung's swing in.</summary>
	private readonly Node2D[] _living = new Node2D[2];

	/// <summary>Every picture the ladder can put on the valley, held for as long as the room is
	/// open. The first look at a rung would otherwise load a full-frame painting, its wind mask and
	/// whatever hangs off it on the main thread, which is a visible stop in the middle of a
	/// dissolve — and the dissolve is the whole point of stepping through the ladder.</summary>
	private readonly Dictionary<string, Resource> _art = new();
	private int _showing;
	private Tween _fade;

	private readonly List<Ladder> _ladders = new();
	private readonly Dictionary<string, Button> _cards = new();
	private readonly Dictionary<string, Label> _prices = new();
	private Fort _chosen;

	/// <summary>The column that says who is standing on the walls, and the line under it that counts
	/// them. The steppers themselves are built once: moving a man between the field and the gate
	/// does not change how many of him there are, so nothing about them goes stale.</summary>
	private VBoxContainer _garrison;
	private Label _manned;
	private Label _larder;

	/// <summary>What the column was last built for. The steppers hold a snapshot of their own
	/// ceiling, so they are rebuilt when the ceiling could have moved and left alone when it cannot
	/// — moving a man from the field to the gate, or a sack from the granary to the larder, changes
	/// where a thing is and never how much of it there is. Rebuilding on every press would free the
	/// slider out from under the finger still holding it.</summary>
	private string _standing = "";

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
		Fort standing = Find(Province.Fortification);
		if (standing != null)
		{
			Choose(standing, standing.Key);
			return;
		}

		// Nothing raised here. The panel still opens on the first rung, so there is something to
		// read the moment the page appears — but the valley behind it stays empty, because an open
		// village is the true picture until the player asks to see one.
		// Asked for on the way in, on a worker thread, so that stepping down the ladder never waits
		// on a disk. By the time the first card is pressed they are usually already in hand.
		foreach (Ladder ladder in _ladders)
		{
			foreach (Fort rung in ladder.Forts)
			{
				Want(FortArt.Scene(rung.Key));
				Want(FortArt.Cloth(rung.Key));
				Want(FortArt.Life(rung.Key));
			}
		}

		Fort first = _ladders.Count > 0 && _ladders[0].Forts.Count > 0 ? _ladders[0].Forts[0] : null;
		Choose(first, showing: null);
	}

	// --- the counters --------------------------------------------------------------------------

	/// <summary>Two counters along the foot of the page, one per material, each with its own heading
	/// so a player reads "these cost wood, those cost stone" before reading any price.</summary>
	protected override void BuildChoosers()
	{
		BuildWalls();


		var row = new HBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkEnd,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		row.AddThemeConstantOverride("separation", 16);
		Body.AddChild(row);
		Body.MoveChild(row, 0);

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
		watch.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		watch.SizeFlagsStretchRatio = 3;
		row.AddChild(watch);
	}

	/// <summary>Who stands on the walls, company by company.
	///
	/// It belongs in this room and nowhere else: manning a gate is meaningless without a gate, and
	/// this is the only screen that knows what the province has raised. The decision it asks is the
	/// one a castle exists to ask — a lord can leave archers on the parapet and ride out with the
	/// rest, and the men he leaves are the ones who still hold the county after he has lost
	/// everything he took into the field.
	///
	/// Companies and not one number, because WHICH men is the whole question. Bows are worth twice
	/// as much behind a parapet as in front of one, and cavalry on a wall are the most expensive men
	/// in the realm standing about watching.</summary>
	private void ShowGarrison()
	{
		string signature = Signature();
		if (signature == _standing)
		{
			Counted();
			return;
		}

		_standing = signature;
		foreach (Node old in _garrison.GetChildren())
		{
			old.QueueFree();
		}

		var heading = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		heading.AddThemeConstantOverride("separation", 10);
		heading.AddChild(Icon("castle", 28));
		Label title = Line("THE WALLS", 20, Cream);
		title.VerticalAlignment = VerticalAlignment.Center;
		heading.AddChild(title);
		_garrison.AddChild(heading);

		var companies = new List<string>(Province.Garrison.Keys);
		foreach (string unit in Province.Castle.Keys)
		{
			if (!companies.Contains(unit))
			{
				companies.Add(unit);
			}
		}

		companies.Sort(System.StringComparer.Ordinal);

		if (Province.Fortification.Length == 0)
		{
			_garrison.AddChild(Line("There is nothing here yet for a man to stand on.", 16, Dim));
			return;
		}

		if (companies.Count == 0)
		{
			_garrison.AddChild(Line("No men in the county to put on them.", 16, Dim));
			return;
		}

		foreach (string unit in companies)
		{
			int held = Province.Castle.GetValueOrDefault(unit);
			int all = held + Province.Garrison.GetValueOrDefault(unit);
			string name = unit;
			_garrison.AddChild(Stepper(Units.Of(unit).Name, held, 5, all, men => Man(name, men), floor: 0));
		}

		_manned = Line("", 16, Soft);
		_manned.HorizontalAlignment = HorizontalAlignment.Center;
		_garrison.AddChild(_manned);

		// The larder. A siege is lost or won on this line and on nothing else, and it has to be
		// carried up BEFORE anybody is at the border — a lord stocking a castle he is already shut
		// out of is a lord who has understood the mechanism a season too late.
		_garrison.AddChild(Chrome.Rule(0));
		int room = Fortifications.Of(Province.Fortification).Stores;
		_garrison.AddChild(Stepper("Grain behind the gate", Province.CastleStores, 5,
			Mathf.Min(room, Province.CastleStores + Province.Grain), Stock, floor: 0));

		_larder = Line("", 16, Soft);
		_larder.HorizontalAlignment = HorizontalAlignment.Center;
		_garrison.AddChild(_larder);
		Counted();
	}

	/// <summary>Everything about the column that decides how it is BUILT, as against what it
	/// reads.</summary>
	private string Signature()
	{
		var mark = new System.Text.StringBuilder(Province.Fortification);
		mark.Append('|').Append(Province.CastleStores + Province.Grain);
		var companies = new List<string>(Province.Garrison.Keys);
		foreach (string unit in Province.Castle.Keys)
		{
			if (!companies.Contains(unit))
			{
				companies.Add(unit);
			}
		}

		companies.Sort(System.StringComparer.Ordinal);
		foreach (string unit in companies)
		{
			mark.Append('|').Append(unit).Append(':')
				.Append(Province.Garrison.GetValueOrDefault(unit) + Province.Castle.GetValueOrDefault(unit));
		}

		return mark.ToString();
	}

	/// <summary>Carries grain up behind the gate, or brings it back down. It is the same grain
	/// either way: nothing is spent, and what is up there is simply not in the granary.</summary>
	private void Stock(int carried)
	{
		int all = Province.CastleStores + Province.Grain;
		carried = Mathf.Clamp(carried, 0, Mathf.Min(all, Fortifications.Of(Province.Fortification).Stores));
		Province.CastleStores = carried;
		Province.Grain = all - carried;
		Refresh();
	}

	/// <summary>Moves one company between the field and the gate. Nothing is raised or spent: these
	/// are the same men either way, and the only question is where they are standing when somebody
	/// comes for the county.</summary>
	/// <summary>Men moved between the field and the gate. The column is not rebuilt for it — see
	/// <see cref="_standing"/> — but the count under it and the larder's reading both move.</summary>
	private void Man(string unit, int onTheWalls)
	{
		int all = Province.Castle.GetValueOrDefault(unit) + Province.Garrison.GetValueOrDefault(unit);
		onTheWalls = Mathf.Clamp(onTheWalls, 0, all);
		Post(Province.Castle, unit, onTheWalls);
		Post(Province.Garrison, unit, all - onTheWalls);
		Refresh();
	}

	private static void Post(Dictionary<string, int> roster, string unit, int men)
	{
		if (men > 0)
		{
			roster[unit] = men;
		}
		else
		{
			roster.Remove(unit);
		}
	}

	private void Counted()
	{
		if (_manned == null)
		{
			return;
		}

		_manned.Text = $"{Province.CastleMen:N0} of {Province.Soldiers:N0} men on the walls";

		// In seasons rather than in sacks, because seasons is the question. Sacks is how it is
		// carried; how long they hold out is what a lord is deciding.
		int eaten = Mathf.CeilToInt(
			Province.CastleMen / GameBalance.Engine.PeoplePerGrain * GameBalance.Engine.SoldierAppetite);
		string holds = Province.CastleMen == 0
			? "nobody up there to eat it"
			: eaten <= 0 ? "as long as you like"
			: $"{Province.CastleStores / eaten:N0} seasons of bread for them";
		_larder.Text = $"{Province.CastleStores:N0} of {Fortifications.Of(Province.Fortification).Stores:N0} sacks — {holds}";
	}

	/// <summary>The two layers a fort is drawn on, laid straight over the valley and under
	/// everything else. They go in at the front of the page rather than at the back, because a room
	/// builds its own furniture last and a wall painted after the furniture would be painted over
	/// the titles and the cards.</summary>
	private void BuildWalls()
	{
		for (int layer = 0; layer < _walls.Length; layer++)
		{
			var wall = new TextureRect
			{
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = MouseFilterEnum.Ignore,
				Modulate = new Color(1f, 1f, 1f, 0f),
			};
			AddChild(wall);
			Chrome.Fill(wall);

			// Index 0 is the valley the scene itself put there; the walls go straight on top of it.
			MoveChild(wall, 1 + layer);
			_walls[layer] = wall;

			int mine = layer;
			wall.Resized += () => Place(mine);
		}
	}

	/// <summary>Dissolves the valley from one rung to the next.
	///
	/// On opening, it shows what the province has actually raised, so walking into the room tells
	/// you what you own before you touch anything. After that it follows the card being read, which
	/// is what makes the ladder worth clicking through: you are looking at what you would get.
	///
	/// A rung with no picture drawn for it yet fades to bare valley rather than holding the last
	/// one, so what is on screen never disagrees with what the panel says.</summary>
	private void ShowWall(string fort)
	{
		var coming = fort != null && FortArt.HasScene(fort) ? Art<Texture2D>(FortArt.Scene(fort)) : null;

		TextureRect showing = _walls[_showing];
		if (showing.Texture == coming)
		{
			return;
		}

		int layer = 1 - _showing;
		TextureRect incoming = _walls[layer];
		incoming.Texture = coming;

		// The wind goes on with the wall it belongs to. Set on the layer rather than baked into the
		// picture, so the same art is what dissolves and only the cloth on it knows the difference.
		incoming.Material = fort != null && FortArt.HasCloth(fort) ? Art<ShaderMaterial>(FortArt.Cloth(fort)) : null;

		// And so does whatever hangs off it. The layer's own fade carries these with it: modulate
		// runs down the tree, so a crane dissolves out with the wall it was built against.
		_living[layer]?.QueueFree();
		_living[layer] = null;
		if (fort != null && FortArt.HasLife(fort))
		{
			_living[layer] = Art<PackedScene>(FortArt.Life(fort)).Instantiate<Node2D>();
			incoming.AddChild(_living[layer]);
			Place(layer);
		}

		_showing = layer;

		// One tween at a time: a second press mid-dissolve would otherwise leave the first fading
		// against the second and both half-lit.
		_fade?.Kill();
		_fade = CreateTween().SetParallel();
		_fade.TweenProperty(incoming, "modulate:a", coming == null ? 0f : 1f, FadeSeconds);
		_fade.TweenProperty(showing, "modulate:a", 0f, FadeSeconds);
	}

	/// <summary>Starts a file on its way in the background. A rung that has no such file yet is not
	/// asked for, which is how a half-drawn ladder stays quiet.</summary>
	private void Want(string path)
	{
		if (ResourceLoader.Exists(path))
		{
			ResourceLoader.LoadThreadedRequest(path);
		}
	}

	/// <summary>Takes a file the room asked for on the way in, waiting on the worker only if it has
	/// not finished yet, and keeps it: a second look at the same rung costs nothing at all.</summary>
	private T Art<T>(string path) where T : Resource
	{
		if (!_art.TryGetValue(path, out Resource held))
		{
			held = ResourceLoader.LoadThreadedGet(path);
			_art[path] = held;
		}

		return (T)held;
	}

	/// <summary>Puts a layer's moving parts where the picture under them actually ended up. The wall
	/// is drawn to cover the page, so the art is scaled by whichever of the two edges needs the most
	/// and centred on what is left — which means a point measured from the picture's middle lands at
	/// the page's middle plus that same offset, scaled. Everything in the scene is placed from the
	/// middle for exactly this reason.</summary>
	private void Place(int layer)
	{
		TextureRect wall = _walls[layer];
		Node2D life = _living[layer];
		if (life == null || wall.Texture == null || wall.Size.X <= 0f || wall.Size.Y <= 0f)
		{
			return;
		}

		Vector2 picture = wall.Texture.GetSize();
		float cover = Mathf.Max(wall.Size.X / picture.X, wall.Size.Y / picture.Y);
		life.Scale = new Vector2(cover, cover);
		life.Position = wall.Size / 2f;
	}

	/// <summary>One card: the fort as it will look, its name, and what it costs. The picture may not
	/// have been drawn yet, in which case the mount stands empty rather than the card vanishing.</summary>
	private Control BuildCard(Fort fort)
	{
		var card = new Button
		{
			CustomMinimumSize = new Vector2(0, 258),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			TooltipText = fort.Blurb,
		};
		Fort chosen = fort;
		card.Pressed += () => Pick(chosen);
		_cards[fort.Key] = card;

		var inset = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
		foreach (string side in new[] { "left", "top", "right", "bottom" })
		{
			inset.AddThemeConstantOverride($"margin_{side}", 8);
		}

		card.AddChild(inset);
		inset.SetAnchorsPreset(LayoutPreset.FullRect);

		var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
		stack.AddThemeConstantOverride("separation", 6);
		inset.AddChild(stack);

		stack.AddChild(Mount(fort.Key, SizeFlags.ExpandFill));

		Label name = Line(fort.Name.ToUpperInvariant(), 14, Cream);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		name.MouseFilter = MouseFilterEnum.Ignore;
		// Two lines, always: "Motte and Double Bailey" does not fit across a card on one, and a
		// label that grows only on that card would leave its picture shorter than its neighbours'.
		name.AutowrapMode = TextServer.AutowrapMode.Word;
		name.VerticalAlignment = VerticalAlignment.Center;
		name.CustomMinimumSize = new Vector2(0, 38);
		stack.AddChild(name);

		// Two to a row rather than all across: a card is narrower than three prices and a duration,
		// and the last one was being cut off at the edge of the counter. The seasons take the last
		// cell rather than a line of their own — what a wall costs and how long it takes are one
		// reading, not two.
		var price = new GridContainer { Columns = 2, MouseFilter = MouseFilterEnum.Ignore };
		price.AddThemeConstantOverride("h_separation", 14);
		price.AddThemeConstantOverride("v_separation", 3);

		// Held to two rows whether a rung needs one or three, so every card's footing is the same
		// depth and the pictures above them line up across the counter.
		var centred = new CenterContainer
		{
			MouseFilter = MouseFilterEnum.Ignore,
			CustomMinimumSize = new Vector2(0, 59),
		};
		centred.AddChild(price);
		stack.AddChild(centred);

		foreach ((string store, int amount) in fort.Cost)
		{
			var group = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
			group.AddThemeConstantOverride("separation", 5);
			group.AddChild(Icon(store, 24));
			Label cost = Line(amount.ToString("N0"), 16, Bright);
			_prices[$"{fort.Key}/{store}"] = cost;
			group.AddChild(cost);
			price.AddChild(group);
		}

		Label takes = Line($"{fort.Seasons} season{(fort.Seasons == 1 ? "" : "s")}", 14, Soft);
		takes.VerticalAlignment = VerticalAlignment.Center;
		takes.MouseFilter = MouseFilterEnum.Ignore;
		price.AddChild(takes);

		DressCard(fort.Key);
		return card;
	}

	/// <summary>The frame a fort's picture sits in, dark enough that a drawing's sky does not run
	/// into the card's own border. Empty until the art exists.</summary>
	private static Control Mount(string fort, SizeFlags vertical)
	{
		var mount = new PanelContainer
		{
			SizeFlagsVertical = vertical,
			ClipContents = true,
			MouseFilter = MouseFilterEnum.Ignore,
		};
		mount.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.06f, 1f),
			BorderWidthLeft = 1,
			BorderWidthTop = 1,
			BorderWidthRight = 1,
			BorderWidthBottom = 1,
			BorderColor = new Color(0.549f, 0.447f, 0.271f, 0.55f),
		});

		if (FortArt.Has(fort))
		{
			mount.AddChild(new TextureRect
			{
				Texture = GD.Load<Texture2D>(FortArt.Picture(fort)),
				ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
				MouseFilter = MouseFilterEnum.Ignore,
			});
		}

		return mount;
	}

	/// <summary>A card is on the counter, lit as the one being read, ringed green as the one already
	/// standing, or ringed blue as the one going up. What the province has and what it is getting
	/// both beat what is merely being read: those are the two a player scans for.</summary>
	private void DressCard(string fort)
	{
		bool standing = fort == Province?.Fortification;
		bool rising = fort == Province?.Building;
		bool lit = _chosen != null && fort == _chosen.Key;

		StyleBoxFlat style = CardStyle(lit
			? new Color(0.20f, 0.15f, 0.06f, 0.94f)
			: new Color(0.078f, 0.075f, 0.078f, 0.9f));
		style.BorderColor = standing
			? new Color(0.60f, 0.86f, 0.55f, 1f)   // already raised
			: rising ? new Color(0.55f, 0.75f, 1f, 1f)  // the masons are on it
			: lit ? new Color(1f, 0.86f, 0.5f, 1f) : new Color(0.549f, 0.447f, 0.271f, 0.8f);
		style.BorderWidthLeft = style.BorderWidthTop = style.BorderWidthRight = style.BorderWidthBottom =
			lit || standing || rising ? 3 : 2;

		foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
		{
			_cards[fort].AddThemeStyleboxOverride(state, style);
		}
	}

	/// <summary>What pressing a card does: reads the rung, stands it in the valley, and names it
	/// aloud. Distinct from Choose, which sets the page without saying anything — the page settles
	/// on a rung when it opens too, and a room that announces itself the moment you walk in wears
	/// thin by the third province.</summary>
	private void Pick(Fort fort)
	{
		Choose(fort, fort?.Key);
		Speak(fort);
	}

	private void Speak(Fort fort)
	{
		string path = fort == null ? null : $"{VoiceDirectory}/{fort.Key}.mp3";
		if (path == null || !ResourceLoader.Exists(path))
		{
			return; // not recorded yet
		}

		Narrator.Say(path);
	}

	/// <summary>Reads one rung while the valley shows another — or none. The two part company once,
	/// on opening a province that has built nothing: the panel has to say something, and the valley
	/// must not say you own a palisade you have never paid for.</summary>
	private void Choose(Fort fort, string showing)
	{
		_chosen = fort;
		ShowWall(showing);
		foreach (string key in _cards.Keys)
		{
			DressCard(key);
		}

		ShowDetail();
	}

	private Fort Find(string key)
	{
		foreach (Ladder ladder in _ladders)
		{
			Fort found = ladder.Forts.Find(fort => fort.Key == key);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	// --- the panel on the right ------------------------------------------------------------------

	protected override void ShowDetail()
	{
		if (Detail == null)
		{
			return;
		}

		ClearDetail();
		if (_chosen == null || Province == null)
		{
			return;
		}

		Label title = Line(_chosen.Name.ToUpperInvariant(), 24, Bright);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(title);

		Control picture = Mount(_chosen.Key, SizeFlags.Fill);
		picture.CustomMinimumSize = new Vector2(0, 150);
		Detail.AddChild(picture);

		Detail.AddChild(BuildLadder());

		// Three lines' worth of room whether the line needs them or not. The blurbs run from two
		// lines to three, and letting the panel shrink to fit moves the Build button up and down
		// under the cursor as the player reads along the ladder.
		Label blurb = Line(_chosen.Blurb, 16, Soft);
		blurb.AutowrapMode = TextServer.AutowrapMode.Word;
		blurb.HorizontalAlignment = HorizontalAlignment.Center;
		blurb.VerticalAlignment = VerticalAlignment.Center;
		blurb.CustomMinimumSize = new Vector2(0, 68);
		Detail.AddChild(blurb);

		Label needs = Line("Requires", 14, Soft);
		needs.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(needs);

		var price = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		price.AddThemeConstantOverride("separation", 16);
		foreach ((string store, int amount) in _chosen.Cost)
		{
			var group = new HBoxContainer();
			group.AddThemeConstantOverride("separation", 7);
			group.AddChild(Icon(store, 28));
			group.AddChild(Line(amount.ToString("N0"), 20,
				Province.Stored(store) >= amount ? Bright : Short));
			price.AddChild(group);
		}

		Detail.AddChild(price);

		Label takes = Line($"{_chosen.Seasons} season{(_chosen.Seasons == 1 ? "" : "s")} of work",
			14, Soft);
		takes.HorizontalAlignment = HorizontalAlignment.Center;
		Detail.AddChild(takes);

		bool standing = _chosen.Key == Province.Fortification;
		bool busy = Province.Building.Length > 0;
		var build = new Button
		{
			Text = busy ? "The masons are at work" : standing ? "Already standing" : "Build",
			CustomMinimumSize = new Vector2(0, 52),
			Disabled = busy || standing || !CanAfford(_chosen),
		};
		build.AddThemeFontSizeOverride("font_size", 19);
		build.Pressed += Build;
		Detail.AddChild(build);

		if (busy)
		{
			Label rising = Line(
				$"Raising {NameOf(Province.Building)} — {Province.BuildSeasonsLeft} " +
				$"season{(Province.BuildSeasonsLeft == 1 ? "" : "s")} left",
				14, Bright);
			rising.HorizontalAlignment = HorizontalAlignment.Center;
			Detail.AddChild(rising);
		}
	}

	private bool CanAfford(Fort fort)
	{
		foreach ((string store, int amount) in fort.Cost)
		{
			if (Province.Stored(store) < amount)
			{
				return false;
			}
		}

		return true;
	}

	private string NameOf(string key) => Find(key)?.Name ?? key;

	/// <summary>The ladder the chosen fort stands on, read left to right with the chosen rung lit:
	/// what a province can raise next, and what it is working towards.</summary>
	private Control BuildLadder()
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		row.AddThemeConstantOverride("separation", 4);

		List<Fort> rungs = _ladders.Find(ladder => ladder.Key == _chosen.Material).Forts;
		for (int step = 0; step < rungs.Count; step++)
		{
			if (step > 0)
			{
				Label arrow = Line("›", 15, Dim);
				arrow.VerticalAlignment = VerticalAlignment.Center;
				row.AddChild(arrow);
			}

			bool here = rungs[step].Key == _chosen.Key;
			Label rung = Line(rungs[step].Short, 13, here ? Bright : Dim);
			rung.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(rung);
		}

		return row;
	}

	/// <summary>Pays for the wall and puts the masons on it. The turn does the rest; what stands in
	/// the province does not change until they are finished.</summary>
	private void Build()
	{
		if (_chosen == null || Province.Building.Length > 0 ||
			_chosen.Key == Province.Fortification || !CanAfford(_chosen))
		{
			return;
		}

		foreach ((string store, int amount) in _chosen.Cost)
		{
			Province.Add(store, -amount);
		}

		Province.Building = _chosen.Key;
		// Priced in hand-seasons: the quoted seasons are what it takes at the nominal gang of masons,
		// and the lord decides whether it gets them.
		Province.BuildLeft = _chosen.Seasons * GameBalance.Engine.MasonsPerBuildSeason;
		Province.BuildSeasonsLeft = _chosen.Seasons;
		Refresh();
	}

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
