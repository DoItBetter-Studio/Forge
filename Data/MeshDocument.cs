using System;
using System.Collections.Generic;
using System.Numerics;

namespace Glyphborn.Forge.Data
{
	// =====================================================================
	// Vertex
	// =====================================================================
	public class MeshVertex
	{
		public Vector3 Position { get; set; }
		public Vector3 Normal { get; set; }
		public Vector2 UV { get; set; }

		/// <summary>Up to 4 bone influences (-1 = unused).</summary>
		public int[] BoneIndices { get; set; } = { -1, -1, -1, -1 };
		public float[] BoneWeights { get; set; } = { 0f, 0f, 0f, 0f };

		/// <summary>Edit-mode selection — not serialised.</summary>
		public bool Selected { get; set; } = false;

		public MeshVertex() { }
		public MeshVertex(Vector3 position) { Position = position; }
		public MeshVertex(Vector3 position, Vector3 normal, Vector2 uv)
		{ Position = position; Normal = normal; UV = uv; }

		public MeshVertex Clone()
		{
			var v = new MeshVertex(Position, Normal, UV);
			Array.Copy(BoneIndices, v.BoneIndices, 4);
			Array.Copy(BoneWeights, v.BoneWeights, 4);
			return v;
		}

		// ----- Weight helpers -----

		public float TotalWeight()
		{
			float t = 0f;
			for (int i = 0; i < 4; i++) if (BoneIndices[i] >= 0) t += BoneWeights[i];
			return t;
		}

		public float GetInfluence(int boneIndex)
		{
			for (int i = 0; i < 4; i++)
				if (BoneIndices[i] == boneIndex) return BoneWeights[i];
			return 0f;
		}

		/// <summary>
		/// Set or blend influence for one bone.
		/// If the bone already has a slot it is updated; otherwise the lowest-weight
		/// slot is replaced. Weights are normalised after every change.
		/// </summary>
		public void SetInfluence(int boneIndex, float weight)
		{
			weight = Math.Clamp(weight, 0f, 1f);

			for (int i = 0; i < 4; i++)
			{
				if (BoneIndices[i] == boneIndex)
				{
					BoneWeights[i] = weight;
					if (weight == 0f) BoneIndices[i] = -1;
					Normalise();
					return;
				}
			}

			if (weight == 0f) return;

			int slot = -1;
			float minW = float.MaxValue;
			for (int i = 0; i < 4; i++)
			{
				if (BoneIndices[i] == -1) { slot = i; break; }
				if (BoneWeights[i] < minW) { minW = BoneWeights[i]; slot = i; }
			}

			BoneIndices[slot] = boneIndex;
			BoneWeights[slot] = weight;
			Normalise();
		}

		public void Normalise()
		{
			float total = TotalWeight();
			if (total < 1e-6f) return;
			for (int i = 0; i < 4; i++)
				if (BoneIndices[i] >= 0) BoneWeights[i] /= total;
		}
	}

	// =====================================================================
	// Edge  (canonical form: A <= B)
	// =====================================================================
	public class MeshEdge
	{
		public int A { get; set; }
		public int B { get; set; }
		public bool Selected { get; set; } = false;

		public MeshEdge() { }
		public MeshEdge(int a, int b) { A = Math.Min(a, b); B = Math.Max(a, b); }

		public bool Contains(int v) => A == v || B == v;

		public override bool Equals(object? obj) => obj is MeshEdge e && e.A == A && e.B == B;
		public override int GetHashCode() => HashCode.Combine(A, B);
	}

	// =====================================================================
	// Face (triangle)
	// =====================================================================
	public class MeshFace
	{
		public int A { get; set; }
		public int B { get; set; }
		public int C { get; set; }
		public bool Selected { get; set; } = false;

		public MeshFace() { }
		public MeshFace(int a, int b, int c) { A = a; B = b; C = c; }

		public bool Contains(int v) => A == v || B == v || C == v;
		public bool IsDegenerate() => A == B || B == C || A == C;

