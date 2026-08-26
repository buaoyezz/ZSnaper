using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

/// <summary>
/// A compact, theme-aware tray menu that visually matches the rest of ZSnaper.
/// </summary>
public sealed class ModernTrayMenu : ContextMenuStrip
{
    private const int CornerRadius = 10;
    private const int LogicalMenuWidth = 220;

    public ModernTrayMenu()
    {
        AutoSize = true;
        BackColor = ThemeManager.Palette.CardBg;
        DropShadowEnabled = true;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        ImageScalingSize = new Size(18, 18);
        MinimumSize = new Size(LogicalMenuWidth, 0);
        Padding = new Padding(0, 7, 0, 7);
        Renderer = new ModernTrayMenuRenderer();
        ShowCheckMargin = false;
        ShowImageMargin = true;
    }

    public ToolStripMenuItem AddAction(
        string text,
        LucideIcon icon,
        EventHandler onClick,
        string? shortcutText = null,
        TrayMenuItemKind kind = TrayMenuItemKind.Normal)
    {
        var item = new ToolStripMenuItem(text, CreateIcon(icon), onClick)
        {
            AutoSize = false,
            ImageScaling = ToolStripItemImageScaling.None,
            Size = new Size(LogicalMenuWidth, 40),
            Padding = Padding.Empty,
            ShortcutKeyDisplayString = shortcutText ?? string.Empty,
            Tag = new TrayMenuItemMetadata(icon, kind)
        };
        Items.Add(item);
        return item;
    }

    public ToolStripMenuItem AddBrandAction(string text, EventHandler onClick)
    {
        ToolStripMenuItem item = AddAction(
            text,
            LucideIcon.Grid3X3,
            onClick,
            kind: TrayMenuItemKind.Primary);
        item.Tag = new TrayMenuItemMetadata(
            LucideIcon.Grid3X3,
            TrayMenuItemKind.Primary,
            UseBrandLogo: true);
        ReplaceBrandLogo(item);
        return item;
    }

    public void AddSectionSeparator()
    {
        Items.Add(new ToolStripSeparator
        {
            AutoSize = false,
            Height = 7,
            Margin = Padding.Empty
        });
    }

    public void ApplyTheme()
    {
        ThemePalette palette = ThemeManager.Palette;
        BackColor = palette.CardBg;
        ForeColor = palette.TextPrimary;
        Renderer = new ModernTrayMenuRenderer();

        foreach (ToolStripItem item in Items)
        {
            item.BackColor = palette.CardBg;
            item.ForeColor = palette.TextPrimary;
            if (item is ToolStripMenuItem menuItem && menuItem.Tag is TrayMenuItemMetadata metadata)
            {
                if (metadata.UseBrandLogo)
                {
                    ReplaceBrandLogo(menuItem);
                }
                else
                {
                    ReplaceIcon(menuItem, metadata.Icon);
                }
            }
        }

        Invalidate(true);
    }

