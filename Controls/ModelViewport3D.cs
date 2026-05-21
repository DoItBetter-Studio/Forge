using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Windows.Forms;
using System.ComponentModel;
using Glyphborn.Forge.Data;

namespace Glyphborn.Forge.Controls
{
	// =====================================================================
	// Enums
	// =====================================================================
	public enum ViewportMode
	{
		Object,      // Camera orbit, insert primitives, select surfaces
		Edit,        // Vertex / edge / face select, move, extrude, etc.
		WeightPaint, // Paint bone influences onto vertices
		PosePreview  // Playback mode — skeleton drives mesh
	}

	public enum ViewportRenderMode
	{
		Solid,
		Wireframe,
		SolidWireframe
	}

	// =====================================================================
	// ModelViewport3D
	// =====================================================================
	public class ModelViewport3D : UserControl
	{
		// ----- Public state -----
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ForgeProject? Project { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ViewportMode Mode { get; set; } = ViewportMode.Object;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public ViewportRenderMode RenderMode { get; set; } = ViewportRenderMode.SolidWireframe;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Data.SelectionMode EditSelectionMode { get; set; } = Data.SelectionMode.Vertex;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ShowBones { get; set; } = true;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool ShowGrid { get; set; } = true;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int SelectedBoneIndex { get; set; } = -1;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ActiveSurfaceIndex { get; set; } = 0;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public AnimationClip? PreviewClip { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public float CurrentFrame { get; set; } = 0f;

		// Weight paint settings
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public float BrushRadius { get; set; } = 0.5f;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public float BrushStrength { get; set; } = 0.1f;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool BrushAdditive { get; set; } = true;

		// Events
		public event Action<int>? BoneSelected;
		public event Action<MeshVertex>? VertexSelected;

		// ----- Camera -----
		private float _yaw = 0.5f, _pitch = 0.4f, _distance = 8f;
		private Vector3 _target = new(0f, 1f, 0f);
		private Vector3 _eye;

		// ----- Lighting -----
		private readonly Vector3 _lightDir = Vector3.Normalize(new Vector3(0.6f, -1f, 0.4f));

		// ----- Mouse -----
		private Point _lastMouse;
		private bool _panning, _orbiting;
		private bool _movingSelection;
		private bool _paintStrokeStarted; // ensures undo is pushed once per weight-paint stroke

		// Box selection
		private Point _boxStart;
		private bool _boxSelecting;
		private RectangleF _boxRect;

		// Translate gizmo
		private enum GizmoAxis { None, X, Y, Z }
		private GizmoAxis _gizmoActive = GizmoAxis.None;
		private GizmoAxis _gizmoHovered = GizmoAxis.None;
		private Vector3 _gizmoCentre;           // world-space centroid of selection
		private PointF _gizmoDragOrigin;       // screen pos where drag started
		private Vector3 _gizmoAxisWorld;        // unit world axis being dragged
		private Vector3 _gizmoLockedCentre;
		static float _prevWorldDelta = 0f;

		private Vector3 _moveStartWorld;
		private Point _moveStartScreen;

		// ----- Rasterizer -----
		private Bitmap? _backbuffer;
		private float[]? _depthBuffer;
		private Matrix4x4 _view, _proj;
		private Timer _timer = null!;

		// Cached bone matrices
		private Matrix4x4[] _boneWorldMatrices = Array.Empty<Matrix4x4>();

		// Screen-space bone positions for picking
		private readonly List<(int idx, PointF pos)> _boneScreenPositions = new();

		// Screen-space vertex positions for edit-mode picking
		private readonly List<(int idx, PointF pos, float depth)> _vertexScreenPositions = new();

		// Colours
		private static readonly Color SurfaceColor = Color.FromArgb(170, 125, 80);
		private static readonly Color SurfaceEditColor = Color.FromArgb(50, 70, 90);
		private static readonly Color WireColor = Color.FromArgb(55, 55, 55);
		private static readonly Color WireEditColor = Color.FromArgb(90, 90, 110);
		private static readonly Color SelVertColor = Color.FromArgb(255, 160, 20);
		private static readonly Color SelFaceColor = Color.FromArgb(50, 80, 200);
		private static readonly Color BoneColor = Color.FromArgb(240, 200, 60);
		private static readonly Color BoneSelColor = Color.FromArgb(60, 200, 255);
		private static readonly Color GridColor = Color.FromArgb(50, 50, 55);

		// =====================================================================
		// Constructor
		// =====================================================================
		public ModelViewport3D()
		{
			DoubleBuffered = true;
			BackColor = Color.FromArgb(30, 30, 32);

			_timer = new Timer { Interval = 32 };
			_timer.Tick += (_, __) => Invalidate();
			_timer.Start();
		}

		// =====================================================================
		// Mouse
		// =====================================================================
		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			_lastMouse = e.Location;
			Capture = true;

			_panning = e.Button == MouseButtons.Middle;
			_orbiting = e.Button == MouseButtons.Right;

			if (e.Button == MouseButtons.Left)
			{
				switch (Mode)
				{
					case ViewportMode.Object:
						TrySelectBone(e.Location);
						break;
					case ViewportMode.Edit:
						// Check gizmo first — if we hit an axis, start axis-constrained drag
						if (HasSelection() && TryGrabGizmo(e.Location))
						{
							// _gizmoActive is now set; skip box select
							break;
						}
						// Record start for potential box-drag; actual select on MouseUp
						_boxStart = e.Location;
						_boxSelecting = true;
						_boxRect = RectangleF.Empty;
						break;
					case ViewportMode.WeightPaint:
						if (!_paintStrokeStarted)
						{
							PushUndo("Paint Weights");
							_paintStrokeStarted = true;
						}
						PaintAtScreenPos(e.Location);
						break;
					case ViewportMode.PosePreview:
						TrySelectBone(e.Location);
						break;
				}
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (!Capture) return;

			float dx = e.X - _lastMouse.X;
			float dy = e.Y - _lastMouse.Y;
			_lastMouse = e.Location;

			if (_panning)
			{
				Cursor = Cursors.Hand;
				float speed = _distance * 0.002f;
				Vector3 right = new(MathF.Cos(_yaw + MathF.PI * 0.5f), 0f, MathF.Sin(_yaw + MathF.PI * 0.5f));
				_target += (right * dx + Vector3.UnitY * -dy) * speed;
			}
			else if (_orbiting)
			{
				Cursor = Cursors.SizeAll;
				_yaw += dx * 0.01f;
				_pitch = Math.Clamp(_pitch - dy * 0.01f, -1.45f, 1.45f);
			}
			else if (e.Button == MouseButtons.Left)
			{
				if (Mode == ViewportMode.Edit)
				{
					if (_gizmoActive != GizmoAxis.None)
					{
						// Project mouse delta onto the screen-space direction of the axis,
						// scale to world units, move selection along that axis only.
						DragGizmo(e.Location);
					}
					else if (_movingSelection)
					{
						float speed = _distance * 0.004f;
						Vector3 right = new(MathF.Cos(_yaw + MathF.PI * 0.5f), 0f, MathF.Sin(_yaw + MathF.PI * 0.5f));
						// Screen Y increases downward, so negate dy to get correct world-up movement
						Vector3 delta = (right * dx + Vector3.UnitY * -dy) * speed;
						ActiveSurface()?.TranslateSelected(delta, EditSelectionMode);
					}
					else if (_boxSelecting)
					{
						float x = Math.Min(_boxStart.X, e.X);
						float y = Math.Min(_boxStart.Y, e.Y);
						float w = Math.Abs(e.X - _boxStart.X);
						float h = Math.Abs(e.Y - _boxStart.Y);
						_boxRect = new RectangleF(x, y, w, h);
					}
				}
				else if (Mode == ViewportMode.WeightPaint)
				{
					PaintAtScreenPos(e.Location);
				}
			}
			else if (e.Button == MouseButtons.None && Mode == ViewportMode.Edit)
			{
				// Recompute centroid each hover frame so the arrows track selection changes
				if (HasSelection()) _gizmoCentre = SelectionCentroid();
				_gizmoHovered = HitTestGizmo(e.Location);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);

			if (e.Button == MouseButtons.Left && Mode == ViewportMode.Edit && _boxSelecting)
			{
				bool additive = (ModifierKeys & Keys.Shift) != 0;
				float dragDist = Dist(e.Location, _boxStart);

				if (dragDist < 4f)
				{
					// Treat as a click — single point select
					TrySelectGeometry(e.Location, additive);
				}
				else
				{
					// Box select everything inside _boxRect
					BoxSelectGeometry(_boxRect, additive);
				}

				_boxSelecting = false;
				_boxRect = RectangleF.Empty;
			}

			_panning = _orbiting = _movingSelection = false;
			_paintStrokeStarted = false;
			_gizmoActive = GizmoAxis.None;
			_boxSelecting = false;
			Capture = false;
			Cursor = Cursors.Default;
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			_distance *= e.Delta > 0 ? 0.9f : 1.1f;
			_distance = Math.Clamp(_distance, 0.5f, 300f);
		}

		protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Cursor = Cursors.Default; }

		// =====================================================================
		// Keyboard — Edit mode shortcuts
		// =====================================================================
		protected override bool IsInputKey(Keys keyData) => true;

		protected override void OnKeyDown(KeyEventArgs e)
		{
			base.OnKeyDown(e);

			// Undo / Redo — handled regardless of mode
			if (e.Control && e.KeyCode == Keys.Z)
			{
				PerformUndo();
				e.Handled = true;
				return;
			}
			if (e.Control && (e.KeyCode == Keys.Y || (e.Shift && e.KeyCode == Keys.Z)))
			{
				PerformRedo();
				e.Handled = true;
				return;
			}

			var surf = ActiveSurface();
			if (surf == null) return;

			switch (Mode)
			{
				case ViewportMode.Edit:
					switch (e.KeyCode)
					{
						case Keys.G:
							PushUndo("Move");
							_movingSelection = true;
							break;
						case Keys.A when !e.Control:
							// Selection change — no undo needed
							if (surf.SelectedVertexIndices().Count == surf.Vertices.Count)
								surf.DeselectAll();
							else
								surf.SelectAll(EditSelectionMode);
							break;
						case Keys.E:
							PushUndo("Extrude");
							surf.ExtrudeSelectedFaces();
							// Immediately enter move mode so the extruded cap follows the mouse
							_movingSelection = true;
							break;
						case Keys.X:
						case Keys.Delete:
							PushUndo("Delete");
							surf.DeleteSelected(EditSelectionMode);
							break;
						case Keys.M:
							PushUndo("Merge");
							surf.MergeSelected();
							break;
						case Keys.F:
							PushUndo("Flip Normals");
							surf.FlipSelectedNormals(EditSelectionMode);
							break;
					}
					break;

				case ViewportMode.WeightPaint:
					if (e.KeyCode == Keys.F)
						BrushAdditive = !BrushAdditive;
					break;
			}
		}

		// =====================================================================
		// Undo / Redo
		// =====================================================================

		/// <summary>Called by ForgeForm (Ctrl+Z) so undo works regardless of focus.</summary>
		public void TriggerUndo() => PerformUndo();

		/// <summary>Called by ForgeForm (Ctrl+Y) so redo works regardless of focus.</summary>
		public void TriggerRedo() => PerformRedo();

		/// <summary>
		/// Push a snapshot of the active surface onto the undo stack.
		/// Call this BEFORE any destructive operation.
		/// </summary>
		private void PushUndo(string label)
		{
			if (Project?.Mesh == null) return;
			var surf = ActiveSurface();
			if (surf == null) return;
			Project.History.Push(label, ActiveSurfaceIndex, surf);
		}

		private void PerformUndo()
		{
			if (Project?.Mesh == null) return;
			int si = Project.History.Undo(Project.Mesh);
			if (si >= 0)
			{
				ActiveSurfaceIndex = si;
				Invalidate();
			}
		}

		private void PerformRedo()
		{
			if (Project?.Mesh == null) return;
			int si = Project.History.Redo(Project.Mesh);
			if (si >= 0)
			{
				ActiveSurfaceIndex = si;
				Invalidate();
			}
		}

		// =====================================================================
		// Paint weights at screen position
		// =====================================================================
		private void PaintAtScreenPos(Point screenPos)
		{
			if (SelectedBoneIndex < 0) return;
			var surf = ActiveSurface();
			if (surf == null) return;

			// Unproject a ray from the screen and find hit position on mesh
			Vector3? hitWorld = PickMeshPoint(screenPos, surf);
			if (hitWorld == null)
			{
				// Fall back: paint on any vertex within brush radius projected to screen
				PaintByScreenDistance(screenPos, surf);
				return;
			}

			surf.PaintWeights(hitWorld.Value, BrushRadius, SelectedBoneIndex, BrushStrength, BrushAdditive);
		}

		private void PaintByScreenDistance(Point screenPos, MeshSurface surf)
		{
			// Project all vertices to screen and paint by screen-space distance
			float screenRadius = BrushRadius * Height / _distance * 20f;
			float r2 = screenRadius * screenRadius;

			foreach (var (idx, spos, depth) in _vertexScreenPositions)
			{
				float ddx = spos.X - screenPos.X;
				float ddy = spos.Y - screenPos.Y;
				if (ddx * ddx + ddy * ddy > r2) continue;

				float falloff = 1f - (ddx * ddx + ddy * ddy) / r2;
				float current = surf.Vertices[idx].GetInfluence(SelectedBoneIndex);
				float target = BrushAdditive
					? Math.Clamp(current + falloff * BrushStrength, 0f, 1f)
					: Math.Clamp(current - falloff * BrushStrength, 0f, 1f);
				surf.Vertices[idx].SetInfluence(SelectedBoneIndex, target);
			}
		}

		// =====================================================================
		// Geometry picking (edit mode)
		// =====================================================================
		private void TrySelectGeometry(Point click, bool additive)
		{
			var surf = ActiveSurface();
			if (surf == null) return;

			if (!additive) surf.DeselectAll();

			if (EditSelectionMode == Data.SelectionMode.Vertex)
			{
				const float pickR = 12f;
				int best = -1; float bestDist = float.MaxValue;
				foreach (var (idx, pos, _) in _vertexScreenPositions)
				{
					float d = Dist(click, pos);
					if (d < pickR && d < bestDist) { bestDist = d; best = idx; }
				}
				if (best >= 0)
				{
					// Determine new state from the pivot vert then apply to all colocated
					bool newState = !surf.Vertices[best].Selected;
					surf.SelectColocated(best, newState);
					VertexSelected?.Invoke(surf.Vertices[best]);
				}
			}
			else if (EditSelectionMode == Data.SelectionMode.Edge)
			{
				const float pickR = 8f;
				MeshEdge? bestEdge = null; float bestDist = float.MaxValue;
				foreach (var edge in surf.Edges)
				{
					var pa = GetVertexScreen(edge.A);
					var pb = GetVertexScreen(edge.B);
					if (pa == null || pb == null) continue;
					float d = DistToSegment(click, pa.Value, pb.Value);
					if (d < pickR && d < bestDist) { bestDist = d; bestEdge = edge; }
				}
				if (bestEdge != null) bestEdge.Selected = !bestEdge.Selected;
			}
			else // Face
			{
				const float pickR = 20f;
				MeshFace? bestFace = null; float bestDist = float.MaxValue;
				foreach (var f in surf.Faces)
				{
					var sa = GetVertexScreen(f.A);
					var sb = GetVertexScreen(f.B);
					var sc = GetVertexScreen(f.C);
					if (sa == null || sb == null || sc == null) continue;
					var centroid = new PointF(
						(sa.Value.X + sb.Value.X + sc.Value.X) / 3f,
						(sa.Value.Y + sb.Value.Y + sc.Value.Y) / 3f);
					float d = Dist(click, centroid);
					if (d < pickR && d < bestDist) { bestDist = d; bestFace = f; }
				}
				if (bestFace != null) bestFace.Selected = !bestFace.Selected;
			}
		}

		/// <summary>Select all geometry whose screen position falls inside rect.</summary>
		private void BoxSelectGeometry(RectangleF rect, bool additive)
		{
			var surf = ActiveSurface();
			if (surf == null) return;

			if (!additive) surf.DeselectAll();

			if (EditSelectionMode == Data.SelectionMode.Vertex)
			{
				// Select all verts inside rect, then also select their colocated neighbours
				var insideIndices = new HashSet<int>();
				foreach (var (idx, pos, _) in _vertexScreenPositions)
					if (rect.Contains(pos)) insideIndices.Add(idx);

				foreach (int idx in insideIndices)
					surf.SelectColocated(idx, true);
			}
			else if (EditSelectionMode == Data.SelectionMode.Edge)
			{
				foreach (var edge in surf.Edges)
				{
					var pa = GetVertexScreen(edge.A);
					var pb = GetVertexScreen(edge.B);
					if (pa == null || pb == null) continue;
					if (rect.Contains(pa.Value) && rect.Contains(pb.Value))
						edge.Selected = true;
				}
			}
			else // Face
			{
				foreach (var f in surf.Faces)
				{
					var sa = GetVertexScreen(f.A);
					var sb = GetVertexScreen(f.B);
					var sc = GetVertexScreen(f.C);
					if (sa == null || sb == null || sc == null) continue;
					if (rect.Contains(sa.Value) && rect.Contains(sb.Value) && rect.Contains(sc.Value))
						f.Selected = true;
				}
			}
		}

		private PointF? GetVertexScreen(int idx)
		{
			foreach (var (i, pos, _) in _vertexScreenPositions)
				if (i == idx) return pos;
			return null;
		}

		// Simple mesh point picking via nearest visible vertex
		private Vector3? PickMeshPoint(Point screenPos, MeshSurface surf)
		{
			const float pickR = 30f;
			int best = -1; float bestDist = float.MaxValue;
			foreach (var (idx, pos, depth) in _vertexScreenPositions)
			{
				float d = Dist(screenPos, pos);
				if (d < pickR && d < bestDist) { bestDist = d; best = idx; }
			}
			return best >= 0 ? surf.Vertices[best].Position : null;
		}

		// =====================================================================
		// Bone picking
		// =====================================================================
		private void TrySelectBone(Point click)
		{
			const float r = 12f;
			int best = -1; float bestDist = float.MaxValue;
			foreach (var (idx, pos) in _boneScreenPositions)
			{
				float d = Dist(click, pos);
				if (d < r && d < bestDist) { bestDist = d; best = idx; }
			}
			if (best != SelectedBoneIndex)
			{
				SelectedBoneIndex = best;
				BoneSelected?.Invoke(best);
			}
		}

		// =====================================================================
		// Resize / visible
		// =====================================================================
		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			_backbuffer?.Dispose(); _backbuffer = null;
			if (Width > 0 && Height > 0)
			{
				_backbuffer = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
				_depthBuffer = new float[Width * Height];
			}
		}

