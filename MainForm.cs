using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AudioCompressor.Algorithms;
using AudioCompressor.Models;
using AudioCompressor.UI;
using NAudio.Wave;

namespace AudioCompressor
{
    public partial class MainForm : Form
    {


        // ─── الخوارزميات المتاحة 
        private readonly Dictionary<string, ICompressionAlgorithm> _algorithms =
            new Dictionary<string, ICompressionAlgorithm>
            {
                { "NLQ",  new NonlinearQuantization() },
                { "DPCM", new DPCM() },
                { "PDC",  new PredictiveDifferentialCoding() },
                { "DM",   new DeltaModulation() },
                { "ADM",  new AdaptiveDeltaModulation() },
            };

        // ─── البيانات الحالية ─────────────────────────────────────────────────
        private AudioFile _currentFile;
        private CompressionResult _lastResult;
        private CancellationTokenSource _cts;

        // ─── تشغيل الصوت ─────────────────────────────────────────────────────



        // private Button btnPlayAudio;
        // private Button btnPauseAudio;
        // private Button btnStopAudio;
        // private bool _isPaused = false;







        private SoundPlayer _player;
        private System.Windows.Forms.Timer _playTimer;
        private DateTime _playStart;
        private const string MciAlias = "AudioCompressorPlayer";
        private bool _mciOpen;

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr hwndCallback);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

        // ─── عناصر الواجهة ───────────────────────────────────────────────────
        private WaveformPanel pnlWaveform;
        private ChartPanel chartRatio;
        private ChartPanel chartSpeed;
        private SplitContainer splitMain;

        // Header
        private Label lblTitle;
        private Label lblSubtitle;

        // File section
        private Button btnBrowse;
        private Button btnOpenFile;
        private Button btnPlayHeader;
        private Button btnStopHeader;
        private Label lblFileName;
        private ProgressBar pbPlayback;
        private Label lblDuration;

        // File Info
        private Label lblInfoSize, lblInfoDuration, lblInfoSampleRate;
        private Label lblInfoChannels, lblInfoBitRate, lblInfoEncoding;
        private Label lblInfoFileNameV, lblInfoSizeV, lblInfoDurationV, lblInfoSampleRateV;
        private Label lblInfoChannelsV, lblInfoBitRateV, lblInfoEncodingV;

        // Algorithm buttons
        private Button[] btnAlgos;
        private string _selectedAlgo = "DPCM";

        // Settings
        private TrackBar trkSampleRate;
        private TrackBar trkQuantLevels;
        private TrackBar trkBitRate;
        private TrackBar trkDeltaStep;
        private Label lblSampleRateVal, lblQuantVal, lblBitRateVal, lblDeltaVal;
        private ToolTip _settingsToolTip;

        // Compression controls
        private Button btnCompress;
        private Button btnDecompress;
        private Button btnCancel;
        private Button btnSave;
        private Button btnSaveDecompressed;
        private Button btnReset;
        private ProgressBar pbCompression;
        private Label lblProgress;
        private Label lblStatus;

        // مسار الملف المفكوك الأخير (مؤقت)
        private string _lastDecompressedWavPath;

        // Report
        private RichTextBox rtbReport;
        private TabControl tabMain;

