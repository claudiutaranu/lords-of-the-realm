using System.Collections.Generic;
using Godot;

/// <summary>The villages and towns at the seats: the model a settlement is drawn with, dressed in
/// its lord's colours with its flag at rest, and where it stands.</summary>
public partial class MapDecoration
{
	/// <summary>The ground a seat keeps for itself, in map pixels. The town's houses stand in a
	/// ring out to TownRing (AddSettlement), and the castle is raised CastleDistance off the seat, on
	/// the flattest side of it (CastleSite), with CastleReach of ground round it. The fields are laid out
	/// from the seat outwards and used to start half a cell from the pin — in the middle of the town
	/// — because the only thing keeping them off it was the clearing the town registers when it is
	/// built, and that is built after the fields and may not be built at all. So the fields read the
	/// same figures and keep off that ground themselves, whatever else has run.</summary>
	public const float TownRing = 38f;

	/// <summary>Each seat's village, by province, so a conquest can hand its banners over, and where
	/// its yard lies on the map, which is the ground a lord walks into the town from.</summary>
	private readonly Dictionary<string, GeometryInstance3D> _settlements = new();
	private readonly Dictionary<string, Vector2> _settlementSites = new();

	/// <summary>The look of each building model drawn through settlement.gdshader — its baked colour
	/// and relief, where its flag pole stands — and which way its flag flies as it was made, on its own
	/// ground plane. One per model: the village and every rung of castle share the shader, not the
	/// numbers.</summary>
	private readonly Dictionary<string, (ShaderMaterial Look, float FlagRest)> _dressed = new();

	/// <summary>How big a place stands on a province's seat. Two rungs, not three: a walled seat is
	/// no longer a kind of town but a fortification standing beside one, because the player builds
	/// that and cannot build the town.</summary>
	public enum Settlement { Hamlet, Town }

	/// <summary>Which model a rung wears, and how big it stands. The ladder runs a timber watchtower
	/// up to a walled castle, which is the progression the fortifications page charges for — so what
	/// the map shows and what the ledger holds are the same fact.</summary>
	/// <param name="Size">How wide it stands on the ground, against a town's village: never less than
	/// the village, so the walls read from the map's height as the county's strength and not as a
	/// shed beside the houses.</param>
	private record Works(string Model, float Size);

	/// <summary>The village every seat stands in: a Meshy hamlet cut down by tools/decimate_meshy.py,
	/// drawn through settlement.gdshader so its banners fly the owner's colour and it takes the season.
	/// Its yard is sized to fill the town's ring — the fields start a lane beyond it (TownGap) and the
	/// castle stands off to one side (CastleBearing) — and a hamlet stands a size down from a town.
	/// It is sunk a hair into the ground, so the thin sheet it stands on reads as the ground itself and
	/// not as a tray laid on it.</summary>
	private const string HamletModel = "settlements/blue-banner-hamlet";
	private const string SettlementShaderPath = "res://assets/shaders/settlement.gdshader";
	private const float HamletShare = 0.85f;

	/// <summary>How much of the town's ring the village itself fills. The ring is the town for the rules
	/// (where a march halts on its gate, where its fields start); the village drawn in it stands at
	/// a little over half of that, so it sits on the map as a place and not as a stamp over the county.</summary>
	private const float VillageSize = 0.58f;
	private const float SettlementSink = 0.04f;
	/// <summary>How far a village may stand off the wind. Every banner flies with the prevailing
	/// wind, so the village is turned to put its flag in it — but eight villages all turned the one
	/// way read as one stamp, so each is set up to this far off, and the shader turns the cloth the
	/// rest of the way round its pole. Kept small because the flag, turned far enough, flies through
	/// the church tower.</summary>
	private static readonly float FlagJitter = Mathf.DegToRad(20f);

	private static Works PlanFor(string fort) => fort switch
	{
		"small-palisade" => new Works("settlements/lionwatch-palisade", 1.0f),
		"medium-fort" => new Works("settlements/fort-palisadeb", 1.05f),
		"large-fort" => new Works("settlements/fort-palisadec", 1.1f),
		"small-castle" => new Works("settlements/fort-keepa", 1.1f),
		"medium-castle" => new Works("settlements/fort-keepb", 1.15f),
		"large-castle" => new Works("settlements/fort-keepc", 1.2f),
		"grand-castle" => new Works("settlements/fort-citadel", 1.3f),
		_ => null,
	};

	/// <summary>A rung still in its scaffolding, the size it will be when it is done. The motte and
	/// the timber keep go up on the same works, and the stone castle and the king's on the same.</summary>
	private static Works UnderWay(string fort) => fort switch
	{
		"small-palisade" => new Works("settlements/fort-build-palisadea", PlanFor(fort).Size),
		"medium-fort" or "large-fort" => new Works("settlements/fort-build-palisadeb", PlanFor(fort).Size),
		"small-castle" => new Works("settlements/fort-build-keepa", PlanFor(fort).Size),
		"medium-castle" => new Works("settlements/fort-build-keepb", PlanFor(fort).Size),
		"large-castle" or "grand-castle" => new Works("settlements/fort-build-keepc", PlanFor(fort).Size),
		_ => null,
	};

