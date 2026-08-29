using System.Drawing.Drawing2D;

namespace ZSnaper.FullInstaller;

internal static class InstallerPalette
{
    public static readonly Color Window = Color.FromArgb(20, 24, 33);
    public static readonly Color WindowRaised = Color.FromArgb(25, 30, 42);
    public static readonly Color Surface = Color.FromArgb(247, 249, 252);
    public static readonly Color Card = Color.White;
    public static readonly Color Text = Color.FromArgb(33, 39, 52);
    public static readonly Color MutedText = Color.FromArgb(104, 112, 128);
    public static readonly Color Accent = Color.FromArgb(61, 126, 255);
    public static readonly Color AccentHover = Color.FromArgb(78, 139, 255);
    public static readonly Color AccentPressed = Color.FromArgb(45, 107, 232);
    public static readonly Color Border = Color.FromArgb(222, 227, 235);
    public static readonly Color Success = Color.FromArgb(40, 177, 123);

    public static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        int diameter = Math.Max(1, radius * 2);
        Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
        GraphicsPath path = new();
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        Size = new Size(140, 48);
    }

    public bool Accent { get; init; }

    public int CornerRadius { get; init; } = 10;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = true;
            Invalidate();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_pressed && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = InstallerPalette.RoundedRectangle(bounds, CornerRadius);

        Color fill;
        Color foreground;
        Color border;
        if (!Enabled)
        {
            fill = Accent ? Color.FromArgb(177, 198, 234) : Color.FromArgb(236, 239, 244);
            foreground = Accent ? Color.FromArgb(244, 247, 252) : Color.FromArgb(159, 166, 179);
            border = fill;
        }
        else if (Accent)
        {
            fill = _pressed ? InstallerPalette.AccentPressed : _hovered ? InstallerPalette.AccentHover : InstallerPalette.Accent;
            foreground = Color.White;
            border = fill;
        }
        else
        {
            fill = _pressed
                ? Color.FromArgb(224, 229, 237)
                : _hovered ? Color.FromArgb(241, 244, 248) : Color.White;
            foreground = InstallerPalette.Text;
            border = InstallerPalette.Border;
        }

        using SolidBrush brush = new(fill);
        e.Graphics.FillPath(brush, path);
        using Pen pen = new(border);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            bounds,
            foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            Rectangle focusBounds = Rectangle.Inflate(bounds, -4, -4);
            ControlPaint.DrawFocusRectangle(e.Graphics, focusBounds, foreground, fill);
        }
    }
}

internal enum ChromeGlyph
{
    Minimize,
    Close
}

internal sealed class ChromeButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public ChromeButton(ChromeGlyph glyph)
    {
        Glyph = glyph;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        TabStop = false;
        Size = new Size(48, 52);
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = glyph == ChromeGlyph.Close ? "关闭安装器" : "最小化安装器";
    }

    public ChromeGlyph Glyph { get; }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color background = Color.Transparent;
        if (_hovered)
        {
            background = Glyph == ChromeGlyph.Close
                ? Color.FromArgb(_pressed ? 184 : 211, 56, 68)
                : Color.FromArgb(_pressed ? 48 : 58, 66, 82);
        }

        e.Graphics.Clear(background == Color.Transparent ? InstallerPalette.Window : background);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen pen = new(Color.FromArgb(216, 222, 234), 1.35F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (Glyph == ChromeGlyph.Minimize)
        {
            e.Graphics.DrawLine(pen, 19, 27, 29, 27);
        }
        else
        {
            e.Graphics.DrawLine(pen, 20, 21, 28, 29);
            e.Graphics.DrawLine(pen, 28, 21, 20, 29);
        }
    }
}

internal sealed class ModernCheckBox : Control
{
    private bool _checked;
    private bool _hovered;

    public ModernCheckBox()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        Height = 30;
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            AccessibleName = Text;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled)
        {
            Checked = !Checked;
        }

        base.OnClick(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            Checked = !Checked;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle box = new(1, (Height - 20) / 2, 20, 20);
        using GraphicsPath path = InstallerPalette.RoundedRectangle(box, 5);
        Color fill = Checked ? InstallerPalette.Accent : _hovered ? Color.FromArgb(244, 247, 252) : Color.White;
        Color border = Checked ? InstallerPalette.Accent : _hovered ? Color.FromArgb(157, 181, 224) : InstallerPalette.Border;
        using SolidBrush brush = new(fill);
        using Pen pen = new(border, 1.2F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);

        if (Checked)
        {
            using Pen checkPen = new(Color.White, 2F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.DrawLines(checkPen, new[]
            {
                new Point(6, box.Top + 10),
                new Point(10, box.Top + 14),
                new Point(16, box.Top + 7)
            });
        }

        Rectangle textBounds = new(32, 0, Math.Max(0, Width - 32), Height);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            Enabled ? InstallerPalette.Text : Color.FromArgb(155, 162, 175),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
    }
}

