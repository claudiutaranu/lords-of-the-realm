using System.Collections.Generic;
using Godot;

/// <summary>The map's building models, loaded once and handed out by name.
///
/// They are Quaternius' Ultimate Fantasy RTS pack (CC0), which suits this map for the same reason
/// the hand-built props did: flat-shaded, untextured, coloured by material. Each file carries its
/// own geometry inline — no .bin beside it, no texture to find — so one file is one drop-in.
///
/// Two ways out of here, because the map needs both. A prop that stands in its thousands goes
/// through <see cref="MeshOf"/> into a MultiMesh, where one draw call covers every instance. A
/// castle, of which a province has exactly one, goes through <see cref="Instance"/> as its own
/// node, where it keeps its materials without anything having to be taken apart.</summary>
public static class Models
{
	private const string Directory = "res://assets/models/rts";

	private static readonly Dictionary<string, Mesh> Meshes = new();
	private static readonly Dictionary<string, PackedScene> Scenes = new();

	/// <summary>The model as a scene, for something there is one of.</summary>
	public static Node3D Instance(string name)
	{
		if (!Scenes.TryGetValue(name, out PackedScene scene))
		{
			scene = GD.Load<PackedScene>($"{Directory}/{name}.gltf");
			Scenes[name] = scene;
		}

		return scene?.Instantiate<Node3D>();
	}

	/// <summary>The model's mesh, for something there are thousands of. Its own surface materials
	/// ride along with it, so a MultiMesh drawing it needs no override and a house keeps its roof a
	/// different colour from its walls.</summary>
	public static Mesh MeshOf(string name)
	{
		if (Meshes.TryGetValue(name, out Mesh cached))
		{
			return cached;
		}

		Node3D model = Instance(name);
		Mesh mesh = model == null ? null : FirstMesh(model);
		model?.QueueFree();
		Meshes[name] = mesh;
		if (mesh == null)
		{
			GD.PushError($"Models: no mesh inside {name}");
		}

		return mesh;
	}

	/// <summary>How tall the model stands in its own units, so a caller can scale it to the world
	/// rather than to a number somebody guessed.</summary>
	public static float HeightOf(string name)
	{
		Mesh mesh = MeshOf(name);
		return mesh?.GetAabb().Size.Y ?? 1f;
	}

	/// <summary>How wide the model is on the ground, in its own units — the longer of its two
	/// horizontal sides, so a thing set out by this figure cannot clip a neighbour whichever way it
	/// happens to be turned.</summary>
	public static float FootprintOf(string name)
	{
		Mesh mesh = MeshOf(name);
		if (mesh == null)
		{
			return 1f;
		}

		Vector3 size = mesh.GetAabb().Size;
		return Mathf.Max(size.X, size.Z);
	}

	/// <summary>The model's mesh with one of its surfaces repainted, for the two things on this map
	/// that turn with the year. The surface is found by the pack's own material name — "Green" for
	/// foliage, "Wheat" for a standing crop — which survives a re-export where a surface index
	/// would not.
	///
	/// The mesh is duplicated first: the cached one is shared by every instance on the map, and
	/// painting that would repaint every wood on the island at once.</summary>
	public static Mesh Repainted(string name, string materialName, StandardMaterial3D paint)
	{
		Mesh source = MeshOf(name);
		if (source == null)
		{
			return null;
		}

		var mesh = (ArrayMesh)source.Duplicate(true);
		for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			if (mesh.SurfaceGetMaterial(surface)?.ResourceName == materialName)
			{
				mesh.SurfaceSetMaterial(surface, paint);
				return mesh;
			}
		}

		GD.PushError($"Models: {name} has no material called {materialName}");
		return mesh;
	}

	private static Mesh FirstMesh(Node node)
	{
		if (node is MeshInstance3D instance)
		{
			return instance.Mesh;
		}

		foreach (Node child in node.GetChildren())
		{
			Mesh found = FirstMesh(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