		public Vector3 ComputeNormal(List<MeshVertex> verts)
		{
			var ab = verts[B].Position - verts[A].Position;
			var ac = verts[C].Position - verts[A].Position;
			var cross = Vector3.Cross(ab, ac);
			return cross.LengthSquared() > 0f ? Vector3.Normalize(cross) : Vector3.UnitY;
		}
	}

	// =====================================================================
	// Selection mode
	// =====================================================================
	public enum SelectionMode { Vertex, Edge, Face }

	// =====================================================================
	// MeshSurface — the editable geometry unit
	// =====================================================================
	public class MeshSurface
	{
		public string Name { get; set; } = "Surface";
		public string MaterialName { get; set; } = "default";

		public List<MeshVertex> Vertices { get; set; } = new();
		public List<MeshFace> Faces { get; set; } = new();
		public List<MeshEdge> Edges { get; set; } = new();

		// ----- Flat index list for the rasterizer -----
		public List<int> BuildIndexList()
		{
			var idx = new List<int>(Faces.Count * 3);
			foreach (var f in Faces) { idx.Add(f.A); idx.Add(f.B); idx.Add(f.C); }
			return idx;
		}

		// =================================================================
		// Edges
		// =================================================================
		public void RebuildEdges()
		{
			var set = new HashSet<MeshEdge>();
			foreach (var f in Faces)
			{
				set.Add(new MeshEdge(f.A, f.B));
				set.Add(new MeshEdge(f.B, f.C));
				set.Add(new MeshEdge(f.C, f.A));
			}
			Edges = new List<MeshEdge>(set);
		}

		// =================================================================
		// Normals
		// =================================================================
		public void RecalculateNormals()
		{
			var accum = new Vector3[Vertices.Count];
			foreach (var f in Faces)
			{
				if (f.IsDegenerate()) continue;
				var n = f.ComputeNormal(Vertices);
				accum[f.A] += n; accum[f.B] += n; accum[f.C] += n;
			}
			for (int i = 0; i < Vertices.Count; i++)
				Vertices[i].Normal = accum[i].LengthSquared() > 0f
					? Vector3.Normalize(accum[i])
					: Vector3.UnitY;
		}

		// =================================================================
		// Weld coincident vertices
		// =================================================================
		/// <summary>
		/// Merges all vertices that share the same position (within epsilon)
		/// into a single vertex.  Call this after generating primitives so
		/// edit-mode treats geometrically shared corners as one point.
		/// Normals are recalculated afterwards.
		/// </summary>
		public void WeldByPosition(float epsilon = 1e-5f)
		{
			// Map each vertex index to the canonical representative index
			var remap = new int[Vertices.Count];
			for (int i = 0; i < Vertices.Count; i++) remap[i] = i;

			for (int i = 0; i < Vertices.Count; i++)
			{
				if (remap[i] != i) continue; // already merged into another
				for (int j = i + 1; j < Vertices.Count; j++)
				{
					if (remap[j] != j) continue;
					if (Vector3.DistanceSquared(Vertices[i].Position, Vertices[j].Position) < epsilon * epsilon)
						remap[j] = i;
				}
			}

			// Remap face indices
			foreach (var f in Faces)
			{
				f.A = remap[f.A];
				f.B = remap[f.B];
				f.C = remap[f.C];
			}
			Faces.RemoveAll(f => f.IsDegenerate());

			// Keep only the canonical verts
			var toRemove = new HashSet<int>();
			for (int i = 0; i < Vertices.Count; i++)
				if (remap[i] != i) toRemove.Add(i);

			if (toRemove.Count > 0)
				RemapAndRemove(toRemove);

			RebuildEdges();
			RecalculateNormals();
		}

		// =================================================================
		// Selection
		// =================================================================
		public void SelectAll(SelectionMode mode)
		{
			if (mode == SelectionMode.Vertex) foreach (var v in Vertices) v.Selected = true;
			else if (mode == SelectionMode.Edge) foreach (var e in Edges) e.Selected = true;
			else foreach (var f in Faces) f.Selected = true;
		}