		protected override void OnVisibleChanged(EventArgs e)
		{
			base.OnVisibleChanged(e);
			_timer.Enabled = Visible;
		}

		// =====================================================================
		// Paint
		// =====================================================================
		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			if (_backbuffer == null || _depthBuffer == null) return;

			// Camera
			_eye = new Vector3(
				_target.X + MathF.Cos(_yaw) * MathF.Cos(_pitch) * _distance,
				_target.Y + MathF.Sin(_pitch) * _distance,
				_target.Z + MathF.Sin(_yaw) * MathF.Cos(_pitch) * _distance);
			var eye = _eye;

			_view = Matrix4x4.CreateLookAt(eye, _target, Vector3.UnitY);
			_proj = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, Width / (float)Height, 0.05f, 500f);

			var bmpData = _backbuffer.LockBits(
				new Rectangle(0, 0, Width, Height),
				ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

			unsafe
			{
				byte* ptr = (byte*)bmpData.Scan0;
				int stride = bmpData.Stride;

				ClearBuffers(ptr, stride);
				UpdateBoneMatrices();
				CacheVertexScreenPositions();

				if (ShowGrid) DrawGrid(ptr, stride);
				if (Project?.Mesh != null) DrawMesh(ptr, stride);
			}

			_backbuffer.UnlockBits(bmpData);
			e.Graphics.DrawImage(_backbuffer, 0, 0);

			// GDI+ overlays
			if (ShowBones && Project?.Skeleton != null)
				DrawBonesOverlay(e.Graphics);

			if (Mode == ViewportMode.Edit)
			{
				DrawEditOverlay(e.Graphics);
				if (_boxSelecting && _boxRect.Width > 1 && _boxRect.Height > 1)
					DrawBoxSelect(e.Graphics);
				if (HasSelection())
					DrawGizmo(e.Graphics);
			}

			if (Mode == ViewportMode.WeightPaint)
				DrawBrushCursor(e.Graphics);

			DrawHUD(e.Graphics, eye);
		}

