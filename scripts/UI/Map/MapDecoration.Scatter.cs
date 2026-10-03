using System.Collections.Generic;
using Godot;

/// <summary>Many copies of one mesh laid over the ground in one draw: the transforms for props and
/// the multimesh that carries them.</summary>
public partial class MapDecoration
{
	// --- placement ---------------------------------------------------------------------------

	private Transform3D PropTransform(Vector2 mapPixel, float scale, float tiltDegrees = 0f)
	{
		Vector3 position = _map.WorldAt(mapPixel);
		Basis basis = Basis.Identity.Rotated(Vector3.Up, _rng.RandfRange(0, Mathf.Tau));
		if (tiltDegrees > 0f)
		{
			// Nothing in a wood grows perfectly plumb; a few degrees kills the plantation look.
			basis = basis.Rotated(Vector3.Right, Mathf.DegToRad(_rng.RandfRange(-tiltDegrees, tiltDegrees)))
				.Rotated(Vector3.Forward, Mathf.DegToRad(_rng.RandfRange(-tiltDegrees, tiltDegrees)));
		}

		// Trees are never all the same height: stretch the vertical separately from the spread.
		var size = new Vector3(scale, scale * _rng.RandfRange(0.85f, 1.25f), scale);
		return new Transform3D(basis.Scaled(size), position);
	}

	/// <summary>One instanced draw per piece. <paramref name="liftY"/> raises the piece in the
	/// prop's own space (primitive meshes are centred on their origin, so without it half of every
	/// tree sits underground), and the jitter gives each instance its own shade — a plain
	/// brightness the shader multiplies the material's colour by, which is what lets
	/// <see cref="SetSeason"/> repaint a whole scatter by setting that one colour.
	/// <paramref name="seasonColors"/>, when given, is the four seasons' colours for this piece.</summary>
	private void AddScatter(Mesh mesh, Color? color, List<Transform3D> transforms, float liftY = 0f,
		float colorJitter = 0f, Color[] seasonColors = null, bool castsShadow = true,
		Node3D parent = null, Material overrideMaterial = null)
	{
		if (transforms.Count == 0 || mesh == null)
		{
			return;
		}

		// A model brought in from the pack paints itself — its walls and its roof are different
		// materials on the one mesh, and an override would flatten both to one colour. Only the
		// shapes built here in code need telling what colour to be, and the few models whose
		// material is swapped for a shader of their own.
		Material material = overrideMaterial;
		if (material == null && color.HasValue)
		{
			var painted = new StandardMaterial3D
			{
				AlbedoColor = color.Value,
				Roughness = 0.95f,
				VertexColorUseAsAlbedo = colorJitter > 0f,
			};
			if (seasonColors != null)
			{
				_seasonal.Add((painted, seasonColors));
			}

			material = painted;
		}

		// Sorted into chunks of the map, each its own MultiMesh — see ChunkSize.
		var chunks = new Dictionary<Vector2I, List<Transform3D>>();
		foreach (Transform3D one in transforms)
		{
			var key = new Vector2I(Mathf.FloorToInt(one.Origin.X / ChunkSize), Mathf.FloorToInt(one.Origin.Z / ChunkSize));
			if (!chunks.TryGetValue(key, out List<Transform3D> chunk))
			{
				chunk = new List<Transform3D>();
				chunks[key] = chunk;
			}

			chunk.Add(one);
		}

		foreach (List<Transform3D> chunk in chunks.Values)
		{
			var multiMesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				Mesh = mesh,
				UseColors = colorJitter > 0f,
				InstanceCount = chunk.Count,
			};
			for (int i = 0; i < chunk.Count; i++)
			{
				multiMesh.SetInstanceTransform(i, chunk[i].TranslatedLocal(Vector3.Up * liftY));
				if (colorJitter > 0f)
				{
					float shade = _rng.RandfRange(1f - colorJitter, 1f + colorJitter);
					multiMesh.SetInstanceColor(i, new Color(shade, shade, shade));
				}
			}

			(parent ?? this).AddChild(new MultiMeshInstance3D
			{
				Multimesh = multiMesh,
				MaterialOverride = material,
				CastShadow = castsShadow
					? GeometryInstance3D.ShadowCastingSetting.On
					: GeometryInstance3D.ShadowCastingSetting.Off,
			});
		}
	}
}