		public void DeselectAll()
		{
			foreach (var v in Vertices) v.Selected = false;
			foreach (var e in Edges) e.Selected = false;
			foreach (var f in Faces) f.Selected = false;
		}

		public List<int> SelectedVertexIndices()
		{
			var r = new List<int>();
			for (int i = 0; i < Vertices.Count; i++)
				if (Vertices[i].Selected) r.Add(i);
			return r;
		}

		/// <summary>
		/// Select (or deselect) all vertices whose position matches the given
		/// vertex's position within epsilon.  Used by click-select so that any
		/// remaining coincident verts are always treated as one point.
		/// </summary>
		public void SelectColocated(int pivotIndex, bool selected, float epsilon = 1e-5f)
		{
			if (pivotIndex < 0 || pivotIndex >= Vertices.Count) return;
			var pivot = Vertices[pivotIndex].Position;
			float e2 = epsilon * epsilon;
			for (int i = 0; i < Vertices.Count; i++)
				if (Vector3.DistanceSquared(Vertices[i].Position, pivot) < e2)
					Vertices[i].Selected = selected;
		}

		// =================================================================
		// Translate
		// =================================================================
		public void TranslateSelected(Vector3 delta, SelectionMode mode)
		{
			var moved = new HashSet<int>();

			if (mode == SelectionMode.Vertex)
			{
				for (int i = 0; i < Vertices.Count; i++)
					if (Vertices[i].Selected) { Vertices[i].Position += delta; moved.Add(i); }
			}
			else if (mode == SelectionMode.Edge)
			{
				foreach (var e in Edges)
				{
					if (!e.Selected) continue;
					if (moved.Add(e.A)) Vertices[e.A].Position += delta;
					if (moved.Add(e.B)) Vertices[e.B].Position += delta;
				}
			}
			else
			{
				foreach (var f in Faces)
				{
					if (!f.Selected) continue;
					if (moved.Add(f.A)) Vertices[f.A].Position += delta;
					if (moved.Add(f.B)) Vertices[f.B].Position += delta;
					if (moved.Add(f.C)) Vertices[f.C].Position += delta;
				}
			}

			RecalculateNormals();
		}

		// =================================================================
		// Extrude selected faces
		// =================================================================
		public void ExtrudeSelectedFaces()
		{
			var selFaces = new List<MeshFace>();
			foreach (var f in Faces) if (f.Selected) selFaces.Add(f);
			if (selFaces.Count == 0) return;

			// Unique cap verts
			var capVerts = new HashSet<int>();
			foreach (var f in selFaces) { capVerts.Add(f.A); capVerts.Add(f.B); capVerts.Add(f.C); }

			// Border edges (shared by exactly one selected face)
			var edgeUseCount = new Dictionary<MeshEdge, int>();
			foreach (var f in selFaces)
			{
				void Inc(int a, int b)
				{
					var e = new MeshEdge(a, b);
					edgeUseCount.TryGetValue(e, out int c);
					edgeUseCount[e] = c + 1;
				}
				Inc(f.A, f.B); Inc(f.B, f.C); Inc(f.C, f.A);
			}
			var borderEdges = new List<MeshEdge>();
			foreach (var kv in edgeUseCount) if (kv.Value == 1) borderEdges.Add(kv.Key);

			// Duplicate cap verts
			var oldToNew = new Dictionary<int, int>();
			foreach (int vi in capVerts)
			{
				oldToNew[vi] = Vertices.Count;
				Vertices.Add(Vertices[vi].Clone());
			}

			// Re-index selected faces to new verts
			foreach (var f in selFaces)
			{ f.A = oldToNew[f.A]; f.B = oldToNew[f.B]; f.C = oldToNew[f.C]; }

			// Side quads
			foreach (var be in borderEdges)
			{
				int oa = be.A, ob = be.B;
				int na = oldToNew[oa], nb = oldToNew[ob];
				Faces.Add(new MeshFace(oa, ob, nb));
				Faces.Add(new MeshFace(oa, nb, na));
			}

			// Leave selection on new cap verts
			DeselectAll();
			foreach (int ni in oldToNew.Values) Vertices[ni].Selected = true;

			RebuildEdges();
			RecalculateNormals();
		}

