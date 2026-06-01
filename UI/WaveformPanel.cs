using System;
using System.Drawing;
using System.Windows.Forms;
using AudioCompressor.Models;

namespace AudioCompressor.UI
{
    /// <summary>عنصر تحكم مخصص لعرض شكل الموجة الصوتية</summary>
    public class WaveformPanel : Panel
    {
        private short[] _samples;
        private Color _waveColor = Color.FromArgb(0, 200, 255);
        private Color _bgColor = Color.FromArgb(10, 15, 25);
        private float _playPosition = 0f; // 0..1

        public WaveformPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);
            BackColor = _bgColor;
            MinimumSize = new Size(100, 60);
        }

        public void LoadSamples(short[] samples)
        {
            _samples = samples;
            _playPosition = 0;
            Invalidate();
        }

        public void SetPlayPosition(float pos)
        {
            _playPosition = pos;
            Invalidate();
        }

        public void Clear()
        {
            _samples = null;
            _playPosition = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(_bgColor);

            int W = Width, H = Height;

            if (_samples == null || _samples.Length == 0)
            {
                // رسم خط منتصف فارغ
                using (Pen p = new Pen(Color.FromArgb(40, 60, 80), 1))
                    g.DrawLine(p, 0, H / 2, W, H / 2);

                string msg = "اسحب ملفاً صوتياً هنا أو اختر ملفاً";
                using (Font f = new Font("Segoe UI", 9))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(80, 130, 160)))
                {
                    SizeF sz = g.MeasureString(msg, f);
                    g.DrawString(msg, f, b, (W - sz.Width) / 2, (H - sz.Height) / 2);
                }
                return;
            }

            // رسم الموجة
            int samplesPerPixel = Math.Max(1, _samples.Length / W);
            using (Pen wavePen = new Pen(_waveColor, 1.2f))
            {
                for (int x = 0; x < W; x++)
                {
                    int start = x * samplesPerPixel;
                    int end = Math.Min(start + samplesPerPixel, _samples.Length);

                    short min = short.MaxValue, max = short.MinValue;
                    for (int i = start; i < end; i++)
                    {
                        if (_samples[i] < min) min = _samples[i];
                        if (_samples[i] > max) max = _samples[i];
                    }

                    float y1 = (float)(H / 2.0 - max / 32768.0 * (H / 2.0 - 2));
                    float y2 = (float)(H / 2.0 - min / 32768.0 * (H / 2.0 - 2));
                    g.DrawLine(wavePen, x, y1, x, y2);
                }
            }

            // رسم مؤشر التشغيل
            if (_playPosition > 0)
            {
                int px = (int)(_playPosition * W);
                using (Pen pp = new Pen(Color.White, 2))
                    g.DrawLine(pp, px, 0, px, H);
            }

            // إطار
            using (Pen border = new Pen(Color.FromArgb(30, 60, 90), 1))
                g.DrawRectangle(border, 0, 0, W - 1, H - 1);
        }
    }
}