		// =====================================================================
		// Buffer clear
		// =====================================================================
		private unsafe void ClearBuffers(byte* ptr, int stride)
		{
			uint bg = Pack(30, 30, 32);
			for (int y = 0; y < Height; y++)
			{
				uint* row = (uint*)(ptr + y * stride);
				for (int x = 0; x < Width; x++) row[x] = bg;
			}
			for (int i = 0; i < _depthBuffer!.Length; i++) _depthBuffer[i] = 0f;
		}

		// =====================================================================
		// Bone matrices
		// =====================================================================
		private void UpdateBoneMatrices()
		{
			var skel = Project?.Skeleton;
			if (skel == null) { _boneWorldMatrices = Array.Empty<Matrix4x4>(); return; }
			_boneWorldMatrices = new Matrix4x4[skel.Bones.Count];
			for (int i = 0; i < skel.Bones.Count; i++)
				_boneWorldMatrices[i] = EvalBoneMatrix(i, skel);
		}

		private Matrix4x4 EvalBoneMatrix(int i, SkeletonDocument skel, HashSet<int>? visited = null)
		{
			visited ??= new HashSet<int>();

			// 🚨 cycle detection
			if (!visited.Add(i))
				return Matrix4x4.Identity;

			var bone = skel.Bones[i];

			Vector3 pos;
			Quaternion rot;
			Vector3 scl;

			if (Mode == ViewportMode.PosePreview && PreviewClip != null)
				(pos, rot, scl) = PreviewClip.EvaluateBone(i, CurrentFrame, skel);
			else
				(pos, rot, scl) = (bone.BindPosition, bone.BindRotation, bone.BindScale);

			var local =
				Matrix4x4.CreateScale(scl) *
				Matrix4x4.CreateFromQuaternion(rot) *
				Matrix4x4.CreateTranslation(pos);

			if (bone.ParentIndex == -1)
				return local;

			return EvalBoneMatrix(bone.ParentIndex, skel, visited) * local;
		}

