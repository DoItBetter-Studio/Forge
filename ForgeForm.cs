using System;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Windows.Forms;
using Glyphborn.Forge.Controls;
using Glyphborn.Forge.Data;
using Glyphborn.Forge.Import;
using SelectionMode = Glyphborn.Forge.Data.SelectionMode;

namespace Glyphborn.Forge
{
	public partial class ForgeForm : Form
	{
		// =================================================================
		// Controls
		// =================================================================
		private MenuStrip _menuStrip = null!;
		private ToolStrip _toolStrip = null!;
		private ToolStrip _modeStrip = null!;
		private ToolStrip _paintStrip = null!;

		private TabControl _leftTabs = null!;
		private TreeView _skeletonTree = null!;
		private ListBox _clipList = null!;
		private ListBox _surfaceList = null!;

		private ModelViewport3D _viewport = null!;
		private AnimationTimeline _timeline = null!;
		private PropertyGrid _inspector = null!;

		// Mode-strip sub-buttons
		private ToolStripButton? _btnVertSel, _btnEdgeSel, _btnFaceSel;

		// Paint-strip controls
		private TrackBar? _tbRadius, _tbStrength;
		private Label? _lblRadius, _lblStrength;
		private ToolStripButton? _btnAdd, _btnErase;

		// =================================================================
		// State
		// =================================================================
		private ForgeProject _project = new("Untitled");
		private string? _projectPath = null;

		// =================================================================
		// Constructor
		// =================================================================
		public ForgeForm()
		{
			InitializeComponent();   // only sets Icon, ClientSize, AutoScale

			Text = "Glyphborn Forge — Untitled (unsaved)";
			Width = 1440;
			Height = 900;
			MinimumSize = new Size(960, 640);
			BackColor = Color.FromArgb(28, 28, 30);
			ForeColor = Color.White;

			// -------------------------------------------------------
			// Build every control, then add to Controls in dock order.
			//
			// WinForms docking rule:
			//   DockStyle.Top controls are consumed top-to-bottom in
			//   REVERSE Controls-collection order (last added = topmost).
			//   DockStyle.Fill must be added LAST.
			//
			// Desired visual order (top → bottom):
			//   menuStrip   (always topmost for MainMenuStrip)
			//   toolStrip
			//   modeStrip
			//   paintStrip  (hidden unless WeightPaint mode)
			//   [Fill content]
			//
			// So we Add() in this order:
			//   1. content panel  (Fill  — must be first so docked strips push it down)
			//   2. paintStrip     (Top)
			//   3. modeStrip      (Top)
			//   4. toolStrip      (Top)
			//   5. menuStrip      (Top — added last so it ends up topmost)
			// -------------------------------------------------------

			var content = BuildContent();   // the 3-column TableLayoutPanel
			BuildStrips();                  // creates _menuStrip, _toolStrip, _modeStrip, _paintStrip
			BuildMenuItems();               // populates _menuStrip

			SuspendLayout();

			Controls.Add(content);      // Fill  — first
			Controls.Add(_paintStrip);  // Top   — will sit just above Fill
			Controls.Add(_modeStrip);   // Top   — above paintStrip
			Controls.Add(_toolStrip);   // Top   — above modeStrip
			Controls.Add(_menuStrip);   // Top   — topmost

			MainMenuStrip = _menuStrip;

			ResumeLayout(false);
			PerformLayout();

			RefreshAll();

			KeyDown += OnFormKeyDown;
			KeyPreview = true;
		}

