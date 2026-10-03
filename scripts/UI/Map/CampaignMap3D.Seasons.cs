using Godot;

/// <summary>The light and the weather over the map: the season's sun, sky and haze turned from one
/// to the next, nightfall, and the rain.</summary>
public partial class CampaignMap3D
{
	/// <summary>How far the shadows reach, as a multiple of the camera's distance: at the far zoom's
	/// steep pitch the far edge of the screen lies about 1.5 distances deep, at the near zoom's low
	/// one several.</summary>
	private const float ShadowReach = 1.6f;
	private const float ShadowReachLow = 4.5f;

	/// <summary>Where the haze begins and where it is thickest, as multiples of the camera's
	/// distance: past the ground on the screen's lower half, which the lord is looking at.</summary>
	private const float HazeFrom = 1.15f;
	private const float HazeTo = 2.4f;

	/// <summary>What a season does to the light over the map: the sun's colour and strength, the sky
	/// it comes out of, and the haze on the horizon. The ground and the sea are seasoned by their own
	/// shaders; this is the weather over them. FogDensity is how thick the haze is at its far end,
	/// 0..1 (depth fog: see HazeFrom) — spring's mist and winter's murk thicker than summer's.</summary>
	private record SeasonLight(Color Sun, float Energy, Color SkyTop, Color SkyHorizon, Color Fog, float FogDensity);

	// Season enum order: spring, summer, autumn, winter.
	private static readonly SeasonLight[] LightBySeason =
	{
		new(new("ffeccf"), 1.30f, new("31558a"), new("9db5c4"), new("aec2d2"), 0.65f),
		new(new("fff2d8"), 1.35f, new("2b4a74"), new("8aa0b4"), new("9fb4c8"), 0.55f),
		new(new("ffdba8"), 1.18f, new("3a5470"), new("c2a681"), new("bfae95"), 0.75f),
		new(new("dfeaff"), 0.92f, new("4c5d74"), new("c3ccd4"), new("cbd6df"), 0.85f),
	};

	/// <summary>Turns the whole map over to a season: the ground, the sea, what grows on it and the
	/// light it all stands in. Called on every turn change, from behind the turn curtain, so the
	/// change is never seen happening.</summary>
	public void SetSeason(Season season) => TurnSeason(season, season, 1f);

	/// <summary>The map part of the way from one season into the next, 0 to 1: the ground, the
	/// leaves, the grass, the sea and the light all blend, so a season turns over across the night
	/// and the dawn rather than switching at one instant. The year goes round — winter turns into
	/// the spring after it, not back through the whole year.</summary>
	public void TurnSeason(Season from, Season to, float along)
	{
		float span = ((int)to - (int)from + 4) % 4;
		float at = (int)from + (span * Mathf.Clamp(along, 0f, 1f));
		_ground?.SetShaderParameter("season", at);
		_water.SetSeason(at % 4f);
		_decoration.SetSeason(at);
		_grass?.SetSeason(at);
		if (along >= 1f)
		{
			_clouds.SetSeason(to);
		}

		SeasonLight a = LightBySeason[(int)from];
		SeasonLight b = LightBySeason[(int)to];
		float t = span == 0 ? 1f : Mathf.Clamp(along, 0f, 1f);
		_day = new SeasonLight(a.Sun.Lerp(b.Sun, t), Mathf.Lerp(a.Energy, b.Energy, t), a.SkyTop.Lerp(b.SkyTop, t),
			a.SkyHorizon.Lerp(b.SkyHorizon, t), a.Fog.Lerp(b.Fog, t), Mathf.Lerp(a.FogDensity, b.FogDensity, t));
		_sky.SkyTopColor = _day.SkyTop;
		_sky.SkyHorizonColor = _day.SkyHorizon;
		_environment.FogDensity = _day.FogDensity;
		Nightfall(_dark);
	}

