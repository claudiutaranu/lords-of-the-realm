using System.Collections.Generic;
using Godot;

/// <summary>What the chosen squads mean to do, painted on the ground under them in broad strips of worn
/// paint, lit like the turf so shadows and woods darken them (battlefield-guide.gdshader): how far a squad that shoots can carry (a red fan ahead of it, as far as the reach the
/// battle measures its shots by, FieldSquad.Range from the standard), and where each has been sent
/// — a gold line to the spot it marches to, ending in an arrowhead, or a red one to the enemy it
/// falls on. Only for squads in hand, so the field is not scored with lines, and laid again every
/// frame as they move. Each squad's paint fades in when it is taken in hand or given an order, stands a
/// moment and fades away, so the field is not left painted over (the user's call).</summary>
public partial class BattlefieldSquads
{
	/// <summary>How many short straight pieces bend the reach's arc round and over the ground, how long a
	/// piece of a line is at most, and how far over the turf the strips lie.</summary>
	private const int FanPieces = 60;

	/// <summary>How far either side of the way a squad faces its reach is drawn: the arc of what it
	/// can loose at without turning.</summary>
	private const float FanHalf = FieldBattle.ShootsWithin;

	/// <summary>How strongly the fan's two sides are painted against its arc: the arc is the reach,
	/// the sides only say which way it lies.</summary>
	private const float FanSidesStrength = 0.5f;

	private float _strength = 1f;
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

	/// <summary>How long a squad's guides stand at full strength, and how long they take to fade.</summary>
	private const float GuideComes = 0.25f;
	private const float GuideStands = 1.5f;
	private const float GuideFades = 0.8f;

	/// <summary>When each squad's guides were last laid afresh, and what it was then doing, on the
	/// wall clock so they fade while the battle is paused too.</summary>
	private readonly Dictionary<FieldSquad, (float Since, Vector2? Goal, FieldSquad Target)> _guidedSince = new();
	private float _fade = 1f;

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
		float now = Time.GetTicksMsec() / 1000f;
		// A squad let go and taken in hand again is a fresh choice: its guides come back.
		foreach (FieldSquad gone in new List<FieldSquad>(_guidedSince.Keys))
		{
			if (!chosen.Contains(gone))
			{
				_guidedSince.Remove(gone);
			}
		}

		bool begun = false;
		foreach (FieldSquad squad in chosen)
		{
			if (!squad.IsStanding)
			{
				continue;
			}

			// Laid afresh when it is first in hand, and again on every new order.
			if (!_guidedSince.TryGetValue(squad, out (float Since, Vector2? Goal, FieldSquad Target) laid)
				|| IsNewOrder(laid.Goal, squad.Goal) || laid.Target != squad.Target)
			{
				laid = (now, squad.Goal, squad.Target);
				_guidedSince[squad] = laid;
			}

			// Faded in, held, faded out.
			float age = now - laid.Since;
			_fade = Mathf.Min(Mathf.Clamp(age / GuideComes, 0f, 1f), 1f - Mathf.Clamp((age - GuideStands) / GuideFades, 0f, 1f));
			if (_fade <= 0f)
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
				Fan(squad.At, squad.Facing, squad.Range, ReachRed);
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

	/// <summary>Whether a squad has been sent somewhere new, not merely had its spot nudged as it goes.</summary>
	private static bool IsNewOrder(Vector2? was, Vector2? now) =>
		was.HasValue != now.HasValue || (was is Vector2 a && now is Vector2 b && a.DistanceTo(b) > NewOrderBeyond);

	private const float NewOrderBeyond = 3f;

	/// <summary>The reach of a squad that shoots, ahead of it and not all round: an arc as far off as
	/// its bows carry across the way it faces, and its two sides drawn back to the squad. It turns as
	/// the squad does, and a squad turns to face whatever comes at it.</summary>
	private void Fan(Vector2 middle, Vector2 facing, float radius, Color colour)
	{
		float ahead = Mathf.Atan2(facing.Y, facing.X);
		float from = ahead - FanHalf;
		float piece = 2f * FanHalf / FanPieces;
		for (int i = 0; i < FanPieces; i++)
		{
			Vector2 outFrom = new(Mathf.Cos(from + (i * piece)), Mathf.Sin(from + (i * piece)));
			Vector2 outTo = new(Mathf.Cos(from + ((i + 1) * piece)), Mathf.Sin(from + ((i + 1) * piece)));
			Strip(middle + (outFrom * radius), middle + (outTo * radius), outFrom, outTo, colour);
		}

		foreach (float side in new[] { from, ahead + FanHalf })
		{
			Vector2 way = new(Mathf.Cos(side), Mathf.Sin(side));
			var across = new Vector2(-way.Y, way.X);
			int pieces = Mathf.CeilToInt(radius / LinePiece);
			for (int i = 0; i < pieces; i++)
			{
				Strip(middle + (way * (radius * i / pieces)), middle + (way * (radius * (i + 1) / pieces)), across, across, colour,
					FanSidesStrength);
			}
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

		_strength = 1f;

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
	private void Strip(Vector2 from, Vector2 to, Vector2 outFrom, Vector2 outTo, Color colour, float strength = 1f)
	{
		_strength = strength;
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
		// How strongly this piece is painted, carried to the shader in the UV: the colour's alpha already
		// says where the strip's edge is.
		_guideMesh.SurfaceSetUV(new Vector2(_strength * _fade, 0f));
		_guideMesh.SurfaceSetColor(colour);
		_guideMesh.SurfaceAddVertex(at);
	}

	private Vector3 OnTurf(Vector2 at) => Ground(at) + (Vector3.Up * GuideLift);
}