		// =================================================================
		// Strips  (menu + three toolstrips)
		// =================================================================
		private void BuildStrips()
		{
			// ---- MenuStrip ----
			_menuStrip = new MenuStrip
			{
				Dock = DockStyle.Top,
				BackColor = Color.FromArgb(38, 38, 42),
				ForeColor = Color.White,
				Renderer = new DarkMenuRenderer()
			};

			// ---- File / render toolbar ----
			_toolStrip = new ToolStrip
			{
				Dock = DockStyle.Top,
				BackColor = Color.FromArgb(38, 38, 42),
				GripStyle = ToolStripGripStyle.Hidden,
				Renderer = new DarkToolStripRenderer()
			};
			TSBtn(_toolStrip, "New", () => NewProject());
			TSBtn(_toolStrip, "Open", () => OpenProject());
			TSBtn(_toolStrip, "Save", () => SaveProject());
			_toolStrip.Items.Add(new ToolStripSeparator());
			TSBtn(_toolStrip, "Export…", () => ExportAll());
			_toolStrip.Items.Add(new ToolStripSeparator());
			TSBtn(_toolStrip, "Solid", () => SetRender(ViewportRenderMode.Solid));
			TSBtn(_toolStrip, "Wire", () => SetRender(ViewportRenderMode.Wireframe));
			TSBtn(_toolStrip, "S+W", () => SetRender(ViewportRenderMode.SolidWireframe));
			_toolStrip.Items.Add(new ToolStripSeparator());
			TSBtn(_toolStrip, "Grid", () => { _viewport.ShowGrid = !_viewport.ShowGrid; });
			TSBtn(_toolStrip, "Bones", () => { _viewport.ShowBones = !_viewport.ShowBones; });

			// ---- Mode strip ----
			_modeStrip = new ToolStrip
			{
				Dock = DockStyle.Top,
				BackColor = Color.FromArgb(45, 45, 50),
				GripStyle = ToolStripGripStyle.Hidden,
				Renderer = new DarkToolStripRenderer()
			};
			ModeBtn("Object", ViewportMode.Object);
			ModeBtn("Edit", ViewportMode.Edit);
			ModeBtn("Weight Paint", ViewportMode.WeightPaint);
			ModeBtn("Pose Preview", ViewportMode.PosePreview);
			_modeStrip.Items.Add(new ToolStripSeparator());

			_btnVertSel = new ToolStripButton("Verts") { ForeColor = Color.White };
			_btnEdgeSel = new ToolStripButton("Edges") { ForeColor = Color.White };
			_btnFaceSel = new ToolStripButton("Faces") { ForeColor = Color.White };
			_btnVertSel.Click += (_, __) => SetSelectionMode(SelectionMode.Vertex);
			_btnEdgeSel.Click += (_, __) => SetSelectionMode(SelectionMode.Edge);
			_btnFaceSel.Click += (_, __) => SetSelectionMode(SelectionMode.Face);
			_modeStrip.Items.Add(_btnVertSel);
			_modeStrip.Items.Add(_btnEdgeSel);
			_modeStrip.Items.Add(_btnFaceSel);

			// ---- Weight-paint strip (hidden by default) ----
			_paintStrip = new ToolStrip
			{
				Dock = DockStyle.Top,
				BackColor = Color.FromArgb(45, 45, 50),
				GripStyle = ToolStripGripStyle.Hidden,
				Renderer = new DarkToolStripRenderer(),
				Visible = false
			};

			_btnAdd = new ToolStripButton("Add") { ForeColor = Color.FromArgb(255, 160, 40), Checked = true, CheckOnClick = true };
			_btnErase = new ToolStripButton("Erase") { ForeColor = Color.FromArgb(100, 180, 255), CheckOnClick = true };
			_btnAdd.Click += (_, __) => { _viewport.BrushAdditive = true; _btnErase!.Checked = false; };
			_btnErase.Click += (_, __) => { _viewport.BrushAdditive = false; _btnAdd!.Checked = false; };
			_paintStrip.Items.Add(_btnAdd);
			_paintStrip.Items.Add(_btnErase);
			_paintStrip.Items.Add(new ToolStripSeparator());

			_lblRadius = new Label { Text = "Radius: 0.50", ForeColor = Color.White, AutoSize = true };
			_tbRadius = new TrackBar { Minimum = 1, Maximum = 50, Value = 10, Width = 120, TickFrequency = 10 };
			_tbRadius.ValueChanged += (_, __) =>
			{
				_viewport.BrushRadius = _tbRadius.Value * 0.05f;
				_lblRadius!.Text = $"Radius: {_viewport.BrushRadius:F2}";
			};
			_paintStrip.Items.Add(new ToolStripControlHost(_lblRadius));
			_paintStrip.Items.Add(new ToolStripControlHost(_tbRadius));
			_paintStrip.Items.Add(new ToolStripSeparator());

			_lblStrength = new Label { Text = "Strength: 0.10", ForeColor = Color.White, AutoSize = true };
			_tbStrength = new TrackBar { Minimum = 1, Maximum = 100, Value = 10, Width = 120, TickFrequency = 10 };
			_tbStrength.ValueChanged += (_, __) =>
			{
				_viewport.BrushStrength = _tbStrength.Value / 100f;
				_lblStrength!.Text = $"Strength: {_viewport.BrushStrength:F2}";
			};
			_paintStrip.Items.Add(new ToolStripControlHost(_lblStrength));
			_paintStrip.Items.Add(new ToolStripControlHost(_tbStrength));
		}

		private void ModeBtn(string label, ViewportMode mode)
		{
			var btn = new ToolStripButton(label) { ForeColor = Color.White };
			btn.Click += (_, __) => SetMode(mode);
			_modeStrip.Items.Add(btn);
		}

		private static void TSBtn(ToolStrip strip, string text, Action click)
		{
			var btn = new ToolStripButton(text) { ForeColor = Color.White };
			btn.Click += (_, __) => click();
			strip.Items.Add(btn);
		}

