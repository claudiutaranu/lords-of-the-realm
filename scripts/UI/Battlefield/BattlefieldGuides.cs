using System.Collections.Generic;
using Godot;

/// <summary>What the chosen squads mean to do, painted on the ground under them in broad strips of worn
/// paint, lit like the turf so shadows and woods darken them (battlefield-guide.gdshader): how far a squad that shoots can carry (a red ring, as wide as the reach the
/// battle measures its shots by, FieldSquad.Range from the standard), and where each has been sent
/// — a gold line to the spot it marches to, ending in an arrowhead, or a red one to the enemy it
/// falls on. Only for squads in hand, so the field is not scored with lines, and laid again every
/// frame as they move.</summary>
public partial class BattlefieldSquads
{
	/// <summary>How many short straight pieces bend the ring round and over the ground, how long a
	/// piece of a line is at most, and how far over the turf the strips lie.</summary>
	private const int RingPieces = 180;
	private const float LinePiece = 3f;
	private const float GuideLift = 0.2f;

	/// <summary>How wide a strip is, as a share of the eye's distance, so it reads the same thickness
	/// from a man's height and from over the whole field; never thinner or wider than these, in metres.</summary>
	private const float GuideWidthShare = 0.022f;
	private const float GuideNarrowest = 0.8f;
	private const float GuideWidest = 4f;

	/// <summary>How long an arrowhead is and how wide across its barbs, in strip widths.</summary>
	private const float HeadLong = 4f;
	private const float HeadWide = 3.5f;

	/// <summary>Not drawn for a squad already all but there: a stub of an arrow under its feet.</summary>
	private const float LineShortest = 2f;

	/// <summary>The paint; its alpha down the middle of a strip, falling to nothing at the edges, tells
	/// the shader where the edge is.</summary>
	private static readonly Color ReachRed = new(0.72f, 0.08f, 0.12f, 1f);
	private static readonly Color MarchGold = new(0.82f, 0.55f, 0.1f, 1f);

	private const string GuideShaderPath = "res://assets/shaders/battlefield-guide.gdshader";

	private MeshInstance3D _guides;
	private ImmediateMesh _guideMesh;
	private float _guideWide;

	/// <summary>Lays the reach and the orders of every chosen squad, or takes them up when none is chosen.</summary>
	private void Guide(ICollection<FieldSquad> chosen, float eyeFar)
	{
		if (_guides == null)
		{
			_guideMesh = new ImmediateMesh();
			_guides = new MeshInstance3D
			{
				Mesh = _guideMesh,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new ShaderMaterial { Shader = GD.Load<Shader>(GuideShaderPath) },
			};
			AddChild(_guides);
		}

		_guideMesh.ClearSurfaces();
		_guideWide = Mathf.Clamp(eyeFar * GuideWidthShare, GuideNarrowest, GuideWidest);
		bool begun = false;
		foreach (FieldSquad squad in chosen)
		{
			if (!squad.IsStanding)
			{
				continue;
			}

			Vector2? to = squad.Target is { IsStanding: true } foe ? foe.At : squad.Goal;
			bool isLine = to is Vector2 end && end.DistanceTo(squad.At) > LineShortest;
			if (!squad.Shoots && !isLine)
			{
				continue;
			}

			if (!begun)
			{
				_guideMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
				begun = true;
			}

			if (squad.Shoots)
			{
				Ring(squad.At, squad.Range, ReachRed);
			}

			if (isLine)
			{
				Arrow(squad.At, to.Value, squad.Target != null ? ReachRed : MarchGold);
			}
		}

		if (begun)
		{
			_guideMesh.SurfaceEnd();
		}
	}

	private void Ring(Vector2 middle, float radius, Color colour)
	{
		float piece = Mathf.Tau / RingPieces;
		for (int i = 0; i < RingPieces; i++)
		{
			Vector2 from = middle + (new Vector2(Mathf.Cos(i * piece), Mathf.Sin(i * piece)) * radius);
			Vector2 to = middle + (new Vector2(Mathf.Cos((i + 1) * piece), Mathf.Sin((i + 1) * piece)) * radius);
			Strip(from, to, (from - middle).Normalized(), (to - middle).Normalized(), colour);
		}
	}

	/// <summary>A line from a squad to where it is going, cut into pieces that follow the ground, and a
	/// head at the far end pointing on.</summary>
	private void Arrow(Vector2 from, Vector2 to, Color colour)
	{
		Vector2 way = (to - from).Normalized();
		var across = new Vector2(-way.Y, way.X);
		float head = _guideWide * HeadLong;
		Vector2 neck = to - (way * Mathf.Min(head, from.DistanceTo(to)));
		int pieces = Mathf.Max(1, Mathf.CeilToInt(from.DistanceTo(neck) / LinePiece));
		for (int i = 0; i < pieces; i++)
		{
			Strip(from.Lerp(neck, i / (float)pieces), from.Lerp(neck, (i + 1) / (float)pieces), across, across, colour);
		}

		// The head: a soft triangle, red-gold in the middle of its back and clear at its barbs.
		Color clear = new(colour, 0f);
		Vector2 barb = across * (_guideWide * HeadWide / 2f);
		Corner(OnTurf(neck + barb), clear);
		Corner(OnTurf(neck), colour);
		Corner(OnTurf(to), colour);
		Corner(OnTurf(neck - barb), clear);
		Corner(OnTurf(to), colour);
		Corner(OnTurf(neck), colour);
	}

	/// <summary>One piece of a strip across the ground, in three bands — clear, coloured, clear — so its
	/// edges fade instead of ending in a hard line. <paramref name="outFrom"/> and
	/// <paramref name="outTo"/> are which way is across it at each end.</summary>
	private void Strip(Vector2 from, Vector2 to, Vector2 outFrom, Vector2 outTo, Color colour)
	{
		Color clear = new(colour, 0f);
		float[] across = { -_guideWide / 2f, 0f, _guideWide / 2f };
		Color[] shade = { clear, colour, clear };
		for (int band = 0; band < 2; band++)
		{
			Vector3 a = OnTurf(from + (outFrom * across[band]));
			Vector3 b = OnTurf(from + (outFrom * across[band + 1]));
			Vector3 c = OnTurf(to + (outTo * across[band + 1]));
			Vector3 d = OnTurf(to + (outTo * across[band]));
			Corner(a, shade[band]);
			Corner(b, shade[band + 1]);
			Corner(c, shade[band + 1]);
			Corner(a, shade[band]);
			Corner(c, shade[band + 1]);
			Corner(d, shade[band]);
		}
	}

	private void Corner(Vector3 at, Color colour)
	{
		_guideMesh.SurfaceSetColor(colour);
		_guideMesh.SurfaceAddVertex(at);
	}

	private Vector3 OnTurf(Vector2 at) => Ground(at) + (Vector3.Up * GuideLift);
}
