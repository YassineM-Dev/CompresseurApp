using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CompresseurPatch;

internal static class GlassTheme
{
    public static readonly Color CanvasTop = Color.FromArgb(9, 11, 18);
    public static readonly Color CanvasBottom = Color.FromArgb(16, 20, 32);

    public static readonly Color GlassFill = Color.FromArgb(148, 22, 27, 42);
    public static readonly Color GlassFillTerminal = Color.FromArgb(195, 10, 13, 21);

    public static readonly Color Ink = Color.FromArgb(242, 246, 252);
    public static readonly Color Muted = Color.FromArgb(156, 168, 188);
    public static readonly Color Danger = Color.FromArgb(255, 108, 118);

    private static Icon? _cachedIcon;

    public static Icon? GetAppIcon()
    {
        if (_cachedIcon is not null)
        {
            return _cachedIcon;
        }

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("CompresseurPatch.Assets.app.ico");
            if (stream is not null)
            {
                _cachedIcon = new Icon(stream);
                return _cachedIcon;
            }
        }
        catch
        {
            // Ignore icon load errors.
        }

        return null;
    }

    public static void ApplyDarkTitleBar(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int darkMode = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));

            // DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2
            int cornerPref = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref cornerPref, sizeof(int));
        }
        catch
        {
            // Older Windows builds may not support these attributes.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static GraphicsPath RoundedRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
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

/// <summary>
/// Frosted glass container panel. Paints its translucent glass surface in OnPaintBackground
/// so child controls with BackColor = Color.Transparent blend seamlessly over the frosted layer.
/// </summary>
internal class GlassCard : Panel
{
    public int CornerRadius { get; set; } = 16;
    public Color GlassColor { get; set; } = GlassTheme.GlassFill;
    public Color BorderHighlightColor { get; set; } = Color.FromArgb(78, 255, 255, 255);
    public Color BorderShadowColor { get; set; } = Color.FromArgb(20, 255, 255, 255);
    public bool DrawTopGlowLine { get; set; } = true;

    public GlassCard()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
        using var path = GlassTheme.RoundedRect(rect, CornerRadius);

        using (var fillBrush = new SolidBrush(GlassColor))
        {
            g.FillPath(fillBrush, path);
        }

        using (var sheenBrush = new LinearGradientBrush(
            new Point(rect.Left, rect.Top),
            new Point(rect.Right, rect.Bottom),
            Color.FromArgb(30, 255, 255, 255),
            Color.FromArgb(3, 255, 255, 255)))
        {
            g.FillPath(sheenBrush, path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
        using var path = GlassTheme.RoundedRect(rect, CornerRadius);

        using (var borderBrush = new LinearGradientBrush(
            new Point(rect.Left, rect.Top),
            new Point(rect.Right, rect.Bottom),
            BorderHighlightColor,
            BorderShadowColor))
        using (var borderPen = new Pen(borderBrush, 1.15f))
        {
            g.DrawPath(borderPen, path);
        }

        if (DrawTopGlowLine && rect.Width > CornerRadius * 2 + 12)
        {
            var topLineStart = new Point(rect.Left + CornerRadius, rect.Top + 1);
            var topLineEnd = new Point(rect.Right - CornerRadius, rect.Top + 1);
            using var topGlowBrush = new LinearGradientBrush(
                topLineStart,
                topLineEnd,
                Color.FromArgb(0, 255, 255, 255),
                Color.FromArgb(110, 255, 255, 255));
            topGlowBrush.InterpolationColors = new ColorBlend(3)
            {
                Colors = new[]
                {
                    Color.FromArgb(0, 255, 255, 255),
                    Color.FromArgb(115, 255, 220, 225),
                    Color.FromArgb(0, 255, 255, 255),
                },
                Positions = new[] { 0f, 0.5f, 1f },
            };
            using var topPen = new Pen(topGlowBrush, 1f);
            g.DrawLine(topPen, topLineStart, topLineEnd);
        }
    }
}

/// <summary>
/// Interactive frosted glass drop zone with hover luminescence and vector upload emblem.
/// </summary>
internal sealed class GlassDropZone : GlassCard
{
    private bool _highlighted;

    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value)
            {
                return;
            }
            _highlighted = value;
            Invalidate(true);
        }
    }

    public GlassDropZone()
    {
        CornerRadius = 18;
        GlassColor = Color.FromArgb(145, 20, 25, 40);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        GlassColor = _highlighted
            ? Color.FromArgb(175, 38, 24, 38)
            : Color.FromArgb(145, 20, 25, 40);

        base.OnPaintBackground(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var glowW = Math.Min(360, Width - 20);
        var glowH = Math.Max(40, Height - 10);
        if (glowW > 20 && glowH > 20)
        {
            using var glowPath = new GraphicsPath();
            glowPath.AddEllipse((Width - glowW) / 2, (Height - glowH) / 2, glowW, glowH);
            using var pgb = new PathGradientBrush(glowPath)
            {
                CenterColor = _highlighted
                    ? Color.FromArgb(55, 255, 64, 96)
                    : Color.FromArgb(24, 255, 64, 96),
                SurroundColors = new[] { Color.FromArgb(0, 255, 64, 96) },
            };
            g.FillPath(pgb, glowPath);
        }

        DrawUploadBadge(g, new Point(Width / 2, 27), _highlighted);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        BorderHighlightColor = _highlighted
            ? Color.FromArgb(220, 255, 82, 112)
            : Color.FromArgb(75, 255, 255, 255);
        BorderShadowColor = _highlighted
            ? Color.FromArgb(120, 255, 140, 64)
            : Color.FromArgb(22, 255, 255, 255);

        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var inner = new Rectangle(9, 9, Math.Max(4, Width - 19), Math.Max(4, Height - 19));
        using var innerPath = GlassTheme.RoundedRect(inner, Math.Max(8, CornerRadius - 5));
        using var dashPen = new Pen(
            _highlighted ? Color.FromArgb(190, 255, 95, 120) : Color.FromArgb(62, 255, 110, 130),
            _highlighted ? 1.6f : 1.2f)
        {
            DashStyle = DashStyle.Dash,
            DashPattern = new[] { 5f, 4f },
        };
        g.DrawPath(dashPen, innerPath);
    }

    private static void DrawUploadBadge(Graphics g, Point center, bool active)
    {
        var pillRect = new Rectangle(center.X - 18, center.Y - 14, 36, 28);
        using var pillPath = GlassTheme.RoundedRect(pillRect, 9);
        using var fill = new SolidBrush(active
            ? Color.FromArgb(90, 255, 64, 96)
            : Color.FromArgb(45, 255, 72, 104));
        using var border = new Pen(active
            ? Color.FromArgb(210, 255, 110, 130)
            : Color.FromArgb(110, 255, 96, 120), 1.2f);

        g.FillPath(fill, pillPath);
        g.DrawPath(border, pillPath);

        using var iconPen = new Pen(active ? Color.White : Color.FromArgb(235, 255, 120, 140), 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLine(iconPen, center.X, center.Y + 6, center.X, center.Y - 5);
        g.DrawLines(iconPen, new[]
        {
            new Point(center.X - 5, center.Y - 1),
            new Point(center.X, center.Y - 6),
            new Point(center.X + 5, center.Y - 1),
        });
    }
}

/// <summary>Frosted glass pill radio button for mode selection.</summary>
internal sealed class GlassRadioButton : RadioButton
{
    private bool _hovered;

    public GlassRadioButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        AutoSize = false;
        Size = new Size(195, 42);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs eventargs)
    {
        base.OnMouseEnter(eventargs);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs eventargs)
    {
        base.OnMouseLeave(eventargs);
        _hovered = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaintBackground(pevent);

        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(1, 1, Math.Max(4, Width - 3), Math.Max(4, Height - 3));
        using var path = GlassTheme.RoundedRect(rect, 11);

        if (Checked)
        {
            using var activeBrush = new LinearGradientBrush(
                new Point(rect.Left, rect.Top),
                new Point(rect.Right, rect.Bottom),
                Color.FromArgb(88, 255, 52, 88),
                Color.FromArgb(48, 255, 134, 48));
            g.FillPath(activeBrush, path);

            using var activeBorder = new Pen(Color.FromArgb(215, 255, 86, 112), 1.35f);
            g.DrawPath(activeBorder, path);
        }
        else
        {
            var fillAlpha = _hovered ? 120 : 80;
            using var idleBrush = new SolidBrush(Color.FromArgb(fillAlpha, 32, 39, 58));
            g.FillPath(idleBrush, path);

            using var idleBorder = new Pen(
                _hovered ? Color.FromArgb(95, 255, 255, 255) : Color.FromArgb(45, 255, 255, 255),
                1f);
            g.DrawPath(idleBorder, path);
        }

        var dotOuter = new Rectangle(13, (Height - 16) / 2, 16, 16);
        if (Checked)
        {
            using var ringPen = new Pen(Color.FromArgb(245, 255, 96, 120), 1.6f);
            g.DrawEllipse(ringPen, dotOuter);

            var dotInner = new Rectangle(dotOuter.X + 4, dotOuter.Y + 4, 8, 8);
            using var coreBrush = new SolidBrush(Color.FromArgb(255, 92, 116));
            g.FillEllipse(coreBrush, dotInner);
        }
        else
        {
            using var ringPen = new Pen(Color.FromArgb(110, 160, 174, 196), 1.4f);
            g.DrawEllipse(ringPen, dotOuter);
        }

        var textColor = !Enabled
            ? Color.FromArgb(100, GlassTheme.Muted)
            : Checked ? GlassTheme.Ink : Color.FromArgb(215, 224, 238);

        var textRect = new Rectangle(37, 0, Width - 44, Height);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            textRect,
            textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Liquid-glass push button supporting primary (fiery gradient) and secondary (frosted slate) styles.</summary>
internal sealed class GlassButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public bool IsPrimary { get; set; }

    public GlassButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        base.OnPaintBackground(pevent);

        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rect = new Rectangle(1, 1, Math.Max(4, Width - 3), Math.Max(4, Height - 3));
        using var path = GlassTheme.RoundedRect(rect, 11);

        if (IsPrimary)
        {
            Color c1, c2;
            if (!Enabled)
            {
                c1 = Color.FromArgb(75, 110, 45, 62);
                c2 = Color.FromArgb(75, 95, 55, 48);
            }
            else if (_pressed)
            {
                c1 = Color.FromArgb(230, 220, 38, 72);
                c2 = Color.FromArgb(230, 220, 105, 34);
            }
            else if (_hovered)
            {
                c1 = Color.FromArgb(255, 255, 72, 106);
                c2 = Color.FromArgb(255, 255, 152, 64);
            }
            else
            {
                c1 = Color.FromArgb(240, 255, 52, 88);
                c2 = Color.FromArgb(240, 255, 126, 48);
            }

            using (var fill = new LinearGradientBrush(
                new Point(rect.Left, rect.Top),
                new Point(rect.Right, rect.Bottom),
                c1,
                c2))
            {
                g.FillPath(fill, path);
            }

            var glossRect = new Rectangle(rect.X + 2, rect.Y + 1, Math.Max(2, rect.Width - 4), Math.Max(2, rect.Height / 2));
            using (var glossPath = GlassTheme.RoundedRect(glossRect, 9))
            using (var glossBrush = new LinearGradientBrush(
                new Point(0, glossRect.Top),
                new Point(0, glossRect.Bottom + 1),
                Color.FromArgb(Enabled ? 72 : 20, 255, 255, 255),
                Color.FromArgb(4, 255, 255, 255)))
            {
                g.FillPath(glossBrush, glossPath);
            }

            using var borderPen = new Pen(
                Enabled ? Color.FromArgb(190, 255, 175, 185) : Color.FromArgb(45, 255, 255, 255),
                1.2f);
            g.DrawPath(borderPen, path);
        }
        else
        {
            int alpha = !Enabled ? 55 : _pressed ? 185 : _hovered ? 160 : 120;
            using (var fill = new SolidBrush(Color.FromArgb(alpha, 34, 42, 62)))
            {
                g.FillPath(fill, path);
            }

            using (var sheen = new LinearGradientBrush(
                new Point(rect.Left, rect.Top),
                new Point(rect.Left, rect.Bottom),
                Color.FromArgb(Enabled ? 28 : 10, 255, 255, 255),
                Color.FromArgb(2, 255, 255, 255)))
            {
                g.FillPath(sheen, path);
            }

            using var borderPen = new Pen(
                !Enabled
                    ? Color.FromArgb(30, 255, 255, 255)
                    : _hovered
                        ? Color.FromArgb(115, 255, 255, 255)
                        : Color.FromArgb(62, 255, 255, 255),
                1.1f);
            g.DrawPath(borderPen, path);
        }

        var fg = !Enabled
            ? Color.FromArgb(110, 150, 162, 180)
            : Color.FromArgb(248, 250, 255);

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            rect,
            fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Dark frosted-glass ComboBox with custom borderless painting.</summary>
internal sealed class GlassComboBox : ComboBox
{
    private const int WM_PAINT = 0x000F;

    public GlassComboBox()
    {
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 22;
        BackColor = Color.FromArgb(30, 37, 54);
        ForeColor = GlassTheme.Ink;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        bool inComboBoxEdit = (e.State & DrawItemState.ComboBoxEdit) == DrawItemState.ComboBoxEdit;

        var bg = selected && !inComboBoxEdit
            ? Color.FromArgb(255, 52, 88)
            : Color.FromArgb(28, 34, 50);

        using (var brush = new SolidBrush(bg))
        {
            g.FillRectangle(brush, e.Bounds);
        }

        var text = Items[e.Index]?.ToString() ?? string.Empty;
        var fg = !Enabled ? GlassTheme.Muted : GlassTheme.Ink;
        var textBounds = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height);
        TextRenderer.DrawText(
            g,
            text,
            Font,
            textBounds,
            fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bg = Color.FromArgb(28, 34, 50);

            var btnRect = new Rectangle(Width - 22, 1, 21, Height - 2);
            using (var btnBrush = new SolidBrush(Color.FromArgb(38, 46, 66)))
            {
                g.FillRectangle(btnBrush, btnRect);
            }

            int cx = btnRect.X + btnRect.Width / 2;
            int cy = btnRect.Y + btnRect.Height / 2;
            using (var arrowPen = new Pen(Enabled ? GlassTheme.Ink : GlassTheme.Muted, 1.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            })
            {
                g.DrawLines(arrowPen, new[]
                {
                    new Point(cx - 4, cy - 2),
                    new Point(cx, cy + 2),
                    new Point(cx + 4, cy - 2),
                });
            }

            var inner = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var innerPen = new Pen(bg, 1.5f))
            {
                g.DrawRectangle(innerPen, inner);
            }

            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using var borderPen = new Pen(
                Enabled ? Color.FromArgb(95, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255),
                1.2f);
            g.DrawRectangle(borderPen, outer);
        }
    }
}
