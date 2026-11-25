using System.Drawing.Drawing2D;

namespace CompresseurPatch;

/// <summary>Liquid-glass progress bar with gradient fill and top specular reflection.</summary>
internal sealed class FlatProgressBar : Control
{
    private double _value;

    public FlatProgressBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Height = 14;
    }

    public double Value
    {
        get => _value;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(clamped - _value) < 0.01)
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }
    }

    public Color BarColor { get; set; } = Color.FromArgb(255, 56, 92);
    public Color BarEndColor { get; set; } = Color.FromArgb(255, 138, 43);
    public Color TrackColor { get; set; } = Color.FromArgb(160, 14, 18, 28);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var bounds = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
        var radius = Math.Max(3, bounds.Height / 2);

        using (var trackPath = RoundedPath(bounds, radius))
        using (var trackBrush = new SolidBrush(TrackColor))
        using (var trackBorder = new Pen(Color.FromArgb(48, 255, 255, 255), 1f))
        {
            g.FillPath(trackBrush, trackPath);
            g.DrawPath(trackBorder, trackPath);
        }

        if (_value <= 0)
        {
            return;
        }

        var fillWidth = Math.Max(bounds.Height, (int)Math.Round(bounds.Width * _value / 100.0));
        var fillRect = new Rectangle(0, 0, Math.Min(fillWidth, bounds.Width), bounds.Height);

        using var barPath = RoundedPath(fillRect, radius);
        using var barBrush = new LinearGradientBrush(
            new Point(fillRect.Left, fillRect.Top),
            new Point(Math.Max(fillRect.Left + 1, fillRect.Right), fillRect.Top),
            BarColor,
            BarEndColor);

        g.FillPath(barBrush, barPath);

        var glossRect = new Rectangle(fillRect.X + 2, fillRect.Y + 1, Math.Max(1, fillRect.Width - 4), Math.Max(1, fillRect.Height / 2));
        using var glossPath = RoundedPath(glossRect, Math.Max(2, radius - 1));
        using var glossBrush = new LinearGradientBrush(
            new Point(0, glossRect.Top),
            new Point(0, glossRect.Bottom + 1),
            Color.FromArgb(95, 255, 255, 255),
            Color.FromArgb(10, 255, 255, 255));
        g.FillPath(glossBrush, glossPath);

        using var barBorder = new Pen(Color.FromArgb(120, 255, 200, 180), 1f);
        g.DrawPath(barBorder, barPath);
    }

    private static GraphicsPath RoundedPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