		// =================================================================
		// Menu items  (populate _menuStrip)
		// =================================================================
		private void BuildMenuItems()
		{
			var file = MMenu("File");
			MItem(file, "New Project", (_, __) => NewProject());
			MItem(file, "Open Project…", (_, __) => OpenProject());
			MItem(file, "Save Project\tCtrl+S", (_, __) => SaveProject());
			MItem(file, "Save As…", (_, __) => SaveProjectAs());
			file.DropDownItems.Add(new ToolStripSeparator());
			MItem(file, "Import Model…", (_, __) => ImportModel());
			file.DropDownItems.Add(new ToolStripSeparator());
			MItem(file, "Export All (.gbx)…", (_, __) => ExportAll());
			file.DropDownItems.Add(new ToolStripSeparator());
			MItem(file, "Exit", (_, __) => Close());

			var mesh = MMenu("Mesh");
			MItem(mesh, "Add Cube", (_, __) => InsertPrimitive("Cube"));
			MItem(mesh, "Add Plane", (_, __) => InsertPrimitive("Plane"));
			MItem(mesh, "Add Cylinder", (_, __) => InsertPrimitive("Cylinder"));
			MItem(mesh, "Add Sphere", (_, __) => InsertPrimitive("Sphere"));
			MItem(mesh, "Add Capsule", (_, __) => InsertPrimitive("Capsule"));
			mesh.DropDownItems.Add(new ToolStripSeparator());
			MItem(mesh, "Delete Active Surface", (_, __) => DeleteActiveSurface());
			mesh.DropDownItems.Add(new ToolStripSeparator());
			MItem(mesh, "Recalculate Normals", (_, __) => ActiveSurface()?.RecalculateNormals());

			var edit = MMenu("Edit");
			MItem(edit, "Select All\tA", (_, __) => ActiveSurface()?.SelectAll(_viewport.EditSelectionMode));
			MItem(edit, "Deselect All", (_, __) => ActiveSurface()?.DeselectAll());
			edit.DropDownItems.Add(new ToolStripSeparator());
			MItem(edit, "Extrude Selected\tE", (_, __) => { ActiveSurface()?.ExtrudeSelectedFaces(); _viewport.Invalidate(); });
			MItem(edit, "Delete Selected\tX", (_, __) => { ActiveSurface()?.DeleteSelected(_viewport.EditSelectionMode); _viewport.Invalidate(); });
			MItem(edit, "Merge Selected\tM", (_, __) => { ActiveSurface()?.MergeSelected(); _viewport.Invalidate(); });
			MItem(edit, "Flip Normals\tF", (_, __) => { ActiveSurface()?.FlipSelectedNormals(_viewport.EditSelectionMode); _viewport.Invalidate(); });

			var skel = MMenu("Skeleton");
			MItem(skel, "Add Root Bone", (_, __) => AddBone(-1));
			MItem(skel, "Add Child Bone", (_, __) => AddBoneToSelected());
			MItem(skel, "Delete Bone", (_, __) => DeleteSelectedBone());
			skel.DropDownItems.Add(new ToolStripSeparator());
			MItem(skel, "Bake Bind Matrices", (_, __) => BakeBindMatrices());

			var anim = MMenu("Animation");
			MItem(anim, "New Clip…", (_, __) => NewClip());
			MItem(anim, "Delete Clip", (_, __) => DeleteClip());
			anim.DropDownItems.Add(new ToolStripSeparator());
			MItem(anim, "Insert Keyframe", (_, __) => InsertKeyframe());

			var view = MMenu("View");
			MItem(view, "Solid", (_, __) => SetRender(ViewportRenderMode.Solid));
			MItem(view, "Wireframe", (_, __) => SetRender(ViewportRenderMode.Wireframe));
			MItem(view, "Solid + Wireframe", (_, __) => SetRender(ViewportRenderMode.SolidWireframe));
			view.DropDownItems.Add(new ToolStripSeparator());
			MItem(view, "Toggle Grid", (_, __) => { _viewport.ShowGrid = !_viewport.ShowGrid; });
			MItem(view, "Toggle Bones", (_, __) => { _viewport.ShowBones = !_viewport.ShowBones; });
		}

		private ToolStripMenuItem MMenu(string text)
		{
			var m = new ToolStripMenuItem(text) { ForeColor = Color.White };
			_menuStrip.Items.Add(m);
			return m;
		}

		private static void MItem(ToolStripMenuItem parent, string text, EventHandler click)
		{
			var it = new ToolStripMenuItem(text) { ForeColor = Color.White };
			it.Click += click;
			parent.DropDownItems.Add(it);
		}

