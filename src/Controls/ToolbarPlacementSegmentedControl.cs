using System.Diagnostics;
using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class ToolbarPlacementSegmentedControl : Control
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private readonly Stopwatch _stopwatch = new();
    private float _sliderIndex;
    private float _startIndex;
    private float _targetIndex;
    private int _durationMs;

    public ToolbarPlacementSegmentedControl()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(204, 28);
        Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular);

        _sliderIndex = Math.Clamp((int)ConfigService.Current.ToolbarPlacement, 0, 3);
        _startIndex = _sliderIndex;
        _targetIndex = _sliderIndex;
        _animationTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animationTimer.Tick += OnAnimationTick;
        ThemeManager.ThemeChanged += OnThemeChanged;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button != MouseButtons.Left) return;

        float segmentWidth = Width / 4f;
        int selected = Math.Clamp((int)(e.X / segmentWidth), 0, 3);
        var placement = (ToolbarPlacementMode)selected;
        if (ConfigService.Current.ToolbarPlacement == placement) return;

        ConfigService.Current.ToolbarPlacement = placement;
        ConfigService.Save();
        StartAnimation(selected);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        ThemePalette palette = ThemeManager.Palette;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using (GraphicsPath backgroundPath = GraphicsHelper.GetRoundedRectangle(bounds, Height / 2))
        using (var background = new SolidBrush(
                   palette.Mode == ThemeMode.Dark
                       ? Color.FromArgb(40, 255, 255, 255)
                       : Color.FromArgb(20, 0, 0, 0)))
        {
            graphics.FillPath(background, backgroundPath);
        }

        float segmentWidth = Width / 4f;
        float activeX = 2f + _sliderIndex * (segmentWidth - 0.7f);
        var activeBounds = new RectangleF(activeX, 2f, segmentWidth - 3f, Height - 5f);
        using (GraphicsPath activePath = GraphicsHelper.GetRoundedRectangle(
                   Rectangle.Round(activeBounds),
                   (Height - 5) / 2))
        using (var activeBrush = new SolidBrush(palette.AccentColor))
        {
            graphics.FillPath(activeBrush, activePath);
        }

        string[] labels = ["左", "中", "右", "Auto"];
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        for (int index = 0; index < labels.Length; index++)
        {
            bool active = Math.Abs(_sliderIndex - index) < 0.5f;
            using var brush = new SolidBrush(active ? palette.AccentForeground : palette.TextMuted);
            graphics.DrawString(
                labels[index],
                Font,
                brush,
                new RectangleF(index * segmentWidth, 0, segmentWidth, Height),
                format);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _animationTimer.Stop();
            _animationTimer.Dispose();
            _stopwatch.Stop();
        }
        base.Dispose(disposing);
    }

    private void StartAnimation(float target)
    {
        _startIndex = _sliderIndex;
        _targetIndex = target;
        _durationMs = Math.Max(40, ConfigService.GetAnimationDuration(170));
        _stopwatch.Restart();
        _animationTimer.Start();
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        float progress = Math.Clamp(_stopwatch.ElapsedMilliseconds / (float)_durationMs, 0f, 1f);
        float eased = 1f - MathF.Pow(1f - progress, 3f);
        _sliderIndex = _startIndex + (_targetIndex - _startIndex) * eased;
        Invalidate();

        if (progress < 1f) return;
        _sliderIndex = _targetIndex;
        _animationTimer.Stop();
        _stopwatch.Stop();
    }

    private void OnThemeChanged() => Invalidate();
}
