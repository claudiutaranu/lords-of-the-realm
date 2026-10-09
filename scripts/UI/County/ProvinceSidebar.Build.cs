using Godot;

/// <summary>The sidebar put together once: the crest and header, the stat row, the stores, the
/// armoury, the labour bar, the march button, and the plate shown for a county that is not the
/// lord's.</summary>
public partial class ProvinceSidebar
{
	// --- building the thing ------------------------------------------------------------------

	private void BuildHeader()
	{
		var content = new HBoxContainer();
		content.AddThemeConstantOverride("separation", 10);
		AddChild(Framed(content, 10));

		_accent = new ColorRect { CustomMinimumSize = new Vector2(4, 0) };
		content.AddChild(_accent);

		_crest = new TextureRect
		{
			CustomMinimumSize = new Vector2(42, 50),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
		};
		content.AddChild(_crest);

		// Expanding, or the name gets the narrowest box the text will fit in.
		var titles = new VBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		titles.AddThemeConstantOverride("separation", 2);
		content.AddChild(titles);

		// One line, always: a name long enough to wrap would make the header taller and push
		// everything under it down the moment the selection changed.
		_name = new Label
		{
			Text = "Select a stronghold",
			TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
			ThemeTypeVariation = "GildedTitle", // the title font and its outline; the gradient is below
		};
		_name.AddThemeFontSizeOverride("font_size", 23);
		GoldTitle.Apply(_name);
		titles.AddChild(_name);

		_realm = Small("", Chrome.Soft);
		titles.AddChild(_realm);
	}