		// =====================================================================
		// Cache vertex screen positions (for picking and weight paint)
		// =====================================================================
		private void CacheVertexScreenPositions()
		{
			_vertexScreenPositions.Clear();
			var surf = ActiveSurface();
			if (surf == null) return;

			var skel = Project?.Skeleton;
			for (int i = 0; i < surf.Vertices.Count; i++)
			{
				Vector3 world = skel != null && _boneWorldMatrices.Length > 0
					? SkinnedPosition(surf.Vertices[i], skel)
					: surf.Vertices[i].Position;
				var clip = ToClip(world);
				if (!InRange(clip)) continue;
				var screen = ToScreen(clip);
				_vertexScreenPositions.Add((i, screen, clip.Z));
			}
		}

		// =====================================================================
		// Grid
		// =====================================================================
		private unsafe void DrawGrid(byte* ptr, int stride)
		{
			const int HALF = 10;
			for (int i = -HALF; i <= HALF; i++)
			{
				float fi = i;
				Color cx = i == 0 ? Color.FromArgb(180, 60, 60) : GridColor;
				Color cz = i == 0 ? Color.FromArgb(60, 60, 180) : GridColor;
				Line3D(new(-HALF, 0, fi), new(HALF, 0, fi), Pack(cz), ptr, stride);
				Line3D(new(fi, 0, -HALF), new(fi, 0, HALF), Pack(cx), ptr, stride);
			}
		}

		// =====================================================================
		// Mesh
		// =====================================================================
		private unsafe void DrawMesh(byte* ptr, int stride)
		{
			var mesh = Project!.Mesh!;
			var skel = Project.Skeleton;
			bool inEdit = Mode == ViewportMode.Edit;
			bool inWeight = Mode == ViewportMode.WeightPaint;

			for (int si = 0; si < mesh.Surfaces.Count; si++)
			{
				var surf = mesh.Surfaces[si];
				bool activeSurf = si == ActiveSurfaceIndex;

				int vc = surf.Vertices.Count;
				var world = new Vector3[vc];
				var clip = new Vector3[vc];
				var screen = new PointF[vc];

				for (int i = 0; i < vc; i++)
				{
					world[i] = skel != null && _boneWorldMatrices.Length > 0
						? SkinnedPosition(surf.Vertices[i], skel)
						: surf.Vertices[i].Position;
					clip[i] = ToClip(world[i]);
					screen[i] = ToScreen(clip[i]);
				}

				var indices = surf.BuildIndexList();
				for (int t = 0; t < indices.Count; t += 3)
				{
					int a = indices[t], b = indices[t + 1], c = indices[t + 2];

					// Fully behind camera — quick reject
					if (clip[a].Z > 1f && clip[b].Z > 1f && clip[c].Z > 1f) continue;

					// World-space backface cull — CW winding so Cross(C-A,B-A) is outward normal
					Vector3 fn = Vector3.Normalize(Vector3.Cross(world[c] - world[a], world[b] - world[a]));
					Vector3 toEye = _eye - world[a];
					bool facingCamera = Vector3.Dot(fn, toEye) > 0f;

					var face = surf.Faces[t / 3];

					if (RenderMode != ViewportRenderMode.Wireframe)
					{
						bool cull = !inEdit && !inWeight && !facingCamera;
						if (!cull)
						{
							uint fillColor;

							if (inWeight && activeSurf)
							{
								float w = 0f;
								if (SelectedBoneIndex >= 0)
									w = (surf.Vertices[a].GetInfluence(SelectedBoneIndex) +
										 surf.Vertices[b].GetInfluence(SelectedBoneIndex) +
										 surf.Vertices[c].GetInfluence(SelectedBoneIndex)) / 3f;
								fillColor = WeightColor(w);
							}
							else if (inEdit && activeSurf && face.Selected)
							{
								fillColor = Pack(SelFaceColor);
							}
							else
							{
								float light = 0.25f + Math.Clamp(Vector3.Dot(fn, -_lightDir), 0f, 1f) * 0.75f;
								Color baseCol = (inEdit && activeSurf) ? SurfaceEditColor : SurfaceColor;
								fillColor = Pack(LitColor(baseCol, light));
							}

							DrawClippedTriangle(world[a], world[b], world[c], fillColor, ptr, stride);
						}
					}

					if (RenderMode != ViewportRenderMode.Solid || (inEdit && activeSurf))
					{
						if (facingCamera || inEdit || inWeight)
						{
							uint wc = inEdit && activeSurf ? Pack(WireEditColor) : Pack(WireColor);
							Line3D(world[a], world[b], wc, ptr, stride);
							Line3D(world[b], world[c], wc, ptr, stride);
							Line3D(world[c], world[a], wc, ptr, stride);
						}
					}
				}
			}
		}

