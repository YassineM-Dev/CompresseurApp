using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CompresseurPatch;

public sealed partial class MainForm : Form
{
    private static readonly Color Canvas = GlassTheme.CanvasTop;
    private static readonly Color Ink = GlassTheme.Ink;
    private static readonly Color Muted = GlassTheme.Muted;
    private static readonly Color Danger = GlassTheme.Danger;

    private readonly GlassDropZone _dropZone = new();
    private readonly Label _dropTitle = new();
    private readonly Label _dropHint = new();
    private readonly Label _infoLabel = new();
    private readonly GlassRadioButton _modeCompress = new();
    private readonly GlassRadioButton _modePatch60 = new();
    private readonly GlassRadioButton _modePatch120 = new();
    private readonly GlassComboBox _crfBox = new();
    private readonly Label _outputLabel = new();
    private readonly FlatProgressBar _progress = new();
    private readonly Label _statusLabel = new();
    private readonly TextBox _log = new();
    private readonly GlassButton _runButton;
    private readonly GlassButton _cancelButton;
    private readonly GlassButton _openFolderButton;

    private bool _dropHover;

    public MainForm()
    {
        Text = "Compresseur/Patch";
        ClientSize = new Size(860, 820);
        MinimumSize = new Size(760, 740);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        ForeColor = Ink;
        Font = new Font("Segoe UI", 9.5f);
        AllowDrop = true;
        DoubleBuffered = true;

        _runButton = MakeButton("Compress video", isPrimary: true, width: 200);
        _cancelButton = MakeButton("Cancel", isPrimary: false, width: 115);
        _openFolderButton = MakeButton("Open folder", isPrimary: false, width: 135);

        BuildLayout();
        WireEvents();
        ApplyInitialState();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        GlassTheme.ApplyDarkTitleBar(Handle);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            base.OnPaintBackground(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bgBrush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(rect.Width, rect.Height),
            GlassTheme.CanvasTop,
            GlassTheme.CanvasBottom))
        {
            g.FillRectangle(bgBrush, rect);
        }

