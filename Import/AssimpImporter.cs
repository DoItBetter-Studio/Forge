using System;
using System.Collections.Generic;
using System.Numerics;
using Assimp;
using AiMatrix = Assimp.Matrix4x4;
using NMatrix = System.Numerics.Matrix4x4;
using ForgeData = Glyphborn.Forge.Data;

namespace Glyphborn.Forge.Import
{
	/// <summary>
	/// Imports any format supported by Assimp (FBX, OBJ, GLTF, DAE, 3DS, …)
	/// and maps it into Forge's MeshDocument, SkeletonDocument, and AnimationDocument.
	/// </summary>
	public static class AssimpImporter
	{
		public const string FILE_FILTER =
			"All Supported Formats|*.fbx;*.obj;*.gltf;*.glb;*.dae;*.3ds;*.blend;*.ply;*.stl;*.x;*.md5mesh;*.smd|" +
			"FBX (*.fbx)|*.fbx|" +
			"OBJ (*.obj)|*.obj|" +
			"GLTF / GLB (*.gltf;*.glb)|*.gltf;*.glb|" +
			"Collada (*.dae)|*.dae|" +
			"3DS Max (*.3ds)|*.3ds|" +
			"Blender (*.blend)|*.blend|" +
			"PLY (*.ply)|*.ply|" +
			"STL (*.stl)|*.stl|" +
			"All Files (*.*)|*.*";

		// =================================================================
		// Public entry point
		// =================================================================

		public static ImportResult Import(string path)
		{
			using var ctx = new AssimpContext();

			var steps =
				PostProcessSteps.Triangulate |
				PostProcessSteps.GenerateSmoothNormals |
				PostProcessSteps.JoinIdenticalVertices |
				PostProcessSteps.LimitBoneWeights |
				PostProcessSteps.ImproveCacheLocality |
				PostProcessSteps.FlipUVs;

			string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

			Scene scene = ctx.ImportFile(path, steps);

			if (scene == null || !scene.HasMeshes)
				throw new InvalidOperationException("Assimp could not load the file or found no meshes.");

			var result = new ImportResult();
			var boneNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);

			// Build node world transforms for bind pose derivation and mesh placement
			var nodeWorldTransforms = new Dictionary<string, NMatrix>(StringComparer.Ordinal);
			BuildNodeTransforms(scene.RootNode, NMatrix.Identity, nodeWorldTransforms);

			// Build mesh-index → node-world-transform map so vertices get placed correctly
			var meshNodeTransforms = new Dictionary<int, NMatrix>();
			CollectMeshNodeTransforms(scene.RootNode, NMatrix.Identity, meshNodeTransforms);

			result.Skeleton = ExtractSkeleton(scene, boneNameToIndex, nodeWorldTransforms);
			result.Mesh = ExtractMesh(scene, boneNameToIndex, meshNodeTransforms);

			if (scene.HasAnimations && result.Skeleton != null)
				result.Animation = ExtractAnimations(scene, boneNameToIndex, result.Skeleton);

			return result;
		}

		private static void BuildNodeTransforms(Node node, NMatrix parentWorld,
			Dictionary<string, NMatrix> table)
		{
			NMatrix local = ToNMatrix(node.Transform);
			NMatrix world = parentWorld * local;
			table[node.Name] = world;
			foreach (var child in node.Children)
				BuildNodeTransforms(child, world, table);
		}

		private static void CollectMeshNodeTransforms(Node node, NMatrix parentWorld,
			Dictionary<int, NMatrix> meshTransforms)
		{
			NMatrix local = ToNMatrix(node.Transform);
			NMatrix world = parentWorld * local;

			foreach (int meshIdx in node.MeshIndices)
				meshTransforms[meshIdx] = world;

			foreach (var child in node.Children)
				CollectMeshNodeTransforms(child, world, meshTransforms);
		}

		// =================================================================
		// Mesh extraction
		// =================================================================

