using System;
using System.Collections.Generic;
using Glyphborn.Forge.Data;

namespace Glyphborn.Forge
{
	/// <summary>
	/// A lightweight snapshot of a single MeshSurface's geometry state.
	/// Selection state is intentionally preserved so undo restores the
	/// selection to what it was before the operation.
	/// </summary>
	public class MeshSnapshot
	{
		public string Label { get; }
		public int SurfaceIndex { get; }

		// Deep copies of all geometry — minimal allocations
		public MeshVertex[] Vertices { get; }
		public MeshFace[] Faces { get; }

		public MeshSnapshot(string label, int surfaceIndex, MeshSurface surf)
		{
			Label = label;
			SurfaceIndex = surfaceIndex;

			// Deep-copy vertices (Clone preserves bone weights)
			Vertices = new MeshVertex[surf.Vertices.Count];
			for (int i = 0; i < surf.Vertices.Count; i++)
			{
				var src = surf.Vertices[i];
				var dst = src.Clone();
				dst.Selected = src.Selected;
				Vertices[i] = dst;
			}

			// Deep-copy faces (faces are simple value-like objects)
			Faces = new MeshFace[surf.Faces.Count];
			for (int i = 0; i < surf.Faces.Count; i++)
			{
				var src = surf.Faces[i];
				Faces[i] = new MeshFace(src.A, src.B, src.C) { Selected = src.Selected };
			}
		}

		/// <summary>
		/// Apply this snapshot back onto the given surface,
		/// fully replacing its vertex and face lists then rebuilding edges.
		/// </summary>
		public void Restore(MeshSurface surf)
		{
			surf.Vertices.Clear();
			surf.Faces.Clear();

			foreach (var v in Vertices)
			{
				var dst = v.Clone();
				dst.Selected = v.Selected;
				surf.Vertices.Add(dst);
			}

			foreach (var f in Faces)
				surf.Faces.Add(new MeshFace(f.A, f.B, f.C) { Selected = f.Selected });

			surf.RebuildEdges();
			surf.RecalculateNormals();
		}
	}

	/// <summary>
	/// Undo/redo manager for mesh editing.
	/// Lives on ForgeProject and is shared across all surfaces in the document.
	///
	/// Usage:
	///   // Before any destructive operation:
	///   History.Push("Extrude", surfaceIndex, surface);
	///   surface.ExtrudeSelectedFaces();
	///
	///   // Ctrl+Z:
	///   History.Undo(mesh);
	///
	///   // Ctrl+Y:
	///   History.Redo(mesh);
	/// </summary>
	public class EditHistory
	{
		public const int MaxDepth = 64;

		private readonly Stack<MeshSnapshot> _undoStack = new();
		private readonly Stack<MeshSnapshot> _redoStack = new();

		public bool CanUndo => _undoStack.Count > 0;
		public bool CanRedo => _redoStack.Count > 0;

		public string? NextUndoLabel => _undoStack.Count > 0 ? _undoStack.Peek().Label : null;
		public string? NextRedoLabel => _redoStack.Count > 0 ? _redoStack.Peek().Label : null;

		/// <summary>
		/// Snapshot the surface BEFORE performing a destructive edit.
		/// Clears the redo stack (new edits invalidate future redo states).
		/// </summary>
		public void Push(string label, int surfaceIndex, MeshSurface surf)
		{
			_undoStack.Push(new MeshSnapshot(label, surfaceIndex, surf));
			_redoStack.Clear();

			// Trim to max depth
			if (_undoStack.Count > MaxDepth)
			{
				// Stack doesn't expose trimming, rebuild
				var items = _undoStack.ToArray();   // top-first
				_undoStack.Clear();
				for (int i = MaxDepth - 1; i >= 0; i--)
					_undoStack.Push(items[i]);
			}
		}

		/// <summary>
		/// Undo the last operation.
		/// Pushes a redo snapshot of the CURRENT state before restoring the undo snapshot.
		/// </summary>
		/// <param name="mesh">The mesh document to operate on.</param>
		/// <returns>The surface index that was restored, or -1 if nothing to undo.</returns>
		public int Undo(MeshDocument mesh)
		{
			if (!CanUndo) return -1;

			var snap = _undoStack.Pop();
			int si = snap.SurfaceIndex;

			if (si < 0 || si >= mesh.Surfaces.Count) return -1;

			var surf = mesh.Surfaces[si];

			// Push current state onto redo stack before restoring
			_redoStack.Push(new MeshSnapshot(snap.Label, si, surf));

			snap.Restore(surf);
			return si;
		}

		/// <summary>
		/// Redo the last undone operation.
		/// </summary>
		/// <returns>The surface index that was restored, or -1 if nothing to redo.</returns>
		public int Redo(MeshDocument mesh)
		{
			if (!CanRedo) return -1;

			var snap = _redoStack.Pop();
			int si = snap.SurfaceIndex;

			if (si < 0 || si >= mesh.Surfaces.Count) return -1;

			var surf = mesh.Surfaces[si];

			// Push current state onto undo stack
			_undoStack.Push(new MeshSnapshot(snap.Label, si, surf));

			snap.Restore(surf);
			return si;
		}

		/// <summary>Clear both stacks (call on New/Open).</summary>
		public void Clear()
		{
			_undoStack.Clear();
			_redoStack.Clear();
		}
	}
}