        DrawAmbientOrb(g, new Rectangle(-90, -70, 460, 360), Color.FromArgb(56, 255, 46, 82));
        DrawAmbientOrb(g, new Rectangle(rect.Width - 360, 150, 440, 380), Color.FromArgb(34, 88, 115, 255));
        DrawAmbientOrb(g, new Rectangle(60, rect.Height - 280, 420, 320), Color.FromArgb(30, 255, 134, 48));
    }

    private static void DrawAmbientOrb(Graphics g, Rectangle bounds, Color centerColor)
    {
        if (bounds.Width <= 10 || bounds.Height <= 10)
        {
            return;
        }

        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        using var pgb = new PathGradientBrush(path)
        {
            CenterColor = centerColor,
            SurroundColors = new[] { Color.FromArgb(0, centerColor.R, centerColor.G, centerColor.B) },
        };
        g.FillPath(pgb, path);
    }

    // ── Layout ──────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(24, 18, 24, 12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildDropZone(), 0, 1);
        root.Controls.Add(BuildInfoRow(), 0, 2);
        root.Controls.Add(BuildModePanel(), 0, 3);
        root.Controls.Add(BuildProgressRow(), 0, 4);
        root.Controls.Add(BuildButtonRow(), 0, 5);
        root.Controls.Add(BuildLogPanel(), 0, 6);
        root.Controls.Add(BuildFooter(), 0, 7);

        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var card = new GlassCard
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CornerRadius = 18,
            GlassColor = Color.FromArgb(155, 22, 27, 42),
            BorderHighlightColor = Color.FromArgb(95, 255, 110, 128),
            BorderShadowColor = Color.FromArgb(28, 255, 255, 255),
            Padding = new Padding(20, 14, 20, 14),
            Margin = new Padding(0, 0, 0, 12),
        };

        var textCol = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
        };
        textCol.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        textCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textCol.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = new Label
        {
            Text = "COMPRESSEUR/PATCH",
            AutoSize = true,
            Font = new Font("Segoe UI", 17.5f, FontStyle.Bold),
            ForeColor = Ink,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        var subtitle = new Label
        {
            Text = "Drop a video → compress it, or patch it to 60 / 120 fps for TikTok.",
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(1, 4, 0, 0),
        };

        textCol.Controls.Add(title, 0, 0);
        textCol.Controls.Add(subtitle, 0, 1);

        card.Controls.Add(textCol);
        return card;
    }

    private Control BuildDropZone()
    {
        _dropZone.Dock = DockStyle.Fill;
        _dropZone.Margin = new Padding(0, 0, 0, 10);
        _dropZone.Cursor = Cursors.Hand;
        _dropZone.AllowDrop = true;

        _dropTitle.Text = "Drop your video here";
        _dropTitle.Dock = DockStyle.Top;
        _dropTitle.Height = 32;
        _dropTitle.TextAlign = ContentAlignment.MiddleCenter;
        _dropTitle.Font = new Font("Segoe UI Semibold", 13f);
        _dropTitle.ForeColor = Ink;
        _dropTitle.BackColor = Color.Transparent;

        _dropHint.Text = "or click to browse   •   mp4, mov, mkv, avi, webm…";
        _dropHint.Dock = DockStyle.Top;
        _dropHint.Height = 24;
        _dropHint.TextAlign = ContentAlignment.MiddleCenter;
        _dropHint.Font = new Font("Segoe UI", 9.25f);
        _dropHint.ForeColor = Muted;
        _dropHint.BackColor = Color.Transparent;

        var spacerTop = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = Color.Transparent };
        var spacerBottom = new Panel { Dock = DockStyle.Bottom, Height = 16, BackColor = Color.Transparent };

        spacerTop.Click += OnBrowseClick;
        spacerBottom.Click += OnBrowseClick;

        // Dock order: last added docks first, so add bottom-up.
        _dropZone.Controls.Add(spacerBottom);
        _dropZone.Controls.Add(_dropHint);
        _dropZone.Controls.Add(_dropTitle);
        _dropZone.Controls.Add(spacerTop);

        return _dropZone;
    }

    private Control BuildInfoRow()
    {
        var pill = new GlassCard
        {
            Dock = DockStyle.Fill,
            Height = 34,
            CornerRadius = 11,
            GlassColor = Color.FromArgb(125, 18, 23, 36),
            DrawTopGlowLine = false,
            Padding = new Padding(14, 0, 14, 0),
            Margin = new Padding(0, 0, 0, 10),
        };

        _infoLabel.AutoSize = false;
        _infoLabel.Dock = DockStyle.Fill;
        _infoLabel.TextAlign = ContentAlignment.MiddleLeft;
        _infoLabel.Font = new Font("Segoe UI Semibold", 9.25f);
        _infoLabel.ForeColor = Muted;
        _infoLabel.BackColor = Color.Transparent;
        _infoLabel.Margin = new Padding(0);
        _infoLabel.Text = "ffmpeg: checking…";

        pill.Controls.Add(_infoLabel);
        return pill;
    }

    private Control BuildModePanel()
    {
        var card = new GlassCard
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            CornerRadius = 16,
            Padding = new Padding(16, 14, 16, 14),
            Margin = new Padding(0, 0, 0, 12),
        };

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var modes = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.Transparent,
        };

        _modeCompress.Text = "Compress (CRF)";
        _modeCompress.Checked = true;
        _modePatch60.Text = "60 fps Patch";
        _modePatch120.Text = "120 fps Patch";

        foreach (var radio in new[] { _modeCompress, _modePatch60, _modePatch120 })
        {
            radio.Font = new Font("Segoe UI Semibold", 9.75f);
            radio.Size = new Size(192, 40);
            radio.Margin = new Padding(0, 0, 12, 0);
            modes.Controls.Add(radio);
        }

        var quality = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent,
        };

        var qualityLabel = new Label
        {
            Text = "Quality (CRF)",
            AutoSize = true,
            ForeColor = Ink,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Margin = new Padding(2, 6, 12, 0),
        };

        _crfBox.Font = new Font("Segoe UI Semibold", 9.5f);
        _crfBox.Width = 78;
        _crfBox.Items.AddRange(new object[] { "16", "18", "20", "23", "26" });
        _crfBox.SelectedIndex = 1;
        _crfBox.Margin = new Padding(0, 2, 14, 0);

        var qualityHint = new Label
        {
            Text = "18 = default method   •   lower = higher quality   •   higher = smaller file",
            AutoSize = true,
            ForeColor = Muted,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 8.75f),
            Margin = new Padding(0, 7, 0, 0),
        };

        quality.Controls.Add(qualityLabel);
        quality.Controls.Add(_crfBox);
        quality.Controls.Add(qualityHint);

        _outputLabel.AutoSize = true;
        _outputLabel.ForeColor = Color.FromArgb(190, 204, 224);
        _outputLabel.BackColor = Color.Transparent;
        _outputLabel.Font = new Font("Segoe UI", 8.75f);
        _outputLabel.Margin = new Padding(2, 2, 0, 0);
        _outputLabel.Text = "Output: —";

        panel.Controls.Add(modes, 0, 0);
        panel.Controls.Add(quality, 0, 1);
        panel.Controls.Add(_outputLabel, 0, 2);

        card.Controls.Add(panel);
        return card;
    }

    private Control BuildProgressRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _progress.Dock = DockStyle.Fill;
        _progress.Height = 14;
        _progress.Margin = new Padding(0, 0, 0, 6);

        _statusLabel.AutoSize = true;
        _statusLabel.ForeColor = Muted;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.Font = new Font("Segoe UI Semibold", 9f);
        _statusLabel.Margin = new Padding(2, 0, 0, 0);
        _statusLabel.Text = "Ready.";

        row.Controls.Add(_progress, 0, 0);
        row.Controls.Add(_statusLabel, 0, 1);

        return row;
    }

    private Control BuildButtonRow()
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12),
            BackColor = Color.Transparent,
        };

        _runButton.Margin = new Padding(0, 0, 10, 0);
        _cancelButton.Margin = new Padding(0, 0, 10, 0);
        _openFolderButton.Margin = new Padding(0);

        row.Controls.Add(_runButton);
        row.Controls.Add(_cancelButton);
        row.Controls.Add(_openFolderButton);

        return row;
    }

    private Control BuildLogPanel()
    {
        var card = new GlassCard
        {
            Dock = DockStyle.Fill,
            CornerRadius = 14,
            GlassColor = GlassTheme.GlassFillTerminal,
            BorderHighlightColor = Color.FromArgb(62, 255, 255, 255),
            BorderShadowColor = Color.FromArgb(18, 255, 255, 255),
            Padding = new Padding(14, 10, 14, 10),
            Margin = new Padding(0),
        };

        var header = new Label
        {
            Text = "●  CONSOLE OUTPUT",
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI Semibold", 8f),
            ForeColor = Color.FromArgb(130, 150, 175),
            BackColor = Color.Transparent,
        };

        _log.Dock = DockStyle.Fill;
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Color.FromArgb(11, 14, 22);
        _log.ForeColor = Color.FromArgb(172, 218, 204);
        _log.Font = new Font("Consolas", 8.75f);
        _log.ScrollBars = ScrollBars.None;
        _log.WordWrap = true;
        _log.TabStop = false;

        card.Controls.Add(_log);
        card.Controls.Add(header);
        return card;
    }

    private Control BuildFooter()
    {
        return new Label
        {
            Text = "© Developped By Yassine",
            Dock = DockStyle.Fill,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.25f),
            ForeColor = Color.FromArgb(124, 138, 162),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
        };
    }

    private static GlassButton MakeButton(string text, bool isPrimary, int width)
    {
        return new GlassButton
        {
            Text = text,
            IsPrimary = isPrimary,
            Width = width,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 9.75f),
        };
    }

    // ── Small UI helpers shared with the workflow partial ───────────────────

    private void AppendLog(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<string>(AppendLog), line);
            return;
        }

        if (_log.TextLength > 200_000)
        {
            _log.Text = _log.Text[^100_000..];
        }

        _log.AppendText(line + Environment.NewLine);
    }

    private void SetProgress(double percent)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action<double>(SetProgress), percent);
            return;
        }

        // ffmpeg can report a stale out_time near the end - never let the bar jump back.
        if (percent <= _progress.Value)
        {
            return;
        }

        _progress.Value = percent;
        _statusLabel.ForeColor = Muted;
        _statusLabel.Text = $"Working…  {percent:0}%";
        UpdateDiscordProgress(percent);
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusLabel.ForeColor = error ? Danger : Muted;
        _statusLabel.Text = text;
    }
}