		private static ForgeData.MeshDocument ExtractMesh(
			Scene scene,
			Dictionary<string, int> boneNameToIndex,
			Dictionary<int, NMatrix> meshNodeTransforms)
		{
			var doc = new ForgeData.MeshDocument(scene.RootNode?.Name ?? "Imported");

			for (int meshIdx = 0; meshIdx < scene.Meshes.Count; meshIdx++)
			{
				var aiMesh = scene.Meshes[meshIdx];
				if (!aiMesh.HasFaces || !aiMesh.HasVertices) continue;

				var surf = new ForgeData.MeshSurface
				{
					Name = aiMesh.Name ?? "Surface",
					MaterialName = scene.Materials.Count > aiMesh.MaterialIndex
						? scene.Materials[aiMesh.MaterialIndex].Name ?? "default"
						: "default"
				};

				bool hasNodeXform = meshNodeTransforms.TryGetValue(meshIdx, out NMatrix nodeXform);
				bool hasUV = aiMesh.HasTextureCoords(0);
				bool hasNormals = aiMesh.HasNormals;

				for (int i = 0; i < aiMesh.VertexCount; i++)
				{
					var av = aiMesh.Vertices[i];
					var an = hasNormals ? aiMesh.Normals[i] : new Vector3D(0, 1, 0);
					var auv = hasUV ? aiMesh.TextureCoordinateChannels[0][i] : new Vector3D(0, 0, 0);

					var pos = new Vector3(av.X, av.Y, av.Z);
					var norm = new Vector3(an.X, an.Y, an.Z);

					bool skinned = aiMesh.HasBones;

					if (!skinned && hasNodeXform && !IsIdentity(nodeXform))
					{
						pos = Vector3.Transform(pos, nodeXform);
						norm = Vector3.Normalize(Vector3.TransformNormal(norm, nodeXform));
					}

					surf.Vertices.Add(new ForgeData.MeshVertex(
						pos, norm, new Vector2(auv.X, auv.Y)));
				}

				// Bone weights
				if (aiMesh.HasBones)
				{
					foreach (var aiBone in aiMesh.Bones)
					{
						if (!boneNameToIndex.TryGetValue(aiBone.Name, out int boneIdx)) continue;
						foreach (var weight in aiBone.VertexWeights)
						{
							if (weight.VertexID >= surf.Vertices.Count) continue;
							surf.Vertices[weight.VertexID].SetInfluence(boneIdx, weight.Weight);
						}
					}
				}

				// Faces
				foreach (var aiFace in aiMesh.Faces)
				{
					if (aiFace.IndexCount != 3) continue;
					surf.Faces.Add(new ForgeData.MeshFace(
						aiFace.Indices[0],
						aiFace.Indices[1],
						aiFace.Indices[2]));
				}

				surf.RebuildEdges();
				doc.Surfaces.Add(surf);
			}

			return doc;
		}

		private static bool IsIdentity(NMatrix m)
		{
			const float eps = 1e-5f;
			return MathF.Abs(m.M11 - 1f) < eps && MathF.Abs(m.M22 - 1f) < eps &&
				   MathF.Abs(m.M33 - 1f) < eps && MathF.Abs(m.M44 - 1f) < eps &&
				   MathF.Abs(m.M12) < eps && MathF.Abs(m.M13) < eps && MathF.Abs(m.M14) < eps &&
				   MathF.Abs(m.M21) < eps && MathF.Abs(m.M23) < eps && MathF.Abs(m.M24) < eps &&
				   MathF.Abs(m.M31) < eps && MathF.Abs(m.M32) < eps && MathF.Abs(m.M34) < eps &&
				   MathF.Abs(m.M41) < eps && MathF.Abs(m.M42) < eps && MathF.Abs(m.M43) < eps;
		}

		// =================================================================
		// Skeleton extraction
		// =================================================================

		private static ForgeData.SkeletonDocument? ExtractSkeleton(
			Scene scene,
			Dictionary<string, int> boneNameToIndex,
			Dictionary<string, NMatrix> nodeWorldTransforms)
		{
			// Collect the set of names that are actual bones (referenced in mesh data)
			var boneNames = new HashSet<string>(StringComparer.Ordinal);
			var boneMatrices = new Dictionary<string, NMatrix>(StringComparer.Ordinal);

			foreach (var aiMesh in scene.Meshes)
			{
				if (!aiMesh.HasBones) continue;
				foreach (var aiBone in aiMesh.Bones)
				{
					boneNames.Add(aiBone.Name);
					boneMatrices[aiBone.Name] = ToNMatrix(aiBone.OffsetMatrix);
				}
			}

			if (boneNames.Count == 0) return null;

			Node? armatureRoot = FindArmatureRoot(scene.RootNode, boneNames);
			if (armatureRoot == null) return null;

			var skel = new ForgeData.SkeletonDocument(armatureRoot.Name);

			BuildSkeletonHierarchy(armatureRoot, -1, NMatrix.Identity,
				skel, boneNameToIndex, boneMatrices, boneNames);

			// Auto-compute DisplayLength from distance to first child
			foreach (var bone in skel.Bones)
			{
				float minDist = float.MaxValue;
				foreach (var child in skel.Bones)
				{
					if (child.ParentIndex != bone.Index) continue;
					float d = child.BindPosition.Length();
					if (d > 0.001f && d < minDist) minDist = d;
				}
				bone.DisplayLength = minDist < float.MaxValue ? minDist : 0.1f;
			}

			return skel;
		}

		private static Node? FindArmatureRoot(Node node, HashSet<string> boneNames)
		{
			// Prefer explicit armature/metarig names first
			if (node.Name.Contains("metarig", StringComparison.OrdinalIgnoreCase) ||
				node.Name.Contains("armature", StringComparison.OrdinalIgnoreCase))
				return node;

			// Fall back: find the lowest common ancestor of all bones in the scene tree
			// by finding the first node whose subtree contains ANY bone
			if (SubtreeContainsBone(node, boneNames))
				return node;

			foreach (var child in node.Children)
			{
				var found = FindArmatureRoot(child, boneNames);
				if (found != null) return found;
			}
			return null;
		}