		// =====================================================================
		// Box selection overlay
		// =====================================================================
		private void DrawBoxSelect(Graphics g)
		{
			using var fill = new SolidBrush(Color.FromArgb(40, 100, 160, 255));
			using var border = new Pen(Color.FromArgb(180, 100, 160, 255), 1f);
			g.FillRectangle(fill, _boxRect);
			g.DrawRectangle(border, _boxRect.X, _boxRect.Y, _boxRect.Width, _boxRect.Height);
		}

		// =====================================================================
		// Translate gizmo
		// =====================================================================

		private const float GizmoLength = 80f;
		private const float GizmoPickR = 8f;

		private bool HasSelection()
		{
			var surf = ActiveSurface();
			if (surf == null) return false;
			if (EditSelectionMode == Data.SelectionMode.Vertex)
				return surf.SelectedVertexIndices().Count > 0;
			if (EditSelectionMode == Data.SelectionMode.Face)
				return surf.Faces.Exists(f => f.Selected);
			return surf.Edges.Exists(e => e.Selected);
		}

		private Vector3 SelectionCentroid()
		{
			var surf = ActiveSurface();
			if (surf == null) return Vector3.Zero;
			var indices = surf.SelectedVertexIndices();
			if (indices.Count == 0) return Vector3.Zero;
			Vector3 sum = Vector3.Zero;
			foreach (int i in indices) sum += surf.Vertices[i].Position;
			return sum / indices.Count;
		}

		/// <summary>
		/// Projects origin+axis into screen space, then extends exactly
		/// GizmoLength pixels from the screen origin of the gizmo centre.
		/// </summary>
		private PointF GizmoTip(Vector3 centre, Vector3 axis)
		{
			var c0 = ToClip(centre);
			var c1 = ToClip(centre + axis * 0.5f);
			if (!InRange(c0)) return ToScreen(c0);
			var s0 = ToScreen(c0);
			var s1 = ToScreen(c1);
			float sx = s1.X - s0.X, sy = s1.Y - s0.Y;
			float len = MathF.Sqrt(sx * sx + sy * sy);
			if (len < 1e-4f) return s0;
			return new PointF(s0.X + sx / len * GizmoLength,
							  s0.Y + sy / len * GizmoLength);
		}

		private void DrawGizmo(Graphics g)
		{
			_gizmoCentre = SelectionCentroid();
			var c = ToClip(_gizmoCentre);
			if (!InRange(c)) return;
			var origin = ToScreen(c);

			DrawGizmoArrow(g, origin, GizmoTip(_gizmoCentre, Vector3.UnitX), GizmoAxis.X,
				Color.FromArgb(210, 55, 55), Color.FromArgb(255, 120, 120));
			DrawGizmoArrow(g, origin, GizmoTip(_gizmoCentre, Vector3.UnitY), GizmoAxis.Y,
				Color.FromArgb(55, 190, 55), Color.FromArgb(120, 255, 120));
			DrawGizmoArrow(g, origin, GizmoTip(_gizmoCentre, Vector3.UnitZ), GizmoAxis.Z,
				Color.FromArgb(55, 75, 210), Color.FromArgb(120, 140, 255));

			using var cb = new SolidBrush(Color.FromArgb(210, 210, 210));
			g.FillRectangle(cb, origin.X - 4, origin.Y - 4, 8, 8);
		}

		private void DrawGizmoArrow(Graphics g, PointF origin, PointF tip,
			GizmoAxis axis, Color baseCol, Color hoverCol)
		{
			bool lit = _gizmoActive == axis || _gizmoHovered == axis;
			Color col = lit ? hoverCol : baseCol;

			using var pen = new Pen(col, lit ? 3f : 2f);
			g.DrawLine(pen, origin, tip);

			float ex = tip.X - origin.X, ey = tip.Y - origin.Y;
			float len = MathF.Sqrt(ex * ex + ey * ey);
			if (len < 1f) return;
			ex /= len; ey /= len;
			float px = -ey, py = ex;
			const float hs = 7f, hw = 4f;
			var tri = new PointF[]
			{
				tip,
				new(tip.X - ex * hs + px * hw, tip.Y - ey * hs + py * hw),
				new(tip.X - ex * hs - px * hw, tip.Y - ey * hs - py * hw)
			};
			using var brush = new SolidBrush(col);
			g.FillPolygon(brush, tri);

			using var font = new Font("Consolas", 8f, FontStyle.Bold);
			string label = axis == GizmoAxis.X ? "X" : axis == GizmoAxis.Y ? "Y" : "Z";
			g.DrawString(label, font, brush, tip.X + 3, tip.Y - 8);
		}

		private GizmoAxis HitTestGizmo(Point p)
		{
			if (!HasSelection()) return GizmoAxis.None;
			return HitTestGizmoAt(p, _gizmoCentre);
		}

		private GizmoAxis HitTestGizmoAt(Point p, Vector3 centre)
		{
			var c = ToClip(centre);
			if (!InRange(c)) return GizmoAxis.None;
			var origin = ToScreen(c);

			GizmoAxis best = GizmoAxis.None;
			float bestDist = GizmoPickR;

			foreach (var (axis, worldAxis) in new (GizmoAxis, Vector3)[]
			{
				(GizmoAxis.X, Vector3.UnitX),
				(GizmoAxis.Y, Vector3.UnitY),
				(GizmoAxis.Z, Vector3.UnitZ)
			})
			{
				float d = DistToSegment(p, origin, GizmoTip(centre, worldAxis));
				if (d < bestDist) { bestDist = d; best = axis; }
			}

			return best;
		}

