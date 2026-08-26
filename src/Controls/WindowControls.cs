using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class WindowControls : Control
{
    private const int ButtonWidth = 36;
    private const int ButtonCount = 2;
    private int _hoveredButton = -1;
    private int _pressedButton = -1;

    public WindowControls()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.SupportsTransparentBackColor |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);
        BackColor = Color.Transparent;
        Size = new Size(ButtonWidth * ButtonCount, 30);
        TabStop = false;
        ThemeManager.ThemeChanged += HandleThemeChanged;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hovered = e.X >= 0 && e.X < Width && e.Y >= 0 && e.Y < Height
            ? Math.Clamp(e.X / ButtonWidth, 0, ButtonCount - 1)
            : -1;
        if (_hoveredButton != hovered)
        {
            _hoveredButton = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredButton = -1;
        _pressedButton = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _pressedButton = Math.Clamp(e.X / ButtonWidth, 0, ButtonCount - 1);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int releasedButton = e.X >= 0 && e.X < Width && e.Y >= 0 && e.Y < Height
            ? Math.Clamp(e.X / ButtonWidth, 0, ButtonCount - 1)
            : -1;
        int pressedButton = _pressedButton;
        _pressedButton = -1;
        Invalidate();

        if (e.Button != MouseButtons.Left || releasedButton != pressedButton)
        {
            return;
        }

        Form? window = FindForm();
        if (window == null) return;
        switch (releasedButton)
        {
            case 0:
                window.WindowState = FormWindowState.Minimized;
                break;
            case 1:
                window.Hide();
                break;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        ThemePalette palette = ThemeManager.Palette;

        for (int index = 0; index < ButtonCount; index++)
        {
            var buttonBounds = new Rectangle(index * ButtonWidth + 2, 2, ButtonWidth - 4, Height - 4);
            if (_hoveredButton == index || _pressedButton == index)
            {
                DrawHoverBackground(
                    graphics,
                    buttonBounds,
                    isClose: index == ButtonCount - 1,
                    isPressed: _pressedButton == index,
                    palette);
            }

            bool closeActive = index == ButtonCount - 1 &&
                (_hoveredButton == index || _pressedButton == index);
            Color glyphColor = closeActive
                ? Color.White
                : palette.WindowControlText;
            DrawCenteredIcon(
                graphics,
                index,
                index == 0 ? LucideIcon.Minus : LucideIcon.X,
                glyphColor);
        }
    }

    private static void DrawCenteredIcon(Graphics graphics, int buttonIndex, LucideIcon icon, Color color)
    {
        const float iconSize = 16f;
        float x = buttonIndex * ButtonWidth + (ButtonWidth - iconSize) / 2f;
        float y = (30f - iconSize) / 2f;
        LucideRenderer.Draw(graphics, icon, x, y, iconSize, color);
    }

    private static void DrawHoverBackground(
        Graphics graphics,
        Rectangle bounds,
        bool isClose,
        bool isPressed,
        ThemePalette palette)
    {
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(bounds, 7);
        if (isClose)
        {
            Color start = isPressed
                ? Color.FromArgb(218, 38, 75)
                : Color.FromArgb(244, 63, 94);
            Color end = isPressed
                ? Color.FromArgb(162, 28, 175)
                : Color.FromArgb(192, 38, 211);
            using var brush = new LinearGradientBrush(
                bounds,
                start,
                end,
                LinearGradientMode.ForwardDiagonal);
            graphics.FillPath(brush, path);

            using var highlightPen = new Pen(Color.FromArgb(58, 255, 255, 255), 1f);
            graphics.DrawPath(highlightPen, path);
            return;
        }

        int startAlpha = isPressed
            ? palette.Mode == ThemeMode.Dark ? 52 : 34
            : palette.Mode == ThemeMode.Dark ? 38 : 24;
        int endAlpha = isPressed
            ? palette.Mode == ThemeMode.Dark ? 32 : 22
            : palette.Mode == ThemeMode.Dark ? 20 : 12;
        using var hoverBrush = new LinearGradientBrush(
            bounds,
            Color.FromArgb(startAlpha, palette.TextPrimary),
            Color.FromArgb(endAlpha, palette.TextPrimary),
            LinearGradientMode.Vertical);
        graphics.FillPath(hoverBrush, path);

        int borderAlpha = palette.Mode == ThemeMode.Dark ? 28 : 16;
        using var borderPen = new Pen(Color.FromArgb(borderAlpha, palette.TextPrimary), 1f);
        graphics.DrawPath(borderPen, path);
    }

    private void HandleThemeChanged() => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= HandleThemeChanged;
        }

        base.Dispose(disposing);
    }
}