		// =================================================================
		// Delete selected
		// =================================================================
		public void DeleteSelected(SelectionMode mode)
		{
			if (mode == SelectionMode.Vertex)
			{
				var sel = new HashSet<int>(SelectedVertexIndices());
				Faces.RemoveAll(f => sel.Contains(f.A) || sel.Contains(f.B) || sel.Contains(f.C));
				RemapAndRemove(sel);
			}
			else if (mode == SelectionMode.Face)
			{
				Faces.RemoveAll(f => f.Selected);
				PurgeOrphanVertices();
			}
			else
			{
				// Edge delete: remove faces that use the selected edges
				var selEdges = new HashSet<MeshEdge>();
				foreach (var e in Edges) if (e.Selected) selEdges.Add(e);
				Faces.RemoveAll(f =>
					selEdges.Contains(new MeshEdge(f.A, f.B)) ||
					selEdges.Contains(new MeshEdge(f.B, f.C)) ||
					selEdges.Contains(new MeshEdge(f.C, f.A)));
				PurgeOrphanVertices();
			}

			RebuildEdges();
			RecalculateNormals();
		}

		// =================================================================
		// Flip normals
		// =================================================================
		public void FlipSelectedNormals(SelectionMode mode)
		{
			if (mode == SelectionMode.Face)
			{
				foreach (var f in Faces)
					if (f.Selected) (f.B, f.C) = (f.C, f.B);
			}
			else
			{
				foreach (var f in Faces) (f.B, f.C) = (f.C, f.B);
			}
			RecalculateNormals();
		}

		// =================================================================
		// Merge selected vertices to centroid
		// =================================================================
		public void MergeSelected()
		{
			var sel = SelectedVertexIndices();
			if (sel.Count < 2) return;

			Vector3 avg = Vector3.Zero;
			foreach (int i in sel) avg += Vertices[i].Position;
			avg /= sel.Count;

			int keep = sel[0];
			Vertices[keep].Position = avg;

			var remap = new Dictionary<int, int>();
			for (int i = 1; i < sel.Count; i++) remap[sel[i]] = keep;

			foreach (var f in Faces)
			{
				if (remap.TryGetValue(f.A, out int ra)) f.A = ra;
				if (remap.TryGetValue(f.B, out int rb)) f.B = rb;
				if (remap.TryGetValue(f.C, out int rc)) f.C = rc;
			}
			Faces.RemoveAll(f => f.IsDegenerate());

			RemapAndRemove(new HashSet<int>(remap.Keys));
			RebuildEdges();
			RecalculateNormals();
		}

		// =================================================================
		// Loop cut — split an edge by inserting a midpoint vertex
		// =================================================================
		public void LoopCut(MeshEdge edge)
		{
			int ai = edge.A, bi = edge.B;
			int mi = Vertices.Count;

			var mv = new MeshVertex
			{
				Position = (Vertices[ai].Position + Vertices[bi].Position) * 0.5f,
				UV = (Vertices[ai].UV + Vertices[bi].UV) * 0.5f
			};
			// Interpolate bone weights
			for (int s = 0; s < 4; s++)
			{
				mv.BoneIndices[s] = Vertices[ai].BoneIndices[s];
				mv.BoneWeights[s] = (Vertices[ai].BoneWeights[s] + Vertices[bi].BoneWeights[s]) * 0.5f;
			}
			Vertices.Add(mv);

			var toAdd = new List<MeshFace>();
			var toRemove = new List<MeshFace>();

			foreach (var f in Faces)
			{
				if (!f.Contains(ai) || !f.Contains(bi)) continue;
				toRemove.Add(f);

				// Find the vertex that is neither ai nor bi
				int ov = f.A != ai && f.A != bi ? f.A :
						 f.B != ai && f.B != bi ? f.B : f.C;

				toAdd.Add(new MeshFace(ai, mi, ov));
				toAdd.Add(new MeshFace(mi, bi, ov));
			}

			foreach (var f in toRemove) Faces.Remove(f);
			Faces.AddRange(toAdd);

			mv.Selected = true;
			RebuildEdges();
			RecalculateNormals();
		}

