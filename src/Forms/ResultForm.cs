using System.Drawing.Drawing2D;
using ZSnaper.Controls;
using ZSnaper.Helpers;
using ZSnaper.Interop;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Forms;

public class ResultForm : Form
{
    private readonly ModernTextEditor _text;
    private readonly ModernButton _copyBtn;
    private readonly ModernButton _cleanBtn;
    private readonly ModernButton _closeBtn;
    private readonly Label _titleLabel;
    private readonly Label _countLabel;
    private readonly System.Windows.Forms.Timer _copyFeedbackTimer;

    public ResultForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(460, 310);
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _titleLabel = new Label
        {
            Text = "OCR 识别结果",
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            Location = new Point(38, 12),
            AutoSize = true
        };

        _countLabel = new Label
        {
            Text = "0 字符",
            Font = new Font("Consolas", 8.5f),
            Location = new Point(150, 14),
            AutoSize = true
        };

        _closeBtn = new ModernButton
        {
            Text = string.Empty,
            Icon = LucideIcon.X,
            IconSize = 15,
            IsPrimary = false,
            CornerRadius = 6,
            Size = new Size(28, 26),
            Location = new Point(Width - 38, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        _closeBtn.Click += (_, _) => Hide();

        _text = new ModernTextEditor
        {
            Location = new Point(14, 42),
            Size = new Size(Width - 28, Height - 95),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            PlaceholderText = "未识别到文字"
        };
        _text.TextChanged += (_, _) => _countLabel.Text = $"{_text.Text.Length} 字符";

        var bottomBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = Color.Transparent,
            Padding = new Padding(14, 6, 14, 6)
        };

        _copyBtn = new ModernButton
        {
            Text = "复制文本",
            Icon = LucideIcon.Copy,
            IsPrimary = true,
            CornerRadius = 8,
            Size = new Size(112, 32),
            Location = new Point(14, 6)
        };
        _copyFeedbackTimer = new System.Windows.Forms.Timer { Interval = 1200 };
        _copyFeedbackTimer.Tick += (_, _) =>
        {
            _copyFeedbackTimer.Stop();
            if (!IsDisposed) _copyBtn.Text = "复制文本";
        };
        _copyBtn.Click += (_, _) =>
        {
            if (_text.TextLength > 0)
            {
                bool copied = CaptureService.TryCopyTextToClipboard(_text.Text);
                _copyBtn.Text = copied ? "已复制" : "复制失败";
                _copyFeedbackTimer.Stop();
                _copyFeedbackTimer.Start();
            }
        };

        _cleanBtn = new ModernButton
        {
            Text = "整理文本",
            Icon = LucideIcon.Sparkles,
            IsPrimary = false,
            CornerRadius = 8,
            Size = new Size(112, 32),
            Location = new Point(134, 6)
        };
        _cleanBtn.Click += (_, _) =>
        {
            if (_text.TextLength > 0)
            {
                _text.Text = OcrTextFormatter.Clean(_text.Text);
            }
        };

        bottomBar.Controls.Add(_copyBtn);
        bottomBar.Controls.Add(_cleanBtn);

        Controls.Add(_titleLabel);
        Controls.Add(_countLabel);
        Controls.Add(_closeBtn);
        Controls.Add(_text);
        Controls.Add(bottomBar);

        ThemeManager.ThemeChanged += ApplyTheme;
        Disposed += (_, _) => ThemeManager.ThemeChanged -= ApplyTheme;
        ApplyTheme();

        Load += (_, _) => NativeMethods.EnableWindowDropShadowAndRoundCorners(Handle, ThemeManager.CurrentMode == ThemeMode.Dark);
    }

    private void ApplyTheme()
    {
        var palette = ThemeManager.Palette;
        // 顶层 Form 不支持半透明 BackColor；保留 CardBg 的 RGB，使用不透明基底。
        BackColor = Color.FromArgb(255, palette.CardBg);
        _titleLabel.ForeColor = palette.TextPrimary;
        _countLabel.ForeColor = palette.TextMuted;
        _text.ApplyTheme();
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y < 40)
        {
            NativeMethods.DragWindow(Handle);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var palette = ThemeManager.Palette;

        // 绘制微边框
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = GraphicsHelper.GetRoundedRectangle(rect, 12);
        using var pen = new Pen(palette.CardBorder, 1f);
        g.DrawPath(pen, path);
        LucideRenderer.Draw(g, LucideIcon.FileText, 14, 12, 17, palette.TextSecondary, 1.8f);
    }

    public void ShowResult(string text, Point near)
    {
        var vs = SystemInformation.VirtualScreen;
        Location = new Point(
            Math.Clamp(near.X, vs.Left + 10, vs.Right - Width - 10),
            Math.Clamp(near.Y, vs.Top + 10, vs.Bottom - Height - 10));
        _text.Text = text;
        Show();
        Activate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _copyFeedbackTimer.Stop();
            _copyFeedbackTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
