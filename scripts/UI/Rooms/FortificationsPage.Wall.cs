using System.Collections.Generic;
using Godot;

/// <summary>The painted wall behind the counters: the rung on show, its living layer, and the fade
/// from one to the next with the art loaded as it is wanted.</summary>
public partial class FortificationsPage
{
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
			// A file nobody asked for on the way in is loaded here and now rather than coming back
			// as nothing: an empty texture is a valley with no walls in it.
			held = ResourceLoader.LoadThreadedGetStatus(path) == ResourceLoader.ThreadLoadStatus.InvalidResource
				? GD.Load<Resource>(path)
				: ResourceLoader.LoadThreadedGet(path);
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
}