		// =================================================================
		// Vertex weight painting (called per-brush-stroke)
		// =================================================================
		/// <summary>
		/// Paint influence for boneIndex onto every vertex within worldRadius of worldCenter.
		/// strength is 0..1 (brush opacity). additive=true adds, false erases.
		/// </summary>
		public void PaintWeights(Vector3 worldCenter, float worldRadius, int boneIndex, float strength, bool additive)
		{
			float r2 = worldRadius * worldRadius;
			foreach (var v in Vertices)
			{
				float d2 = Vector3.DistanceSquared(v.Position, worldCenter);
				if (d2 >= r2) continue;

				float falloff = 1f - d2 / r2; // 0 at edge, 1 at centre
				float delta = falloff * strength;

				float current = v.GetInfluence(boneIndex);
				float target = additive
					? Math.Clamp(current + delta, 0f, 1f)
					: Math.Clamp(current - delta, 0f, 1f);

				v.SetInfluence(boneIndex, target);
			}
		}

		// =================================================================
		// Internal helpers
		// =================================================================
		private void PurgeOrphanVertices()
		{
			var referenced = new HashSet<int>();
			foreach (var f in Faces) { referenced.Add(f.A); referenced.Add(f.B); referenced.Add(f.C); }
			var orphans = new HashSet<int>();
			for (int i = 0; i < Vertices.Count; i++)
				if (!referenced.Contains(i)) orphans.Add(i);
			if (orphans.Count > 0) RemapAndRemove(orphans);
		}

		private void RemapAndRemove(HashSet<int> toRemove)
		{
			var remapTable = new int[Vertices.Count];
			var newVerts = new List<MeshVertex>(Vertices.Count - toRemove.Count);
			int next = 0;

			for (int i = 0; i < Vertices.Count; i++)
			{
				if (toRemove.Contains(i)) { remapTable[i] = -1; continue; }
				remapTable[i] = next++;
				newVerts.Add(Vertices[i]);
			}
			Vertices = newVerts;

			foreach (var f in Faces)
			{
				f.A = remapTable[f.A];
				f.B = remapTable[f.B];
				f.C = remapTable[f.C];
			}
			Faces.RemoveAll(f => f.A < 0 || f.B < 0 || f.C < 0 || f.IsDegenerate());
		}
	}

	// =====================================================================
	// MeshDocument  — top-level container + primitive factory
	// =====================================================================
	public class MeshDocument
	{
		public string Name { get; set; } = "Untitled";
		public List<MeshSurface> Surfaces { get; set; } = new();

		public MeshDocument() { }
		public MeshDocument(string name) { Name = name; }

