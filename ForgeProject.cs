using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Numerics;
using Glyphborn.Forge.Data;
using System.ComponentModel;

namespace Glyphborn.Forge
{
	/// <summary>
	/// A complete Glyphborn Forge project.
	///
	/// Internal format:  .forge  — binary blob, keeps all editor state.
	/// Engine export:    .gbx    — one file containing whichever of skeleton /
	///                             mesh / animation are present; the engine
	///                             skips missing sections.  No separate files.
	/// </summary>
	public class ForgeProject
	{
		// ----- Magic / version -----
		public const string FORGE_MAGIC = "GBFG";  // Glyphborn Forge
		public const string GBX_MAGIC = "GBXP";   // Glyphborn eXport Package
		public const int FORMAT_VERSION = 2;

		// ----- Section tags embedded in .gbx -----
		private const string TAG_MESH = "MSEC";
		private const string TAG_SKEL = "SSEC";
		private const string TAG_ANIM = "ASEC";
		private const string TAG_END = "XEND";

		// ----- Project data -----
		public string ProjectName { get; set; } = "Untitled";
		public DateTime LastSaved { get; set; } = DateTime.UtcNow;

		public MeshDocument? Mesh { get; set; }
		public SkeletonDocument? Skeleton { get; set; }
		public AnimationDocument? Animation { get; set; }
		public EditHistory History { get; } = new EditHistory();

		public ForgeProject() { }
		public ForgeProject(string name) { ProjectName = name; }

		// =================================================================
		// .forge  —  internal save / load
		// =================================================================

		public void Save(string path)
		{
			LastSaved = DateTime.UtcNow;
			using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
			using var w = new BinaryWriter(fs, Encoding.UTF8);

			WriteHeader(w);
			WriteMeshSection(w, Mesh);
			WriteSkeletonSection(w, Skeleton);
			WriteAnimationSection(w, Animation);
		}

		public static ForgeProject Load(string path)
		{
			using var fs = File.Open(path, FileMode.Open, FileAccess.Read);
			using var r = new BinaryReader(fs, Encoding.UTF8);

			var p = new ForgeProject();
			p.ReadHeader(r);
			p.Mesh = ReadMeshSection(r);
			p.Skeleton = ReadSkeletonSection(r);
			p.Animation = ReadAnimationSection(r);
			p.History.Clear();
			return p;
		}

		// =================================================================
		// .gbx  —  unified engine export
		// All three sections are optional.  Missing data is simply skipped.
		// =================================================================

		/// <summary>
		/// Export everything the project contains into a single .gbx file.
		/// The engine reads the section tags and loads whatever is present.
		/// </summary>
		public void ExportAll(string path)
		{
			using var fs = File.Open(path, FileMode.Create, FileAccess.Write);
			using var w = new BinaryWriter(fs, Encoding.UTF8);

			// File header
			w.Write(Encoding.ASCII.GetBytes(GBX_MAGIC)); // 4 bytes
			w.Write(FORMAT_VERSION);
			w.Write(ProjectName);

			// Skeleton section
			if (Skeleton != null)
			{
				WriteTag(w, TAG_SKEL);
				WriteSkeletonData(w, Skeleton);
			}

			// Mesh section
			if (Mesh != null)
			{
				WriteTag(w, TAG_MESH);
				WriteMeshData(w, Mesh);
			}

			// Animation section
			if (Animation != null)
			{
				WriteTag(w, TAG_ANIM);
				WriteAnimationData(w, Animation);
			}

			WriteTag(w, TAG_END);
		}

		// =================================================================
		// Internal .forge header
		// =================================================================
		private void WriteHeader(BinaryWriter w)
		{
			w.Write(Encoding.ASCII.GetBytes(FORGE_MAGIC));
			w.Write(FORMAT_VERSION);
			w.Write(ProjectName);
			w.Write(LastSaved.ToBinary());
		}

		private void ReadHeader(BinaryReader r)
		{
			string magic = new(r.ReadChars(4));
			if (magic != FORGE_MAGIC)
				throw new InvalidDataException($"Not a .forge file (got: {magic})");

			int ver = r.ReadInt32();
			if (ver > FORMAT_VERSION)
				throw new InvalidDataException($"File requires Forge v{ver}; this is v{FORMAT_VERSION}");

			ProjectName = r.ReadString();
			LastSaved = DateTime.FromBinary(r.ReadInt64());
		}

