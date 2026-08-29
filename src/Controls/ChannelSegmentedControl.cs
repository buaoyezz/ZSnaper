using System.Diagnostics;
using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

/// <summary>
/// 版本发布通道分段选择器 (Release / Beta / Alpha)
/// </summary>
public class ChannelSegmentedControl : Control
{
    private float _sliderIndex = 2f; // 0 = 正式(Release), 1 = 公测(Beta), 2 = 内测(Alpha)
    private float _startPos = 2f;
    private float _targetPos = 2f;
    private readonly System.Windows.Forms.Timer _animTimer;
    private readonly Stopwatch _stopwatch = new();
    private int _durationMs = 200;

    public ChannelSegmentedControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Size = new Size(185, 28);
        Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular);

        _sliderIndex = ChannelToIndex(ConfigService.Current.UpdateChannel);
        _startPos = _sliderIndex;
        _targetPos = _sliderIndex;

        _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _animTimer.Tick += OnAnimTick;

        ThemeManager.ThemeChanged += Invalidate;
        ConfigService.ConfigChanged += OnConfigChanged;
    }

    private static int ChannelToIndex(string channel)
    {
        return channel.ToLowerInvariant() switch
        {
            "release" or "stable" => 0,
            "beta" => 1,
            _ => 2 // 默认 Alpha
        };
    }

    private static string IndexToChannel(int index)
    {
        return index switch
        {
            0 => "Release",
            1 => "Beta",
            _ => "Alpha"
        };
    }

    private void OnConfigChanged()
    {
        int targetIndex = ChannelToIndex(ConfigService.Current.UpdateChannel);
        if (Math.Abs(_targetPos - targetIndex) > 0.01f)
        {
            StartAnimation(targetIndex);
        }
    }

    private void StartAnimation(float target)
    {
        _startPos = _sliderIndex;
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
        _sliderIndex = _startPos + (_targetPos - _startPos) * ease;

        Invalidate();

        if (t >= 1f)
        {
            _sliderIndex = _targetPos;
            _animTimer.Stop();
            _stopwatch.Stop();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left)
        {
            float segWidth = Width / 3f;
            int selected = Math.Clamp((int)(e.X / segWidth), 0, 2);
            string channel = IndexToChannel(selected);

            if (!string.Equals(ConfigService.Current.UpdateChannel, channel, StringComparison.OrdinalIgnoreCase))
            {
                ConfigService.Current.UpdateChannel = channel;
                ConfigService.Save();
                StartAnimation(selected);
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

        float segWidth = Width / 3f;

        // 平滑滑动的胶囊滑块
        float startX = 2f;
        float currentX = startX + _sliderIndex * (segWidth - 1.3f);

        var activeRect = new RectangleF(currentX, 2f, segWidth - 3f, Height - 5f);
        using (var activePath = GraphicsHelper.GetRoundedRectangle(Rectangle.Round(activeRect), (Height - 5) / 2))
        {
            using var activeBrush = new SolidBrush(palette.AccentColor);
            g.FillPath(activeBrush, activePath);
        }

        // 绘制三档标签："Release" / "Beta" / "Alpha"
        string[] labels = ["正式", "公测", "内测"];
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (int i = 0; i < 3; i++)
        {
            var textRect = new RectangleF(i * segWidth, 0, segWidth, Height);
            bool isActive = Math.Abs(_sliderIndex - i) < 0.5f;

            using var textBrush = new SolidBrush(isActive ? palette.AccentForeground : palette.TextMuted);
            g.DrawString(labels[i], Font, textBrush, textRect, sf);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= Invalidate;
            ConfigService.ConfigChanged -= OnConfigChanged;
            _animTimer.Stop();
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
