using System;
using System.Drawing;
using System.Windows.Forms;
using Glyphborn.Forge.Data;
using System.ComponentModel;

namespace Glyphborn.Forge.Controls
{
	/// <summary>
	/// Keyframe timeline panel for Glyphborn Forge.
	/// Shows a scrollable frame strip, per-bone channel rows, playback controls, and frame scrubbing.
	/// </summary>
	public class AnimationTimeline : UserControl
	{
		// =================================================================
		// State
		// =================================================================
		private AnimationClip? _clip;
		private SkeletonDocument? _skeleton;

		private float _currentFrame = 0f;
		private bool _playing = false;
		private System.Windows.Forms.Timer _playTimer = null!;

		private int _scrollOffsetX = 0;  // horizontal scroll in pixels
		private int _frameWidth = 16;    // pixels per frame
		private bool _draggingPlayhead = false;

		private const int HEADER_H = 24;   // top ruler strip height
		private const int ROW_H = 18;      // per-bone row height
		private const int LABEL_W = 120;   // left label column width
		private const int CTRL_H = 36;     // playback control bar height

		// =================================================================
		// Public API
		// =================================================================

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public AnimationClip? Clip
		{
			get => _clip;
			set { _clip = value; _currentFrame = 0; Invalidate(); }
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public SkeletonDocument? Skeleton
		{
			get => _skeleton;
			set { _skeleton = value; Invalidate(); }
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public float CurrentFrame
		{
			get => _currentFrame;
			set
			{
				_currentFrame = value;
				FrameChanged?.Invoke(_currentFrame);
				Invalidate();
			}
		}

		public event Action<float>? FrameChanged;

		// =================================================================
		// Constructor
		// =================================================================
		public AnimationTimeline()
		{
			DoubleBuffered = true;
			BackColor = Color.FromArgb(22, 22, 25);
			Height = CTRL_H + HEADER_H + ROW_H * 6 + 8; // default visible

			_playTimer = new System.Windows.Forms.Timer { Interval = 33 }; // ~30fps
			_playTimer.Tick += OnPlayTick;
		}

		private void OnPlayTick(object? sender, EventArgs e)
		{
			if (_clip == null) { Stop(); return; }

			float delta = _clip.FrameRate / (1000f / _playTimer.Interval);
			_currentFrame += delta;

			if (_currentFrame >= _clip.FrameCount)
			{
				if (_clip.Loop)
					_currentFrame %= _clip.FrameCount;
				else
				{
					_currentFrame = _clip.FrameCount - 1;
					Stop();
				}
			}

			FrameChanged?.Invoke(_currentFrame);
			Invalidate();
		}

		// =================================================================
		// Paint
		// =================================================================
		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			var g = e.Graphics;

			int totalHeight = ClientSize.Height;
			int ctrlTop = totalHeight - CTRL_H;

			DrawPlaybackControls(g, 0, ctrlTop, ClientSize.Width, CTRL_H);
			DrawRuler(g, 0, 0, ClientSize.Width, HEADER_H);
			DrawChannelRows(g, 0, HEADER_H, ClientSize.Width, ctrlTop - HEADER_H);
			DrawPlayhead(g, ctrlTop);
		}

		// -----------------------------------------------------------------
		// Playback control bar
		// -----------------------------------------------------------------
		private void DrawPlaybackControls(Graphics g, int x, int y, int w, int h)
		{
			using var bg = new SolidBrush(Color.FromArgb(30, 30, 35));
			g.FillRectangle(bg, x, y, w, h);

			using var border = new Pen(Color.FromArgb(50, 50, 55));
			g.DrawLine(border, x, y, x + w, y);

			// Simple text buttons (real impl would use drawn shapes)
			int bw = 60, bh = 24, by = y + (h - bh) / 2;
			int bx = x + 8;

			DrawButton(g, _playing ? "⏸ Pause" : "▶ Play", bx, by, bw, bh);
			DrawButton(g, "⏮ Start", bx + bw + 6, by, bw, bh);
			DrawButton(g, "⏭ End", bx + bw * 2 + 12, by, bw, bh);

			// Frame info
			int fc = _clip?.FrameCount ?? 0;
			string info = $"Frame {_currentFrame:F0} / {fc}  |  {_clip?.FrameRate ?? 0:F0} fps";
			using var infoFont = new Font("Consolas", 8f);
			using var infoBrush = new SolidBrush(Color.FromArgb(180, 180, 180));
			g.DrawString(info, infoFont, infoBrush, bx + bw * 3 + 24, by + 4);
		}

		private void DrawButton(Graphics g, string label, int x, int y, int w, int h)
		{
			using var bg = new SolidBrush(Color.FromArgb(50, 50, 60));
			using var border = new Pen(Color.FromArgb(80, 80, 90));
			using var fg = new SolidBrush(Color.FromArgb(210, 210, 220));
			using var font = new Font("Segoe UI", 7.5f);

			g.FillRectangle(bg, x, y, w, h);
			g.DrawRectangle(border, x, y, w, h);
			g.DrawString(label, font, fg, x + 4, y + 4);
		}

		// -----------------------------------------------------------------
		// Ruler strip
		// -----------------------------------------------------------------
		private void DrawRuler(Graphics g, int x, int y, int w, int h)
		{
			using var bg = new SolidBrush(Color.FromArgb(35, 35, 40));
			g.FillRectangle(bg, x, y, w, h);

			using var border = new Pen(Color.FromArgb(55, 55, 60));
			g.DrawLine(border, x, y + h - 1, x + w, y + h - 1);

			if (_clip == null) return;

			using var tickPen = new Pen(Color.FromArgb(80, 80, 90));
			using var majorPen = new Pen(Color.FromArgb(120, 120, 130));
			using var font = new Font("Consolas", 7f);
			using var numBrush = new SolidBrush(Color.FromArgb(160, 160, 170));

			int frameCount = _clip.FrameCount;
			int tickInterval = _frameWidth >= 16 ? 1 : 5;

			for (int f = 0; f < frameCount; f += tickInterval)
			{
				int px = LABEL_W + f * _frameWidth - _scrollOffsetX;
				if (px < LABEL_W || px > w) continue;

				bool major = f % 5 == 0;
				g.DrawLine(major ? majorPen : tickPen, px, y + (major ? 4 : 10), px, y + h - 1);

				if (major)
					g.DrawString(f.ToString(), font, numBrush, px + 2, y + 4);
			}

			// Label column header
			using var hdrBrush = new SolidBrush(Color.FromArgb(40, 40, 45));
			g.FillRectangle(hdrBrush, 0, y, LABEL_W, h);
			g.DrawLine(border, LABEL_W, y, LABEL_W, y + h);
		}

		// -----------------------------------------------------------------
		// Channel rows
		// -----------------------------------------------------------------
		private void DrawChannelRows(Graphics g, int x, int y, int w, int h)
		{
			if (_clip == null || _skeleton == null) return;

			int row = 0;
			foreach (var bone in _skeleton.Bones)
			{
				int ry = y + row * ROW_H;
				if (ry > y + h) break;

				bool even = row % 2 == 0;
				using var rowBg = new SolidBrush(even ? Color.FromArgb(28, 28, 32) : Color.FromArgb(32, 32, 38));
				g.FillRectangle(rowBg, x, ry, w, ROW_H);

				// Label
				using var lf = new Font("Consolas", 7.5f);
				using var lb = new SolidBrush(bone.Index == -1 ? Color.Gray : Color.FromArgb(200, 200, 210));
				g.DrawString(bone.Name, lf, lb, 6, ry + 3);

				// Separator
				using var sep = new Pen(Color.FromArgb(40, 40, 45));
				g.DrawLine(sep, x, ry + ROW_H - 1, x + w, ry + ROW_H - 1);

				// Keyframe diamonds
				var ch = _clip.GetOrCreateChannel(bone.Index >= 0 ? bone.Index : row);
				DrawKeyframeDiamonds(g, ry, ch);

				row++;
			}

			// Label column separator
			using var colSep = new Pen(Color.FromArgb(55, 55, 60));
			g.DrawLine(colSep, LABEL_W, y, LABEL_W, y + h);
		}

		private void DrawKeyframeDiamonds(Graphics g, int rowY, BoneChannel ch)
		{
			if (_clip == null) return;

			using var kfBrush = new SolidBrush(Color.FromArgb(255, 180, 40));
			using var kfPen = new Pen(Color.FromArgb(200, 120, 20));

			void DrawDiamond(int frame)
			{
				int px = LABEL_W + frame * _frameWidth - _scrollOffsetX;
				if (px < LABEL_W || px > ClientSize.Width) return;

				int cy = rowY + ROW_H / 2;
				const int s = 4;
				var diamond = new PointF[]
				{
					new(px, cy - s),
					new(px + s, cy),
					new(px, cy + s),
					new(px - s, cy)
				};
				g.FillPolygon(kfBrush, diamond);
				g.DrawPolygon(kfPen, diamond);
			}

			foreach (var k in ch.PositionKeys) DrawDiamond(k.Frame);
			foreach (var k in ch.RotationKeys) DrawDiamond(k.Frame);
			foreach (var k in ch.ScaleKeys) DrawDiamond(k.Frame);
		}

		// -----------------------------------------------------------------
		// Playhead
		// -----------------------------------------------------------------
		private void DrawPlayhead(Graphics g, int ctrlTop)
		{
			if (_clip == null) return;

			int px = LABEL_W + (int)(_currentFrame * _frameWidth) - _scrollOffsetX;
			if (px < LABEL_W || px > ClientSize.Width) return;

			using var playPen = new Pen(Color.FromArgb(255, 80, 80), 2f);
			using var playBrush = new SolidBrush(Color.FromArgb(255, 80, 80));

			g.DrawLine(playPen, px, 0, px, ctrlTop);

			// Triangle handle at top
			var tri = new PointF[]
			{
				new(px, HEADER_H),
				new(px - 5, HEADER_H - 8),
				new(px + 5, HEADER_H - 8)
			};
			g.FillPolygon(playBrush, tri);
		}

		// =================================================================
		// Mouse — scrubbing & buttons
		// =================================================================
		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);

