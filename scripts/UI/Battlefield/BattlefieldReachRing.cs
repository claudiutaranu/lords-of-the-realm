using System.Collections.Generic;
using Godot;

/// <summary>How far the chosen bowmen can shoot, drawn on the ground round them: a thin broken ring,
/// laid over the rolls of the field, as wide as the reach the battle measures their shots by
/// (FieldSquad.Range, from the squad's standard). Only for squads that shoot, and only while they are
/// in hand, so the field is not scored with circles.</summary>
public partial class BattlefieldSquads
{
	/// <summary>How many pieces the ring is cut into, how much of each is drawn (the rest is the gap
	/// between dashes), how wide it is and how far over the turf it lies, in metres.</summary>
	private const int RingPieces = 96;
	private const float RingDash = 0.6f;
	private const float RingWidth = 0.35f;
	private const float RingLift = 0.15f;
	private static readonly Color RingColour = new(0.85f, 0.3f, 0.25f, 0.55f);

	private MeshInstance3D _ring;
	private ImmediateMesh _ringMesh;

	/// <summary>Lays the reach of every chosen squad that shoots, or takes it up when none is chosen.</summary>
	private void Reach(ICollection<FieldSquad> chosen)
	{
		if (_ring == null)
		{
			_ringMesh = new ImmediateMesh();
			_ring = new MeshInstance3D
			{
				Mesh = _ringMesh,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = RingColour,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				},
			};
			AddChild(_ring);
		}

		_ringMesh.ClearSurfaces();
		bool begun = false;
		foreach (FieldSquad squad in chosen)
		{
			if (!squad.Shoots || !squad.IsStanding)
			{
				continue;
			}

			if (!begun)
			{
				_ringMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
				begun = true;
			}

			Ring(squad.At, squad.Range);
		}

		if (begun)
		{
			_ringMesh.SurfaceEnd();
		}
	}

	/// <summary>One broken ring of dashes, each a flat strip across the ground.</summary>
	private void Ring(Vector2 middle, float radius)
	{
		float piece = Mathf.Tau / RingPieces;
		for (int i = 0; i < RingPieces; i++)
		{
			float from = i * piece;
			float to = from + (piece * RingDash);
			var outFrom = new Vector2(Mathf.Cos(from), Mathf.Sin(from));
			var outTo = new Vector2(Mathf.Cos(to), Mathf.Sin(to));
			Vector3 a = OnTurf(middle + (outFrom * (radius - (RingWidth / 2f))));
			Vector3 b = OnTurf(middle + (outFrom * (radius + (RingWidth / 2f))));
			Vector3 c = OnTurf(middle + (outTo * (radius + (RingWidth / 2f))));
			Vector3 d = OnTurf(middle + (outTo * (radius - (RingWidth / 2f))));
			foreach (Vector3 corner in new[] { a, b, c, a, c, d })
			{
				_ringMesh.SurfaceAddVertex(corner);
			}
		}
	}

	private Vector3 OnTurf(Vector2 at) => Ground(at) + (Vector3.Up * RingLift);
}
