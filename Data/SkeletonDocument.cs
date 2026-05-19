using System;
using System.Collections.Generic;
using System.Numerics;

namespace Glyphborn.Forge.Data
{
	/// <summary>
	/// A single bone in the skeleton hierarchy.
	/// Position/Rotation/Scale are stored in LOCAL space (relative to parent).
	/// </summary>
	public class Bone
	{
		public string Name { get; set; } = "Bone";

		/// <summary>Index of this bone in the flat skeleton bone list. -1 = unassigned.</summary>
		public int Index { get; set; } = -1;

		/// <summary>Parent bone index. -1 = root bone.</summary>
		public int ParentIndex { get; set; } = -1;

		// Bind pose (rest pose) — local space
		public Vector3 BindPosition { get; set; } = Vector3.Zero;
		public Quaternion BindRotation { get; set; } = Quaternion.Identity;
		public Vector3 BindScale { get; set; } = Vector3.One;

		/// <summary>
		/// Inverse bind matrix — transforms from world space into bone local space.
		/// Computed once when the skeleton is finalised.
		/// </summary>
		public Matrix4x4 InverseBindMatrix { get; set; } = Matrix4x4.Identity;

		/// <summary>Display length in the viewport (not a physics value).</summary>
		public float DisplayLength { get; set; } = 1.0f;

		/// <summary>Arbitrary per-bone metadata (e.g. "attachment", "ik_target").</summary>
		public Dictionary<string, string> Tags { get; set; } = new();

		public Bone() { }
		public Bone(string name, int index, int parentIndex = -1)
		{
			Name = name;
			Index = index;
			ParentIndex = parentIndex;
		}
	}

	/// <summary>
	/// Complete skeleton document (.gbsk export target).
	/// Bones are stored in a flat list; hierarchy is expressed via ParentIndex.
	/// </summary>
	public class SkeletonDocument
	{
		public string Name { get; set; } = "Untitled";

		/// <summary>Flat ordered list — a bone's index in this list IS its Index property.</summary>
		public List<Bone> Bones { get; set; } = new();

		public SkeletonDocument() { }
		public SkeletonDocument(string name) { Name = name; }

		// -----------------------------------------------------------------
		// Hierarchy helpers
		// -----------------------------------------------------------------

		public IEnumerable<Bone> RootBones()
		{
			foreach (var b in Bones)
				if (b.ParentIndex == -1)
					yield return b;
		}

		public IEnumerable<Bone> ChildrenOf(int parentIndex)
		{
			foreach (var b in Bones)
				if (b.ParentIndex == parentIndex)
					yield return b;
		}

		public Bone? FindBone(string name)
		{
			foreach (var b in Bones)
				if (b.Name == name)
					return b;
			return null;
		}

		/// <summary>
		/// Add a new bone as a child of the given parent (or as root if parentIndex == -1).
		/// Returns the new bone.
		/// </summary>
		public Bone AddBone(string name, int parentIndex = -1)
		{
			var bone = new Bone(name, Bones.Count, parentIndex);
			Bones.Add(bone);
			return bone;
		}

		/// <summary>
		/// Removes the bone at the given index and re-maps all references.
		/// Children of the removed bone are re-parented to its parent (or become roots).
		/// </summary>
		public void RemoveBone(int index)
		{
			if (index < 0 || index >= Bones.Count) return;

			int removedParent = Bones[index].ParentIndex;
			Bones.RemoveAt(index);

			// Re-index
			for (int i = 0; i < Bones.Count; i++)
			{
				Bones[i].Index = i;

				if (Bones[i].ParentIndex == index)
					Bones[i].ParentIndex = removedParent; // re-parent orphans
				else if (Bones[i].ParentIndex > index)
					Bones[i].ParentIndex--;
			}
		}

		/// <summary>
		/// Compute the world-space transform of a bone by walking up the hierarchy.
		/// Uses bind pose values.
		/// </summary>
		public Matrix4x4 GetBindWorldMatrix(int boneIndex)
		{
			if (boneIndex < 0 || boneIndex >= Bones.Count)
				return Matrix4x4.Identity;

			var bone = Bones[boneIndex];
			var local = Matrix4x4.CreateScale(bone.BindScale)
					  * Matrix4x4.CreateFromQuaternion(bone.BindRotation)
					  * Matrix4x4.CreateTranslation(bone.BindPosition);

			if (bone.ParentIndex == -1)
				return local;

			return local * GetBindWorldMatrix(bone.ParentIndex);
		}

		/// <summary>Recomputes all InverseBindMatrix values from current bind pose.</summary>
		public void BakeInverseBindMatrices()
		{
			for (int i = 0; i < Bones.Count; i++)
			{
				var world = GetBindWorldMatrix(i);
				Matrix4x4.Invert(world, out var inv);
				Bones[i].InverseBindMatrix = inv;
			}
		}
	}
}