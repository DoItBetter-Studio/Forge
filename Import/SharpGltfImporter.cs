using Glyphborn.Forge.Data;
using Glyphborn.Forge.Maths;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.Numerics;

namespace Glyphborn.Forge.Import
{
	public static class SharpGltfImporter
	{
		private static Mat4 ToForgeMatrix(Matrix4x4 n)
		{
			var r = new Mat4
			{
				m = new float[4, 4]
			};

			// Convert System.Numerics row-major
			// into Forge column-major semantics

			r.m[0, 0] = n.M11;
			r.m[0, 1] = n.M21;
			r.m[0, 2] = n.M31;
			r.m[0, 3] = n.M41;

			r.m[1, 0] = n.M12;
			r.m[1, 1] = n.M22;
			r.m[1, 2] = n.M32;
			r.m[1, 3] = n.M42;

			r.m[2, 0] = n.M13;
			r.m[2, 1] = n.M23;
			r.m[2, 2] = n.M33;
			r.m[2, 3] = n.M43;

			r.m[3, 0] = n.M14;
			r.m[3, 1] = n.M24;
			r.m[3, 2] = n.M34;
			r.m[3, 3] = n.M44;

			return r;
		}

		public static ImportResult Import(string path)
		{
			var model = ModelRoot.Load(path);

			var result = new ImportResult();

			result.Mesh = ExtractMeshes(model);

			if (model.LogicalSkins.Count > 0)
				result.Skeleton = ExtractSkeleton(model);

			//if (model.LogicalAnimations.Count > 0 && result.Skeleton != null)
			//	result.Animation = ExtractAnimations(model, result.Skeleton);

			return result;
		}

		private static MeshDocument ExtractMeshes(ModelRoot model)
		{
			var doc = new MeshDocument("Imported");

			foreach (var mesh in model.LogicalMeshes)
			{
				foreach (var prim in mesh.Primitives)
				{
					var surface = new MeshSurface();

					var positions = prim.GetVertexAccessor("POSITION").AsVector3Array();
					var normals = prim.GetVertexAccessor("NORMAL")?.AsVector3Array();
					var uvs = prim.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();

					for (int i = 0; i < positions.Count; i++)
					{
						surface.Vertices.Add(new MeshVertex(
							positions[i],
							normals != null ? normals[i] : Vector3.UnitY,
							uvs != null ? uvs[i] : Vector2.Zero));
					}

					var indices = prim.IndexAccessor.AsIndicesArray();

					for (int i = 0; i < indices.Count; i += 3)
					{
						surface.Faces.Add(new MeshFace(
							(int)indices[i],
							(int)indices[i + 2],
							(int)indices[i + 1]));
					}

					surface.RebuildEdges();
					doc.Surfaces.Add(surface);
				}
			}

			return doc;
		}

		private static SkeletonDocument ExtractSkeleton(ModelRoot model)
		{
			var skin = model.LogicalSkins.Count > 0
				? model.LogicalSkins[0]
				: null;

			if (skin == null)
				return null;

			var skel = new SkeletonDocument(skin.Name ?? "Skeleton");

			// Map node -> bone index
			var nodeToIndex = new Dictionary<Node, int>();

			// -----------------------------
			// PASS 1: Create bones
			// -----------------------------
			for (int i = 0; i < skin.JointsCount; i++)
			{
				var (node, inverseBind) = skin.GetJoint(i);

				var bone = new Bone
				{
					Name = node.Name ?? $"bone_{i}",
					Index = i,
					InverseBindMatrix = inverseBind,
					DisplayLength = 0.1f
				};

				skel.Bones.Add(bone);
				nodeToIndex[node] = i;
			}

			// -----------------------------
			// PASS 2: Resolve hierarchy (DIRECT from node tree)
			// -----------------------------
			for (int i = 0; i < skin.JointsCount; i++)
			{
				var (node, _) = skin.GetJoint(i);

				int parentIndex = -1;

				var parent = node.VisualParent;

				// Walk upward until we find another joint in this skin
				while (parent != null)
				{
					if (nodeToIndex.TryGetValue(parent, out parentIndex))
						break;

					parent = parent.VisualParent;
				}

				skel.Bones[i].ParentIndex = parentIndex;
			}

			// -----------------------------
			// PASS 3: Bind pose (True skeletal space relative transforms)
			// -----------------------------
			for (int i = 0; i < skin.JointsCount; i++)
			{
				var (node, _) = skin.GetJoint(i);

				var trs = node.LocalTransform;

				var bone = skel.Bones[i];

				bone.BindPosition = trs.Translation;
				bone.BindRotation = trs.Rotation;
				bone.BindScale = trs.Scale;
			}

			// -----------------------------
			// PASS 4: Display length (optional visual helper only)
			// -----------------------------
			foreach (var bone in skel.Bones)
			{
				float minChildDist = float.MaxValue;

				foreach (var child in skel.Bones)
				{
					if (child.ParentIndex != bone.Index)
						continue;

					var d = child.BindPosition.Length();

					if (d > 0.001f && d < minChildDist)
						minChildDist = d;
				}

				bone.DisplayLength = minChildDist < float.MaxValue
					? minChildDist
					: 0.1f;
			}

			return skel;
		}
	}
}