internal sealed class SlimProgressBar : Control
{
    private int _value;

    public SlimProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = 4;
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath backgroundPath = InstallerPalette.RoundedRectangle(bounds, Math.Max(1, Height / 2));
        using SolidBrush backgroundBrush = new(Color.FromArgb(228, 233, 241));
        e.Graphics.FillPath(backgroundBrush, backgroundPath);
        if (Value <= 0)
        {
            return;
        }

        int progressWidth = Math.Max(Height, (int)Math.Round(Width * Value / 100D));
        Rectangle progressBounds = new(0, 0, Math.Min(Width - 1, progressWidth), Height - 1);
        using GraphicsPath progressPath = InstallerPalette.RoundedRectangle(progressBounds, Math.Max(1, Height / 2));
        using SolidBrush progressBrush = new(InstallerPalette.Accent);
        e.Graphics.FillPath(progressBrush, progressPath);
    }
}

internal sealed class WelcomeCanvas : Panel
{
    private bool _useMicaBackdrop;

    public WelcomeCanvas()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
    }

    public bool UseMicaBackdrop
    {
        get => _useMicaBackdrop;
        set
        {
            if (_useMicaBackdrop == value)
            {
                return;
            }

            _useMicaBackdrop = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (UseMicaBackdrop)
        {
            e.Graphics.Clear(Color.Transparent);
        }
        else
        {
            e.Graphics.Clear(Color.FromArgb(29, 33, 43));
        }

        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        int horizontalInset = Math.Max(24, Width / 32);
        int verticalInset = Math.Max(20, Height / 26);
        Rectangle podBounds = new(
            horizontalInset,
            verticalInset,
            Math.Max(1, Width - horizontalInset * 2),
            Math.Max(1, Height - verticalInset * 2));

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath podPath = InstallerPalette.RoundedRectangle(podBounds, 28);
        using SolidBrush shadowBrush = new(Color.FromArgb(75, 0, 0, 0));
        e.Graphics.TranslateTransform(0, 5);
        e.Graphics.FillPath(shadowBrush, podPath);
        e.Graphics.ResetTransform();

        using SolidBrush podBrush = new(Color.FromArgb(246, 4, 7, 12));
        using Pen podBorder = new(Color.FromArgb(70, 84, 99, 122), 1F);
        e.Graphics.FillPath(podBrush, podPath);
        e.Graphics.DrawPath(podBorder, podPath);
    }
}

internal sealed class NextButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public NextButton()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        Font = new Font("Segoe UI Semibold", 10.5F, FontStyle.Bold);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && Enabled)
        {
            _pressed = true;
            Focus();
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Enabled && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = true;
            Invalidate();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_pressed && e.KeyCode is Keys.Enter or Keys.Space)
        {
            _pressed = false;
            Invalidate();
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = InstallerPalette.RoundedRectangle(bounds, 13);
        Color fill = !Enabled
            ? Color.FromArgb(60, 84, 99, 112)
            : _pressed
                ? Color.FromArgb(23, 78, 101)
                : _hovered ? Color.FromArgb(41, 122, 155) : Color.FromArgb(29, 101, 132);

        using SolidBrush brush = new(fill);
        using Pen border = new(Color.FromArgb(104, 105, 173, 199), 1F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(border, path);

        Rectangle textBounds = new(22, 0, Math.Max(0, Width - 64), Height);
        TextRenderer.DrawText(
            e.Graphics,
            "NEXT",
            Font,
            textBounds,
            Enabled ? Color.White : Color.FromArgb(160, 190, 201),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        using Pen arrowPen = new(Enabled ? Color.White : Color.FromArgb(160, 190, 201), 1.7F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        int arrowX = Width - 31;
        int arrowY = Height / 2;
        e.Graphics.DrawLine(arrowPen, arrowX - 8, arrowY, arrowX + 5, arrowY);
        e.Graphics.DrawLine(arrowPen, arrowX, arrowY - 5, arrowX + 5, arrowY);
        e.Graphics.DrawLine(arrowPen, arrowX, arrowY + 5, arrowX + 5, arrowY);

        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4));
        }
    }
}

internal sealed class GradientPanel : Panel
{
    public Color StartColor { get; init; }
    public Color EndColor { get; init; }

    public GradientPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using LinearGradientBrush brush = new(ClientRectangle, StartColor, EndColor, 32F);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath glowPath = new();
        glowPath.AddEllipse(Width - 380, -190, 520, 520);
        using PathGradientBrush glow = new(glowPath)
        {
            CenterColor = Color.FromArgb(58, 66, 131, 255),
            SurroundColors = [Color.FromArgb(0, 66, 131, 255)]
        };
        e.Graphics.FillPath(glow, glowPath);
    }
}