        // ─── Constructor ──────────────────────────────────────────────────────
        public MainForm()
        {
            //InitializeComponent();
            BuildUI();
            SetupTimers();
            UpdateUI();

            this.Load += (s, e) =>
            {
                ConfigureMainSplitter();
            };
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  بناء الواجهة برمجياً
        // ═══════════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // ─── Header ───────────────────────────────────────────────────────
            Panel pnlHeader = MakePanel(Color.FromArgb(13, 20, 35), new Rectangle(0, 0, 1200, 56));
            pnlHeader.Dock = DockStyle.Top;
            pnlHeader.Height = 56;
            pnlHeader.BorderStyle = BorderStyle.None;

            lblTitle = new Label
            {
                Text = "🎵  AUDIO COMPRESSOR",
                ForeColor = Color.FromArgb(0, 200, 255),
                Font = new Font("Consolas", 14, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 10)
            };
            lblSubtitle = new Label
            {
                Text = "AUDIO COMPRESSOR ",
                ForeColor = Color.FromArgb(100, 130, 160),
                Font = new Font("Segoe UI", 8),
                AutoSize = true,
                Location = new Point(18, 36)
            };
            btnOpenFile = MakeButton("📂  Open audio file", Color.FromArgb(0, 90, 150), 175, 34);
            btnOpenFile.Click += BtnBrowse_Click;
            // Header playback buttons (always visible)
            btnPlayHeader = MakeButton("▶", Color.FromArgb(0, 120, 80), 44, 30);
            btnPlayHeader.Click += BtnPlay_Click;
            btnPlayHeader.FlatStyle = FlatStyle.Flat;
            btnPlayHeader.Font = new Font("Segoe UI", 9, FontStyle.Bold);

            btnStopHeader = MakeButton("■", Color.FromArgb(120, 50, 50), 44, 30);
            btnStopHeader.Click += BtnStop_Click;
            btnStopHeader.FlatStyle = FlatStyle.Flat;
            btnStopHeader.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSubtitle, btnOpenFile, btnPlayHeader, btnStopHeader });
            pnlHeader.Resize += (s, e) =>
            {
                int x = Math.Max(8, pnlHeader.ClientSize.Width - btnOpenFile.Width - 14);
                btnOpenFile.Location = new Point(x, 11);
                btnStopHeader.Location = new Point(x - (btnStopHeader.Width + 8), 11);
                btnPlayHeader.Location = new Point(x - (btnStopHeader.Width + btnPlayHeader.Width + 12), 11);
            };

            // ─── Main Split ───────────────────────────────────────────────────
            splitMain = new SplitContainer();

            splitMain.Dock = DockStyle.Fill;
            splitMain.Orientation = Orientation.Vertical;
            splitMain.BackColor = Color.FromArgb(10, 15, 25);

            // Fill أولاً ثم Top — حتى لا يغطي المحتوى على الشريط الجانبي
            this.Controls.Add(splitMain);
            this.Controls.Add(pnlHeader);
            pnlHeader.BringToFront();

            // ابنِ الواجهة مرة واحدة فقط
            BuildSidebar(splitMain.Panel1);
            BuildMainPanel(splitMain.Panel2);

            // بعد ما يأخذ حجم فعلي
            splitMain.HandleCreated += (s, e) => ConfigureMainSplitter();
            splitMain.SizeChanged += (s, e) => ConfigureMainSplitter();
        }

        private void ConfigureMainSplitter()
        {
            if (splitMain == null || splitMain.IsDisposed)
                return;

            int width = splitMain.ClientSize.Width;
            if (width <= 1)
                return;

            int panel1Min = Math.Min(240, width - 1);
            int panel2Min = Math.Min(400, Math.Max(0, width - panel1Min - 1));

            if (splitMain.Panel1MinSize != panel1Min)
                splitMain.Panel1MinSize = panel1Min;
            if (splitMain.Panel2MinSize != panel2Min)
                splitMain.Panel2MinSize = panel2Min;

            int minDistance = splitMain.Panel1MinSize;
            int maxDistance = width - splitMain.Panel2MinSize;
            if (minDistance > maxDistance)
                return;

            int distance = Math.Max(minDistance, Math.Min(270, maxDistance));
            if (splitMain.SplitterDistance != distance)
                splitMain.SplitterDistance = distance;
        }


        // ─── Sidebar ──────────────────────────────────────────────────────────
        private void BuildSidebar(SplitterPanel panel)
        {
            panel.BackColor = Color.FromArgb(13, 20, 35);
            panel.Padding = new Padding(10, 12, 6, 8);

            FlowLayoutPanel flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 6, 0, 0)
            };
            panel.Controls.Add(flow);
            int sideW = Math.Max(180, panel.ClientSize.Width - panel.Padding.Horizontal - 4);

            // ── Algorithm ──
            flow.Controls.Add(MakeSpacer(sideW, 4));
            flow.Controls.Add(MakeSectionLabel("خوارزمية الضغط", sideW));

            string[] algoKeys = { "NLQ", "DPCM", "PDC", "DM", "ADM" };
            btnAlgos = new Button[algoKeys.Length];
            for (int i = 0; i < algoKeys.Length; i++)
            {
                string key = algoKeys[i];
                var algo = _algorithms[key];
                Button btn = new Button
                {
                    Text = $"{algo.ShortName} — {algo.Name}",
                    Tag = key,
                    Width = sideW,
                    Height = 38,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = key == _selectedAlgo
                        ? Color.FromArgb(0, 60, 100)
                        : Color.FromArgb(20, 30, 50),
                    ForeColor = key == _selectedAlgo
                        ? Color.FromArgb(0, 200, 255)
                        : Color.FromArgb(120, 150, 180),
                    Font = new Font("Segoe UI", 8),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(8, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    Margin = new Padding(0, i == 0 ? 4 : 2, 0, 0)
                };
                btn.FlatAppearance.BorderColor = key == _selectedAlgo
                    ? Color.FromArgb(0, 150, 200) : Color.FromArgb(30, 50, 80);
                btn.Click += (s, e) => SelectAlgorithm((string)((Button)s).Tag);
                btnAlgos[i] = btn;
                flow.Controls.Add(btn);
            }

            flow.Controls.Add(MakeSpacer(sideW, 10));

            // ── Settings ──
            flow.Controls.Add(MakeSectionLabel("إعدادات الضغط", sideW));

            // Sample Rate
            lblSampleRateVal = MakeValueLabel("22050 Hz");
            flow.Controls.Add(MakeSettingRow("معدل أخذ العينات", lblSampleRateVal, sideW));
            trkSampleRate = MakeTrackBar(8000, 48000, 22050, sideW);
            trkSampleRate.ValueChanged += (s, e) => UpdateSliderDisplay(lblSampleRateVal, trkSampleRate, v => v + " Hz");
            flow.Controls.Add(trkSampleRate);

            // Quantization Levels
            lblQuantVal = MakeValueLabel("256");
            flow.Controls.Add(MakeSettingRow("مستويات التكميم", lblQuantVal, sideW));
            trkQuantLevels = MakeTrackBar(16, 4096, 256, sideW);
            trkQuantLevels.TickFrequency = 256;
            trkQuantLevels.ValueChanged += (s, e) => UpdateSliderDisplay(lblQuantVal, trkQuantLevels, v => v.ToString());
            flow.Controls.Add(trkQuantLevels);

            // Bit Rate
            lblBitRateVal = MakeValueLabel("128 kbps");
            flow.Controls.Add(MakeSettingRow("معدل البت المستهدف", lblBitRateVal, sideW));
            trkBitRate = MakeTrackBar(32, 320, 128, sideW);
            trkBitRate.TickFrequency = 32;
            trkBitRate.ValueChanged += (s, e) => UpdateSliderDisplay(lblBitRateVal, trkBitRate, v => v + " kbps");
            flow.Controls.Add(trkBitRate);

            // Delta Step
            lblDeltaVal = MakeValueLabel("512");
            flow.Controls.Add(MakeSettingRow("حجم الخطوة (DM/ADM)", lblDeltaVal, sideW));
            trkDeltaStep = MakeTrackBar(16, 4096, 512, sideW);
            trkDeltaStep.TickFrequency = 256;
            trkDeltaStep.ValueChanged += (s, e) => UpdateSliderDisplay(lblDeltaVal, trkDeltaStep, v => v.ToString());
            flow.Controls.Add(trkDeltaStep);

            _settingsToolTip = new ToolTip { AutoPopDelay = 5000, InitialDelay = 150, ShowAlways = true };
            WireSliderToolTip(trkSampleRate, lblSampleRateVal, v => v + " Hz");
            WireSliderToolTip(trkQuantLevels, lblQuantVal, v => v.ToString());
            WireSliderToolTip(trkBitRate, lblBitRateVal, v => v + " kbps");
            WireSliderToolTip(trkDeltaStep, lblDeltaVal, v => v.ToString());

            flow.Controls.Add(MakeSpacer(sideW, 12));

            // ── Action Buttons ──
            btnCompress = MakeButton("▶  بدء الضغط", Color.FromArgb(0, 100, 180), sideW, 38);
            btnCompress.Click += BtnCompress_Click;
            flow.Controls.Add(btnCompress);

            btnDecompress = MakeButton("◀  فك الضغط", Color.FromArgb(30, 80, 50), sideW, 34);
            btnDecompress.Click += BtnDecompress_Click;
            flow.Controls.Add(btnDecompress);

            btnCancel = MakeButton("✕  إلغاء العملية", Color.FromArgb(140, 40, 40), sideW, 34);
            btnCancel.Click += (s, e) => _cts?.Cancel();
            flow.Controls.Add(btnCancel);

            btnSave = MakeButton("💾  حفظ الملف المضغوط", Color.FromArgb(30, 100, 50), sideW, 34);
            btnSave.Click += BtnSave_Click;
            flow.Controls.Add(btnSave);

            btnSaveDecompressed = MakeButton("💾  حفظ الملف المفكوك", Color.FromArgb(30, 100, 50), sideW, 34);
            btnSaveDecompressed.Click += BtnSaveDecompressed_Click;
            flow.Controls.Add(btnSaveDecompressed);

            btnReset = MakeButton("↺  إعادة ضبط", Color.FromArgb(50, 55, 70), sideW, 30);
            btnReset.Click += (s, e) => ResetAll();
            flow.Controls.Add(btnReset);

            flow.Resize += (s, e) => SyncSidebarFlowWidths(flow);
            SyncSidebarFlowWidths(flow);

            panel.HandleCreated += (s, e) =>
            {
                flow.AutoScrollPosition = new Point(0, 0);
            };
        }

        // ─── Main Panel ───────────────────────────────────────────────────────
        private void BuildMainPanel(SplitterPanel panel)
        {
            panel.BackColor = Color.FromArgb(10, 15, 25);
            panel.Padding = new Padding(10, 8, 10, 8);

            TableLayoutPanel tbl = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); // waveform + player
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); // file info
            tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // progress
            tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // tabs
            panel.Controls.Add(tbl);

            // Row 0: Waveform + player
            tbl.Controls.Add(BuildWaveformSection(), 0, 0);

            // Row 1: File info
            tbl.Controls.Add(BuildFileInfoSection(), 0, 1);

            // Row 2: Progress
            tbl.Controls.Add(BuildProgressSection(), 0, 2);

            // Row 3: Tabs
            tbl.Controls.Add(BuildTabs(), 0, 3);
        }

        private Panel BuildWaveformSection()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            // File row
            Panel topRow = new Panel { Height = 42, Dock = DockStyle.Top, BackColor = Color.FromArgb(6, 10, 18) };

            btnBrowse = new Button
            {
                Text = "📂  Open audio file",
                Width = 160,
                Height = 32,
                Location = new Point(8, 5),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 90, 150),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += BtnBrowse_Click;

            lblFileName = new Label
            {
                Text = "لم يتم اختيار ملف",
                ForeColor = Color.FromArgb(100, 130, 160),
                Font = new Font("Segoe UI", 9),
                AutoSize = false,
                Width = 400,
                Height = 28,
                Location = new Point(176, 8),
                TextAlign = ContentAlignment.MiddleLeft
            };
            // btnPlay = new Button
            // {
            //     Text = "تشغيل",
            //     Width = 80,
            //     Height = 28,
            //     FlatStyle = FlatStyle.Flat,
            //     BackColor = Color.FromArgb(0, 120, 80),
            //     ForeColor = Color.White,
            //     Font = new Font("Segoe UI", 9),
            //     Cursor = Cursors.Hand,
            //     Enabled = false
            // };
            // btnPlay.FlatAppearance.BorderSize = 0;
            // btnPlay.Click += BtnPlay_Click;

            // btnStop = new Button
            // {
            //     Text = "إيقاف",
            //     Width = 80,
            //     Height = 28,
            //     FlatStyle = FlatStyle.Flat,
            //     BackColor = Color.FromArgb(120, 50, 50),
            //     ForeColor = Color.White,
            //     Font = new Font("Segoe UI", 9),
            //     Cursor = Cursors.Hand,
            //     Enabled = false
            // };
            // btnStop.FlatAppearance.BorderSize = 0;
            // btnStop.Click += BtnStop_Click;

            // Anchor buttons to right using a layout panel for reliable positioning
            TableLayoutPanel topRowLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(8, 5, 8, 5)
            };
            topRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topRowLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            topRowLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            FlowLayoutPanel controlsPanel = new FlowLayoutPanel
            {
                Width = 180,
                Height = 35,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = false
            };

            lblFileName.Dock = DockStyle.Fill;
            lblFileName.Margin = new Padding(12, 0, 0, 0);
            lblFileName.Height = 28;

            topRowLayout.Controls.Add(btnBrowse, 0, 0);
            topRowLayout.Controls.Add(lblFileName, 1, 0);
            topRowLayout.Controls.Add(controlsPanel, 2, 0);

            topRow.Controls.Add(topRowLayout);

            // Waveform
            pnlWaveform = new WaveformPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(6, 10, 18),
                AllowDrop = true
            };
            pnlWaveform.DragEnter += PnlWaveform_DragEnter;
            pnlWaveform.DragDrop += PnlWaveform_DragDrop;
            pnlWaveform.DoubleClick += (s, e) => BtnBrowse_Click(s, e);
            pnlWaveform.Cursor = Cursors.Hand;

            // Playback bar
            Panel pbRow = new Panel { Height = 28, Dock = DockStyle.Bottom, BackColor = Color.Transparent };
            pbPlayback = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Maximum = 1000,
                Height = 8,
                Width = 500,
                Location = new Point(0, 10)
            };
            pbRow.SizeChanged += (s, e) => pbPlayback.Width = pbRow.Width - 100;
            lblDuration = new Label
            {
                Text = "00:00.0 / 00:00.0",
                ForeColor = Color.FromArgb(100, 140, 170),
                Font = new Font("Consolas", 8),
                AutoSize = true,
                Location = new Point(510, 7)
            };
            pbRow.SizeChanged += (s, e) => lblDuration.Location = new Point(pbPlayback.Width + 8, 7);
            pbRow.Controls.AddRange(new Control[] { pbPlayback, lblDuration });
            // ترتيب Dock: Add Fill first, then Bottom and Top controls
            p.Controls.Add(pnlWaveform);
            p.Controls.Add(pbRow);
            p.Controls.Add(topRow);

            return p;
        }

        private Panel BuildFileInfoSection()
        {
            Panel p = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(13, 20, 35),
                Padding = new Padding(10, 6, 10, 6)
            };
            Panel inner = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            p.Controls.Add(inner);



            TableLayoutPanel grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 7,
                RowCount = 2,
                BackColor = Color.Transparent
            };
            for (int c = 0; c < 7; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            string[] keys = { "اسم الملف", "حجم الملف", "المدة الزمنية", "معدل العينات", "القنوات", "معدل البت", "نوع الترميز" };
            Label[] keyLabels = new Label[7];
            Label[] valLabels = new Label[7];

            for (int i = 0; i < 7; i++)
            {
                keyLabels[i] = new Label
                {
                    Text = keys[i],
                    ForeColor = Color.FromArgb(90, 120, 150),
                    Font = new Font("Segoe UI", 8),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.BottomCenter
                };
                valLabels[i] = new Label
                {
                    Text = "—",
                    ForeColor = Color.FromArgb(200, 225, 255),
                    Font = new Font("Consolas", 9, FontStyle.Bold),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.TopCenter,
                    AutoEllipsis = true
                };
                grid.Controls.Add(keyLabels[i], i, 0);
                grid.Controls.Add(valLabels[i], i, 1);
            }

            lblInfoFileNameV = valLabels[0]; lblInfoSizeV = valLabels[1];
            lblInfoDurationV = valLabels[2]; lblInfoSampleRateV = valLabels[3];
            lblInfoChannelsV = valLabels[4]; lblInfoBitRateV = valLabels[5];
            lblInfoEncodingV = valLabels[6];

            inner.Controls.Add(grid);
            return p;
        }

        private Panel BuildProgressSection()
        {
            Panel p = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };

            lblProgress = new Label
            {
                Text = "جاهز",
                ForeColor = Color.FromArgb(100, 140, 170),
                Font = new Font("Segoe UI", 8),
                Dock = DockStyle.Top,
                Height = 18
            };

            pbCompression = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Maximum = 100,
                Height = 18,
                Dock = DockStyle.Top
            };

            lblStatus = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(0, 200, 100),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Dock = DockStyle.Bottom,
                Height = 18,
                TextAlign = ContentAlignment.MiddleLeft
            };

            p.Controls.AddRange(new Control[] { lblStatus, pbCompression, lblProgress });
            return p;
        }

        private Control BuildTabs()
        {
            tabMain = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9)
            };

            // Tab 1: Charts
            TabPage tabCharts = new TabPage("📊  مراقبة الأداء");
            tabCharts.BackColor = Color.FromArgb(10, 15, 25);

            TableLayoutPanel chartLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            chartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            chartLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            chartRatio = new ChartPanel("نسبة الضغط أثناء التنفيذ", "%",
                Color.FromArgb(0, 200, 255));
            chartRatio.Dock = DockStyle.Fill;

            chartSpeed = new ChartPanel("سرعة المعالجة", " KB/s",
                Color.FromArgb(50, 200, 100));
            chartSpeed.Dock = DockStyle.Fill;

            chartLayout.Controls.Add(chartRatio, 0, 0);
            chartLayout.Controls.Add(chartSpeed, 1, 0);
            tabCharts.Controls.Add(chartLayout);

            // Tab 2: Report
            TabPage tabReport = new TabPage("📋  تقرير الضغط");
            tabReport.BackColor = Color.FromArgb(10, 15, 25);

            rtbReport = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(6, 10, 18),
                ForeColor = Color.FromArgb(180, 210, 240),
                Font = new Font("Consolas", 9),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                Text = "سيظهر التقرير هنا بعد اكتمال عملية الضغط..."
            };
            tabReport.Controls.Add(rtbReport);

            tabMain.TabPages.AddRange(new[] { tabCharts, tabReport });
            return tabMain;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helper Builders
        // ═══════════════════════════════════════════════════════════════════════
        private Panel MakePanel(Color bg, Rectangle r)
        {
            return new Panel { BackColor = bg, Bounds = r };
        }

        private Label MakeSectionLabel(string text, int width)
        {
            return new Label
            {
                Text = "  " + text,
                ForeColor = Color.FromArgb(0, 150, 200),
                Font = new Font("Segoe UI", 8, FontStyle.Bold),
                Width = width,
                Height = 22,
                BackColor = Color.FromArgb(0, 40, 70),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 0, 2)
            };
        }

        private Label MakeSpacer(int w, int h)
        {
            return new Label { Width = w, Height = h, BackColor = Color.Transparent };
        }

        private Label MakeValueLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Color.FromArgb(0, 200, 255),
                Font = new Font("Consolas", 9, FontStyle.Bold),
                AutoSize = false,
                Width = 92,
                Height = 20,
                TextAlign = ContentAlignment.MiddleRight
            };
        }

        private Panel MakeSettingRow(string name, Label valueLabel, int width)
        {
            var row = new Panel
            {
                Width = width,
                Height = 22,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 2)
            };
            valueLabel.Dock = DockStyle.Right;
            var nameL = new Label
            {
                Text = name,
                ForeColor = Color.FromArgb(120, 150, 180),
                Font = new Font("Segoe UI", 8),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            row.Controls.Add(valueLabel);
            row.Controls.Add(nameL);
            return row;
        }

        private TrackBar MakeTrackBar(int min, int max, int val, int width)
        {
            return new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = val,
                Width = width,
                Height = 40,
                TickStyle = TickStyle.BottomRight,
                BackColor = Color.FromArgb(13, 20, 35),
                Margin = new Padding(0, 0, 0, 4)
            };
        }

        private void UpdateSliderDisplay(Label valueLabel, TrackBar trackBar, Func<int, string> format)
        {
            string text = format(trackBar.Value);
            valueLabel.Text = text;
            if (_settingsToolTip != null)
                _settingsToolTip.SetToolTip(trackBar, text);
        }

        private void WireSliderToolTip(TrackBar trackBar, Label valueLabel, Func<int, string> format)
        {
            void refresh()
            {
                UpdateSliderDisplay(valueLabel, trackBar, format);
            }

            trackBar.ValueChanged += (s, e) => refresh();
            trackBar.Scroll += (s, e) => refresh();
            trackBar.MouseDown += (s, e) => refresh();
            trackBar.MouseMove += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                    refresh();
            };
            refresh();
        }

        private static void SyncSidebarFlowWidths(FlowLayoutPanel flow)
        {
            int w = Math.Max(180, flow.ClientSize.Width -
                (flow.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0));

            foreach (Control c in flow.Controls)
            {
                if (c is TrackBar || c is Button)
                    c.Width = w;
                else if (c is Panel)
                    c.Width = w;
                else if (c is Label lbl)
                {
                    if (lbl.BackColor == Color.FromArgb(0, 40, 70))
                        lbl.Width = w;
                    else if (lbl.Height <= 12)
                        lbl.Width = w;
                }
            }
        }

        private Button MakeButton(string text, Color bg, int width, int height)
        {
            Button b = new Button
            {
                Text = text,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9),
                TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 3, 0, 0)
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private void SetupTimers()
        {
            _playTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _playTimer.Tick += PlayTimer_Tick;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  File Loading
        // ═══════════════════════════════════════════════════════════════════════
        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "اختر ملفاً صوتياً";
                dlg.Filter = "ملفات صوتية|*.wav;*.mp3;*.ogg;*.flac;*.aiff|كل الملفات|*.*";
                dlg.FilterIndex = 1;
                dlg.CheckFileExists = true;
                dlg.CheckPathExists = true;
                dlg.Multiselect = false;
                dlg.RestoreDirectory = true;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    LoadAudioFile(dlg.FileName);
            }
        }

        private void PnlWaveform_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
        }

        private void PnlWaveform_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                string ext = Path.GetExtension(files[0]).ToLower();

                if (ext == ".wav" ||
                    ext == ".mp3" ||
                    ext == ".ogg" ||
                    ext == ".flac" ||
                    ext == ".aiff")
                {
                    LoadAudioFile(files[0]);
                }
            }
        }

        private void LoadAudioFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                string ext = Path.GetExtension(path).ToUpperInvariant().TrimStart('.');

                short[] samples = ReadAudioFile(path, out int sampleRate, out int channels, out int bitsPerSample);
                long fileSize = new FileInfo(path).Length;

                if (false)
                {
                    // قراءة WAV مباشرة
                    samples = ReadWavFile(path, ref sampleRate, ref channels, ref bitsPerSample);
                }
                else if (false)
                {
                    // محاولة تحويل الملف إلى WAV مؤقت باستخدام ffmpeg
                    string wavTemp = ConvertToWavWithFFmpeg(path);
                    if (wavTemp != null && File.Exists(wavTemp))
                    {
                        try
                        {
                            samples = ReadWavFile(wavTemp, ref sampleRate, ref channels, ref bitsPerSample);
                            File.Delete(wavTemp);
                        }
                        catch
                        {
                            if (File.Exists(wavTemp)) File.Delete(wavTemp);
                            throw new Exception($"تعذّر تحويل {ext} باستخدام ffmpeg. تأكد من تثبيت ffmpeg على النظام.");
                        }
                    }
                    else
                    {
                        throw new Exception($"ffmpeg غير متثبت. يرجى تثبيت ffmpeg لدعم صيغة {ext}\n" +
                            "من الموقع: https://ffmpeg.org/download.html");
                    }
                }

                if (samples == null || samples.Length == 0)
                { MessageBox.Show("تعذّر قراءة بيانات الصوت.", "خطأ"); return; }

                double duration = (double)samples.Length / sampleRate / channels;

                _currentFile = new AudioFile
                {
                    FilePath = path,
                    FileSize = fileSize,
                    Duration = duration,
                    SampleRate = sampleRate,
                    Channels = channels,
                    BitsPerSample = bitsPerSample,
                    Encoding = Path.GetExtension(path).TrimStart('.').ToUpper(),
                    Samples = samples
                };

                _lastResult = null;
                ResetProgress();

                // تحديث الواجهة
                lblFileName.Text = _currentFile.FileName;
                lblFileName.ForeColor = Color.FromArgb(0, 200, 255);

                // عرض خصائص الملف
                lblInfoFileNameV.Text = _currentFile.FileName;
                lblInfoSizeV.Text = _currentFile.FileSizeFormatted;
                lblInfoDurationV.Text = _currentFile.DurationFormatted;
                lblInfoSampleRateV.Text = _currentFile.SampleRate + " Hz";
                lblInfoChannelsV.Text = _currentFile.Channels == 1 ? "Mono" : "Stereo";
                lblInfoBitRateV.Text = _currentFile.BitRate + " kbps";
                lblInfoEncodingV.Text = _currentFile.Encoding;

                // رسم الموجة
                pnlWaveform.LoadSamples(samples);

                // إعداد المشغل
                CloseMciPlayer();
                _player = new SoundPlayer();

                UpdateUI();
                lblStatus.Text = "✓ تم تحميل الملف بنجاح";
                lblStatus.ForeColor = Color.FromArgb(50, 200, 100);
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ في تحميل الملف:\n" + ex.Message, "خطأ");
            }
        }

        private short[] ReadAudioFile(string path, out int sampleRate, out int channels, out int bitsPerSample)
        {
            using (var reader = new AudioFileReader(path))
            {
                sampleRate = reader.WaveFormat.SampleRate;
                channels = reader.WaveFormat.Channels;
                bitsPerSample = 16;

                var samples = new List<short>();
                float[] buffer = new float[sampleRate * channels];
                int read;

                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    for (int i = 0; i < read; i++)
                    {
                        float value = Math.Max(-1.0f, Math.Min(1.0f, buffer[i]));
                        samples.Add((short)(value * short.MaxValue));
                    }
                }

                return samples.ToArray();
            }
        }

        private short[] ReadWavFile(string path, ref int sampleRate, ref int channels, ref int bitsPerSample)
        {
            short[] samples = null;
            using (FileStream fs = File.OpenRead(path))
            using (BinaryReader br = new BinaryReader(fs))
            {
                // قراءة WAV header
                string riff = new string(br.ReadChars(4));
                br.ReadInt32();
                string wave = new string(br.ReadChars(4));

                if (riff != "RIFF" || wave != "WAVE")
                    throw new Exception("ملف WAV غير صالح");// RIFF header
                while (fs.Position < fs.Length - 8)
                {
                    string chunkId = new string(br.ReadChars(4));
                    int chunkSize = br.ReadInt32();
                    if (chunkId == "fmt ")
                    {
                        br.ReadInt16(); // format
                        channels = br.ReadInt16();
                        sampleRate = br.ReadInt32();
                        br.ReadInt32(); // byte rate
                        br.ReadInt16(); // block align
                        bitsPerSample = br.ReadInt16();
                        if (chunkSize > 16) br.ReadBytes(chunkSize - 16);
                    }
                    else if (chunkId == "data")
                    {
                        byte[] raw = br.ReadBytes(chunkSize);
                        samples = new short[raw.Length / 2];
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = BitConverter.ToInt16(raw, i * 2);
                        break;
                    }
                    else br.ReadBytes(chunkSize);
                }
            }
            return samples;
        }

        private string ConvertToWavWithFFmpeg(string inputPath)
        {
            try
            {
                string tempWav = Path.Combine(Path.GetTempPath(), "audiocomp_" + Guid.NewGuid().ToString() + ".wav");

                var processInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{inputPath}\" -acodec pcm_s16le -ar 44100 -ac 2 \"{tempWav}\" -y",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process.WaitForExit(30000); // timeout 30 seconds
                    if (process.ExitCode == 0 && File.Exists(tempWav))
                        return tempWav;
                }
            }
            catch
            {
            }

            return null;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Playback
        // ═══════════════════════════════════════════════════════════════════════
        private static int SendMci(string command)
        {
            return mciSendString(command, null, 0, IntPtr.Zero);
        }

        private static string GetMciError(int code)
        {
            StringBuilder sb = new StringBuilder(256);
            return mciGetErrorString(code, sb, sb.Capacity) ? sb.ToString() : "Unknown playback error";
        }

        // private static double TryGetMediaDurationSeconds(string path)
        // {
        //     string alias = "AudioCompressorDuration";
        //     SendMci($"close {alias}");

        //     string ext = Path.GetExtension(path).ToLowerInvariant();
        //     string deviceType = ext == ".wav" ? "waveaudio" : "mpegvideo";
        //     int result = SendMci($"open \"{path}\" type {deviceType} alias {alias}");

        //     if (result != 0)
        //         result = SendMci($"open \"{path}\" alias {alias}");

        //     if (result != 0)
        //         return 0;

        //     try
        //     {
        //         SendMci($"set {alias} time format milliseconds");

        //         StringBuilder length = new StringBuilder(64);
        //         result = mciSendString($"status {alias} length", length, length.Capacity, IntPtr.Zero);
        //         if (result != 0)
        //             return 0;

        //         if (double.TryParse(length.ToString(), out double milliseconds) && milliseconds > 0)
        //             return milliseconds / 1000.0;

        //         return 0;
        //     }
        //     finally
        //     {
        //         SendMci($"close {alias}");
        //     }
        // }

        private void OpenMciPlayer(string path)
        {
            CloseMciPlayer();

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string deviceType = ext == ".wav" ? "waveaudio" : "mpegvideo";
            int result = SendMci($"open \"{path}\" type {deviceType} alias {MciAlias}");

            if (result != 0)
                result = SendMci($"open \"{path}\" alias {MciAlias}");

            if (result != 0)
                throw new InvalidOperationException(GetMciError(result));

            _mciOpen = true;
        }

        private void CloseMciPlayer()
        {
            if (!_mciOpen) return;
            SendMci($"stop {MciAlias}");
            SendMci($"close {MciAlias}");
            _mciOpen = false;
        }

        private void BtnPlay_Click(object sender, EventArgs e)
        {
            if (_currentFile == null) return;
            // if (_player == null)
            // { MessageBox.Show("التشغيل متاح فقط لملفات WAV.", "تنبيه"); return; }

            try
            {
                OpenMciPlayer(_currentFile.FilePath);
                int result = SendMci($"play {MciAlias}");
                if (result != 0)
                    throw new InvalidOperationException(GetMciError(result));
                _playStart = DateTime.Now;
                _playTimer.Start();
                SetPlaybackButtons(true);
            }
            catch (Exception ex)
            { MessageBox.Show("خطأ في التشغيل: " + ex.Message); }
        }

        private void BtnStop_Click(object sender, EventArgs e)
        {
            CloseMciPlayer();
            _playTimer.Stop();
            pbPlayback.Value = 0;
            pnlWaveform.SetPlayPosition(0);
            lblDuration.Text = $"00:00.0 / {_currentFile?.DurationFormatted ?? "00:00.0"}";
            SetPlaybackButtons(false);
        }

        private void PlayTimer_Tick(object sender, EventArgs e)
        {
            if (_currentFile == null) return;
            double elapsed = (DateTime.Now - _playStart).TotalSeconds;
            double pct = Math.Min(1.0, elapsed / _currentFile.Duration);
            pbPlayback.Value = (int)(pct * 1000);
            pnlWaveform.SetPlayPosition((float)pct);
            lblDuration.Text = $"{TimeSpan.FromSeconds(elapsed):mm\\:ss\\.f} / {_currentFile.DurationFormatted}";

            if (elapsed >= _currentFile.Duration)
            {
                CloseMciPlayer();
                _playTimer.Stop();
                SetPlaybackButtons(false);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Compression
        // ═══════════════════════════════════════════════════════════════════════
        private async void BtnCompress_Click(object sender, EventArgs e)
        {
            if (_currentFile == null) { MessageBox.Show("اختر ملفاً صوتياً أولاً."); return; }

            var settings = BuildSettings();
            var algo = _algorithms[_selectedAlgo];

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // إعداد واجهة أثناء الضغط
            SetCompressingState(true);
            chartRatio.Reset();
            chartSpeed.Reset();
            ResetProgress();

            var progress = new Progress<int>(pct =>
            {
                if (this.IsDisposed) return;
                this.Invoke((Action)(() =>
                {
                    pbCompression.Value = pct;
                    lblProgress.Text = $"جاري الضغط بخوارزمية {algo.ShortName}… {pct}%";

                    // تحديث الرسوم البيانية
                    double ratio = pct * (1.0 - (1.0 / (settings.QuantizationLevels / 16.0 + 1)));
                    double speed = _currentFile.FileSize / 1024.0 * pct / 100.0 / Math.Max(0.1,
                        (DateTime.Now - _compressStart).TotalSeconds);
                    chartRatio.AddValue((float)ratio);
                    chartSpeed.AddValue((float)speed);
                }));
            });

            _compressStart = DateTime.Now;

            try
            {
                byte[] compressed = await Task.Run(() =>
                {
                    var p = new Progress<int>(v => ((IProgress<int>)progress).Report(v));
                    return algo.Compress(_currentFile.Samples, settings, p);
                }, token);

                double elapsed = (DateTime.Now - _compressStart).TotalSeconds;

                // استخدم حجم الملف الفعلي (يتضمن رؤوس الملفات وغيرها)
                // بدل حساب حجم PCM الخام لتطابق معلومات الملف الفعلية
                _lastResult = new CompressionResult
                {
                    OriginalSize = _currentFile.FileSize,
                    CompressedSize = compressed.Length,
                    ElapsedSeconds = elapsed,
                    Settings = settings,
                    CompressedData = compressed
                };

                pbCompression.Value = 100;
                lblProgress.Text = $"اكتمل الضغط — {elapsed:F2} ثانية";
                lblStatus.Text = $"✓ تمت العملية | نسبة التوفير: {_lastResult.SavingPercent:F1}% | الحجم: {_lastResult.OriginalSizeFormatted} → {_lastResult.CompressedSizeFormatted}";
                lblStatus.ForeColor = Color.FromArgb(50, 220, 100);

                GenerateReport();
                tabMain.SelectedIndex = 1; // switch to report tab
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "✗ تم إلغاء عملية الضغط";
                lblStatus.ForeColor = Color.FromArgb(220, 80, 80);
                pbCompression.Value = 0;
                lblProgress.Text = "ملغى";
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء الضغط:\n" + ex.Message, "خطأ");
            }
            finally
            {
                SetCompressingState(false);
                UpdateUI();
            }
        }

        private DateTime _compressStart;

        private void BtnDecompress_Click(object sender, EventArgs e)
        {
            if (_lastResult?.CompressedData == null)
            { MessageBox.Show("لا توجد بيانات مضغوطة. قم بالضغط أولاً."); return; }

            try
            {
                var algo = _algorithms[_selectedAlgo];
                var settings = BuildSettings();
                short[] recovered = algo.Decompress(_lastResult.CompressedData, settings);

                int origLen = _currentFile?.Samples?.Length ?? 0;
                int recLen = recovered?.Length ?? 0;
                int min = Math.Min(origLen, recLen);
                long diffCount = 0;
                double mse = 0.0;
                for (int i = 0; i < min; i++)
                {
                    int d = recovered[i] - _currentFile.Samples[i];
                    if (d != 0) diffCount++;
                    mse += (double)d * d;
                }
                if (min > 0) mse /= min;

                string status = (recLen == origLen && diffCount == 0) ? "✓" : "⚠";
                string details =
                    $"{status} تم فك الضغط\n" +
                    $"الخوارزمية: {algo.Name}\n" +
                    $"عدد العينات الأصلية: {origLen:N0}\n" +
                    $"عدد العينات المستعادة: {recLen:N0}\n" +
                    $"الاختلافات (عينات): {diffCount:N0}\n" +
                    $"MSE (على العينات المقارنة): {mse:F2}\n" +
                    $"المدة التقريبية: {(double)recLen / _currentFile.SampleRate:F2} ثانية";

                MessageBox.Show(details, "فك الضغط", MessageBoxButtons.OK, MessageBoxIcon.Information);
                // بعد العرض، احفظ النتيجة كملف WAV مؤقت وشغّله تلقائياً
                try
                {
                    string tmpWav = WriteTempWav(recovered, _currentFile.SampleRate, _currentFile.Channels);
                    OpenMciPlayer(tmpWav);
                    int res = SendMci($"play {MciAlias}");
                    if (res != 0) throw new InvalidOperationException(GetMciError(res));
                    _playStart = DateTime.Now;
                    _playTimer.Start();
                    SetPlaybackButtons(true);
                    lblStatus.Text = $"✓ تم تشغيل الملف المفكوك (مؤقت)";
                    lblStatus.ForeColor = Color.FromArgb(50, 200, 100);
                }
                catch (Exception ex)
                {
                    // لا تفشل العملية الكلية إذا فشل التشغيل
                    lblStatus.Text = "تحذير: لم يتم تشغيل الملف بعد فك الضغط.";
                    lblStatus.ForeColor = Color.FromArgb(220, 160, 40);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطأ أثناء فك الضغط:\n" + ex.Message, "خطأ");
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            if (_lastResult?.CompressedData == null)
            { MessageBox.Show("لا توجد بيانات مضغوطة للحفظ."); return; }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "حفظ الملف المضغوط";
                dlg.Filter = "ملف مضغوط|*.acp|كل الملفات|*.*";
                dlg.FileName = Path.GetFileNameWithoutExtension(_currentFile.FilePath) +
                               $"_{_selectedAlgo}_compressed.acp";

                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // نكتب ملف مخصص: header + البيانات
                        using (BinaryWriter bw = new BinaryWriter(File.Create(dlg.FileName)))
                        {
                            bw.Write("ACP1".ToCharArray());        // magic
                            bw.Write(_selectedAlgo.PadRight(4).ToCharArray()); // algo
                            bw.Write(_currentFile.SampleRate);
                            bw.Write(_currentFile.Channels);
                            bw.Write(_currentFile.Samples.Length); // عدد العينات
                            bw.Write(_lastResult.CompressedData.Length);
                            bw.Write(_lastResult.CompressedData);
                        }
                        lblStatus.Text = $"✓ تم الحفظ: {Path.GetFileName(dlg.FileName)}";
                        lblStatus.ForeColor = Color.FromArgb(50, 200, 100);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("خطأ في الحفظ:\n" + ex.Message, "خطأ");
                    }
                }
            }
        }

        private string WriteTempWav(short[] samples, int sampleRate, int channels)
        {
            string path = Path.Combine(Path.GetTempPath(), "audiocomp_decompressed_" + Guid.NewGuid().ToString() + ".wav");
            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                int bytesPerSample = 2;
                int blockAlign = channels * bytesPerSample;
                int byteRate = sampleRate * blockAlign;
                int dataSize = samples.Length * bytesPerSample;

                // RIFF header
                bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + dataSize);
                bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

                // fmt chunk
                bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                bw.Write(16); // PCM
                bw.Write((short)1); // PCM format
                bw.Write((short)channels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write((short)blockAlign);
                bw.Write((short)(bytesPerSample * 8));

                // data chunk
                bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                bw.Write(dataSize);

                // write samples (assume interleaved if multi-channel)
                foreach (short s in samples)
                    bw.Write(s);
            }
            _lastDecompressedWavPath = path;
            return path;
        }

        private void BtnSaveDecompressed_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(_lastDecompressedWavPath) || !File.Exists(_lastDecompressedWavPath))
            {
                MessageBox.Show("لا يوجد ملف مفكوك مؤقت لحفظه. قم بفك الضغط أولاً.", "تنبيه", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Title = "حفظ الملف المفكوك WAV";
                dlg.Filter = "WAV audio|*.wav|All files|*.*";
                dlg.FileName = Path.GetFileNameWithoutExtension(_currentFile?.FilePath ?? "decompressed") + "_decompressed.wav";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.Copy(_lastDecompressedWavPath, dlg.FileName, true);
                        lblStatus.Text = $"✓ تم حفظ الملف المفكوك: {Path.GetFileName(dlg.FileName)}";
                        lblStatus.ForeColor = Color.FromArgb(50, 200, 100);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("خطأ في حفظ الملف المفكوك:\n" + ex.Message, "خطأ");
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Report
        // ═══════════════════════════════════════════════════════════════════════
        private void GenerateReport()
        {
            if (_lastResult == null) return;
            var r = _lastResult;
            var s = r.Settings;
            var algo = _algorithms[_selectedAlgo];

            rtbReport.Clear();
            App("═══════════════════════════════════════════════════════════", Color.FromArgb(0, 100, 150));
            App("    تقرير عملية الضغط — Audio Compression Report", Color.FromArgb(0, 200, 255));
            App("═══════════════════════════════════════════════════════════", Color.FromArgb(0, 100, 150));
            App("");
            App("  [الملف الأصلي]", Color.FromArgb(0, 160, 220));
            AppRow("  الاسم", _currentFile.FileName);
            // أظهر كلا الحجمين: حجم الملف على القرص وحجم PCM الخام بعد فك الترميز
            long pcmSize = _currentFile?.Samples != null ? _currentFile.Samples.Length * 2L : 0L;
            string pcmSizeFormatted = FormatSize(pcmSize);
            AppRow("  الحجم (ملف)", _currentFile.FileSizeFormatted);
            AppRow("  الحجم (PCM خام)", pcmSizeFormatted);
            AppRow("  المدة", _currentFile.DurationFormatted);
            AppRow("  معدل العينات", _currentFile.SampleRate + " Hz");
            AppRow("  القنوات", _currentFile.Channels == 1 ? "Mono" : "Stereo");
            AppRow("  نوع الترميز", _currentFile.Encoding);
            App("");
            App("  [نتائج الضغط]", Color.FromArgb(0, 160, 220));
            AppRow("  الخوارزمية", algo.Name);
            AppRow("  حجم الملف بعد الضغط", $"{r.CompressedSize:N0} بايت ({r.CompressedSizeFormatted})");
            // حساب نسبتي التوفير والضغط بالمقارنة مع PCM الخام وأيضاً بالمقارنة مع حجم الملف (إن وُجد)
            double savingRelativeToPcm = pcmSize > 0 ? (1.0 - (double)r.CompressedSize / pcmSize) * 100 : 0;
            double ratioRelativeToPcm = (pcmSize > 0 && r.CompressedSize > 0) ? (double)pcmSize / r.CompressedSize : 1;
            AppRow("  نسبة التوفير (نسبة إلى الملف)", $"{r.SavingPercent:F1}%");
            AppRow("  نسبة التوفير (نسبة إلى PCM)", $"{savingRelativeToPcm:F1}%");
            AppRow("  نسبة الضغط (نسبة إلى الملف)", $"{r.CompressionRatio:F2}:1");
            AppRow("  نسبة الضغط (نسبة إلى PCM)", $"{ratioRelativeToPcm:F2}:1");
            if (_currentFile?.Samples != null && _currentFile.Samples.Length > 0)
            {
                double bps = r.CompressedSize * 8.0 / _currentFile.Samples.Length;
                AppRow("  بتات/عينة (فعلي)", $"{bps:F2} bit");
            }
            App("");
            App("  ملاحظة: الخوارزميات بنفس معدل البت/عينة تعطي حجماً", Color.FromArgb(100, 140, 170));
            App("  متشابهاً — لكن بيانات الضغط وجودة الصوت تختلف.", Color.FromArgb(100, 140, 170));
            AppRow("  الزمن المستغرق", $"{r.ElapsedSeconds:F3} ثانية");
            App("");
            App("  [إعدادات الضغط]", Color.FromArgb(0, 160, 220));
            AppRow("  معدل العينات المستهدف", s.TargetSampleRate + " Hz");
            AppRow("  معدل البت المستهدف", s.TargetBitRate + " kbps");
            AppRow("  مستويات التكميم", s.QuantizationLevels.ToString());
            AppRow("  حجم الخطوة (DM/ADM)", s.DeltaStep.ToString());
            App("");
            App("═══════════════════════════════════════════════════════════", Color.FromArgb(0, 100, 150));
            App($"  تاريخ التقرير: {DateTime.Now:yyyy-MM-dd HH:mm:ss}", Color.FromArgb(80, 110, 140));
        }

        private void App(string text, Color? color = null)
        {
            rtbReport.SelectionColor = color ?? Color.FromArgb(180, 210, 240);
            rtbReport.AppendText(text + "\n");
        }

        private void AppRow(string key, string val)
        {
            rtbReport.SelectionColor = Color.FromArgb(100, 140, 170);
            rtbReport.AppendText(key.PadRight(30));
            rtbReport.SelectionColor = Color.FromArgb(220, 240, 255);
            rtbReport.AppendText(val + "\n");
        }

        private string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1048576.0).ToString("F2") + " MB";
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════════════
        private CompressionSettings BuildSettings() => new CompressionSettings
        {
            AlgorithmKey = _selectedAlgo,
            TargetSampleRate = trkSampleRate.Value,
            QuantizationLevels = trkQuantLevels.Value,
            TargetBitRate = trkBitRate.Value,
            DeltaStep = trkDeltaStep.Value,
            ExpectedSampleCount = _currentFile?.Samples?.Length ?? 0
        };

        private void SelectAlgorithm(string key)
        {
            _selectedAlgo = key;
            for (int i = 0; i < btnAlgos.Length; i++)
            {
                bool active = (string)btnAlgos[i].Tag == key;
                btnAlgos[i].BackColor = active ? Color.FromArgb(0, 60, 100) : Color.FromArgb(20, 30, 50);
                btnAlgos[i].ForeColor = active ? Color.FromArgb(0, 200, 255) : Color.FromArgb(120, 150, 180);
                btnAlgos[i].FlatAppearance.BorderColor = active
                    ? Color.FromArgb(0, 150, 200) : Color.FromArgb(30, 50, 80);
            }
        }

        private void SetCompressingState(bool compressing)
        {
            btnCompress.Enabled = !compressing;
            btnDecompress.Enabled = !compressing;
            btnCancel.Enabled = compressing;
            btnSave.Enabled = !compressing;
        }

        private void ResetProgress()
        {
            pbCompression.Value = 0;
            lblProgress.Text = "جاهز";
            lblStatus.Text = "";
            chartRatio?.Reset();
            chartSpeed?.Reset();
        }

        private void SetPlaybackButtons(bool isPlaying)
        {
            bool hasFile = _currentFile != null;

            if (btnPlayHeader != null)
                btnPlayHeader.Enabled = hasFile && !isPlaying;
            if (btnStopHeader != null)
                btnStopHeader.Enabled = hasFile && isPlaying;
        }

        private void ResetAll()
        {
            CloseMciPlayer();
            _cts?.Cancel();
            _playTimer.Stop();
            _currentFile = null;
            _lastResult = null;
            pnlWaveform.Clear();
            pbPlayback.Value = 0;
            ResetProgress();
            lblFileName.Text = "لم يتم اختيار ملف";
            lblFileName.ForeColor = Color.FromArgb(100, 130, 160);
            lblInfoFileNameV.Text = lblInfoSizeV.Text = lblInfoDurationV.Text = lblInfoSampleRateV.Text =
            lblInfoChannelsV.Text = lblInfoBitRateV.Text = lblInfoEncodingV.Text = "—";
            rtbReport.Text = "سيظهر التقرير هنا بعد اكتمال عملية الضغط...";
            lblStatus.Text = "";
            SetPlaybackButtons(false);
            UpdateUI();
        }

        private void UpdateUI()
        {
            bool hasFile = _currentFile != null;
            bool hasResult = _lastResult != null;
            SetPlaybackButtons(_mciOpen);
            // btnCompress.Enabled = hasFile;
            //btnDecompress.Enabled = hasResult;
            // btnSave.Enabled = hasResult;
            //btnCancel.Enabled = false;
        }
    }
}