	// Population, loyalty, tax, ration: the four numbers a lord is judged on, in one strip.
	private void BuildStats()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);
		_held.AddChild(Framed(row, 8));

		// 20 and 14 rather than 22 and 16: tax and ration read as words, not numbers, and at the
		// larger size the four cells together are wider than the sidebar is — which pushed the whole
		// sidebar out every time the selection landed on a province you hold.
		_population = StatCell(row, Icon("population", 20), () => PeoplePressed?.Invoke(),
			"See how this county's people have fared, season by season");
		row.AddChild(Divider());
		_loyalty = StatCell(row, Icon("heart", 20), () => LoyaltyPressed?.Invoke(),
			"See where this county's goodwill went");
		row.AddChild(Divider());
		_tax = StatCell(row, Icon("gold", 20), () => TaxPressed?.Invoke(), "Set what this county pays");
		row.AddChild(Divider());
		_ration = StatCell(row, Icon("food", 20), () => RationPressed?.Invoke(),
			"Set what this county is given to eat");
	}

	private Label StatCell(HBoxContainer row, Control icon, System.Action pressed = null, string hint = "")
	{
		var cell = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		cell.AddThemeConstantOverride("separation", 5);
		cell.AddChild(icon);

		var value = new Label { VerticalAlignment = VerticalAlignment.Center };
		value.AddThemeFontSizeOverride("font_size", 17);
		value.AddThemeColorOverride("font_color", Text);
		cell.AddChild(value);

		row.AddChild(cell);
		if (pressed == null)
		{
			return value;
		}

		// A cell that can be pressed has to say so before it is pressed, and the cursor cannot say
		// it — the game draws the same sword over everything. So the number itself answers the
		// mouse: it brightens under the pointer, the way the plaques in the rooms do.
		cell.MouseFilter = MouseFilterEnum.Stop;
		cell.TooltipText = hint;
		cell.MouseEntered += () => value.AddThemeColorOverride("font_color", Pointed);
		cell.MouseExited += () => value.AddThemeColorOverride("font_color", Resting(value));
		cell.GuiInput += pointer =>
		{
			if (pointer is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
			{
				pressed();
			}
		};

		return value;
	}

	// What the province holds, and under it what next turn adds to it.
	private void BuildResources()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);
		_held.AddChild(Framed(row, 8));

		bool first = true;
		foreach ((ResourceType type, string icon) in Resources)
		{
			if (!first)
			{
				row.AddChild(Divider());
			}

			first = false;

			var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cell.AddThemeConstantOverride("separation", 2);
			_stockIcon[type] = Icon(icon, 34);
			cell.AddChild(Centered(_stockIcon[type]));

			var stock = new Label { HorizontalAlignment = HorizontalAlignment.Center };
			stock.AddThemeFontSizeOverride("font_size", 24);
			stock.AddThemeColorOverride("font_color", Text);
			cell.AddChild(stock);

			Label yield = Small("", Gain);
			yield.HorizontalAlignment = HorizontalAlignment.Center;
			cell.AddChild(yield);

			_stock[type] = stock;
			_yield[type] = yield;
			row.AddChild(cell);

			// A click on the cow or the basket asks after that store, as in the original.
			cell.MouseFilter = MouseFilterEnum.Stop;
			cell.TooltipText = "Ask after this store";
			cell.MouseEntered += () => stock.AddThemeColorOverride("font_color", Pointed);
			cell.MouseExited += () => stock.AddThemeColorOverride("font_color", Text);
			ResourceType asked = type;
			cell.GuiInput += pointer =>
			{
				if (pointer is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				{
					StorePressed?.Invoke(asked);
				}
			};
		}
	}

	/// <summary>What the county has in its armoury, and what the smithy adds to it a season. This is
	/// where the muster used to stand, and it earns the room better: men raised in the yard leave
	/// the county, weapons stay in it until somebody is given them.</summary>
	private void BuildArmoury()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 2);

		foreach ((string weapon, string icon) in Arms)
		{
			Control divider = null;
			if (_armoury.Count > 0)
			{
				divider = Divider();
				row.AddChild(divider);
			}

			var cell = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			cell.AddThemeConstantOverride("separation", 2);
			cell.AddChild(Centered(Icon(icon, 34)));

			var count = new Label { HorizontalAlignment = HorizontalAlignment.Center };
			count.AddThemeFontSizeOverride("font_size", 24);
			count.AddThemeColorOverride("font_color", Text);
			cell.AddChild(count);

			Label yield = Small("", Gain);
			yield.HorizontalAlignment = HorizontalAlignment.Center;
			cell.AddChild(yield);

			_armoury.Add((divider, cell, count, yield, weapon));
			row.AddChild(cell);
		}

		for (int i = 0; i < Resources.Length; i++)
		{
			var room = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
			_armouryRoom.Add(room);
			row.AddChild(room);
		}

		_armouryFrame = Framed(row, 8);
		_held.AddChild(_armouryFrame);
	}

	/// <summary>The labour bar, on the panel the lord is looking at his county from — which is where
	/// Lords of the Realm kept it. The province's own screen carries one too; neither remembers
	/// anything, both read the allocation, so they cannot drift apart.</summary>
	private void BuildLabour()
	{
		_labour = new LabourBar();
		_labourFrame = Framed(_labour, 8);
		_held.AddChild(_labourFrame);
		// Nothing on this panel follows the grip while it moves — the bar keeps its own two counts —
		// so only the letting go is worth a redraw, and that is when the forecasts change.
		_labour.Settled += Refresh;
	}

	/// <summary>What stands in place of the books for a province you do not hold: the keep on its
	/// hill, unlit, and a line saying why there are no numbers under it. A column of dashes where
	/// the stores should be reads as a province with nothing in it rather than as one you cannot
	/// see into.</summary>
	private void BuildForeign()
	{
		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 8);

		// Covered and expanding: the panel runs the whole height the books would have taken, and the
		// keep is cropped to fill it rather than leaving a dark gap under a small picture.
		stack.AddChild(new TextureRect
		{
			Texture = GD.Load<Texture2D>(UnclaimedArtPath),
			CustomMinimumSize = new Vector2(0, 190),
			SizeFlagsVertical = SizeFlags.ExpandFill,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
		});

		_word = Small("No word reaches you of what it holds.", Chrome.Soft);
		_word.AutowrapMode = TextServer.AutowrapMode.Word;
		_word.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(_word);

		_foreign = Framed(stack, 8);
		_foreign.SizeFlagsVertical = SizeFlags.ExpandFill;
		_foreign.Visible = false;
		AddChild(_foreign);
	}
}
