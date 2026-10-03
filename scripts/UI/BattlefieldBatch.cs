using Godot;

/// <summary>One MultiMesh's figures, filled in here and handed to the renderer in one go. Set one
/// man at a time, four hundred men with their bars and their dead were a few thousand calls into
/// the engine every frame; filled here, they are one.</summary>
public sealed class BattlefieldBatch
{
	/// <summary>Godot's own layout of a 3D instance: the three rows of its transform, then its colour
	/// if the MultiMesh carries colours, then its custom data if it carries that.</summary>
	private const int TransformFloats = 12;
	private const int ColourFloats = 4;

	private readonly MultiMesh _mesh;
	private readonly float[] _floats;
	private readonly int _stride;
	private readonly int _custom;
	private int _shown;

	public BattlefieldBatch(MultiMesh mesh)
	{
		_mesh = mesh;
		Count = mesh.InstanceCount;
		_custom = TransformFloats + (mesh.UseColors ? ColourFloats : 0);
		_stride = _custom + (mesh.UseCustomData ? ColourFloats : 0);
		_floats = new float[Count * _stride];
		_shown = mesh.VisibleInstanceCount;
	}

	/// <summary>How many figures it has room for.</summary>
	public int Count { get; }

	public MultiMesh Mesh => _mesh;

	public void Place(int index, Transform3D where)
	{
		int at = index * _stride;
		Basis b = where.Basis;
		(_floats[at], _floats[at + 1], _floats[at + 2], _floats[at + 3]) = (b.Row0.X, b.Row0.Y, b.Row0.Z, where.Origin.X);
		(_floats[at + 4], _floats[at + 5], _floats[at + 6], _floats[at + 7]) = (b.Row1.X, b.Row1.Y, b.Row1.Z, where.Origin.Y);
		(_floats[at + 8], _floats[at + 9], _floats[at + 10], _floats[at + 11]) = (b.Row2.X, b.Row2.Y, b.Row2.Z, where.Origin.Z);
	}

	public void Colour(int index, Color colour) => Put((index * _stride) + TransformFloats, colour);

	/// <summary>Sets a figure's custom data, and says whether that changed it.</summary>
	public bool Custom(int index, Color data)
	{
		int at = (index * _stride) + _custom;
		bool isChanged = _floats[at] != data.R || _floats[at + 1] != data.G || _floats[at + 2] != data.B || _floats[at + 3] != data.A;
		Put(at, data);
		return isChanged;
	}

	/// <summary>Hands everything filled in to the renderer, the first <paramref name="shown"/> drawn —
	/// or nothing, when none are: most of a melee has no arrow in the air.</summary>
	public void Show(int shown)
	{
		if (shown > 0)
		{
			RenderingServer.MultimeshSetBuffer(_mesh.GetRid(), _floats);
		}

		Shown(shown);
	}

	/// <summary>Draws the first <paramref name="shown"/> as they were last handed over.</summary>
	public void Shown(int shown)
	{
		if (shown != _shown)
		{
			_mesh.VisibleInstanceCount = shown;
			_shown = shown;
		}
	}

	private void Put(int at, Color colour) =>
		(_floats[at], _floats[at + 1], _floats[at + 2], _floats[at + 3]) = (colour.R, colour.G, colour.B, colour.A);
}
