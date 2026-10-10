using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Custom themed slider for Flappy Launcher dialogs.
    /// Supports smooth drag, mouse wheel, arrow keys, and live value updates.
    /// </summary>
    internal sealed class FlappySlider : Control
    {
        private int _minimum = 0;
        private int _maximum = 100;
        private int _value = 0;
        private bool _isDragging;
        private bool _isHovered;

        public event EventHandler ValueChanged;
        public event EventHandler ValueCommitted;

        public int Minimum
        {
            get => _minimum;
            set
            {
                _minimum = value;
                if (_value < _minimum) Value = _minimum;
                Invalidate();
            }
        }

        public int Maximum
        {
            get => _maximum;
            set
            {
                _maximum = Math.Max(_minimum, value);
                if (_value > _maximum) Value = _maximum;
                Invalidate();
            }
        }

        public int Value
        {
            get => _value;
            set
            {
                int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    ValueChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public FlappySlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Size = new Size(200, 26);
        }

        private Rectangle GetTrackBounds()
        {
            const int trackH = 6;
            const int thumbRadius = 9;
            int y = (Height - trackH) / 2;
            int x = thumbRadius + 2;
            int w = Math.Max(10, Width - (x * 2));
            return new Rectangle(x, y, w, trackH);
        }

        private float GetThumbCenterX()
        {
            var track = GetTrackBounds();
            if (_maximum <= _minimum) return track.Left;
            float ratio = (float)(_value - _minimum) / (_maximum - _minimum);
            return track.Left + ratio * track.Width;
        }

        private void UpdateValueFromMouse(int mouseX)
        {
            var track = GetTrackBounds();
            float ratio = (float)(mouseX - track.Left) / track.Width;
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            int rawVal = _minimum + (int)Math.Round(ratio * (_maximum - _minimum));
            Value = rawVal;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Capture = true;
                Focus();
                UpdateValueFromMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                UpdateValueFromMouse(e.X);
            }
            else
            {
                float cx = GetThumbCenterX();
                float cy = Height / 2f;
                float distSq = (e.X - cx) * (e.X - cx) + (e.Y - cy) * (e.Y - cy);
                bool hovered = distSq <= 12 * 12;
                if (hovered != _isHovered)
                {
                    _isHovered = hovered;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                Capture = false;
                Invalidate();
                ValueCommitted?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_isHovered)
            {
                _isHovered = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = e.Delta > 0 ? 5 : -5;
            Value += step;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) ? 1 :
                       (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) ? -1 : 0;
            if (step != 0)
            {
                Value += step;
                ValueCommitted?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
            }
        }

        private static GraphicsPath CreateCapsule(Rectangle bounds)
        {
            var path = new GraphicsPath();
            int d = bounds.Height;
            if (bounds.Width < d)
            {
                path.AddEllipse(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 90, 180);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var track = GetTrackBounds();
            float thumbX = GetThumbCenterX();
            float thumbY = Height / 2f;
            float thumbRadius = (_isDragging || _isHovered) ? 9f : 8f;

            // Track background
            using (var trackPath = CreateCapsule(track))
            using (var trackBrush = new SolidBrush(FlappyTheme.Panel))
            using (var trackPen = new Pen(Color.FromArgb(120, FlappyTheme.Gold), 1f))
            {
                g.FillPath(trackBrush, trackPath);
                g.DrawPath(trackPen, trackPath);
            }

            // Filled active track (from 0 to thumb)
            if (thumbX > track.Left + 2)
            {
                int fillW = (int)Math.Max(track.Height, thumbX - track.Left);
                var fillRect = new Rectangle(track.X, track.Y, Math.Min(track.Width, fillW), track.Height);
                using (var fillPath = CreateCapsule(fillRect))
                using (var fillBrush = new SolidBrush(FlappyTheme.Gold))
                {
                    g.FillPath(fillBrush, fillPath);
                }
            }

            // Tick marks on track (25%, 50%, 75%)
            using (var tickBrush = new SolidBrush(Color.FromArgb(140, FlappyTheme.Mute)))
            {
                for (int pct = 25; pct <= 75; pct += 25)
                {
                    float tx = track.Left + (track.Width * (pct / 100f));
                    if (Math.Abs(tx - thumbX) > thumbRadius + 2)
                    {
                        g.FillEllipse(tickBrush, tx - 1.5f, thumbY - 1.5f, 3f, 3f);
                    }
                }
            }

            // Thumb
            Color thumbColor = (_isDragging || _isHovered) ? FlappyTheme.GoldHi : FlappyTheme.Gold;
            using (var thumbBrush = new SolidBrush(thumbColor))
            using (var thumbPen = new Pen(FlappyTheme.GoldHi, 2f))
            using (var innerBrush = new SolidBrush(FlappyTheme.Back))
            {
                var rThumb = new RectangleF(thumbX - thumbRadius, thumbY - thumbRadius, thumbRadius * 2, thumbRadius * 2);
                g.FillEllipse(thumbBrush, rThumb);
                g.DrawEllipse(thumbPen, rThumb);

                // Small center dot
                const float dotR = 2.5f;
                g.FillEllipse(innerBrush, thumbX - dotR, thumbY - dotR, dotR * 2, dotR * 2);
            }
        }
    }
}