			int ctrlTop = ClientSize.Height - CTRL_H;

			// Playback buttons
			if (e.Y >= ctrlTop)
			{
				int bx = 8, by = ctrlTop + (CTRL_H - 24) / 2;
				int bw = 60;

				if (HitButton(e.X, e.Y, bx, by, bw, 24)) { TogglePlay(); return; }
				if (HitButton(e.X, e.Y, bx + bw + 6, by, bw, 24)) { CurrentFrame = 0; return; }
				if (HitButton(e.X, e.Y, bx + bw * 2 + 12, by, bw, 24)) { CurrentFrame = (_clip?.FrameCount - 1) ?? 0; return; }
				return;
			}

			// Ruler scrub
			if (e.Y < HEADER_H || e.X >= LABEL_W)
			{
				_draggingPlayhead = true;
				ScrubTo(e.X);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (_draggingPlayhead) ScrubTo(e.X);
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			_draggingPlayhead = false;
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);

			if (ModifierKeys == Keys.Control)
			{
				// Zoom timeline
				_frameWidth = Math.Clamp(_frameWidth + (e.Delta > 0 ? 2 : -2), 4, 64);
			}
			else
			{
				// Scroll horizontally
				_scrollOffsetX = Math.Max(0, _scrollOffsetX - e.Delta / 4);
			}

			Invalidate();
		}

		private void ScrubTo(int mouseX)
		{
			if (_clip == null) return;
			float frame = (mouseX - LABEL_W + _scrollOffsetX) / (float)_frameWidth;
			CurrentFrame = Math.Clamp(frame, 0, _clip.FrameCount - 1);
		}

		private static bool HitButton(int mx, int my, int x, int y, int w, int h)
			=> mx >= x && mx <= x + w && my >= y && my <= y + h;

		private void TogglePlay()
		{
			if (_playing) Stop();
			else Play();
		}

		private void Play()
		{
			_playing = true;
			_playTimer.Start();
			Invalidate();
		}

		private void Stop()
		{
			_playing = false;
			_playTimer.Stop();
			Invalidate();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_playTimer?.Stop();
				_playTimer?.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}