		private bool TryGrabGizmo(Point p)
		{
			Vector3 centre = SelectionCentroid();
			var hit = HitTestGizmoAt(p, centre);
			if (hit == GizmoAxis.None) return false;

			// Snapshot before the drag mutates anything
			PushUndo("Move");

			_gizmoCentre = centre;
			_gizmoActive = hit;
			_gizmoDragOrigin = new PointF(p.X, p.Y);
			_gizmoAxisWorld = hit switch
			{
				GizmoAxis.X => Vector3.UnitX,
				GizmoAxis.Y => Vector3.UnitY,
				GizmoAxis.Z => Vector3.UnitZ,
				_ => Vector3.Zero
			};

			Capture = true;
			return true;
		}

		private void DragGizmo(Point current)
		{
			var surf = ActiveSurface();
			if (surf == null) return;

			var c = ToClip(_gizmoCentre);
			if (!InRange(c)) return;

			var origin = ToScreen(c);
			var tip = GizmoTip(_gizmoCentre, _gizmoAxisWorld);

			float axX = tip.X - origin.X;
			float axY = tip.Y - origin.Y;

			float axLen = MathF.Sqrt(axX * axX + axY * axY);
			if (axLen < 1e-4f) return;

			// normalize axis in screen space
			axX /= axLen;
			axY /= axLen;

			// mouse delta from drag start (IMPORTANT)
			float mdx = current.X - _gizmoDragOrigin.X;
			float mdy = current.Y - _gizmoDragOrigin.Y;

			float screenProjected = mdx * axX + mdy * axY;

			float worldDelta = screenProjected / GizmoLength;

			// No accumulation, No prev state
			surf.TranslateSelected(_gizmoAxisWorld * worldDelta, EditSelectionMode);

			_gizmoDragOrigin = current; // Update drag origin for next delta
		}

		// =====================================================================
		// Edit overlay — selected vertices / edges dots
		// =====================================================================
		private void DrawEditOverlay(Graphics g)
		{
			var surf = ActiveSurface();
			if (surf == null) return;

			using var selBrush = new SolidBrush(SelVertColor);
			using var normBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
			using var selEdgePen = new Pen(SelVertColor, 2f);

			if (EditSelectionMode == Data.SelectionMode.Vertex)
			{
				foreach (var (idx, pos, _) in _vertexScreenPositions)
				{
					bool sel = surf.Vertices[idx].Selected;
					const float r = 4f;
					g.FillEllipse(sel ? selBrush : normBrush, pos.X - r, pos.Y - r, r * 2, r * 2);
				}
			}
			else if (EditSelectionMode == Data.SelectionMode.Edge)
			{
				foreach (var edge in surf.Edges)
				{
					if (!edge.Selected) continue;
					var pa = GetVertexScreen(edge.A);
					var pb = GetVertexScreen(edge.B);
					if (pa == null || pb == null) continue;
					g.DrawLine(selEdgePen, pa.Value, pb.Value);
				}
			}
		}

		// =====================================================================
		// Bones overlay
		// =====================================================================
		private void DrawBonesOverlay(Graphics g)
		{
			var skel = Project!.Skeleton!;
			_boneScreenPositions.Clear();

			using var bonePen = new Pen(BoneColor, 2f);
			using var boneSelPen = new Pen(BoneSelColor, 3f);
			using var boneBrush = new SolidBrush(BoneColor);
			using var boneSelBrush = new SolidBrush(BoneSelColor);

			for (int i = 0; i < skel.Bones.Count; i++)
			{
				var bone = skel.Bones[i];
				bool sel = i == SelectedBoneIndex;

				if (_boneWorldMatrices.Length <= i) continue;

				var bm = _boneWorldMatrices[i];
				Vector3 wHead = new(bm.M41, bm.M42, bm.M43);
				Vector3 wTail = Vector3.Transform(new(0, bone.DisplayLength, 0), bm);

				var cHead = ToClip(wHead);
				var cTail = ToClip(wTail);
				if (!InRange(cHead) && !InRange(cTail)) continue;

				var sHead = ToScreen(cHead);
				var sTail = ToScreen(cTail);

				g.DrawLine(sel ? boneSelPen : bonePen, sHead, sTail);

				const float r = 5f;
				g.FillEllipse(sel ? boneSelBrush : boneBrush, sHead.X - r, sHead.Y - r, r * 2, r * 2);
				_boneScreenPositions.Add((i, sHead));

				if (sel) g.DrawString(bone.Name, Font, Brushes.Cyan, sHead.X + 7, sHead.Y - 7);

				if (bone.ParentIndex >= 0 && bone.ParentIndex < _boneWorldMatrices.Length)
				{
					var pm = _boneWorldMatrices[bone.ParentIndex];
					var cp = ToClip(new(pm.M41, pm.M42, pm.M43));
					if (InRange(cp))
					{
						using var pp = new Pen(Color.FromArgb(90, BoneColor.R, BoneColor.G, BoneColor.B), 1f);
						g.DrawLine(pp, sHead, ToScreen(cp));
					}
				}
			}
		}

		// =====================================================================
		// Weight paint brush cursor
		// =====================================================================
		private void DrawBrushCursor(Graphics g)
		{
			var cursor = PointToClient(Cursor.Position);
			float screenR = BrushRadius * Height / _distance * 20f;
			using var pen = new Pen(BrushAdditive
				? Color.FromArgb(180, 255, 100, 30)
				: Color.FromArgb(180, 100, 180, 255), 2f);
			g.DrawEllipse(pen, cursor.X - screenR, cursor.Y - screenR, screenR * 2, screenR * 2);
		}

		// =====================================================================
		// HUD
		// =====================================================================
		private void DrawHUD(Graphics g, Vector3 eye)
		{
			using var f = new Font("Consolas", 8f);
			using var b = new SolidBrush(Color.FromArgb(200, 220, 220, 220));

			string modeName = Mode.ToString();
			if (Mode == ViewportMode.Edit) modeName += $" [{EditSelectionMode}]";
			if (Mode == ViewportMode.WeightPaint && SelectedBoneIndex >= 0)
			{
				var boneName = Project?.Skeleton?.Bones[SelectedBoneIndex].Name ?? "?";
				modeName += $" — Bone: {boneName}  R:{BrushRadius:F2}  Str:{BrushStrength:F2}  {(BrushAdditive ? "ADD" : "ERASE")}";
			}

			g.DrawString($"Mode: {modeName}", f, b, 8, 8);
			g.DrawString($"Frame: {CurrentFrame:F0}  Surfaces: {Project?.Mesh?.Surfaces.Count ?? 0}  Verts: {ActiveSurface()?.Vertices.Count ?? 0}", f, b, 8, 22);

			DrawAxisIndicator(g);
		}