		// =================================================================
		// Primitive generators
		// =================================================================
		public static MeshSurface MakeCube(string name = "Cube", float size = 1f)
		{
			float h = size * 0.5f;
			var s = new MeshSurface { Name = name };

			// 8 unique corners — indices match standard cube layout:
			//  0:-x-y-z  1:+x-y-z  2:+x+y-z  3:-x+y-z
			//  4:-x-y+z  5:+x-y+z  6:+x+y+z  7:-x+y+z
			s.Vertices.Add(new MeshVertex(new(-h, -h, -h))); // 0
			s.Vertices.Add(new MeshVertex(new(h, -h, -h))); // 1
			s.Vertices.Add(new MeshVertex(new(h, h, -h))); // 2
			s.Vertices.Add(new MeshVertex(new(-h, h, -h))); // 3
			s.Vertices.Add(new MeshVertex(new(-h, -h, h))); // 4
			s.Vertices.Add(new MeshVertex(new(h, -h, h))); // 5
			s.Vertices.Add(new MeshVertex(new(h, h, h))); // 6
			s.Vertices.Add(new MeshVertex(new(-h, h, h))); // 7

			// Faces wound CCW as seen from outside (outward normals via right-hand rule)
			// Front  (-Z)
			s.Faces.Add(new MeshFace(0, 2, 1));
			s.Faces.Add(new MeshFace(0, 3, 2));
			// Back   (+Z)
			s.Faces.Add(new MeshFace(4, 5, 6));
			s.Faces.Add(new MeshFace(4, 6, 7));
			// Left   (-X)
			s.Faces.Add(new MeshFace(0, 4, 7));
			s.Faces.Add(new MeshFace(0, 7, 3));
			// Right  (+X)
			s.Faces.Add(new MeshFace(1, 2, 6));
			s.Faces.Add(new MeshFace(1, 6, 5));
			// Bottom (-Y)
			s.Faces.Add(new MeshFace(0, 1, 5));
			s.Faces.Add(new MeshFace(0, 5, 4));
			// Top    (+Y)
			s.Faces.Add(new MeshFace(3, 7, 6));
			s.Faces.Add(new MeshFace(3, 6, 2));

			s.RebuildEdges();
			s.RecalculateNormals();
			return s;
		}

		public static MeshSurface MakePlane(string name = "Plane", float size = 1f, int subdX = 1, int subdZ = 1)
		{
			var s = new MeshSurface { Name = name };
			float h = size * 0.5f;
			int w = subdX + 1, d = subdZ + 1;

			for (int z = 0; z < d; z++)
				for (int x = 0; x < w; x++)
					s.Vertices.Add(new MeshVertex(
						new(-h + (float)x / subdX * size, 0, -h + (float)z / subdZ * size),
						Vector3.UnitY,
						new((float)x / subdX, (float)z / subdZ)));

			for (int z = 0; z < subdZ; z++)
				for (int x = 0; x < subdX; x++)
				{
					int i = z * w + x;
					s.Faces.Add(new MeshFace(i, i + 1, i + w + 1));
					s.Faces.Add(new MeshFace(i, i + w + 1, i + w));
				}

			s.RebuildEdges();
			return s;
		}

		public static MeshSurface MakeCylinder(string name = "Cylinder", float radius = 0.5f, float height = 1f, int segs = 12)
		{
			var s = new MeshSurface { Name = name };
			float h = height * 0.5f;

			for (int i = 0; i < segs; i++)
			{
				float a = MathF.PI * 2f * i / segs;
				float x = MathF.Cos(a) * radius, z = MathF.Sin(a) * radius;
				s.Vertices.Add(new MeshVertex(new(x, -h, z), new(x, 0, z), new((float)i / segs, 0)));
				s.Vertices.Add(new MeshVertex(new(x, h, z), new(x, 0, z), new((float)i / segs, 1)));
			}
			int botC = s.Vertices.Count; s.Vertices.Add(new MeshVertex(new(0, -h, 0), -Vector3.UnitY, new(0.5f, 0.5f)));
			int topC = s.Vertices.Count; s.Vertices.Add(new MeshVertex(new(0, h, 0), Vector3.UnitY, new(0.5f, 0.5f)));

			for (int i = 0; i < segs; i++)
			{
				int n = (i + 1) % segs;
				int b0 = i * 2, t0 = i * 2 + 1, b1 = n * 2, t1 = n * 2 + 1;
				s.Faces.Add(new MeshFace(b0, b1, t1)); s.Faces.Add(new MeshFace(b0, t1, t0));
				s.Faces.Add(new MeshFace(botC, b1, b0)); s.Faces.Add(new MeshFace(topC, t0, t1));
			}

			s.RebuildEdges(); s.RecalculateNormals();
			s.WeldByPosition();
			return s;
		}

