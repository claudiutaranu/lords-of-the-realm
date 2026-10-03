using System;
using Godot;

/// <summary>What a room hangs on its walls: the signs that choose what is shown, lit when chosen,
/// and the stepper every order is counted on.</summary>
public partial class RoomPage
{
	/// <summary>Hangs one sign in the room, the way a smith labels his own wall. Signs are children
	/// of the page rather than of a container, anchored by the fractions in SignSpots, so each stays
	/// over its own corner however the window is shaped. A key with no spot picked for it yet hangs
	/// nowhere rather than landing in the middle of the room.</summary>
	protected Button HangSign(string key, string name, string icon, string blurb, Action pressed)
	{
		if (!SignSpots.TryGetValue(key, out Vector2 spot))
		{
			return null;
		}

		// The button's own icon and label, not a container laid inside it: a Button centres those
		// itself, where a child container has to be given a size and quietly sat in the corner when
		// it was not.
		var sign = new Button
		{
			TooltipText = blurb,
			Text = name.ToUpperInvariant(),
			Icon = GD.Load<Texture2D>($"{IconDirectory}/{icon}.png"),
			Alignment = HorizontalAlignment.Center,
			IconAlignment = HorizontalAlignment.Left,
			ExpandIcon = false,
		};
		sign.AddThemeFontSizeOverride("font_size", 20);
		sign.AddThemeColorOverride("font_color", Cream);
		sign.AddThemeColorOverride("font_hover_color", new Color(1f, 0.92f, 0.72f));
		sign.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.92f, 0.72f));
		sign.AddThemeConstantOverride("h_separation", 12);
		sign.AddThemeConstantOverride("icon_max_width", 34);
		sign.Pressed += pressed;
		AddChild(sign);

		_signs[key] = sign;
		DressSign(key, lit: false);

		// Measured after it is dressed, so the frame and its padding are counted into the width.
		Chrome.Anchor(sign, spot, sign.GetCombinedMinimumSize().X, 64);
		return sign;
	}

	/// <summary>Lights one sign and puts every other one back on the wall. A null key lights none.</summary>
	protected void LightSign(string key)
	{
		foreach (string hanging in _signs.Keys)
		{
			DressSign(hanging, hanging == key);
		}
	}

	private void DressSign(string key, bool lit) => Chrome.DressPlaque(_signs[key], lit);

	/// <summary>A number the player sets: the plates either side, the reading between them, and a
	/// slider running to what the room will allow — men the province can raise, sacks the purse can
	/// pay for. <paramref name="settled"/> is called with the new number once it has stopped moving,
	/// and a page rebuilds its panel from there.</summary>
	/// <param name="floor">The lowest the number may go. It defaults to one step, because an order
	/// for nothing is not an order — but an allocation of nobody is a real answer, so a room that
	/// hands out its own people passes zero.</param>
	protected Control Stepper(string label, int value, int step, int ceiling, Action<int> settled, int floor = -1)
	{
		floor = floor < 0 ? step : floor;
		ceiling = Mathf.Max(floor, ceiling);
		value = Mathf.Clamp(value, floor, ceiling);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 10);
		if (label != null)
		{
			Label name = Line(label, 16, Soft);
			name.VerticalAlignment = VerticalAlignment.Center;
			row.AddChild(name);
		}

		int current = value;
		int bound = ceiling;
		int bottom = floor;
		row.AddChild(Plate("−", 44, () => settled(Mathf.Clamp(current - step, bottom, bound))));

		Label reading = Line(value.ToString("N0"), 23, Bright);
		reading.HorizontalAlignment = HorizontalAlignment.Center;
		reading.CustomMinimumSize = new Vector2(96, 0);
		reading.VerticalAlignment = VerticalAlignment.Center;
		row.AddChild(reading);

		row.AddChild(Plate("+", 44, () => settled(Mathf.Clamp(current + step, bottom, bound))));

		var slider = new HSlider
		{
			MinValue = floor,
			MaxValue = ceiling,
			Step = step,
			Value = value,
			Editable = ceiling > floor,
			TooltipText = $"Up to {ceiling:N0}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(130, 0),
		};

		slider.DragStarted += () => _dragging = true;
		slider.DragEnded += _ =>
		{
			_dragging = false;
			settled((int)slider.Value);
		};

		slider.ValueChanged += moved =>
		{
			reading.Text = ((int)moved).ToString("N0");

			// A wheel or an arrow key moves it without ever grabbing it, and those have to settle
			// the panel themselves.
			if (!_dragging)
			{
				settled((int)moved);
			}
		};

		row.AddChild(slider);
		return row;
	}
}
