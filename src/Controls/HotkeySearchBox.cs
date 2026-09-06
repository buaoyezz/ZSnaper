using System.Drawing.Drawing2D;
using ZSnaper.Helpers;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Controls;

public sealed class HotkeySearchBox : Control
{
    private readonly TextBox _editor;
    private bool _hovered;
    private bool _clearHovered;

    private Rectangle ClearButtonRect =>
        new(Width - 32, (Height - 16) / 2, 16, 16);

    private Rectangle ClearButtonHitRect =>
        new(Width - 36, (Height - 26) / 2, 26, 26);

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _editor.Text;
        set
        {
            _editor.Text = value ?? string.Empty;
            UpdateEditorBounds();
            Invalidate();
        }
    }

    public HotkeySearchBox()
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
        AccessibleRole = AccessibleRole.Text;
        AccessibleName = "搜索快捷键";

        _editor = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = new Font("Microsoft YaHei UI", 9f),
            PlaceholderText = "搜索功能或按键，例如：贴图、Ctrl…",
            Location = new Point(40, 10),
            TabIndex = 0
        };
        _editor.TextChanged += (_, _) =>
        {
            UpdateEditorBounds();
            OnTextChanged(EventArgs.Empty);
            Invalidate();
        };
        Size = new Size(420, 38);
        _editor.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && _editor.Text.Length > 0)
            {
                _editor.Clear();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        _editor.GotFocus += (_, _) => Invalidate();
        _editor.LostFocus += (_, _) => Invalidate();
        Controls.Add(_editor);

        MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
        MouseLeave += (_, _) =>
        {
            _hovered = false;
            if (_clearHovered)
            {
                _clearHovered = false;
                Cursor = Cursors.Default;
            }
            Invalidate();
        };
        MouseMove += (_, e) =>
        {
            bool overClear = _editor.Text.Length > 0 && ClearButtonHitRect.Contains(e.Location);
            if (_clearHovered != overClear)
            {
                _clearHovered = overClear;
                Cursor = overClear ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        };
        MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _editor.Text.Length > 0 && ClearButtonHitRect.Contains(e.Location))
            {
                Clear();
                _editor.Focus();
                return;
            }

            _editor.Focus();
        };

        ThemeManager.ThemeChanged += ApplyTheme;
        ApplyTheme();
    }

    public void Clear() => _editor.Clear();

    private void ApplyTheme()
    {
        ThemePalette palette = ThemeManager.Palette;
        _editor.BackColor = palette.InputBg;
        _editor.ForeColor = palette.TextPrimary;
        Invalidate();
    }

    private void UpdateEditorBounds()
    {
        if (_editor == null) return;
        int rightReserve = _editor.Text.Length > 0 ? 36 : 14;
        _editor.SetBounds(
            40,
            Math.Max(6, (Height - _editor.PreferredHeight) / 2),
            Math.Max(1, Width - 40 - rightReserve),
            _editor.PreferredHeight);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateEditorBounds();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ThemePalette palette = ThemeManager.Palette;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Rectangle bounds = new(0, 0, Width - 1, Height - 1);
        using GraphicsPath path = GraphicsHelper.GetRoundedRectangle(bounds, 9);
        using (var fill = new SolidBrush(palette.InputBg))
        {
            e.Graphics.FillPath(fill, path);
        }

        Color borderColor = _editor.Focused
            ? palette.AccentColor
            : _hovered ? palette.TextMuted : palette.CardBorder;
        using (var border = new Pen(borderColor, _editor.Focused ? 1.4f : 1f))
        {
            e.Graphics.DrawPath(border, path);
        }

        LucideRenderer.Draw(e.Graphics, LucideIcon.Search, 14, (Height - 16) / 2f, 16, palette.TextMuted, 1.8f);

        if (_editor.Text.Length > 0)
        {
            Rectangle clearRect = ClearButtonRect;
            Color clearColor = _clearHovered ? palette.TextPrimary : palette.TextMuted;
            LucideRenderer.Draw(e.Graphics, LucideIcon.X, clearRect.X, clearRect.Y, 16, clearColor, _clearHovered ? 2.2f : 1.8f);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ThemeManager.ThemeChanged -= ApplyTheme;
        }

        base.Dispose(disposing);
    }
}
