using System.Diagnostics;
using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public class ThemeSegmentedControl : Control
{
    private float _sliderPosition = 0f; // 0.0 = 浅色 (左), 1.0 = 深色 (右)
    private float _startPos = 0f;
    private float _targetPos = 0f;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly Stopwatch _stopwatch = new();
    private int _durationMs = 200;

    public ThemeSegmentedControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(130, 28);
        Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);

        _sliderPosition = ThemeManager.CurrentMode == ThemeMode.Light ? 0f : 1f;

        _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animTimer.Tick += OnAnimTick;

        ThemeManager.ThemeChanged += OnGlobalThemeChanged;
    }

    private void OnGlobalThemeChanged()
    {
        float target = ThemeManager.CurrentMode == ThemeMode.Light ? 0f : 1f;
        if (Math.Abs(_targetPos - target) > 0.01f)
        {
            StartAnimation(target);
        }
    }

    private void StartAnimation(float target)
    {
        _startPos = _sliderPosition;
        _targetPos = target;
        _durationMs = Math.Max(40, ConfigService.GetAnimationDuration(180));
        _stopwatch.Restart();
        _animTimer.Start();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        float elapsed = _stopwatch.ElapsedMilliseconds;
        float t = Math.Clamp(elapsed / _durationMs, 0f, 1f);

        float ease = 1f - MathF.Pow(1f - t, 3f);
        _sliderPosition = _startPos + (_targetPos - _startPos) * ease;

        Invalidate();

        if (t >= 1f)
        {
            _sliderPosition = _targetPos;
            _animTimer.Stop();
            _stopwatch.Stop();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            if (e.X < Width / 2 && ThemeManager.CurrentMode != ThemeMode.Light)
            {
                ThemeManager.CurrentMode = ThemeMode.Light;
            }
            else if (e.X >= Width / 2 && ThemeManager.CurrentMode != ThemeMode.Dark)
            {
                ThemeManager.CurrentMode = ThemeMode.Dark;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var palette = ThemeManager.Palette;
        var isDark = palette.Mode == ThemeMode.Dark;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        // 外层背景胶囊
        using (var bgPath = GraphicsHelper.GetRoundedRectangle(rect, Height / 2))
        {
            Color outerBg = isDark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(20, 0, 0, 0);
            using var bgBrush = new SolidBrush(outerBg);
            g.FillPath(bgBrush, bgPath);
        }

        int halfWidth = Width / 2;

        // 平滑滑动的胶囊滑块
        float startX = 2f;
        float endX = halfWidth + 1f;
        float currentX = startX + (endX - startX) * _sliderPosition;

        var activeRect = new RectangleF(currentX, 2f, halfWidth - 3f, Height - 5f);
        using (var activePath = GraphicsHelper.GetRoundedRectangle(Rectangle.Round(activeRect), (Height - 5) / 2))
        {
            using var activeBrush = new SolidBrush(palette.AccentColor);
            g.FillPath(activeBrush, activePath);
        }

        // 绘制文字 "浅色" & "深色"
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var lightRect = new Rectangle(0, 0, halfWidth, Height);
        var darkRect = new Rectangle(halfWidth, 0, halfWidth, Height);

        bool isLight = _sliderPosition < 0.5f;

        using (var lightTextBrush = new SolidBrush(isLight ? palette.AccentForeground : palette.TextMuted))
        {
            g.DrawString("浅色", Font, lightTextBrush, lightRect, sf);
        }

        using (var darkTextBrush = new SolidBrush(!isLight ? palette.AccentForeground : palette.TextMuted))
        {
            g.DrawString("深色", Font, darkTextBrush, darkRect, sf);
        }
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