		private void DrawAxisIndicator(Graphics g)
		{
			int cx = 40, cy = Height - 40;
			float sc = 20f;

			Vector3 ox = Vector3.Transform(Vector3.UnitX, _view);
			Vector3 oy = Vector3.Transform(Vector3.UnitY, _view);
			Vector3 oz = Vector3.Transform(Vector3.UnitZ, _view);

			using var px = new Pen(Color.FromArgb(220, 60, 60), 2);
			using var py = new Pen(Color.FromArgb(60, 220, 60), 2);
			using var pz = new Pen(Color.FromArgb(60, 60, 220), 2);
			using var fnt = new Font("Consolas", 7f);

			g.DrawLine(px, cx, cy, cx + (int)(ox.X * sc), cy - (int)(ox.Y * sc));
			g.DrawLine(py, cx, cy, cx + (int)(oy.X * sc), cy - (int)(oy.Y * sc));
			g.DrawLine(pz, cx, cy, cx + (int)(oz.X * sc), cy - (int)(oz.Y * sc));

			g.DrawString("X", fnt, Brushes.Red, cx + (int)(ox.X * sc), cy - (int)(ox.Y * sc));
			g.DrawString("Y", fnt, Brushes.Lime, cx + (int)(oy.X * sc), cy - (int)(oy.Y * sc));
			g.DrawString("Z", fnt, Brushes.CornflowerBlue, cx + (int)(oz.X * sc), cy - (int)(oz.Y * sc));
		}

		// =====================================================================
		// Skinning
		// =====================================================================
		private Vector3 SkinnedPosition(MeshVertex vert, SkeletonDocument skel)
		{
			Vector3 result = Vector3.Zero;
			float total = 0f;
			for (int i = 0; i < 4; i++)
			{
				int bi = vert.BoneIndices[i];
				float bw = vert.BoneWeights[i];
				if (bi < 0 || bw <= 0f || bi >= _boneWorldMatrices.Length) continue;
				var skinned = Vector3.Transform(
					Vector3.Transform(vert.Position, skel.Bones[bi].InverseBindMatrix),
					_boneWorldMatrices[bi]);
				result += skinned * bw;
				total += bw;
			}

			if (total > 0.0001f) result /= total;
			else result = vert.Position; // No valid bone influences, fallback to unskinned pos

			return result;
		}

		// =====================================================================
		// Rasterizer — Sutherland-Hodgman clip + perspective-correct raster
		// =====================================================================

		private unsafe void DrawClippedTriangle(Vector3 w0, Vector3 w1, Vector3 w2,
			uint color, byte* ptr, int stride)
		{
			Vector4 h0 = ClipSpaceH(w0);
			Vector4 h1 = ClipSpaceH(w1);
			Vector4 h2 = ClipSpaceH(w2);

			var poly = new List<Vector4>(8) { h0, h1, h2 };

			ClipPlane(poly, v => v.W + v.Z);  // near
			ClipPlane(poly, v => v.W - v.Z);  // far
			ClipPlane(poly, v => v.W + v.X);  // left
			ClipPlane(poly, v => v.W - v.X);  // right
			ClipPlane(poly, v => v.W + v.Y);  // bottom
			ClipPlane(poly, v => v.W - v.Y);  // top

			if (poly.Count < 3) return;

			for (int i = 1; i < poly.Count - 1; i++)
				RasterTri(poly[0], poly[i], poly[i + 1], color, ptr, stride);
		}

		private static void ClipPlane(List<Vector4> poly, Func<Vector4, float> signedDist)
		{
			if (poly.Count == 0) return;
			var result = new List<Vector4>(poly.Count + 1);

			for (int i = 0; i < poly.Count; i++)
			{
				Vector4 cur = poly[i];
				Vector4 next = poly[(i + 1) % poly.Count];
				float dc = signedDist(cur);
				float dn = signedDist(next);
				bool curIn = dc >= -1e-5f;
				bool nextIn = dn >= -1e-5f;

				if (curIn) result.Add(cur);

				if (curIn != nextIn)
				{
					float denom = dc - dn;
					float t = MathF.Abs(denom) < 1e-10f ? 0f : Math.Clamp(dc / denom, 0f, 1f);
					result.Add(cur + (next - cur) * t);
				}
			}

			poly.Clear();
			poly.AddRange(result);
		}

		private unsafe void RasterTri(Vector4 h0, Vector4 h1, Vector4 h2,
			uint color, byte* ptr, int stride)
		{
			const float wEps = 1e-5f;
			if (h0.W < wEps) h0.W = wEps;
			if (h1.W < wEps) h1.W = wEps;
			if (h2.W < wEps) h2.W = wEps;

			float iw0 = 1f / h0.W, iw1 = 1f / h1.W, iw2 = 1f / h2.W;

			float sx0 = (h0.X * iw0 * 0.5f + 0.5f) * Width;
			float sy0 = (1f - (h0.Y * iw0 * 0.5f + 0.5f)) * Height;
			float sx1 = (h1.X * iw1 * 0.5f + 0.5f) * Width;
			float sy1 = (1f - (h1.Y * iw1 * 0.5f + 0.5f)) * Height;
			float sx2 = (h2.X * iw2 * 0.5f + 0.5f) * Width;
			float sy2 = (1f - (h2.Y * iw2 * 0.5f + 0.5f)) * Height;

			float area = (sx1 - sx0) * (sy2 - sy0) - (sx2 - sx0) * (sy1 - sy0);
			if (MathF.Abs(area) < 1f) return;
			float invArea = 1f / area;

			int minX = Math.Clamp((int)MathF.Floor(MathF.Min(sx0, MathF.Min(sx1, sx2))), 0, Width - 1);
			int maxX = Math.Clamp((int)MathF.Ceiling(MathF.Max(sx0, MathF.Max(sx1, sx2))), 0, Width - 1);
			int minY = Math.Clamp((int)MathF.Floor(MathF.Min(sy0, MathF.Min(sy1, sy2))), 0, Height - 1);
			int maxY = Math.Clamp((int)MathF.Ceiling(MathF.Max(sy0, MathF.Max(sy1, sy2))), 0, Height - 1);

			for (int py = minY; py <= maxY; py++)
				for (int px = minX; px <= maxX; px++)
				{
					float fpx = px + 0.5f, fpy = py + 0.5f;
					float e0 = (sx1 - sx0) * (fpy - sy0) - (fpx - sx0) * (sy1 - sy0);
					float e1 = (sx2 - sx1) * (fpy - sy1) - (fpx - sx1) * (sy2 - sy1);
					float e2 = (sx0 - sx2) * (fpy - sy2) - (fpx - sx2) * (sy0 - sy2);

					if (area > 0f) { if (e0 < 0f || e1 < 0f || e2 < 0f) continue; }
					else { if (e0 > 0f || e1 > 0f || e2 > 0f) continue; }

					float interpInvW = (e0 * invArea) * iw0 + (e1 * invArea) * iw1 + (e2 * invArea) * iw2;
					int bufIdx = py * Width + px;
					if (interpInvW <= _depthBuffer![bufIdx]) continue;
					_depthBuffer[bufIdx] = interpInvW;
					*(uint*)(ptr + py * stride + px * 4) = color;
				}
		}