		// =================================================================
		// Content  (3-column layout — returned, not yet added to Controls)
		// =================================================================
		private Control BuildContent()
		{
			var root = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1,
				BackColor = Color.FromArgb(42, 42, 46)
			};
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
			root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));

			// ------ LEFT ------
			_leftTabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.FlatButtons };

			var skelPage = DarkTab("Skeleton");
			_skeletonTree = new TreeView
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(26, 26, 30),
				ForeColor = Color.FromArgb(210, 210, 225),
				BorderStyle = BorderStyle.None,
				HideSelection = false
			};
			_skeletonTree.AfterSelect += OnSkeletonSelect;
			_skeletonTree.NodeMouseClick += OnSkeletonRightClick;
			skelPage.Controls.Add(_skeletonTree);
			_leftTabs.TabPages.Add(skelPage);

			var animPage = DarkTab("Animations");
			_clipList = new ListBox
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(26, 26, 30),
				ForeColor = Color.FromArgb(210, 210, 225),
				BorderStyle = BorderStyle.None
			};
			_clipList.SelectedIndexChanged += OnClipSelected;
			animPage.Controls.Add(_clipList);
			_leftTabs.TabPages.Add(animPage);

			var surfPage = DarkTab("Surfaces");
			var surfLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
			surfLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			surfLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));

			_surfaceList = new ListBox
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(26, 26, 30),
				ForeColor = Color.FromArgb(210, 210, 225),
				BorderStyle = BorderStyle.None
			};
			_surfaceList.SelectedIndexChanged += OnSurfaceSelected;
			surfLayout.Controls.Add(_surfaceList, 0, 0);

			var addFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(30, 30, 34) };
			foreach (string p in new[] { "Cube", "Plane", "Sphere" })
			{
				string prim = p;
				var b = DarkButton($"+ {prim}");
				b.Click += (_, __) => InsertPrimitive(prim);
				addFlow.Controls.Add(b);
			}
			surfLayout.Controls.Add(addFlow, 0, 1);
			surfPage.Controls.Add(surfLayout);
			_leftTabs.TabPages.Add(surfPage);

			root.Controls.Add(_leftTabs, 0, 0);

			// ------ CENTER ------
			var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
			center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
			center.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

			_viewport = new ModelViewport3D { Dock = DockStyle.Fill };
			_viewport.BoneSelected += OnBoneSelectedInViewport;
			_viewport.VertexSelected += OnVertexSelectedInViewport;
			center.Controls.Add(_viewport, 0, 0);

			_timeline = new AnimationTimeline { Dock = DockStyle.Fill };
			_timeline.FrameChanged += f => _viewport.CurrentFrame = f;
			center.Controls.Add(_timeline, 0, 1);

			root.Controls.Add(center, 1, 0);

			// ------ RIGHT ------
			_inspector = new PropertyGrid
			{
				Dock = DockStyle.Fill,
				BackColor = Color.FromArgb(26, 26, 30),
				LineColor = Color.FromArgb(48, 48, 54),
				CategoryForeColor = Color.FromArgb(150, 150, 170),
				ViewForeColor = Color.FromArgb(210, 210, 225),
				HelpBackColor = Color.FromArgb(22, 22, 26),
				HelpForeColor = Color.FromArgb(150, 150, 170)
			};
			root.Controls.Add(_inspector, 2, 0);

			return root;
		}

		// =================================================================
		// Mode / render helpers
		// =================================================================
		private void SetMode(ViewportMode mode)
		{
			_viewport.Mode = mode;
			_paintStrip.Visible = mode == ViewportMode.WeightPaint;
			bool isEdit = mode == ViewportMode.Edit;
			if (_btnVertSel != null) _btnVertSel.Enabled = isEdit;
			if (_btnEdgeSel != null) _btnEdgeSel.Enabled = isEdit;
			if (_btnFaceSel != null) _btnFaceSel.Enabled = isEdit;
		}

		private void SetSelectionMode(SelectionMode sm)
		{
			_viewport.EditSelectionMode = sm;
			if (_btnVertSel != null) _btnVertSel.Checked = sm == SelectionMode.Vertex;
			if (_btnEdgeSel != null) _btnEdgeSel.Checked = sm == SelectionMode.Edge;
			if (_btnFaceSel != null) _btnFaceSel.Checked = sm == SelectionMode.Face;
		}

		private void SetRender(ViewportRenderMode rm) => _viewport.RenderMode = rm;

		// =================================================================
		// Keyboard
		// =================================================================
		private void OnFormKeyDown(object? sender, KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.S) { SaveProject(); e.Handled = true; return; }

			// Route undo/redo through the viewport so it has access to History
			if (e.Control && e.KeyCode == Keys.Z)
			{
				_viewport.TriggerUndo();
				UpdateSurfaceList(); // surface list may need refreshing after undo
				e.Handled = true;
				return;
			}
			if (e.Control && (e.KeyCode == Keys.Y || (e.Shift && e.KeyCode == Keys.Z)))
			{
				_viewport.TriggerRedo();
				UpdateSurfaceList();
				e.Handled = true;
				return;
			}
		}

		// =================================================================
		// State refresh
		// =================================================================
		private void RefreshAll()
		{
			_viewport.Project = _project;
			_timeline.Skeleton = _project.Skeleton;
			UpdateSkeletonTree();
			UpdateClipList();
			UpdateSurfaceList();
			UpdateTitle();
		}

		private void UpdateTitle()
			=> Text = $"Glyphborn Forge — {_project.ProjectName}{(_projectPath == null ? " (unsaved)" : "")}";

		private void UpdateSkeletonTree()
		{
			_skeletonTree.Nodes.Clear();
			var skel = _project.Skeleton;
			if (skel == null) return;
			void AddNode(TreeNodeCollection col, int idx)
			{
				var bone = skel.Bones[idx];
				var node = col.Add(bone.Name);
				node.Tag = bone.Index;
				foreach (var child in skel.ChildrenOf(idx)) AddNode(node.Nodes, child.Index);
			}
			foreach (var r in skel.RootBones()) AddNode(_skeletonTree.Nodes, r.Index);
			_skeletonTree.ExpandAll();
		}

		private void UpdateClipList()
		{
			_clipList.Items.Clear();
			if (_project.Animation == null) return;
			foreach (var c in _project.Animation.Clips) _clipList.Items.Add(c.Name);
		}

		private void UpdateSurfaceList()
		{
			_surfaceList.Items.Clear();
			if (_project.Mesh == null) return;
			foreach (var s in _project.Mesh.Surfaces) _surfaceList.Items.Add(s.Name);
			if (_surfaceList.Items.Count > 0)
				_surfaceList.SelectedIndex = Math.Min(_viewport.ActiveSurfaceIndex, _surfaceList.Items.Count - 1);
		}

		// =================================================================
		// Events
		// =================================================================
		private void OnSkeletonSelect(object? sender, TreeViewEventArgs e)
		{
			if (e.Node?.Tag is int idx)
			{
				_viewport.SelectedBoneIndex = idx;
				_inspector.SelectedObject = _project.Skeleton?.Bones[idx];
			}
		}

		private void OnSkeletonRightClick(object? sender, TreeNodeMouseClickEventArgs e)
		{
			if (e.Button != MouseButtons.Right || e.Node?.Tag is not int idx) return;
			var ctx = new ContextMenuStrip();
			ctx.Items.Add("Add Child Bone", null, (_, __) => AddBone(idx));
			ctx.Items.Add("Delete Bone", null, (_, __) => DeleteBone(idx));
			ctx.Items.Add(new ToolStripSeparator());
			ctx.Items.Add("Rename…", null, (_, __) => RenameBone(idx));
			ctx.Show(_skeletonTree, e.Location);
		}

		private void OnBoneSelectedInViewport(int boneIndex)
		{
			_viewport.SelectedBoneIndex = boneIndex;
			SelectTreeNode(boneIndex);
			if (_project.Skeleton != null && boneIndex >= 0 && boneIndex < _project.Skeleton.Bones.Count)
				_inspector.SelectedObject = _project.Skeleton.Bones[boneIndex];
		}

		private void OnVertexSelectedInViewport(MeshVertex vert)
			=> _inspector.SelectedObject = new VertexInspectorProxy(vert);

		private void OnClipSelected(object? sender, EventArgs e)
		{
			if (_clipList.SelectedIndex < 0 || _project.Animation == null) return;
			int idx = _clipList.SelectedIndex;
			if (idx >= _project.Animation.Clips.Count) return;
			var clip = _project.Animation.Clips[idx];
			_timeline.Clip = clip;
			_viewport.PreviewClip = clip;
			_inspector.SelectedObject = clip;
		}

		private void OnSurfaceSelected(object? sender, EventArgs e)
		{
			if (_surfaceList.SelectedIndex < 0) return;
			_viewport.ActiveSurfaceIndex = _surfaceList.SelectedIndex;
			if (_project.Mesh != null && _surfaceList.SelectedIndex < _project.Mesh.Surfaces.Count)
				_inspector.SelectedObject = _project.Mesh.Surfaces[_surfaceList.SelectedIndex];
		}

		// =================================================================
		// Primitive insertion
		// =================================================================
		private void InsertPrimitive(string type)
		{
			EnsureMesh();
			MeshSurface surf = type switch
			{
				"Cube" => MeshDocument.MakeCube(),
				"Plane" => MeshDocument.MakePlane(),
				"Cylinder" => MeshDocument.MakeCylinder(),
				"Sphere" => MeshDocument.MakeSphere(),
				"Capsule" => MeshDocument.MakeCapsule(),
				_ => MeshDocument.MakeCube()
			};
			surf.Name = $"{type}_{_project.Mesh!.Surfaces.Count}";
			_project.Mesh.Surfaces.Add(surf);
			UpdateSurfaceList();
			_viewport.ActiveSurfaceIndex = _project.Mesh.Surfaces.Count - 1;
			_surfaceList.SelectedIndex = _viewport.ActiveSurfaceIndex;
			_viewport.Invalidate();
		}

		private void DeleteActiveSurface()
		{
			var m = _project.Mesh;
			if (m == null || m.Surfaces.Count == 0) return;
			int idx = _viewport.ActiveSurfaceIndex;
			if (idx >= m.Surfaces.Count) return;
			if (MessageBox.Show($"Delete surface '{m.Surfaces[idx].Name}'?",
				"Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			m.Surfaces.RemoveAt(idx);
			_viewport.ActiveSurfaceIndex = Math.Max(0, idx - 1);
			UpdateSurfaceList();
			_viewport.Invalidate();
		}

		// =================================================================
		// Skeleton operations
		// =================================================================
		private void EnsureSkeleton() => _project.Skeleton ??= new SkeletonDocument(_project.ProjectName);
		private void EnsureMesh() => _project.Mesh ??= new MeshDocument(_project.ProjectName);

		private void AddBone(int parentIndex)
		{
			EnsureSkeleton();
			string name = Prompt("New bone name:", $"Bone_{_project.Skeleton!.Bones.Count}");
			if (string.IsNullOrWhiteSpace(name)) return;
			_project.Skeleton.AddBone(name, parentIndex);
			_project.Skeleton.BakeInverseBindMatrices();
			UpdateSkeletonTree();
		}

		private void AddBoneToSelected() => AddBone(_viewport.SelectedBoneIndex);
		private void DeleteSelectedBone() { if (_viewport.SelectedBoneIndex >= 0) DeleteBone(_viewport.SelectedBoneIndex); }

		private void DeleteBone(int idx)
		{
			if (_project.Skeleton == null) return;
			if (MessageBox.Show($"Delete bone '{_project.Skeleton.Bones[idx].Name}'?",
				"Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			_project.Skeleton.RemoveBone(idx);
			_project.Skeleton.BakeInverseBindMatrices();
			_viewport.SelectedBoneIndex = -1;
			UpdateSkeletonTree();
		}

		private void RenameBone(int idx)
		{
			if (_project.Skeleton == null) return;
			string name = Prompt("Rename bone:", _project.Skeleton.Bones[idx].Name);
			if (string.IsNullOrWhiteSpace(name)) return;
			_project.Skeleton.Bones[idx].Name = name;
			UpdateSkeletonTree();
		}

		private void BakeBindMatrices()
		{
			_project.Skeleton?.BakeInverseBindMatrices();
			MessageBox.Show("Inverse bind matrices baked.", "Forge", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		// =================================================================
		// Animation operations
		// =================================================================
		private void EnsureAnimation()
			=> _project.Animation ??= new AnimationDocument(_project.ProjectName)
			{ SkeletonName = _project.Skeleton?.Name ?? "" };

		private void NewClip()
		{
			EnsureAnimation();
			string name = Prompt("Clip name:", "NewClip");
			if (string.IsNullOrWhiteSpace(name)) return;
			_project.Animation!.AddClip(name);
			UpdateClipList();
			_clipList.SelectedIndex = _clipList.Items.Count - 1;
		}

		private void DeleteClip()
		{
			if (_clipList.SelectedIndex < 0 || _project.Animation == null) return;
			int idx = _clipList.SelectedIndex;
			if (MessageBox.Show($"Delete clip '{_project.Animation.Clips[idx].Name}'?",
				"Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
			_project.Animation.Clips.RemoveAt(idx);
			_timeline.Clip = null;
			_viewport.PreviewClip = null;
			UpdateClipList();
		}

		private void InsertKeyframe()
		{
			var clip = _timeline.Clip;
			var skel = _project.Skeleton;
			if (clip == null || skel == null) return;
			int bi = _viewport.SelectedBoneIndex;
			if (bi < 0) { MessageBox.Show("Select a bone first."); return; }
			int frame = (int)MathF.Round(_viewport.CurrentFrame);
			var bone = skel.Bones[bi];
			var ch = clip.GetOrCreateChannel(bi);
			ch.PositionKeys.Add(new Keyframe<Vector3>(frame, bone.BindPosition));
			ch.RotationKeys.Add(new Keyframe<Quaternion>(frame, bone.BindRotation));
			ch.ScaleKeys.Add(new Keyframe<Vector3>(frame, bone.BindScale));
			ch.PositionKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
			ch.RotationKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
			ch.ScaleKeys.Sort((a, b) => a.Frame.CompareTo(b.Frame));
			_timeline.Invalidate();
		}

		// =================================================================
		// File operations
		// =================================================================
		private void NewProject()
		{
			if (!ConfirmUnsaved()) return;
			_project = new ForgeProject("Untitled");
			_projectPath = null;
			RefreshAll();
		}

		private void OpenProject()
		{
			if (!ConfirmUnsaved()) return;
			using var dlg = new OpenFileDialog
			{ Title = "Open Forge Project", Filter = "Forge Project (*.forge)|*.forge", DefaultExt = "forge" };
			if (dlg.ShowDialog() != DialogResult.OK) return;
			try { _project = ForgeProject.Load(dlg.FileName); _projectPath = dlg.FileName; RefreshAll(); }
			catch (Exception ex) { ShowError(ex.Message); }
		}

		private void SaveProject()
		{
			if (_projectPath == null) SaveProjectAs();
			else DoSave(_projectPath);
		}

		private void SaveProjectAs()
		{
			using var dlg = new SaveFileDialog
			{
				Title = "Save Forge Project",
				Filter = "Forge Project (*.forge)|*.forge",
				DefaultExt = "forge",
				FileName = _project.ProjectName
			};
			if (dlg.ShowDialog() != DialogResult.OK) return;
			_projectPath = dlg.FileName;
			_project.ProjectName = Path.GetFileNameWithoutExtension(_projectPath);
			DoSave(_projectPath);
		}

		private void DoSave(string path)
		{
			try { _project.Save(path); UpdateTitle(); }
			catch (Exception ex) { ShowError(ex.Message); }
		}

		private void ExportAll()
		{
			using var dlg = new SaveFileDialog
			{
				Title = "Export Glyphborn Package",
				Filter = "Glyphborn Package (*.gbx)|*.gbx",
				DefaultExt = "gbx",
				FileName = _project.ProjectName
			};
			if (dlg.ShowDialog() != DialogResult.OK) return;
			try
			{
				_project.ExportAll(dlg.FileName);
				var parts = new System.Collections.Generic.List<string>();
				if (_project.Skeleton != null) parts.Add("skeleton");
				if (_project.Mesh != null) parts.Add("mesh");
				if (_project.Animation != null) parts.Add("animation");
				string what = parts.Count > 0 ? string.Join(", ", parts) : "empty project";
				MessageBox.Show($"Exported: {what}\n→ {dlg.FileName}", "Export Complete",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex) { ShowError(ex.Message); }
		}

		private void ImportModel()
		{
			using var dlg = new OpenFileDialog
			{
				Title = "Import Model",
				Filter = AssimpImporter.FILE_FILTER
			};
			if (dlg.ShowDialog() != DialogResult.OK) return;

			try
			{
				var result = ModelImporter.Import(dlg.FileName);
				var imported = new System.Collections.Generic.List<string>();

				// Mesh — append surfaces to current project
				if (result.HasMesh)
				{
					_project.Mesh ??= new MeshDocument(_project.ProjectName);
					foreach (var surf in result.Mesh!.Surfaces)
						_project.Mesh.Surfaces.Add(surf);
					imported.Add($"{result.Mesh.Surfaces.Count} surface(s)");
				}

				// Skeleton — only set if none exists yet
				if (result.HasSkeleton)
				{
					if (_project.Skeleton == null)
					{
						_project.Skeleton = result.Skeleton;
						imported.Add($"skeleton ({result.Skeleton!.Bones.Count} bones)");
					}
					else
					{
						imported.Add("skeleton skipped (project already has one)");
					}
				}

				// Animation — append clips to existing animation document
				if (result.HasAnimation)
				{
					if (_project.Animation == null)
					{
						_project.Animation = result.Animation;
					}
					else
					{
						foreach (var clip in result.Animation!.Clips)
							_project.Animation.Clips.Add(clip);
					}
					imported.Add($"{result.Animation!.Clips.Count} clip(s)");
				}

				RefreshAll();
				_project.History.Clear(); // imported geometry shouldn't be undone away

				string summary = imported.Count > 0
					? string.Join(", ", imported)
					: "nothing (file may be empty)";

				MessageBox.Show($"Imported: {summary}", "Import Complete",
					MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{
				ShowError($"Import failed:\n{ex.Message}");
			}
		}

		// =================================================================
		// Helpers
		// =================================================================
		private MeshSurface? ActiveSurface()
		{
			var m = _project.Mesh;
			if (m == null || m.Surfaces.Count == 0) return null;
			return m.Surfaces[Math.Clamp(_viewport.ActiveSurfaceIndex, 0, m.Surfaces.Count - 1)];
		}

		private bool ConfirmUnsaved()
			=> MessageBox.Show("Discard unsaved changes?", "Forge",
			   MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

		private void ShowError(string msg)
			=> MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

		private void SelectTreeNode(int boneIndex)
		{
			foreach (TreeNode n in AllTreeNodes(_skeletonTree.Nodes))
				if (n.Tag is int idx && idx == boneIndex) { _skeletonTree.SelectedNode = n; return; }
		}

		private static System.Collections.Generic.IEnumerable<TreeNode> AllTreeNodes(TreeNodeCollection col)
		{
			foreach (TreeNode n in col) { yield return n; foreach (var c in AllTreeNodes(n.Nodes)) yield return c; }
		}

		private static TabPage DarkTab(string title)
			=> new(title) { BackColor = Color.FromArgb(26, 26, 30), ForeColor = Color.White };

		private static Button DarkButton(string text)
			=> new()
			{
				Text = text,
				Height = 26,
				AutoSize = true,
				BackColor = Color.FromArgb(50, 50, 58),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};

		private static string Prompt(string label, string defaultValue)
		{
			var f = new Form
			{
				Text = "Forge",
				Width = 320,
				Height = 120,
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MaximizeBox = false,
				MinimizeBox = false,
				BackColor = Color.FromArgb(34, 34, 38),
				ForeColor = Color.White,
				StartPosition = FormStartPosition.CenterParent
			};
			var lbl = new Label { Text = label, Left = 12, Top = 12, Width = 290, ForeColor = Color.White };
			var txt = new TextBox
			{
				Left = 12,
				Top = 32,
				Width = 290,
				Text = defaultValue,
				BackColor = Color.FromArgb(48, 48, 56),
				ForeColor = Color.White,
				BorderStyle = BorderStyle.FixedSingle
			};
			var ok = new Button
			{
				Text = "OK",
				Left = 140,
				Top = 60,
				Width = 72,
				Height = 26,
				BackColor = Color.FromArgb(55, 85, 135),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				DialogResult = DialogResult.OK
			};
			var cancel = new Button
			{
				Text = "Cancel",
				Left = 222,
				Top = 60,
				Width = 72,
				Height = 26,
				BackColor = Color.FromArgb(48, 48, 56),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat,
				DialogResult = DialogResult.Cancel
			};
			f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
			f.AcceptButton = ok;
			f.CancelButton = cancel;
			return f.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
		}
	}

	// =================================================================
	// Vertex inspector proxy
	// =================================================================
	[System.ComponentModel.TypeConverter(typeof(System.ComponentModel.ExpandableObjectConverter))]
	public class VertexInspectorProxy
	{
		private readonly MeshVertex _v;
		public VertexInspectorProxy(MeshVertex v) { _v = v; }
		public float X { get => _v.Position.X; set => _v.Position = new(value, _v.Position.Y, _v.Position.Z); }
		public float Y { get => _v.Position.Y; set => _v.Position = new(_v.Position.X, value, _v.Position.Z); }
		public float Z { get => _v.Position.Z; set => _v.Position = new(_v.Position.X, _v.Position.Y, value); }
		public float UV_X { get => _v.UV.X; set => _v.UV = new(value, _v.UV.Y); }
		public float UV_Y { get => _v.UV.Y; set => _v.UV = new(_v.UV.X, value); }
		public int Bone0 { get => _v.BoneIndices[0]; set => _v.BoneIndices[0] = value; }
		public int Bone1 { get => _v.BoneIndices[1]; set => _v.BoneIndices[1] = value; }
		public int Bone2 { get => _v.BoneIndices[2]; set => _v.BoneIndices[2] = value; }
		public int Bone3 { get => _v.BoneIndices[3]; set => _v.BoneIndices[3] = value; }
		public float Weight0 { get => _v.BoneWeights[0]; set { _v.BoneWeights[0] = value; _v.Normalise(); } }
		public float Weight1 { get => _v.BoneWeights[1]; set { _v.BoneWeights[1] = value; _v.Normalise(); } }
		public float Weight2 { get => _v.BoneWeights[2]; set { _v.BoneWeights[2] = value; _v.Normalise(); } }
		public float Weight3 { get => _v.BoneWeights[3]; set { _v.BoneWeights[3] = value; _v.Normalise(); } }
	}

	// =================================================================
	// Dark renderers
	// =================================================================
	internal class DarkMenuRenderer : ToolStripProfessionalRenderer { public DarkMenuRenderer() : base(new DarkColors()) { } }
	internal class DarkToolStripRenderer : ToolStripProfessionalRenderer { public DarkToolStripRenderer() : base(new DarkColors()) { } }

	internal class DarkColors : ProfessionalColorTable
	{
		public override Color MenuStripGradientBegin => Color.FromArgb(38, 38, 42);
		public override Color MenuStripGradientEnd => Color.FromArgb(38, 38, 42);
		public override Color ToolStripGradientBegin => Color.FromArgb(38, 38, 42);
		public override Color ToolStripGradientMiddle => Color.FromArgb(38, 38, 42);
		public override Color ToolStripGradientEnd => Color.FromArgb(38, 38, 42);
		public override Color MenuItemSelected => Color.FromArgb(55, 85, 135);
		public override Color MenuItemSelectedGradientBegin => Color.FromArgb(55, 85, 135);
		public override Color MenuItemSelectedGradientEnd => Color.FromArgb(55, 85, 135);
		public override Color MenuItemBorder => Color.FromArgb(75, 105, 155);
		public override Color MenuBorder => Color.FromArgb(52, 52, 58);
		public override Color ToolStripDropDownBackground => Color.FromArgb(38, 38, 42);
		public override Color ImageMarginGradientBegin => Color.FromArgb(38, 38, 42);
		public override Color ImageMarginGradientMiddle => Color.FromArgb(38, 38, 42);
		public override Color ImageMarginGradientEnd => Color.FromArgb(38, 38, 42);
		public override Color SeparatorDark => Color.FromArgb(58, 58, 64);
		public override Color SeparatorLight => Color.FromArgb(68, 68, 74);
		public override Color ButtonSelectedGradientBegin => Color.FromArgb(55, 85, 135);
		public override Color ButtonSelectedGradientEnd => Color.FromArgb(55, 85, 135);
		public override Color ButtonSelectedBorder => Color.FromArgb(75, 105, 155);
	}
}