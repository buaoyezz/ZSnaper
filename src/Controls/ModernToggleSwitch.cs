using System.Diagnostics;
using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public class ModernToggleSwitch : Control
{
    private bool _checked = true;
    private bool _isHovered;

    // 动画状态 (0.0f = 关闭, 1.0f = 开启)
    private float _animationProgress = 1.0f;
    private float _startProgress = 1.0f;
    private float _targetProgress = 1.0f;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly Stopwatch _stopwatch = new();
    private int _durationMs = 200;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked != value)
            {
                _checked = value;
                StartAnimation(value ? 1.0f : 0.0f);
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? CheckedChanged;

    public ModernToggleSwitch()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(40, 22);

        _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animTimer.Tick += OnAnimTick;
    }

    private void StartAnimation(float target)
    {
        _startProgress = _animationProgress;
        _targetProgress = target;
        _durationMs = Math.Max(40, ConfigService.GetAnimationDuration(180));
        _stopwatch.Restart();
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        float elapsed = _stopwatch.ElapsedMilliseconds;
        float t = Math.Clamp(elapsed / _durationMs, 0f, 1f);

        // 丝滑 EaseOutCubic 缓动曲线
        float ease = 1f - MathF.Pow(1f - t, 3f);
        _animationProgress = _startProgress + (_targetProgress - _startProgress) * ease;

        Invalidate();

        if (t >= 1f)
        {
            _animationProgress = _targetProgress;
            _animTimer.Stop();
            _stopwatch.Stop();
        }
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

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            Checked = !Checked;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var palette = ThemeManager.Palette;
        var isDark = palette.Mode == ThemeMode.Dark;

        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = GraphicsHelper.GetRoundedRectangle(rect, Height / 2);

        // 轨道底色（插值过渡与 Hover 微光反馈）
        Color offColor = isDark ? Color.FromArgb(_isHovered ? 65 : 50, 255, 255, 255) : Color.FromArgb(_isHovered ? 45 : 35, 0, 0, 0);
        Color onColor = _isHovered ? Color.FromArgb(240, palette.AccentColor) : palette.AccentColor;
        Color trackColor = BlendColors(offColor, onColor, _animationProgress);

        using (var brush = new SolidBrush(trackColor))
        {
            g.FillPath(brush, path);
        }

        // 滑块圆球平滑滑动 (Thumb)
        int thumbSize = Height - 6;
        float startX = 3f;
        float endX = Width - thumbSize - 3f;
        float currentX = startX + (endX - startX) * _animationProgress;

        var thumbRect = new RectangleF(currentX, 3f, thumbSize, thumbSize);
        Color thumbColor = BlendColors(Color.White, palette.AccentForeground, _animationProgress);
        using (var thumbBrush = new SolidBrush(thumbColor))
        {
            g.FillEllipse(thumbBrush, thumbRect);
        }
    }

    private static Color BlendColors(Color c1, Color c2, float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        int a = (int)(c1.A + (c2.A - c1.A) * factor);
        int r = (int)(c1.R + (c2.R - c1.R) * factor);
        int g = (int)(c1.G + (c2.G - c1.G) * factor);
        int b = (int)(c1.B + (c2.B - c1.B) * factor);
        return Color.FromArgb(a, r, g, b);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