		// =================================================================
		// Section helpers
		// =================================================================
		private static void WriteTag(BinaryWriter w, string tag)
			=> w.Write(Encoding.ASCII.GetBytes(tag)); // always 4 bytes

		private static string ReadTag(BinaryReader r)
			=> new(r.ReadChars(4));

		// =================================================================
		// Mesh  (internal save)
		// =================================================================
		private static void WriteMeshSection(BinaryWriter w, MeshDocument? mesh)
		{
			w.Write(mesh != null);
			if (mesh != null) WriteMeshData(w, mesh);
		}

		private static MeshDocument? ReadMeshSection(BinaryReader r)
		{
			if (!r.ReadBoolean()) return null;
			return ReadMeshData(r);
		}

		private static void WriteMeshData(BinaryWriter w, MeshDocument mesh)
		{
			w.Write(mesh.Name);
			w.Write(mesh.Surfaces.Count);
			foreach (var surf in mesh.Surfaces)
			{
				w.Write(surf.Name);
				w.Write(surf.MaterialName);

				w.Write(surf.Vertices.Count);
				foreach (var v in surf.Vertices)
				{
					WriteVec3(w, v.Position);
					WriteVec3(w, v.Normal);
					WriteVec2(w, v.UV);
					for (int i = 0; i < 4; i++) w.Write(v.BoneIndices[i]);
					for (int i = 0; i < 4; i++) w.Write(v.BoneWeights[i]);
				}

				w.Write(surf.Faces.Count);
				foreach (var f in surf.Faces)
				{ w.Write(f.A); w.Write(f.B); w.Write(f.C); }
			}
		}

		private static MeshDocument ReadMeshData(BinaryReader r)
		{
			var mesh = new MeshDocument(r.ReadString());
			int sc = r.ReadInt32();
			for (int s = 0; s < sc; s++)
			{
				var surf = new MeshSurface
				{
					Name = r.ReadString(),
					MaterialName = r.ReadString()
				};
				int vc = r.ReadInt32();
				for (int i = 0; i < vc; i++)
				{
					var v = new MeshVertex
					{
						Position = ReadVec3(r),
						Normal = ReadVec3(r),
						UV = ReadVec2(r)
					};
					for (int j = 0; j < 4; j++) v.BoneIndices[j] = r.ReadInt32();
					for (int j = 0; j < 4; j++) v.BoneWeights[j] = r.ReadSingle();
					surf.Vertices.Add(v);
				}
				int fc = r.ReadInt32();
				for (int i = 0; i < fc; i++)
					surf.Faces.Add(new MeshFace(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()));

				surf.RebuildEdges();
				mesh.Surfaces.Add(surf);
			}
			return mesh;
		}

		// =================================================================
		// Skeleton
		// =================================================================
		private static void WriteSkeletonSection(BinaryWriter w, SkeletonDocument? skel)
		{
			w.Write(skel != null);
			if (skel != null) WriteSkeletonData(w, skel);
		}

		private static SkeletonDocument? ReadSkeletonSection(BinaryReader r)
		{
			if (!r.ReadBoolean()) return null;
			return ReadSkeletonData(r);
		}

		private static void WriteSkeletonData(BinaryWriter w, SkeletonDocument skel)
		{
			w.Write(skel.Name);
			w.Write(skel.Bones.Count);
			foreach (var bone in skel.Bones)
			{
				w.Write(bone.Name);
				w.Write(bone.Index);
				w.Write(bone.ParentIndex);
				WriteVec3(w, bone.BindPosition);
				WriteQuat(w, bone.BindRotation);
				WriteVec3(w, bone.BindScale);
				WriteMat4(w, bone.InverseBindMatrix);
				w.Write(bone.DisplayLength);

				w.Write(bone.Tags.Count);
				foreach (var kv in bone.Tags) { w.Write(kv.Key); w.Write(kv.Value); }
			}
		}

		private static SkeletonDocument ReadSkeletonData(BinaryReader r)
		{
			var skel = new SkeletonDocument(r.ReadString());
			int bc = r.ReadInt32();
			for (int i = 0; i < bc; i++)
			{
				var bone = new Bone
				{
					Name = r.ReadString(),
					Index = r.ReadInt32(),
					ParentIndex = r.ReadInt32(),
					BindPosition = ReadVec3(r),
					BindRotation = ReadQuat(r),
					BindScale = ReadVec3(r),
					InverseBindMatrix = ReadMat4(r),
					DisplayLength = r.ReadSingle()
				};
				int tc = r.ReadInt32();
				for (int t = 0; t < tc; t++) bone.Tags[r.ReadString()] = r.ReadString();
				skel.Bones.Add(bone);
			}
			return skel;
		}