	/// <summary>How far into the night the map is, 0 day to 1 the dead of night: the sun sinks and
	/// cools to moonlight, the whole scene darkens, and the haze goes blue. The turn passes at the
	/// darkest point (CampaignMapPage.TurnTheSeason), and dawn breaks on the new season.</summary>
	public void Nightfall(float dark)
	{
		_dark = dark;
		if (_day == null)
		{
			return;
		}

		_sun.LightEnergy = Mathf.Lerp(_day.Energy, _day.Energy * NightSun, dark);
		_sun.LightColor = _day.Sun.Lerp(Moonlight, dark);
		_environment.TonemapExposure = Mathf.Lerp(1f, NightExposure, dark);
		_environment.FogLightColor = _day.Fog.Lerp(Moonlight.Darkened(0.5f), dark);
	}

	private SeasonLight _day;
	private MapGrass _grass;

	/// <summary>Where the map's grass is sown from: fixed, so every session sees the same meadow.</summary>
	private const ulong GrassSeed = 1268;
	private float _dark;

	/// <summary>The night the turn passes in: how much of the sun is left, what colour it has gone,
	/// and how dark the whole scene is taken down to.</summary>
	private const float NightSun = 0.15f;
	private const float NightExposure = 0.45f;
	private static readonly Color Moonlight = new("7d93c4");

	/// <summary>A storm over a map pixel, for the few seasons the weather is the news.</summary>
	public void Rain(Vector2 mapPixel) => _rain.Shower(MapToWorld(mapPixel));

	public void StopRain() => _rain.StopAll();

	// --- world building ------------------------------------------------------------------

	private void BuildEnvironment()
	{
		_sky = new ProceduralSkyMaterial
		{
			SkyTopColor = new Color("2b4a74"),
			SkyHorizonColor = new Color("8aa0b4"),
			GroundHorizonColor = new Color("6b7280"),
			SunAngleMax = 24.0f,
		};
		_environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = _sky },
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightEnergy = 0.45f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
			// A haze over the far country only: it begins past the ground the lord is looking at
			// (UpdateCamera sets where, by the zoom), so what is near stays sharp and bright and the
			// land toward the horizon goes soft and pale.
			FogEnabled = true,
			FogMode = Godot.Environment.FogModeEnum.Depth,
			FogLightColor = new Color("c3ccd3"),
			FogDensity = 0.7f,
			FogDepthCurve = 1.2f,
			FogSkyAffect = 0.6f,
			FogAerialPerspective = 0.3f,
			SsaoEnabled = true,
			SsaoRadius = 1.8f,
			SsaoIntensity = 1.2f,
			// The island reflected in the sea round it. Only reaches opaque surfaces, which is why the
			// water gave up its transparency for it.
			SsrEnabled = true,
			SsrMaxSteps = 64,
			SsrFadeIn = 0.15f,
			SsrFadeOut = 2.0f,
		};
		AddChild(new WorldEnvironment { Environment = _environment });

		// Low sun: long shadows off the ridge are what make the relief read as relief.
		_sun = new DirectionalLight3D
		{
			LightEnergy = 1.55f,
			LightColor = new Color("fff0cf"),
			ShadowEnabled = true,
			// How far the shadows reach is set with the zoom, in UpdateCamera, and the cascades are laid
			// over the stretch of ground the camera can actually see. The default splits spent most of
			// their cascades on the air between the lens and the land, drew every shadow on screen from
			// the coarsest, and let them stop at a fixed distance, which from the default height was
			// two thirds of the way up the screen: a line with shadow on one side and none on the
			// other, dragged across the island by every pan. The cascades are blended into each other
			// for the same reason, and the shadows fade out past the top of the screen, not on it.
			// Two, not four: every cascade over this ground draws the woods into it again, and four
			// cost 2.2 ms a frame more than two (measured at 1920x1080) for sharper shadows nobody can
			// tell apart at map height. Two still give the view twice the old resolution.
			DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits,
			DirectionalShadowSplit1 = 0.72f,
			DirectionalShadowBlendSplits = true,
			DirectionalShadowFadeStart = 0.97f,
			ShadowBlur = 1.4f,
			ShadowBias = 0.15f,
			ShadowNormalBias = 1.5f,
		};
		_sun.RotationDegrees = new Vector3(-42, -38, 0);
		AddChild(_sun);
	}
}
