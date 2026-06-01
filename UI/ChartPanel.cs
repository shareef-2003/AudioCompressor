using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AudioCompressor.UI
{
    /// <summary>رسم بياني خطي في الزمن الحقيقي</summary>
    public class ChartPanel : Panel
    {
        private readonly Queue<float> _values = new Queue<float>();
        private readonly int _maxPoints;
        private readonly Color _lineColor;
        private readonly Color _fillColor;
        private readonly string _label;
        private readonly string _unit;
        private float _maxValue = 100f;

        public ChartPanel(string label, string unit, Color color, int maxPoints = 60)
        {
            _label = label;
            _unit = unit;
            _lineColor = color;
            _fillColor = Color.FromArgb(40, color);
            _maxPoints = maxPoints;

            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(8, 13, 22);
            MinimumSize = new Size(80, 60);
        }

        public void AddValue(float value)
        {
            _values.Enqueue(value);
            if (_values.Count > _maxPoints)
                _values.Dequeue();
            if (value > _maxValue) _maxValue = value * 1.2f;
            Invalidate();
        }

        public void Reset()
        {
            _values.Clear();
            _maxValue = 100f;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int W = Width - 10, H = Height - 24;
            int ox = 5, oy = 4;

            // شبكة
            using (Pen grid = new Pen(Color.FromArgb(20, 40, 60), 1))
            {
                for (int i = 0; i <= 4; i++)
                {
                    int y = oy + H - (int)(H * i / 4.0);
                    g.DrawLine(grid, ox, y, ox + W, y);
                }
            }

            float[] vals = _values.ToArray();
            if (vals.Length > 1)
            {
                float dx = (float)W / (_maxPoints - 1);
                int offsetX = (int)((_maxPoints - vals.Length) * dx);

                // Fill polygon
                PointF[] poly = new PointF[vals.Length + 2];
                for (int i = 0; i < vals.Length; i++)
                {
                    float x = ox + offsetX + i * dx;
                    float y = oy + H - (vals[i] / _maxValue * H);
                    y = Math.Max(oy, Math.Min(oy + H, y));
                    poly[i] = new PointF(x, y);
                }
                poly[vals.Length] = new PointF(poly[vals.Length - 1].X, oy + H);
                poly[vals.Length + 1] = new PointF(poly[0].X, oy + H);

                using (SolidBrush fill = new SolidBrush(_fillColor))
                    g.FillPolygon(fill, poly);

                // Line
                PointF[] linePoints = new PointF[vals.Length];
                Array.Copy(poly, linePoints, vals.Length);
                using (Pen linePen = new Pen(_lineColor, 1.8f))
                    g.DrawLines(linePen, linePoints);

                // آخر قيمة
                float lastVal = vals[vals.Length - 1];
                using (Font f = new Font("Consolas", 9, FontStyle.Bold))
                using (SolidBrush b = new SolidBrush(_lineColor))
                    g.DrawString(lastVal.ToString("F1") + _unit, f, b,
                        ox + W - 70, oy + 2);
            }

            // التسمية
            using (Font lf = new Font("Segoe UI", 8))
            using (SolidBrush lb = new SolidBrush(Color.FromArgb(120, 160, 180)))
                g.DrawString(_label, lf, lb, ox, oy + H + 4);

            // إطار
            using (Pen border = new Pen(Color.FromArgb(25, 50, 75), 1))
                g.DrawRectangle(border, ox, oy, W, H);
        }
    }
}