		// =================================================================
		// Animation
		// =================================================================
		private static void WriteAnimationSection(BinaryWriter w, AnimationDocument? anim)
		{
			w.Write(anim != null);
			if (anim != null) WriteAnimationData(w, anim);
		}

		private static AnimationDocument? ReadAnimationSection(BinaryReader r)
		{
			if (!r.ReadBoolean()) return null;
			return ReadAnimationData(r);
		}

		private static void WriteAnimationData(BinaryWriter w, AnimationDocument anim)
		{
			w.Write(anim.Name);
			w.Write(anim.SkeletonName);
			w.Write(anim.Clips.Count);
			foreach (var clip in anim.Clips)
			{
				w.Write(clip.Name);
				w.Write(clip.FrameCount);
				w.Write(clip.FrameRate);
				w.Write(clip.Loop);
				w.Write(clip.Channels.Count);
				foreach (var ch in clip.Channels)
				{
					w.Write(ch.BoneIndex);
					WriteKeyList(w, ch.PositionKeys, WriteVec3);
					WriteKeyList(w, ch.RotationKeys, WriteQuat);
					WriteKeyList(w, ch.ScaleKeys, WriteVec3);
				}
			}
		}

		private static AnimationDocument ReadAnimationData(BinaryReader r)
		{
			var anim = new AnimationDocument(r.ReadString()) { SkeletonName = r.ReadString() };
			int cc = r.ReadInt32();
			for (int c = 0; c < cc; c++)
			{
				var clip = new AnimationClip
				{
					Name = r.ReadString(),
					FrameCount = r.ReadInt32(),
					FrameRate = r.ReadSingle(),
					Loop = r.ReadBoolean()
				};
				int chc = r.ReadInt32();
				for (int i = 0; i < chc; i++)
				{
					var ch = new BoneChannel(r.ReadInt32());
					ch.PositionKeys.AddRange(ReadKeyList(r, ReadVec3));
					ch.RotationKeys.AddRange(ReadKeyList(r, ReadQuat));
					ch.ScaleKeys.AddRange(ReadKeyList(r, ReadVec3));
					clip.Channels.Add(ch);
				}
				anim.Clips.Add(clip);
			}
			return anim;
		}

		// =================================================================
		// Keyframe lists
		// =================================================================
		private static void WriteKeyList<T>(BinaryWriter w, List<Keyframe<T>> keys, Action<BinaryWriter, T> write)
		{
			w.Write(keys.Count);
			foreach (var k in keys) { w.Write(k.Frame); w.Write((int)k.Interpolation); write(w, k.Value); }
		}

		private static List<Keyframe<T>> ReadKeyList<T>(BinaryReader r, Func<BinaryReader, T> read)
		{
			int n = r.ReadInt32();
			var list = new List<Keyframe<T>>(n);
			for (int i = 0; i < n; i++)
				list.Add(new Keyframe<T> { Frame = r.ReadInt32(), Interpolation = (InterpolationMode)r.ReadInt32(), Value = read(r) });
			return list;
		}

		// =================================================================
		// Primitive I/O
		// =================================================================
		private static void WriteVec2(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
		private static void WriteVec3(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }
		private static void WriteQuat(BinaryWriter w, Quaternion q) { w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W); }
		private static void WriteMat4(BinaryWriter w, Matrix4x4 m)
		{
			w.Write(m.M11); w.Write(m.M12); w.Write(m.M13); w.Write(m.M14);
			w.Write(m.M21); w.Write(m.M22); w.Write(m.M23); w.Write(m.M24);
			w.Write(m.M31); w.Write(m.M32); w.Write(m.M33); w.Write(m.M34);
			w.Write(m.M41); w.Write(m.M42); w.Write(m.M43); w.Write(m.M44);
		}

		private static Vector2 ReadVec2(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());
		private static Vector3 ReadVec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		private static Quaternion ReadQuat(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
		private static Matrix4x4 ReadMat4(BinaryReader r) => new(
			r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
			r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
			r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(),
			r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
	}
}