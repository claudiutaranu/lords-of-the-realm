using System.Collections.Generic;
using Godot;

/// <summary>Everything drawn on the field that is not a man standing: the marks of a front being
/// drawn, health bars, the fallen, and the arrows in the air.</summary>
public partial class BattlefieldSquads
{
	/// <summary>Marks on the ground where each man will stand on a front the lord is drawing, and a
	/// caret before each company's front pointing the way it will face; or takes them away (null).</summary>
	public void Mark(List<Vector2> spots, List<Vector2> fronts, Vector2 facing)
	{
		if (_marks == null)
		{
			var look = new StandardMaterial3D
			{
				AlbedoColor = MarkGold,
				// Seen from above whichever way its triangles happen to wind.
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			};
			_marks = Many(new TorusMesh { InnerRadius = MarkRadius * 0.7f, OuterRadius = MarkRadius, Rings = 16, RingSegments = 6 },
				MostMarks, look, coloured: false);
			_arrows3 = Many(ArrowMesh(), MostArrows, look, coloured: false);
		}

		MultiMesh marks = _marks.Multimesh;
		int count = Mathf.Min(spots?.Count ?? 0, marks.InstanceCount);
		for (int i = 0; i < count; i++)
		{
			marks.SetInstanceTransform(i, new Transform3D(Basis.Identity, Ground(spots[i]) + (Vector3.Up * 0.12f)));
		}

		marks.VisibleInstanceCount = count;

		MultiMesh arrows = _arrows3.Multimesh;
		int shown = Mathf.Min(fronts?.Count ?? 0, arrows.InstanceCount);
		for (int i = 0; i < shown; i++)
		{
			Vector3 at = Ground(fronts[i] + (facing * ArrowAhead)) + (Vector3.Up * 0.13f);
			arrows.SetInstanceTransform(i, new Transform3D(new Basis(Vector3.Up, Yaw(facing)), at));
		}

		arrows.VisibleInstanceCount = shown;
	}