    public static Bitmap CreateIcon(LucideIcon icon)
    {
        var bitmap = new Bitmap(18, 18, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        LucideRenderer.Draw(graphics, icon, 0, 0, 18, ThemeManager.Palette.TextSecondary, 1.8f);
        return bitmap;
    }

    public static Bitmap CreateBrandLogo()
    {
        var bitmap = new Bitmap(18, 18, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        LogoRenderer.DrawLogo(graphics, 1.5f, 0, 18, ThemeManager.Palette.TextPrimary);
        return bitmap;
    }

    public static void ReplaceIcon(ToolStripMenuItem item, LucideIcon icon)
    {
        Image? previous = item.Image;
        item.Image = CreateIcon(icon);
        item.Tag = item.Tag is TrayMenuItemMetadata metadata
            ? metadata with { Icon = icon }
            : new TrayMenuItemMetadata(icon, TrayMenuItemKind.Normal);
        previous?.Dispose();
    }

    public static void ReplaceBrandLogo(ToolStripMenuItem item)
    {
        Image? previous = item.Image;
        item.Image = CreateBrandLogo();
        item.Tag = item.Tag is TrayMenuItemMetadata metadata
            ? metadata with { UseBrandLogo = true }
            : new TrayMenuItemMetadata(
                LucideIcon.Grid3X3,
                TrayMenuItemKind.Primary,
                UseBrandLogo: true);
        previous?.Dispose();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        Region? previous = Region;
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(
            new Rectangle(0, 0, Width, Height),
            CornerRadius);
        Region = new Region(path);
        previous?.Dispose();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size preferred = base.GetPreferredSize(proposedSize);
        int width = (int)Math.Round(LogicalMenuWidth * DeviceDpi / 96f);
        return new Size(width, preferred.Height);
    }
}

public enum TrayMenuItemKind
{
    Normal,
    Primary,
    ThemeToggle,
    Destructive
}

internal sealed record TrayMenuItemMetadata(
    LucideIcon Icon,
    TrayMenuItemKind Kind,
    bool UseBrandLogo = false);

internal sealed class ModernTrayMenuRenderer : ToolStripProfessionalRenderer
{
    private const int IconLeft = 8;
    private const int IconSize = 18;
    private const int TextLeft = 40;
    private const int ShortcutAreaWidth = 64;
    private const int RightPadding = 8;

    public ModernTrayMenuRenderer()
        : base(new ModernTrayMenuColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(ThemeManager.Palette.CardBg);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(bounds, 10);
        using var pen = new Pen(Opaque(ThemeManager.Palette.CardBorder, ThemeManager.Palette.CardBg), 1f);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        ThemePalette palette = ThemeManager.Palette;
        TrayMenuItemKind kind = GetKind(e.Item);

        if (e.Item.Selected || e.Item.Pressed)
        {
            Color fill = kind switch
            {
                TrayMenuItemKind.Primary => Blend(
                    palette.CardBg,
                    palette.AccentColor,
                    e.Item.Pressed ? 0.18f : 0.12f),
                TrayMenuItemKind.Destructive => Blend(
                    palette.CardBg,
                    Color.FromArgb(239, 68, 68),
                    e.Item.Pressed ? 0.18f : 0.11f),
                _ => Blend(
                    palette.CardBg,
                    palette.TextPrimary,
                    palette.Mode == ThemeMode.Dark ? 0.08f : 0.05f)
            };
            var bounds = new Rectangle(2, 2, e.Item.Width - 4, e.Item.Height - 4);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(bounds, 7);
            using var brush = new SolidBrush(fill);
            e.Graphics.FillPath(brush, path);
        }

        if (kind == TrayMenuItemKind.ThemeToggle)
        {
            DrawThemeToggle(e.Graphics, e.Item);
        }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        ThemePalette palette = ThemeManager.Palette;
        float scale = e.Graphics.DpiX / 96f;
        int textLeft = Scale(TextLeft, scale);
        int shortcutLeft = e.Item.Width - Scale(ShortcutAreaWidth, scale);
        int rightPadding = Scale(RightPadding, scale);
        TrayMenuItemKind kind = GetKind(e.Item);
        string renderedText = e.Text ?? string.Empty;
        bool isShortcut = e.Item is ToolStripMenuItem menuItem &&
                          !string.IsNullOrEmpty(menuItem.ShortcutKeyDisplayString) &&
                          string.Equals(renderedText, menuItem.ShortcutKeyDisplayString, StringComparison.Ordinal);

        Rectangle textBounds;
        Color textColor;
        if (isShortcut)
        {
            textBounds = new Rectangle(
                shortcutLeft,
                Scale(7, scale),
                e.Item.Width - shortcutLeft - rightPadding,
                e.Item.Height - Scale(14, scale));
            textColor = e.Item.Selected ? palette.TextSecondary : palette.TextMuted;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using GraphicsPath keyPath = GraphicsHelper.GetRoundedRectangle(textBounds, Scale(5, scale));
            using (var keyBrush = new SolidBrush(Blend(
                       palette.CardBg,
                       palette.TextPrimary,
                       palette.Mode == ThemeMode.Dark ? 0.07f : 0.045f)))
            using (var keyPen = new Pen(Opaque(palette.CardBorder, palette.CardBg)))
            {
                e.Graphics.FillPath(keyBrush, keyPath);
                e.Graphics.DrawPath(keyPen, keyPath);
            }
        }
        else
        {
            int right = kind == TrayMenuItemKind.ThemeToggle
                ? e.Item.Width - Scale(54, scale)
                : e.Item is ToolStripMenuItem { ShortcutKeyDisplayString.Length: > 0 }
                    ? shortcutLeft - Scale(8, scale)
                    : e.Item.Width - rightPadding;
            textBounds = new Rectangle(textLeft, 0, Math.Max(0, right - textLeft), e.Item.Height);
            textColor = kind switch
            {
                TrayMenuItemKind.Destructive when e.Item.Selected => Color.FromArgb(248, 113, 113),
                TrayMenuItemKind.Primary when e.Item.Selected => palette.AccentColor,
                _ => palette.TextPrimary
            };
        }

        TextRenderer.DrawText(
            e.Graphics,
            isShortcut ? renderedText.Replace(" ", string.Empty, StringComparison.Ordinal) : renderedText,
            e.Item.Owner?.Font ?? e.Item.Font,
            textBounds,
            textColor,
            (isShortcut ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) |
            TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine |
            TextFormatFlags.NoPadding |
            TextFormatFlags.NoPrefix);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null)
        {
            return;
        }

        float scale = e.Graphics.DpiX / 96f;
        int size = Scale(IconSize, scale);
        int x = Scale(IconLeft, scale);
        int y = (e.Item.Height - size) / 2;
        TrayMenuItemKind kind = GetKind(e.Item);

        if (kind == TrayMenuItemKind.Primary)
        {
            int surfaceSize = Scale(26, scale);
            var surfaceBounds = new Rectangle(
                x - Scale(4, scale),
                (e.Item.Height - surfaceSize) / 2,
                surfaceSize,
                surfaceSize);
            using GraphicsPath surfacePath = GraphicsHelper.GetRoundedRectangle(surfaceBounds, Scale(7, scale));
            using var surfaceBrush = new SolidBrush(Blend(
                ThemeManager.Palette.CardBg,
                ThemeManager.Palette.AccentColor,
                ThemeManager.Palette.Mode == ThemeMode.Dark ? 0.16f : 0.10f));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(surfaceBrush, surfacePath);
        }

        var bounds = new Rectangle(
            x,
            y,
            size,
            size);

        GraphicsState state = e.Graphics.Save();
        try
        {
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(e.Image, bounds);
        }
        finally
        {
            e.Graphics.Restore(state);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        int left = Scale(TextLeft, e.Graphics.DpiX / 96f);
        using var pen = new Pen(Opaque(ThemeManager.Palette.SeparatorColor, ThemeManager.Palette.CardBg));
        e.Graphics.DrawLine(pen, left, y, e.Item.Width - 9, y);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Keep the icon rail visually integrated with the menu surface.
    }

    private static void DrawThemeToggle(Graphics graphics, ToolStripItem item)
    {
        ThemePalette palette = ThemeManager.Palette;
        float scale = graphics.DpiX / 96f;
        int width = Scale(34, scale);
        int height = Scale(18, scale);
        int x = item.Width - Scale(44, scale);
        int y = (item.Height - height) / 2;
        bool isOn = ThemeManager.CurrentMode == ThemeMode.Dark;
        Color trackColor = isOn
            ? palette.AccentColor
            : Blend(palette.CardBg, palette.TextMuted, 0.30f);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using GraphicsPath trackPath = GraphicsHelper.GetRoundedRectangle(
            new Rectangle(x, y, width, height),
            height / 2);
        using (var trackBrush = new SolidBrush(trackColor))
        {
            graphics.FillPath(trackBrush, trackPath);
        }

        int thumbSize = Scale(14, scale);
        int thumbX = isOn
            ? x + width - thumbSize - Scale(2, scale)
            : x + Scale(2, scale);
        int thumbY = y + (height - thumbSize) / 2;
        using var thumbBrush = new SolidBrush(isOn ? palette.AccentForeground : palette.CardBg);
        graphics.FillEllipse(thumbBrush, thumbX, thumbY, thumbSize, thumbSize);
    }

    private static TrayMenuItemKind GetKind(ToolStripItem item) =>
        item.Tag is TrayMenuItemMetadata metadata ? metadata.Kind : TrayMenuItemKind.Normal;

    private static Color Opaque(Color foreground, Color background)
    {
        if (foreground.A == 255)
        {
            return foreground;
        }

        return Blend(background, Color.FromArgb(255, foreground), foreground.A / 255f);
    }

    private static int Scale(int value, float scale) => (int)Math.Round(value * scale);

    private static Color Blend(Color background, Color foreground, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(background.R + (foreground.R - background.R) * amount),
            (int)Math.Round(background.G + (foreground.G - background.G) * amount),
            (int)Math.Round(background.B + (foreground.B - background.B) * amount));
    }
}

internal sealed class ModernTrayMenuColorTable : ProfessionalColorTable
{
    private static ThemePalette Palette => ThemeManager.Palette;

    public override Color ToolStripDropDownBackground => Palette.CardBg;
    public override Color ImageMarginGradientBegin => Palette.CardBg;
    public override Color ImageMarginGradientMiddle => Palette.CardBg;
    public override Color ImageMarginGradientEnd => Palette.CardBg;
    public override Color MenuBorder => Palette.CardBorder;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Palette.NavItemHover;
    public override Color MenuItemSelectedGradientBegin => Palette.NavItemHover;
    public override Color MenuItemSelectedGradientEnd => Palette.NavItemHover;
    public override Color SeparatorDark => Palette.SeparatorColor;
    public override Color SeparatorLight => Color.Transparent;
}
