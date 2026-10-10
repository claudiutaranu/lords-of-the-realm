using Godot;

/// <summary>The castle's flag on the field (FieldBattle.Flag): a small pole in the bailey, no taller than the palisade, with the
/// holder's banner on it and a ring of worn earth round it, the ground that has to be held. As the
/// attackers hold it the banner comes down the pole, and when it is down the attacker's goes up.</summary>
public partial class BattlefieldCastle
{
	private const float PoleHigh = 4.5f;
	private const float FlagWide = 1.3f;
	private const float FlagTall = 0.9f;

	private Node3D _pole;
	private MeshInstance3D _cloth;
	private StandardMaterial3D _clothLook;

	private void Flag()
	{
		if (_pole == null)
		{
			Vector3 at = _land.On(_battle.Flag);
			_pole = new Node3D { Position = at };
			_pole.AddChild(new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.1f, Height = PoleHigh },
				Position = Vector3.Up * (PoleHigh / 2f),
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("5a4026"), Roughness = 0.9f },
			});
			_pole.AddChild(new MeshInstance3D
			{
				Mesh = new TorusMesh { InnerRadius = FieldBattle.FlagReach - 0.35f, OuterRadius = FieldBattle.FlagReach, Rings = 48 },
				Position = Vector3.Up * 0.45f,
				Scale = new Vector3(1f, 0.02f, 1f),
				MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.7f, 0.3f, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
			});
			_clothLook = new StandardMaterial3D
			{
				AlbedoTexture = GD.Load<Texture2D>("res://assets/ui/diplomacy/banner-cloth.png"),
				AlbedoColor = _holder,
				Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
				CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				Roughness = 0.9f,
			};
			_cloth = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(FlagWide, FlagTall) }, MaterialOverride = _clothLook };
			_pole.AddChild(_cloth);
			AddChild(_pole);
		}

		// Down the pole as it is taken; the taker's colour once it is down.
		float taken = _battle.FlagTaken;
		bool isTaken = _battle.IsFlagTaken;
		float top = PoleHigh - (FlagTall / 2f) - 0.2f;
		float low = (FlagTall / 2f) + 0.3f;
		_cloth.Position = new Vector3(FlagWide / 2f, isTaken ? top : Mathf.Lerp(top, low, taken), 0f);
		_cloth.Rotation = new Vector3(0f, Mathf.Sin(Time.GetTicksMsec() / 700f) * 0.15f, 0f);
		_clothLook.AlbedoColor = isTaken ? _taker : _holder;
	}
}
