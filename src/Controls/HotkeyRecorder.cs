using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class HotkeyRecorder : Control
{
    private HotkeyGesture _gesture;
    private bool _isHovered;
    private bool _isRecording;

    public Func<HotkeyGesture, HotkeyChangeResult>? TryCommit { get; set; }

    public event Action<HotkeyChangeResult>? Feedback;

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            Invalidate();
        }
    }

    public HotkeyRecorder()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.Selectable,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(142, 34);
        Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = "修改快捷键";
    }

    protected override bool IsInputKey(Keys keyData) =>
        _isRecording || base.IsInputKey(keyData);

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        BeginRecording();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_isRecording)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                BeginRecording();
                e.SuppressKeyPress = true;
            }
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            CancelRecording();
            Feedback?.Invoke(new HotkeyChangeResult(false, "已取消修改"));
            return;
        }

        if (HotkeyGesture.IsModifierKey(e.KeyCode))
        {
            Invalidate();
            return;
        }

        HotkeyGesture proposed = HotkeyGesture.FromKeyEvent(e);
        if (!proposed.IsValid)
        {
            Feedback?.Invoke(new HotkeyChangeResult(false, "请使用 Ctrl、Alt 或 Shift 组合键"));
            return;
        }

        HotkeyChangeResult result = TryCommit?.Invoke(proposed)
            ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪");
        if (result.Success)
        {
            _gesture = proposed;
        }

        _isRecording = false;
        Feedback?.Invoke(result);
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        CancelRecording();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ThemePalette palette = ThemeManager.Palette;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(rect, 8);
        Color fillColor = _isRecording
            ? Color.FromArgb(palette.Mode == ThemeMode.Dark ? 32 : 18, palette.AccentColor)
            : _isHovered
                ? palette.NavItemHover
                : palette.CardBg;
        using (var fillBrush = new SolidBrush(fillColor))
        {
            graphics.FillPath(fillBrush, path);
        }

        Color borderColor = _isRecording || Focused ? palette.AccentColor : palette.CardBorder;
        using (var borderPen = new Pen(borderColor, _isRecording ? 1.4f : 1f))
        {
            graphics.DrawPath(borderPen, path);
        }

        Color contentColor = _isRecording ? palette.AccentColor : palette.TextPrimary;
        LucideRenderer.Draw(graphics, LucideIcon.Keyboard, 11, 9, 16, contentColor, 1.8f);
        string displayText = _isRecording ? "请按组合键…" : _gesture.DisplayText;
        TextRenderer.DrawText(
            graphics,
            displayText,
            Font,
            new Rectangle(36, 0, Math.Max(1, Width - 44), Height),
            contentColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private void BeginRecording()
    {
        Focus();
        _isRecording = true;
        Feedback?.Invoke(new HotkeyChangeResult(true, "请按下新的组合键，Esc 取消"));
        Invalidate();
    }

    private void CancelRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        _isRecording = false;
        Invalidate();
    }
}