	/// <summary>A caret lying on the ground, its point down +Z (the way a man faces at no turn): two
	/// bars meeting at a point, <see cref="ArrowLong"/> metres from the open end to the point.</summary>
	private static ArrayMesh ArrowMesh()
	{
		var points = new List<Vector3>();
		var tip = new Vector3(0f, 0f, ArrowLong);
		foreach (float side in new[] { -1f, 1f })
		{
			var end = new Vector3(side * ArrowWide / 2f, 0f, 0f);
			Vector3 along = (tip - end).Normalized();
			Vector3 across = new Vector3(-along.Z, 0f, along.X) * (CaretThick / 2f);
			points.AddRange(new[] { end - across, end + across, tip + across, end - across, tip + across, tip - across });
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = points.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	/// <summary>A man's health over his head, facing the lord: a dark bar the full width, and inside
	/// it as much as he has left, in his lord's colour — which is what tells the two sides apart in a
	/// press — going to red as he nears his end.</summary>
	private void HealthBar(int index, Vector3 head, float left, Basis facingUs, Color side)
	{
		Vector3 over = head + (Vector3.Up * BarOver);
		_barsBehind.Place(index, new Transform3D(facingUs.ScaledLocal(new Vector3(BarWide, BarHigh, 1f)), over));
		_barsBehind.Colour(index, BarBehind);

		// The dark bar is its frame: the health inside it stops short of its edges all round.
		float share = Mathf.Clamp(left, 0f, 1f);
		float inside = BarWide - (2f * BarEdge);
		float wide = inside * share;
		Vector3 shifted = over - (facingUs.X * ((inside - wide) / 2f));
		_barsFilled.Place(index,
			new Transform3D(facingUs.ScaledLocal(new Vector3(Mathf.Max(0.001f, wide), BarHigh - (2f * BarEdge), 1f)), shifted));
		_barsFilled.Colour(index, side.Lerp(Dying, Mathf.Clamp((LowHealth - share) / LowHealth, 0f, 1f)));
	}

	/// <summary>Everyone the battle says has fallen, laid on his back where he stood — and, a few
	/// seconds on, let go of.</summary>
	private void LayDown(float clock)
	{
		foreach (FieldBattle.Fall fall in _battle.Fell)
		{
			// He is done walking and turning: kept, every man who ever fell stayed in the books.
			_turned.Remove(fall.Man);
			_gaits.Remove(fall.Man);
			if (!_bySquad.TryGetValue(fall.Squad, out Drawn drawn))
			{
				continue;
			}

			SoldierFigure figure = drawn.Figure;
			float stature = figure.Stature;
			// A man baked with a death plays it and ends on his back; anyone else is laid there.
			Basis lying = new Basis(Vector3.Up, Yaw(fall.Facing)) * new Basis(Vector3.Right, -Mathf.Pi / 2f);
			drawn.Dead.Add((
				Dies(figure)
					? Standing(figure, Ground(fall.At), Yaw(fall.Facing), stature)
					: new Transform3D(lying.Scaled(Vector3.One * ScaleOf(figure, stature)), Ground(fall.At) + (Vector3.Up * 0.2f)),
				clock));
			drawn.HasNewDead = true;
		}

		_battle.Fell.Clear();
		foreach (Drawn drawn in _drawn)
		{
			Lay(drawn, clock);
		}
	}

	/// <summary>Whether a figure was baked with a death of its own: the peasant was not, and asked for
	/// one the first of them to fall took the field down with him.</summary>
	private static bool Dies(SoldierFigure figure) => figure.IsBaked && figure.Clips.ContainsKey(DeathClip);

	/// <summary>The dead of one kind, drawn going as they go, and gone once they have. A man lying
	/// still is not handed to the renderer again: only a new death, a body let go, or one still
	/// falling or fading sends the squad's dead over.</summary>
	private static void Lay(Drawn drawn, float clock)
	{
		List<(Transform3D Lying, float Fell)> dead = drawn.Dead;
		int kept = 0;
		for (int i = 0; i < dead.Count; i++)
		{
			if (clock - dead[i].Fell <= LyingFor + GoingFor)
			{
				dead[kept++] = dead[i];
			}
		}

		bool isMoved = drawn.HasNewDead || kept < dead.Count;
		dead.RemoveRange(kept, dead.Count - kept);
		drawn.HasNewDead = false;
		BattlefieldBatch bodies = drawn.Fallen;
		int shown = Mathf.Min(dead.Count, bodies.Count);
		bool isChanged = isMoved;
		for (int i = 0; i < shown; i++)
		{
			if (isMoved)
			{
				bodies.Place(i, dead[i].Lying);
			}

			float going = Mathf.Clamp((clock - dead[i].Fell - LyingFor) / GoingFor, 0f, 1f);
			float dying = Dies(drawn.Figure) ? drawn.Figure.Row(DeathClip, clock - dead[i].Fell) : 0f;
			isChanged |= bodies.Custom(i, new Color(dying, 0f, 0f, -going));
		}

		if (isChanged)
		{
			bodies.Show(shown);
		}
		else
		{
			bodies.Shown(shown);
		}
	}

	/// <summary>Every arrow on its arc, from the bowman's shoulder to where it comes down.</summary>
	private void Fly(float clock)
	{
		int shown = 0;
		foreach (FieldBattle.Arrow arrow in _battle.Arrows)
		{
			float along = (clock - arrow.Loosed) / arrow.Flight;
			if (along < 0f || along > 1f || shown >= _arrows.Count)
			{
				continue;
			}

			Vector3 from = Ground(arrow.From) + (Vector3.Up * ArrowRise);
			Vector3 to = Ground(arrow.To);
			float high = from.DistanceTo(to) * ArrowArc;
			Vector3 Point(float t) => from.Lerp(to, t) + (Vector3.Up * high * 4f * t * (1f - t));
			Vector3 here = Point(along);
			Vector3 ahead = Point(Mathf.Min(1f, along + 0.02f)) - here;
			if (ahead.LengthSquared() > 1e-6f)
			{
				_arrows.Place(shown++, new Transform3D(Basis.LookingAt(ahead.Normalized()), here));
			}
		}

		_arrows.Show(shown);
	}

	private MultiMeshInstance3D Many(Mesh mesh, int count, Material look, bool coloured)
	{
		var many = new MultiMeshInstance3D
		{
			Multimesh = new MultiMesh
			{
				TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
				UseColors = coloured,
				Mesh = mesh,
				InstanceCount = Mathf.Max(1, count),
				VisibleInstanceCount = 0,
			},
			MaterialOverride = look,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(many);
		return many;
	}

	/// <summary>A health bar's material: drawn over everything, so a man's bar is not hidden by the
	/// man in front of him, the filled part over the dark.</summary>
	private static StandardMaterial3D Bar(int layer) => new()
	{
		ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
		VertexColorUseAsAlbedo = true,
		Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		NoDepthTest = true,
		CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		RenderPriority = layer,
	};
}
