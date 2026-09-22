using System.Collections.Generic;
using Godot;

/// <summary>Weather over the campaign map: a handful of noise-shaped sheets drifting above the
/// terrain, each fading in and out on its own slow cycle, so cloud cover is never the same twice
/// and never obviously repeats. How much of it there is depends on the season — a clear summer, an
/// overcast winter — and the change is folded in gradually rather than switching on the turn.</summary>
public partial class MapClouds : Node3D
{
	private const string CloudShaderPath = "res://assets/shaders/cloud.gdshader";
	private const float CoverChangeRate = 0.25f; // how fast the sky answers a change of season
	// Everything below is a fraction of the map it is drawn over, so the same weather works on a
	// small campaign map and a large one without a number being retuned.
	private const float CloudsPerSquareUnit = 1f / 1800f;
	// Clouds belong above the highest ground, so this is a multiple of the map's own relief, not of
	// its width — a wide map with low hills does not need a higher sky. It cannot go much past this
	// either: at closest zoom the camera itself is only about 40 units up, and clouds above it would
	// cover the screen.
	private const float PeakClearance = 1.5f;
	private const float FieldFactor = 1.05f;      // how far from the camera clouds are kept
	// A cloud fades across most of the field rather than in a band at its rim. The rim is crossed
	// by the wind in a minute and a half, but by a panning camera in half a second, and it was that
	// difference that made clouds blink in and out while moving.
	private const float EdgeFadeFactor = 0.3f;

	private class Cloud
	{
		public MeshInstance3D Mesh;
		public ShaderMaterial Material;
		public Vector3 World;    // where the cloud actually is; the wind moves it, the camera does not
		public Vector3 Drift;
		public float FadeRate;
		public float Phase;
	}

	private readonly List<Cloud> _clouds = new();
	private readonly RandomNumberGenerator _rng = new();
	private Vector2 _mapSize;
	private Vector3 _focus = Vector3.Zero;
	private float _fieldRadius = 100f;
	private float _cover = 0.4f;
	private float _targetCover = 0.4f;
	private Color _tint = new("ffffff");
	private double _elapsed;

	public void Build(Vector2 mapSize, float peakHeight)
	{
		_mapSize = mapSize;
		float altitude = peakHeight * PeakClearance;
		_fieldRadius = Mathf.Max(mapSize.X, mapSize.Y) * FieldFactor;
		_rng.Seed = 8412;

		// Cloud count follows the map's area: a bigger realm needs more sky filled, and neither
		// number has to be touched when a new campaign brings a map of its own size.
		int cloudCount = Mathf.Clamp(Mathf.RoundToInt(mapSize.X * mapSize.Y * CloudsPerSquareUnit), 12, 34);
		for (int i = 0; i < cloudCount; i++)
		{
			var material = new ShaderMaterial { Shader = GD.Load<Shader>(CloudShaderPath) };
			material.SetShaderParameter("seed", _rng.RandfRange(0f, 100f));

			float size = _rng.RandfRange(0.18f, 0.42f) * mapSize.X;
			var mesh = new MeshInstance3D
			{
				Mesh = new PlaneMesh { Size = new Vector2(size, size), Material = material },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			};
			AddChild(mesh);

			var cloud = new Cloud
			{
				Mesh = mesh,
				Material = material,
				World = new Vector3(
					_rng.RandfRange(-1f, 1f) * _fieldRadius,
					altitude + _rng.RandfRange(-2f, 7f),
					_rng.RandfRange(-1f, 1f) * _fieldRadius),
				// Everything drifts the same way — that is the prevailing wind — but at its own pace.
				Drift = new Vector3(_rng.RandfRange(0.35f, 0.9f), 0f, _rng.RandfRange(-0.25f, 0.15f)),
				FadeRate = _rng.RandfRange(0.02f, 0.055f),
				Phase = _rng.RandfRange(0f, Mathf.Tau),
			};
			_clouds.Add(cloud);
		}
	}

	/// <summary>Summer skies are open, winter's are not. Autumn and spring sit between.</summary>
	public void SetSeason(Season season)
	{
		(_targetCover, _tint) = season switch
		{
			Season.Spring => (0.58f, new Color("f4f6f8")),
			// Not a bare sky. A clear summer still has weather in it, and a map with nothing at all
			// over it reads as a diorama under glass rather than as country seen from a height.
			Season.Summer => (0.42f, new Color("fffdf6")),
			Season.Autumn => (0.7f, new Color("e8e6e2")),
			_ => (0.9f, new Color("dfe3e8")),
		};
	}

	/// <summary>Keeps the weather with the player: clouds are placed around whatever the camera is
	/// looking at, so panning never runs out of sky and never leaves an empty one behind.</summary>
	public void SetFocus(Vector3 focus)
	{
		_focus = focus;
	}

	public override void _Process(double delta)
	{
		_elapsed += delta;
		_cover = Mathf.MoveToward(_cover, _targetCover, CoverChangeRate * (float)delta);

		float fadeStart = _fieldRadius * EdgeFadeFactor;
		float period = _fieldRadius * 2f;
		foreach (Cloud cloud in _clouds)
		{
			// The wind moves clouds, and nothing else does: they hang over the ground, so panning
			// has to slide past them rather than carry them along.
			cloud.World += cloud.Drift * (float)delta;

			// The field repeats around wherever the camera looks, which is what keeps sky over the
			// player without a cloud for every corner of the world. A cloud is only ever shifted by
			// a whole period, and only once it has drifted past the fade edge, so the jump happens
			// while it is fully transparent.
			Vector3 relative = cloud.World - _focus;
			relative.X -= Mathf.Round(relative.X / period) * period;
			relative.Z -= Mathf.Round(relative.Z / period) * period;

			cloud.Mesh.Position = new Vector3(_focus.X + relative.X, cloud.World.Y, _focus.Z + relative.Z);

			// Each cloud breathes on its own cycle, so cover thins and gathers instead of sitting still.
			float breath = 0.5f + 0.5f * Mathf.Sin((float)_elapsed * cloud.FadeRate * Mathf.Tau + cloud.Phase);

			// And fades out towards the edge of the field, so panning dissolves clouds rather than
			// cutting them off.
			float distance = new Vector2(relative.X, relative.Z).Length();
			float edge = 1f - Mathf.Clamp((distance - fadeStart) / Mathf.Max(_fieldRadius - fadeStart, 0.001f), 0f, 1f);
			edge = edge * edge * (3f - 2f * edge); // smoothstep: no kink where the fade begins

			cloud.Material.SetShaderParameter("opacity", _cover * Mathf.Lerp(0.25f, 1.0f, breath) * edge);
			cloud.Material.SetShaderParameter("tint", _tint);
		}
	}
}
