using System.Collections.Generic;
using Godot;

/// <summary>What a catapult at work is seen to do (BattlefieldCastle): every few seconds a stone goes
/// up in a high arc from its arm and comes down on the stretch it is beating at, in a burst of dust
/// and splinters of stone. Only the look of it — the wall's health goes down by the slice
/// (FieldWall.Pound), stone or no stone.</summary>
public partial class BattlefieldCastle
{
	private const float ThrowEvery = 3.5f;
	private const float ThrowFlight = 1.8f;
	private const float ThrowArc = 22f;
	private const float StoneSize = 0.45f;
	private const float ArmHigh = 3f;
	private static readonly Color StoneGrey = new("6e6a62");
	private static readonly Color DustColour = new(0.62f, 0.58f, 0.5f, 0.85f);

	private sealed class Flying
	{
		public MeshInstance3D Stone;
		public Vector3 From;
		public Vector3 To;
		public float Age;
	}

	private readonly Dictionary<FieldSquad, float> _reloads = new();
	private readonly List<Flying> _flying = new();

	private void Throw(float delta)
	{
		foreach (FieldSquad squad in _squads)
		{
			if (squad.Unit != SiegeEngines.Catapult || !squad.IsStanding || squad.IsMoving || squad.Soldiers.Count == 0)
			{
				continue;
			}

			Vector2 at = squad.Soldiers[0].At;
			if (_wall.InThrow(at) is not FieldWall.Target target)
			{
				continue;
			}

			float reload = _reloads.GetValueOrDefault(squad, ThrowEvery * 0.4f) - delta;
			if (reload <= 0f)
			{
				reload = ThrowEvery;
				Launch(_land.On(at) + (Vector3.Up * ArmHigh), Strike(target));
			}

			_reloads[squad] = reload;
		}

		for (int i = _flying.Count - 1; i >= 0; i--)
		{
			Flying stone = _flying[i];
			stone.Age += delta;
			float t = Mathf.Min(stone.Age / ThrowFlight, 1f);
			stone.Stone.Position = stone.From.Lerp(stone.To, t) + (Vector3.Up * ThrowArc * 4f * t * (1f - t));
			stone.Stone.RotateX(delta * 4f);
			if (t >= 1f)
			{
				Burst(stone.To);
				stone.Stone.QueueFree();
				_flying.RemoveAt(i);
			}
		}
	}

	/// <summary>Where a stone comes down on a stretch: its face, a little below the walkway.</summary>
	private Vector3 Strike(FieldWall.Target target)
	{
		Vector2 on = _wall.PointOf(target.Gap);
		float high = BattlefieldWalls.Raised(_wall, on - (FieldWall.Outward(target.Gap.On) * 1f));
		return _land.On(on + (FieldWall.Outward(target.Gap.On) * 0.6f)) + (Vector3.Up * high * 0.7f);
	}

	private void Launch(Vector3 from, Vector3 to)
	{
		var stone = new MeshInstance3D
		{
			Mesh = new SphereMesh { Radius = StoneSize, Height = StoneSize * 1.7f, RadialSegments = 8, Rings = 4 },
			MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneGrey, Roughness = 1f },
			Position = from,
		};
		AddChild(stone);
		_flying.Add(new Flying { Stone = stone, From = from, To = to });
	}

	/// <summary>Dust and chips of stone thrown off the wall where the stone lands, once, and gone.</summary>
	private void Burst(Vector3 at)
	{
		foreach ((int amount, float size, float speed, Color colour, float life) in new[]
		{
			(36, 0.9f, 4f, DustColour, 1.6f),
			(18, 0.18f, 9f, StoneGrey, 1.1f),
		})
		{
			var puff = new CpuParticles3D
			{
				Position = at,
				OneShot = true,
				Emitting = true,
				Amount = amount,
				Lifetime = life,
				Explosiveness = 0.95f,
				Direction = Vector3.Up,
				Spread = 70f,
				InitialVelocityMin = speed * 0.4f,
				InitialVelocityMax = speed,
				Gravity = new Vector3(0f, -9f, 0f) * (size < 0.5f ? 1f : 0.08f),
				ScaleAmountMin = 0.6f,
				ScaleAmountMax = 1.4f,
				Mesh = new SphereMesh { Radius = size / 2f, Height = size, RadialSegments = 6, Rings = 3 },
				Color = colour,
			};
			puff.MaterialOverride = new StandardMaterial3D
			{
				VertexColorUseAsAlbedo = true,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				Roughness = 1f,
			};
			AddChild(puff);
			GetTree().CreateTimer(life + 0.2f).Timeout += puff.QueueFree;
		}
	}
}
