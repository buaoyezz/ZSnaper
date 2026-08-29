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
    private bool _isForceRecording;

    public Func<HotkeyGesture, bool, HotkeyChangeResult>? TryCommit { get; set; }
    public Func<bool, HotkeyChangeResult>? BeginRecordingRequest { get; set; }
    public Func<HotkeyChangeResult>? EndRecordingRequest { get; set; }

    public event Action<HotkeyChangeResult>? Feedback;
    public event Action? RecordingStateChanged;

    public HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            _gesture = value;
            Invalidate();
        }
    }

    public bool IsForceRecording => _isForceRecording;

    public bool IsRecording => _isRecording;

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
        if (_isRecording)
        {
            CancelExternalRecording();
            return;
        }

        StartRecording(forceBinding: false);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_isRecording)
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                StartRecording(forceBinding: false);
                e.SuppressKeyPress = true;
            }
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            HotkeyChangeResult endResult = CancelRecording();
            Feedback?.Invoke(endResult.Success
                ? new HotkeyChangeResult(false, "已取消修改")
                : endResult);
            return;
        }

        if (HotkeyGesture.IsModifierKey(e.KeyCode))
        {
            Invalidate();
            return;
        }

        HotkeyGesture proposed = HotkeyGesture.FromKeyEvent(e);
        bool gestureIsValid = _isForceRecording
            ? proposed.IsValidForForceBinding
            : proposed.IsValid;
        if (!gestureIsValid)
        {
            Feedback?.Invoke(new HotkeyChangeResult(
                false,
                _isForceRecording
                    ? "强力绑定请按一个非修饰键，Esc 用于取消"
                    : "请按下 PrintScreen，或使用 Ctrl、Alt、Shift 组合键"));
            return;
        }

        CommitGesture(proposed);
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
        HotkeyChangeResult endResult = CancelRecording();
        if (!endResult.Success)
        {
            Feedback?.Invoke(endResult);
        }
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
        string displayText = _isRecording
            ? (_isForceRecording ? "请按强力按键…" : "请按按键…")
            : _gesture.DisplayText;
        TextRenderer.DrawText(
            graphics,
            displayText,
            Font,
            new Rectangle(36, 0, Math.Max(1, Width - 44), Height),
            contentColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    public void StartRecording(bool forceBinding)
    {
        if (_isRecording)
        {
            if (_isForceRecording == forceBinding)
            {
                return;
            }

            HotkeyChangeResult endResult = FinishRecording();
            if (!endResult.Success)
            {
                Feedback?.Invoke(endResult);
                return;
            }
        }

        HotkeyChangeResult beginResult = BeginRecordingRequest?.Invoke(forceBinding)
            ?? new HotkeyChangeResult(true, string.Empty);
        if (!beginResult.Success)
        {
            Feedback?.Invoke(beginResult);
            return;
        }

        Focus();
        _isRecording = true;
        _isForceRecording = forceBinding;
        RecordingStateChanged?.Invoke();
        Feedback?.Invoke(new HotkeyChangeResult(
            true,
            forceBinding ? "请按下要强力绑定的按键或组合键，Esc 取消" : "请按下新的按键或组合键，Esc 取消"));
        Invalidate();
    }

    public void CommitRecordedGesture(HotkeyGesture gesture)
    {
        if (_isRecording)
        {
            CommitGesture(gesture);
        }
    }

    public void CancelExternalRecording()
    {
        if (!_isRecording)
        {
            return;
        }

        HotkeyChangeResult endResult = FinishRecording();
        Feedback?.Invoke(endResult.Success
            ? new HotkeyChangeResult(false, "已取消修改")
            : endResult);
    }

    private HotkeyChangeResult CancelRecording()
    {
        if (!_isRecording)
        {
            return new HotkeyChangeResult(true, string.Empty);
        }

        HotkeyChangeResult result = FinishRecording();
        Invalidate();
        return result;
    }

    private void CommitGesture(HotkeyGesture proposed)
    {
        HotkeyChangeResult result = TryCommit?.Invoke(proposed, _isForceRecording)
            ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪");
        if (result.Success)
        {
            _gesture = proposed;
        }

        HotkeyChangeResult endResult = FinishRecording();
        if (result.Success && !endResult.Success)
        {
            result = endResult;
        }

        Feedback?.Invoke(result);
        Invalidate();
    }

    private HotkeyChangeResult FinishRecording()
    {
        _isRecording = false;
        _isForceRecording = false;
        RecordingStateChanged?.Invoke();
        HotkeyChangeResult result = EndRecordingRequest?.Invoke()
            ?? new HotkeyChangeResult(true, string.Empty);
        Invalidate();
        return result;
    }
}