		public static MeshSurface MakeSphere(string name = "Sphere", float radius = 0.5f, int rings = 8, int segs = 12)
		{
			var s = new MeshSurface { Name = name };

			s.Vertices.Add(new MeshVertex(new(0, radius, 0), Vector3.UnitY, new(0.5f, 0f)));

			for (int r = 1; r < rings; r++)
			{
				float phi = MathF.PI * r / rings;
				for (int i = 0; i < segs; i++)
				{
					float theta = MathF.PI * 2f * i / segs;
					var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
					s.Vertices.Add(new MeshVertex(n * radius, n, new((float)i / segs, (float)r / rings)));
				}
			}

			int bot = s.Vertices.Count;
			s.Vertices.Add(new MeshVertex(new(0, -radius, 0), -Vector3.UnitY, new(0.5f, 1f)));

			for (int i = 0; i < segs; i++)
				s.Faces.Add(new MeshFace(0, 1 + i, 1 + (i + 1) % segs));

			for (int r = 0; r < rings - 2; r++)
				for (int i = 0; i < segs; i++)
				{
					int i0 = 1 + r * segs + i, i1 = 1 + r * segs + (i + 1) % segs;
					int i2 = 1 + (r + 1) * segs + i, i3 = 1 + (r + 1) * segs + (i + 1) % segs;
					s.Faces.Add(new MeshFace(i0, i2, i3)); s.Faces.Add(new MeshFace(i0, i3, i1));
				}

			int ls = 1 + (rings - 2) * segs;
			for (int i = 0; i < segs; i++)
				s.Faces.Add(new MeshFace(bot, ls + (i + 1) % segs, ls + i));

			s.RebuildEdges();
			return s;
		}

		public static MeshSurface MakeCapsule(string name = "Capsule", float radius = 0.5f, float height = 2f, int segs = 12, int rings = 4)
		{
			var s = new MeshSurface { Name = name };
			float bodyH = Math.Max(0f, height - radius * 2f);
			float h = bodyH * 0.5f;
			int totalRings = rings * 2 + 2;

			// Top hemisphere
			for (int r = 0; r <= rings; r++)
			{
				float phi = MathF.PI * 0.5f * r / rings;
				for (int i = 0; i < segs; i++)
				{
					float theta = MathF.PI * 2f * i / segs;
					float x = MathF.Cos(phi) * MathF.Cos(theta) * radius;
					float y = h + MathF.Sin(phi) * radius;
					float z = MathF.Cos(phi) * MathF.Sin(theta) * radius;
					s.Vertices.Add(new MeshVertex(new(x, y, z), Vector3.Normalize(new(x, y - h, z)),
						new((float)i / segs, (float)r / (totalRings - 1))));
				}
			}

			// Bottom hemisphere
			for (int r = 0; r <= rings; r++)
			{
				float phi = MathF.PI * 0.5f * r / rings;
				for (int i = 0; i < segs; i++)
				{
					float theta = MathF.PI * 2f * i / segs;
					float x = MathF.Cos(phi) * MathF.Cos(theta) * radius;
					float y = -h - MathF.Sin(phi) * radius;
					float z = MathF.Cos(phi) * MathF.Sin(theta) * radius;
					s.Vertices.Add(new MeshVertex(new(x, y, z), Vector3.Normalize(new(x, y + h, z)),
						new((float)i / segs, 1f - (float)r / (totalRings - 1))));
				}
			}

			for (int r = 0; r < totalRings - 1; r++)
				for (int i = 0; i < segs; i++)
				{
					int i0 = r * segs + i, i1 = r * segs + (i + 1) % segs;
					int i2 = (r + 1) * segs + i, i3 = (r + 1) * segs + (i + 1) % segs;
					s.Faces.Add(new MeshFace(i0, i2, i3)); s.Faces.Add(new MeshFace(i0, i3, i1));
				}

			s.RebuildEdges(); s.RecalculateNormals();
			return s;
		}
	}
}