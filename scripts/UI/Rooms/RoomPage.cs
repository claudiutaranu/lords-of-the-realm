using System;
using System.Collections.Generic;
using Godot;

/// <summary>A room of a province, opened over the campaign map: the smithy, the training yard, the
/// market. They differ in what they are for — one takes an order and makes you wait for it, another
/// trades across a counter on the spot — but they are all the same room seen from the door: film
/// underneath, the province's stores along the top, a title, signs hung over what the room offers,
/// and a panel in the corner reading whatever is chosen.
///
/// That frame is all this class is. What the room does with a choice is the page's own business.
///
/// It opens over the map rather than replacing it, so the turn, the camera and the selection are
/// exactly where they were when it closes.</summary>
public abstract partial class RoomPage : Control
{
	// The game's handwriting is Chrome's, so the hall and the rooms are lettered and framed alike.
	// These are here only so a page can reach them without naming Chrome on every line.
	protected const string IconDirectory = Chrome.IconDirectory;

	protected static readonly Color Cream = Chrome.Cream;
	protected static readonly Color Dim = Chrome.Dim;
	protected static readonly Color Short = Chrome.Short;
	protected static readonly Color Bright = Chrome.Bright;
	protected static readonly Color Soft = Chrome.Soft;

	/// <summary>Raised when the player shuts the room, so the map can drop this page and refresh
	/// whatever happened in here.</summary>
	public event Action Closed;

	protected ProvinceEconomy Province { get; private set; }

	/// <summary>The row across the foot of the page: whatever a room lays its choices out in goes
	/// here, to the left of the panel that reads them.</summary>
	protected HBoxContainer Body { get; private set; }

	/// <summary>The column inside the panel in the corner. A page empties it and fills it again
	/// whenever what it reads has changed.</summary>
	protected VBoxContainer Detail { get; private set; }

	private readonly Dictionary<string, Button> _signs = new();
	/// <summary>True between a slider's grab and its release. A page rebuilds its panel from scratch
	/// whenever the number changes, which would free the slider under the hand still dragging it, so
	/// while that hand is down only the reading moves and the rebuild waits for the release.</summary>
	private bool _dragging;
	private ResourceBar _stores;
	private Label _provinceLabel;

	// --- what each room says for itself --------------------------------------------------------

	protected abstract string RoomName { get; }

	/// <summary>A line under the room's name, where it has one.</summary>
	protected virtual string Tagline => null;

	/// <summary>Whether the room wears the big gilded title across the top. Most do: a room is a
	/// place you have stepped into and it says so. A screen that is mostly a picture to be read —
	/// the province and everything standing on it — says its name in the corner instead, because a
	/// title across the middle of the land is a title across the land.</summary>
	protected virtual bool ShowsTitle => true;

	/// <summary>Where each sign hangs, in the video frame's own proportions — over the thing it
	/// names. Read off a still of the film rather than laid out by a container: the player is
	/// choosing off the room itself, not off a list beside it.</summary>
	protected abstract Dictionary<string, Vector2> SignSpots { get; }

	/// <summary>Reads whatever the room offers, before anything is built from it.</summary>
	protected abstract void Load();

	/// <summary>How the room offers its choices. A wall of signs, a row of cards — each room says.</summary>
	protected abstract void BuildChoosers();

	/// <summary>Told the room has just been opened, so it can settle on a first choice.</summary>
	protected abstract void Opened();

	/// <summary>Fills the panel in the corner with whatever is chosen now.</summary>
	protected abstract void ShowDetail();

	// --- the page ------------------------------------------------------------------------------

	public override void _Ready()
	{
		// A few seconds of the room, played back to back: it should never stop moving. Not every
		// room is filmed, though — a view over a valley is a painting, and holds still — so a scene
		// that puts something other than a player behind itself is left alone.
		var background = GetNodeOrNull<VideoStreamPlayer>("Background");
		if (background != null)
		{
			background.Finished += background.Play;
		}

		Load();
		BuildChrome();
	}

	/// <summary>Escape shuts the room, the same as the button in the corner. The room is the last
	/// thing added over the map, so it is offered the key first and swallows it — the map behind
	/// never sees the press that closed what was on top of it.</summary>
	public override void _UnhandledKeyInput(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			GetViewport().SetInputAsHandled();
			Closed?.Invoke();
		}
	}

	/// <summary>Opens the room for one province. Everything on the page is read against it.</summary>
	public void Open(ProvinceEconomy province)
	{
		Province = province;
		_provinceLabel.Text = province.ProvinceName;
		Opened();
		Refresh();
	}

	/// <summary>Re-reads the province: its stores along the top, and the panel in the corner.</summary>
	public virtual void Refresh()
	{
		_stores.Show(Province);
		ShowDetail();
	}

	/// <summary>Shuts the room, from inside it for a page that has its own way out, or from the map
	/// when everything over it is being put away.</summary>
	public void Close() => Closed?.Invoke();

	/// <summary>How wide the reading panel stands. The yard's is narrower: eight cards share its row.</summary>
	protected virtual int DetailWidth => 470;

	/// <summary>Empties the panel in the corner, for a page about to fill it again.</summary>
	protected void ClearDetail()
	{
		foreach (Node child in Detail.GetChildren())
		{
			child.QueueFree();
		}
	}

	// --- small parts ---------------------------------------------------------------------------

	protected static PanelContainer Framed(Control content, int margin) => Chrome.Framed(content, margin);

	protected static StyleBoxFlat CardStyle(Color background) => Chrome.CardStyle(background);

	protected static Label Line(string text, int size, Color color) => Chrome.Line(text, size, color);

	protected static TextureRect Icon(string name, int side) => Chrome.Icon(name, side);

	protected static Button Plate(string text, int side, Action pressed) => Chrome.Plate(text, side, pressed);
}