		private static bool SubtreeContainsBone(Node node, HashSet<string> boneNames)
		{
			if (boneNames.Contains(node.Name)) return true;
			foreach (var child in node.Children)
				if (SubtreeContainsBone(child, boneNames)) return true;
			return false;
		}

		private static void BuildSkeletonHierarchy(
			Node node,
			int parentIdx,
			NMatrix parentWorld,
			ForgeData.SkeletonDocument skel,
			Dictionary<string, int> boneNameToIndex,
			Dictionary<string, NMatrix> boneMatrices,
			HashSet<string> boneNames)
		{
			NMatrix local = ToNMatrix(node.Transform);
			NMatrix myWorld = parentWorld * local;

			int myIdx = parentIdx; // default: pass parent down if this node isn't a bone

			// Only add this node as a bone if it's in the actual bone set
			if (boneNames.Contains(node.Name))
			{
				myIdx = skel.Bones.Count;
				boneNameToIndex[node.Name] = myIdx;

				if (!boneMatrices.TryGetValue(node.Name, out NMatrix invBind))
					invBind = NMatrix.Identity;

				NMatrix.Decompose(local,
					out Vector3 scale,
					out System.Numerics.Quaternion rot,
					out Vector3 pos);

				skel.Bones.Add(new ForgeData.Bone
				{
					Name = node.Name,
					Index = myIdx,
					ParentIndex = parentIdx,
					InverseBindMatrix = invBind,
					BindPosition = pos,
					BindRotation = rot,
					BindScale = scale,
					DisplayLength = 0.1f
				});
			}

			foreach (var child in node.Children)
				BuildSkeletonHierarchy(child, myIdx, myWorld, skel, boneNameToIndex, boneMatrices, boneNames);
		}

		// =================================================================
		// Animation extraction
		// =================================================================

		private static ForgeData.AnimationDocument ExtractAnimations(
			Scene scene,
			Dictionary<string, int> boneNameToIndex,
			ForgeData.SkeletonDocument skel)
		{
			var doc = new ForgeData.AnimationDocument("Imported")
			{
				SkeletonName = skel.Name ?? ""
			};

			foreach (var aiAnim in scene.Animations)
			{
				double ticksPerSecond = aiAnim.TicksPerSecond > 0 ? aiAnim.TicksPerSecond : 24.0;
				float frameRate = 24f;
				int frameCount = Math.Max(1, (int)Math.Ceiling(aiAnim.DurationInTicks / ticksPerSecond * frameRate));

				var clip = new ForgeData.AnimationClip
				{
					Name = string.IsNullOrWhiteSpace(aiAnim.Name) ? "Take001" : aiAnim.Name,
					FrameCount = frameCount,
					FrameRate = frameRate,
					Loop = true
				};

				foreach (var aiChannel in aiAnim.NodeAnimationChannels)
				{
					if (!boneNameToIndex.TryGetValue(aiChannel.NodeName, out int boneIdx)) continue;

					var ch = clip.GetOrCreateChannel(boneIdx);

					foreach (var key in aiChannel.PositionKeys)
						ch.PositionKeys.Add(new ForgeData.Keyframe<Vector3>(
							TickToFrame(key.Time, ticksPerSecond, frameRate),
							new Vector3(key.Value.X, key.Value.Y, key.Value.Z)));

					foreach (var key in aiChannel.RotationKeys)
						ch.RotationKeys.Add(new ForgeData.Keyframe<System.Numerics.Quaternion>(
							TickToFrame(key.Time, ticksPerSecond, frameRate),
							new System.Numerics.Quaternion(key.Value.X, key.Value.Y, key.Value.Z, key.Value.W)));

					foreach (var key in aiChannel.ScalingKeys)
						ch.ScaleKeys.Add(new ForgeData.Keyframe<Vector3>(
							TickToFrame(key.Time, ticksPerSecond, frameRate),
							new Vector3(key.Value.X, key.Value.Y, key.Value.Z)));

					ch.PositionKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
					ch.RotationKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
					ch.ScaleKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
				}

				doc.Clips.Add(clip);
			}

			return doc;
		}

		private static int TickToFrame(double tick, double ticksPerSecond, float frameRate)
			=> Math.Max(0, (int)Math.Round(tick / ticksPerSecond * frameRate));

		private static NMatrix ToNMatrix(AiMatrix m) => new NMatrix(
			m.A1, m.A2, m.A3, m.A4,
			m.B1, m.B2, m.B3, m.B4,
			m.C1, m.C2, m.C3, m.C4,
			m.D1, m.D2, m.D3, m.D4);
	}
}