	/// <summary>Raises the village on a province's seat, flying its lord's colour — or, for a seat
	/// that already has one, hands the banners to whoever holds it now. One node per seat rather than
	/// a scatter, since each flies its own colour; they share one material, and the colour is a
	/// per-instance parameter on it.</summary>
	public void AddSettlement(string province, Vector2 seatPixel, Settlement kind, Color lord)
	{
		if (_settlements.TryGetValue(province, out GeometryInstance3D standing))
		{
			standing.SetInstanceShaderParameter("lord_color", lord);
			return;
		}

		Mesh mesh = Models.MeshOf(HamletModel);
		if (mesh == null)
		{
			return;
		}

		(ShaderMaterial look, float flagRest) = Dressed(HamletModel, mesh);

		float scale = VillageYard(kind) / Models.FootprintOf(HamletModel);
		// Turned so its flag flies with the wind, give or take its own few degrees (FlagJitter). A turn
		// about y takes a direction at angle a on the ground (atan2 of z over x) to a - yaw.
		Vector2 wind = MapClouds.PrevailingWind;
		float jitter = (PlotYaw(province) / Mathf.Tau - 0.5f) * 2f * FlagJitter;
		float yaw = flagRest - Mathf.Atan2(wind.Y, wind.X) + jitter;
		var village = new MeshInstance3D
		{
			Mesh = mesh,
			MaterialOverride = look,
			Transform = new Transform3D(Basis.Identity.Rotated(Vector3.Up, yaw).Scaled(Vector3.One * scale),
				_map.WorldAt(seatPixel) + (Vector3.Down * SettlementSink)),
		};
		AddChild(village);
		village.SetInstanceShaderParameter("lord_color", lord);
		village.SetInstanceShaderParameter("flag_turn", jitter);
		_settlements[province] = village;
		_settlementSites[province] = seatPixel;

		// Wide of the yard, so a village sits in a clearing rather than in a thicket — and the ground
		// its castle will stand on cleared as well, whether or not it has one yet: the woods are laid
		// once, and a castle raised later in a wood came up inside the trees.
		_clearings.Add((seatPixel, TownRing + 14f));
		_clearings.Add((CastleSite(seatPixel), CastleReach));
	}

	/// <summary>A building model's look through settlement.gdshader, made once per model: the colour
	/// and relief tools/decimate_meshy.py baked for it, and its flag pole, which is the model's
	/// highest point — its finial.</summary>
	private (ShaderMaterial Look, float FlagRest) Dressed(string model, Mesh mesh)
	{
		if (_dressed.TryGetValue(model, out (ShaderMaterial, float) known))
		{
			return known;
		}

		var look = new ShaderMaterial { Shader = GD.Load<Shader>(SettlementShaderPath) };
		if (mesh.SurfaceGetMaterial(0) is BaseMaterial3D baked)
		{
			look.SetShaderParameter("albedo_texture", baked.AlbedoTexture);
		}

		look.SetShaderParameter("relief_texture", GD.Load<Texture2D>($"res://assets/models/{model}{ReliefSuffix}"));
		Vector3 pole = Vector3.Down * float.MaxValue;
		foreach (Vector3 vertex in mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
		{
			pole = vertex.Y > pole.Y ? vertex : pole;
		}

		look.SetShaderParameter("flag_pole", new Vector2(pole.X, pole.Z));
		_dressed[model] = (look, FlagRest(mesh, pole));
		return _dressed[model];
	}

	/// <summary>Which way a village model's flag flies as it was made: the angle on its ground plane
	/// (atan2 of z over x) from the pole to the middle of the cloth. The cloth is read off the texture's
	/// alpha, where tools/decimate_meshy.py baked how far along the flag each vertex lies, and each
	/// vertex counts for as much as it sways, so the fly end decides it more than the hoist.</summary>
	private static float FlagRest(Mesh mesh, Vector3 pole)
	{
		if (mesh.SurfaceGetMaterial(0) is not BaseMaterial3D { AlbedoTexture: Texture2D texture })
		{
			return 0f;
		}

		Image picture = texture.GetImage();
		if (picture.IsCompressed())
		{
			picture.Decompress();
		}

		Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		Vector2[] uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
		Vector2 fly = Vector2.Zero;
		for (int i = 0; i < vertices.Length; i++)
		{
			float sway = picture.GetPixel(
				Mathf.Clamp((int)(uvs[i].X * picture.GetWidth()), 0, picture.GetWidth() - 1),
				Mathf.Clamp((int)(uvs[i].Y * picture.GetHeight()), 0, picture.GetHeight() - 1)).A;
			fly += new Vector2(vertices[i].X - pole.X, vertices[i].Z - pole.Z) * sway;
		}

		return Mathf.Atan2(fly.Y, fly.X);
	}

	public string TownAt(Vector2 pixel)
	{
		foreach ((string province, Vector2 yard) in _settlementSites)
		{
			if (pixel.DistanceTo(yard) <= TownRing)
			{
				return province;
			}
		}

		return "";
	}

	/// <summary>A point that many pixels from the seat, kept inside the map.</summary>
	/// <summary>How wide a village stands, in world units.</summary>
	private float VillageYard(Settlement kind) =>
		2f * TownRing / _map.PixelsPerUnit * VillageSize * (kind == Settlement.Hamlet ? HamletShare : 1f);

	/// <summary>Like PropTransform, but for things people built: no tilt, no vertical stretch, and
	/// a yaw the caller can pin down so a roof lands on its own walls.</summary>
	private Transform3D BuildingTransform(Vector2 mapPixel, float scale, float? yaw = null)
	{
		Basis basis = Basis.Identity.Rotated(Vector3.Up, yaw ?? _rng.RandfRange(0, Mathf.Tau));
		return new Transform3D(basis.Scaled(Vector3.One * scale), _map.WorldAt(mapPixel));
	}
}