		private Vector4 ClipSpaceH(Vector3 world)
		{
			Vector4 v = Vector4.Transform(new Vector4(world, 1f), _view);
			return Vector4.Transform(v, _proj);
		}

		private unsafe void Line3D(Vector3 w0, Vector3 w1, uint color, byte* ptr, int stride)
		{
			const float near = 0.05f;
			Vector4 v0 = Vector4.Transform(new Vector4(w0, 1f), _view);
			Vector4 v1 = Vector4.Transform(new Vector4(w1, 1f), _view);

			bool in0 = v0.Z <= -near, in1 = v1.Z <= -near;
			if (!in0 && !in1) return;

			if (in0 != in1)
			{
				float t = (-near - v0.Z) / (v1.Z - v0.Z);
				Vector4 mid = v0 + (v1 - v0) * t;
				if (!in0) v0 = mid; else v1 = mid;
			}

			Vector4 c0 = Vector4.Transform(v0, _proj);
			Vector4 c1 = Vector4.Transform(v1, _proj);
			if (MathF.Abs(c0.W) < 1e-6f || MathF.Abs(c1.W) < 1e-6f) return;

			float iw0 = 1f / c0.W, iw1 = 1f / c1.W;
			PointF s0 = new((c0.X * iw0 * 0.5f + 0.5f) * Width, (1f - (c0.Y * iw0 * 0.5f + 0.5f)) * Height);
			PointF s1 = new((c1.X * iw1 * 0.5f + 0.5f) * Width, (1f - (c1.Y * iw1 * 0.5f + 0.5f)) * Height);

			LineScreen(s0, s1, iw0, iw1, color, ptr, stride);
		}

		private unsafe void LineScreen(PointF a, PointF b, float da, float db, uint color, byte* ptr, int stride)
		{
			int x0 = (int)a.X, y0 = (int)a.Y, x1 = (int)b.X, y1 = (int)b.Y;
			int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
			int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
			int err = dx + dy, steps = Math.Max(dx, -dy);

			for (int i = 0; i <= steps; i++)
			{
				float t = steps > 0 ? i / (float)steps : 0f;
				float depth = da + (db - da) * t;
				if (x0 >= 0 && x0 < Width && y0 >= 0 && y0 < Height)
				{
					int idx = y0 * Width + x0;
					if (depth > _depthBuffer![idx])
					{
						_depthBuffer[idx] = depth;
						*(uint*)(ptr + y0 * stride + x0 * 4) = color;
					}
				}
				int e2 = 2 * err;
				if (e2 >= dy) { err += dy; x0 += sx; }
				if (e2 <= dx) { err += dx; y0 += sy; }
			}
		}

		// =====================================================================
		// Math helpers
		// =====================================================================
		private Vector3 ToClip(Vector3 world)
		{
			var v = Vector4.Transform(new Vector4(world, 1.0f), _view);
			v = Vector4.Transform(v, _proj);

			if (MathF.Abs(v.W) > 0.0001f)
			{
				v.X /= v.W;
				v.Y /= v.W;
				v.Z /= v.W;
				v.Z = v.Z * 0.5f + 0.5f;
			}

			return new(v.X, v.Y, v.Z);
		}

		private PointF ToScreen(Vector3 clip)
			=> new((clip.X + 1f) * 0.5f * Width, (1f - clip.Y) * 0.5f * Height);

		private static bool InRange(Vector3 c)
			=> c.X >= -1f && c.X <= 1f &&
			   c.Y >= -1f && c.Y <= 1f &&
			   c.Z >= 0f && c.Z <= 1f;
		private static float EdgeFn(Vector3 a, Vector3 b, Vector3 c)
			=> (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

		private static Color LitColor(Color c, float light)
			=> Color.FromArgb(c.A,
				(int)Math.Clamp(c.R * light, 0, 255),
				(int)Math.Clamp(c.G * light, 0, 255),
				(int)Math.Clamp(c.B * light, 0, 255));

		private static uint Pack(Color c) => Pack(c.R, c.G, c.B, c.A);
		private static uint Pack(byte r, byte g, byte b, byte a = 255)
			=> ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

		// Weight heatmap: blue (0) → green → red (1)
		private static uint WeightColor(float w)
		{
			w = Math.Clamp(w, 0f, 1f);
			byte r = (byte)(255 * Math.Clamp(w * 2f - 1f, 0f, 1f));
			byte g = (byte)(255 * Math.Clamp(w < 0.5f ? w * 2f : 2f - w * 2f, 0f, 1f));
			byte b = (byte)(255 * Math.Clamp(1f - w * 2f, 0f, 1f));
			return Pack(r, g, b);
		}

		private static float Dist(Point p, PointF q)
		{
			float dx = p.X - q.X, dy = p.Y - q.Y;
			return MathF.Sqrt(dx * dx + dy * dy);
		}

		private static float DistToSegment(Point p, PointF a, PointF b)
		{
			float dx = b.X - a.X, dy = b.Y - a.Y;
			float lenSq = dx * dx + dy * dy;
			if (lenSq < 1e-6f) return Dist(p, a);
			float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
			return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
		}

		private MeshSurface? ActiveSurface()
		{
			var mesh = Project?.Mesh;
			if (mesh == null || mesh.Surfaces.Count == 0) return null;
			return mesh.Surfaces[Math.Clamp(ActiveSurfaceIndex, 0, mesh.Surfaces.Count - 1)];
		}

		// =====================================================================
		// Cleanup
		// =====================================================================
		protected override void Dispose(bool disposing)
		{
			if (disposing) { _timer?.Stop(); _timer?.Dispose(); _backbuffer?.Dispose(); }
			base.Dispose(disposing);
		}
	}
}