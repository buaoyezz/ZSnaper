using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ZSnaper.Controls;
using ZSnaper.Helpers;
using ZSnaper.Interop;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Forms;

public sealed class PinnedImageForm : Form
{
    private const int MinimumLongEdge = 96;
    private const int MaximumLongEdge = 4096;

    private readonly Bitmap _image;
    private readonly ModernTrayMenu _menu;
    private readonly System.Windows.Forms.Timer _hintTimer = new() { Interval = 2400 };
    private double _scale;
    private string _hintText = "拖动移动 · 滚轮缩放 · Ctrl + 滚轮调透明度 · 右键更多";
    private bool _showHint = true;
    private bool _resourcesDisposed;

    public PinnedImageForm(Bitmap image, Point near)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Width <= 0 || image.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(image), "Pinned image dimensions must be positive.");
        }

        _image = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(_image))
        {
            graphics.DrawImageUnscaled(image, Point.Empty);
        }

        Text = "ZSnaper 贴图";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        AccessibleName = "ZSnaper 贴图";
        AccessibleDescription = "可拖动、缩放、调整透明度的置顶截图";
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        Rectangle workArea = Screen.FromPoint(near).WorkingArea;
        double fitScale = Math.Min(
            workArea.Width * 0.68d / _image.Width,
            workArea.Height * 0.68d / _image.Height);
        _scale = ClampScale(Math.Min(1d, fitScale));
        ClientSize = GetScaledSize(_scale);
        Location = ClampLocation(near, workArea, ClientSize);

        _menu = new ModernTrayMenu();
        _menu.AddAction("复制贴图", LucideIcon.Copy, (_, _) => CopyImage(), "Ctrl+C");
        _menu.AddAction("保存贴图", LucideIcon.Folder, (_, _) => SaveImage(), "Ctrl+S");
        _menu.AddAction("恢复原始大小", LucideIcon.Monitor, (_, _) => ResetToActualSize(), "0");
        _menu.AddSectionSeparator();
        _menu.AddAction(
            "关闭贴图",
            LucideIcon.X,
            (_, _) => Close(),
            "Esc",
            TrayMenuItemKind.Destructive);
        _menu.Opening += (_, _) => _menu.ApplyTheme();
        ContextMenuStrip = _menu;

        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            _showHint = false;
            Invalidate();
        };
        Shown += (_, _) =>
        {
            _hintTimer.Start();
            Activate();
        };
        Load += (_, _) =>
            NativeMethods.EnableWindowDropShadowAndRoundCorners(
                Handle,
                ThemeManager.CurrentMode == ThemeMode.Dark);
        ThemeManager.ThemeChanged += ApplyTheme;
    }

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        Graphics graphics = eventArgs.Graphics;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(_image, ClientRectangle);

        using (var border = new Pen(Color.FromArgb(150, ThemeManager.Palette.AccentColor), 1f))
        {
            graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        }

        if (_showHint) DrawHint(graphics);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        base.OnMouseDown(eventArgs);
        if (eventArgs.Button == MouseButtons.Left)
        {
            NativeMethods.DragWindow(Handle);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs eventArgs)
    {
        base.OnMouseDoubleClick(eventArgs);
        if (eventArgs.Button == MouseButtons.Left)
        {
            ResetToActualSize();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs eventArgs)
    {
        base.OnMouseWheel(eventArgs);
        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            Opacity = Math.Clamp(Opacity + (eventArgs.Delta > 0 ? 0.08d : -0.08d), 0.25d, 1d);
            ShowHint($"透明度 {Opacity:P0}");
            return;
        }

        double factor = eventArgs.Delta > 0 ? 1.1d : 1d / 1.1d;
        ApplyScale(_scale * factor, eventArgs.Location);
        ShowHint($"缩放 {_scale:P0}");
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        if (keyData == (Keys.Control | Keys.C))
        {
            CopyImage();
            return true;
        }
        if (keyData == (Keys.Control | Keys.S))
        {
            SaveImage();
            return true;
        }
        if (keyData == Keys.D0 || keyData == Keys.NumPad0)
        {
            ResetToActualSize();
            return true;
        }

        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            ThemeManager.ThemeChanged -= ApplyTheme;
            _hintTimer.Stop();
            _hintTimer.Dispose();
            _menu.Dispose();
            _image.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ApplyScale(double requestedScale, Point anchorClient)
    {
        double nextScale = ClampScale(requestedScale);
        if (Math.Abs(nextScale - _scale) < 0.001d) return;

        Point anchorScreen = PointToScreen(anchorClient);
        double horizontalRatio = anchorClient.X / (double)Math.Max(1, ClientSize.Width);
        double verticalRatio = anchorClient.Y / (double)Math.Max(1, ClientSize.Height);
        Size nextSize = GetScaledSize(nextScale);

        _scale = nextScale;
        ClientSize = nextSize;
        Location = new Point(
            anchorScreen.X - (int)Math.Round(nextSize.Width * horizontalRatio),
            anchorScreen.Y - (int)Math.Round(nextSize.Height * verticalRatio));
        Invalidate();
    }

    private void ResetToActualSize()
    {
        ApplyScale(1d, new Point(ClientSize.Width / 2, ClientSize.Height / 2));
        ShowHint($"原始大小 {_scale:P0}");
    }

    private void CopyImage()
    {
        ShowHint(CaptureService.TryCopyToClipboard(_image) ? "贴图已复制" : "复制失败，剪贴板正忙");
    }

    private void SaveImage()
    {
        bool saved = CaptureService.TrySaveToPictures(_image, out string? filePath, out _);
        ShowHint(saved && filePath is not null ? $"已保存 {Path.GetFileName(filePath)}" : "保存贴图失败");
    }

    private void ShowHint(string text)
    {
        _hintText = text;
        _showHint = true;
        _hintTimer.Stop();
        _hintTimer.Start();
        Invalidate();
    }

    private void ApplyTheme()
    {
        _menu.ApplyTheme();
        Invalidate();
    }

    private void DrawHint(Graphics graphics)
    {
        Size measured = TextRenderer.MeasureText(
            _hintText,
            Font,
            new Size(Math.Max(80, Width - 24), 0),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        int hintWidth = Math.Min(Math.Max(80, Width - 20), measured.Width + 24);
        int hintHeight = 30;
        var bounds = new Rectangle(
            (Width - hintWidth) / 2,
            Math.Max(8, Height - hintHeight - 12),
            hintWidth,
            hintHeight);
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(bounds, 8);
        using (var brush = new SolidBrush(Color.FromArgb(190, 18, 18, 20)))
        {
            graphics.FillPath(brush, path);
        }
        TextRenderer.DrawText(
            graphics,
            _hintText,
            Font,
            bounds,
            Color.White,
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }

    private double ClampScale(double scale)
    {
        int longEdge = Math.Max(_image.Width, _image.Height);
        double minimum = MinimumLongEdge / (double)longEdge;
        double maximum = Math.Min(4d, MaximumLongEdge / (double)longEdge);
        return Math.Clamp(scale, minimum, Math.Max(minimum, maximum));
    }

    private Size GetScaledSize(double scale) => new(
        Math.Max(1, (int)Math.Round(_image.Width * scale)),
        Math.Max(1, (int)Math.Round(_image.Height * scale)));

    private static Point ClampLocation(Point desired, Rectangle workArea, Size size) => new(
        Math.Clamp(desired.X, workArea.Left, Math.Max(workArea.Left, workArea.Right - size.Width)),
        Math.Clamp(desired.Y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - size.Height)));
}
