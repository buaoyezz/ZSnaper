using System.Drawing.Drawing2D;
using SkiaSharp;
using ZSnaper.Controls;
using ZSnaper.Helpers;
using ZSnaper.Interop;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Forms;

public class MainForm : Form
{
    private readonly SkiaRasterLayer _mainSurface = new();
    private bool _mainSurfaceDirty = true;
    private int _currentTabIndex = 0;
    private bool _hasSystemBackdrop;

    // 控件
    private readonly WindowControls _windowControls;
    private readonly Panel _sidebarPanel;
    private readonly List<NavMenuButton> _navButtons = [];

    // 页面容器
    private readonly Panel _pageContainer;
    private readonly Panel[] _pages = new Panel[5];
    private ModernTextEditor _ocrTextBox = null!;
    private Label _homeCaptureCountLabel = null!;
    private Label _homeOcrCountLabel = null!;
    private Label _homeLatestResultLabel = null!;
    private LinkLabel _homeOpenResultLink = null!;
    private string? _homeLatestFilePath;

    // 事件
    public event Action<bool>? RequestCapture;
    public event Func<HotkeyCommand, HotkeyGesture, HotkeyChangeResult>? RequestHotkeyChange;

    public MainForm()
    {
        Text = "ZSnaper";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(680, 420);
        MinimumSize = new Size(620, 380);
        BackColor = Color.Black;
        DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        // 1. 窗口控制按钮
        _windowControls = new WindowControls
        {
            Location = new Point(Width - 86, 10),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };

        // 2. 左侧侧边栏导航面板
        _sidebarPanel = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(14, 62),
            Size = new Size(136, Height - 78),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };

        // 侧边栏按钮列表
        (LucideIcon icon, string label, bool isBottom)[] menuItems = [
            (LucideIcon.Camera, "截图识别", false),
            (LucideIcon.FileText, "OCR 记录", false),
            (LucideIcon.Keyboard, "快捷键", false),
            (LucideIcon.Sliders, "偏好设置", true),
            (LucideIcon.Info, "关于软件", true)
        ];

        for (int i = 0; i < menuItems.Length; i++)
        {
            int index = i;
            var item = menuItems[i];
            var btn = new NavMenuButton
            {
                Icon = item.icon,
                LabelText = item.label,
                Size = new Size(136, 36),
                IsActive = i == 0
            };

            if (!item.isBottom)
            {
                btn.Location = new Point(0, i * 40);
                btn.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            }
            else
            {
                int bottomOffset = (menuItems.Length - i) * 40;
                btn.Location = new Point(0, _sidebarPanel.Height - bottomOffset);
                btn.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            }

            btn.Click += (_, _) => SwitchTab(index);
            _navButtons.Add(btn);
            _sidebarPanel.Controls.Add(btn);
        }

        // 3. 右侧主内容区域
        _pageContainer = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(164, 52),
            Size = new Size(Width - 178, Height - 66),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(0)
        };

        // 初始化各个页面
        InitPages();

        // 添加控件到窗体
        Controls.Add(_windowControls);
        Controls.Add(_sidebarPanel);
        Controls.Add(_pageContainer);

        // 注册全局主题变更监听
        ThemeManager.ThemeChanged += OnThemeChanged;
        Disposed += (_, _) =>
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _mainSurface.Dispose();
        };

        // 加载时启用 Windows 11 原生圆角与 DWM 阴影
        Load += (_, _) => UpdateDwmEffect();
    }

    private void UpdateDwmEffect()
    {
        NativeMethods.EnableWindowDropShadowAndRoundCorners(
            Handle,
            ThemeManager.CurrentMode == ThemeMode.Dark);
        bool enabled = false;
        if (_hasSystemBackdrop != enabled)
        {
            _hasSystemBackdrop = enabled;
            _mainSurfaceDirty = true;
        }
    }

    private void OnThemeChanged()
    {
        _mainSurfaceDirty = true;

        if (_ocrTextBox != null && !_ocrTextBox.IsDisposed)
        {
            _ocrTextBox.ApplyTheme();
        }

        UpdateDwmEffect();
        Invalidate(true);
    }

    private void SwitchTab(int index)
    {
        if (index < 0 || index >= _pages.Length || _pages[index] is null)
        {
            return;
        }

        Panel selectedPage = _pages[index];
        if (_currentTabIndex == index &&
            selectedPage.Parent == _pageContainer &&
            selectedPage.Visible)
        {
            return;
        }

        _pageContainer.SuspendLayout();
        for (int i = 0; i < _navButtons.Count; i++)
        {
            _navButtons[i].IsActive = i == index;
        }

        for (int i = 0; i < _pages.Length; i++)
        {
            Panel? page = _pages[i];
            if (page is not null)
            {
                page.Visible = i == index;
            }
        }

        selectedPage.BringToFront();
        _currentTabIndex = index;
        _pageContainer.ResumeLayout(performLayout: false);
    }

    private Panel CreateBasePage()
    {
        return new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(0, 0),
            Size = _pageContainer.ClientSize,
            Dock = DockStyle.Fill
        };
    }

    private void InitPages()
    {
        _pages[0] = CreateCapturePage();
        _pages[1] = CreateOcrStudioPage();
        _pages[2] = CreateHotkeysPage();
        _pages[3] = CreateSettingsPage();
        _pages[4] = CreateAboutPage();

        _pageContainer.SuspendLayout();
        for (int index = 0; index < _pages.Length; index++)
        {
            Panel page = _pages[index];
            page.Visible = index == _currentTabIndex;
            _pageContainer.Controls.Add(page);
        }
        _pages[_currentTabIndex].BringToFront();
        _pageContainer.ResumeLayout(performLayout: false);
    }

    // 页面 1：快捷截图主面板
    private Panel CreateCapturePage()
    {
        var panel = CreateBasePage();
        int contentWidth = panel.Width - 16;

        var welcomeRegion = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(0, 4),
            Size = new Size(contentWidth, 70),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var welcomeEyebrow = new Label
        {
            Text = "Welcome",
            Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(15, 1)
        };

        var welcomeTitle = new Label
        {
            Text = "ZSnaper",
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 23)
        };

        var welcomeDescription = new Label
        {
            Text = "What You Want to Capture Today?",
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(15, 51)
        };

        welcomeRegion.Controls.Add(welcomeEyebrow);
        welcomeRegion.Controls.Add(welcomeTitle);
        welcomeRegion.Controls.Add(welcomeDescription);

        var statsTitle = new Label
        {
            Text = "本次运行",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 89)
        };

        _homeCaptureCountLabel = new Label
        {
            Text = "0",
            Font = new Font("Segoe UI Variable Display", 17f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 109)
        };

        var captureCountCaption = new Label
        {
            Text = "捕捉",
            Font = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(1, 141)
        };

        var statsDividerOne = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(142, 113),
            Size = new Size(1, 38)
        };

        _homeOcrCountLabel = new Label
        {
            Text = "0",
            Font = new Font("Segoe UI Variable Display", 17f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(163, 109)
        };

        var ocrCountCaption = new Label
        {
            Text = "文字读取",
            Font = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(164, 141)
        };

        var statsDividerTwo = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(304, 113),
            Size = new Size(1, 38)
        };

        var readyValue = new Label
        {
            Text = "就绪",
            Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(326, 113)
        };

        var readyCaption = new Label
        {
            Text = "本地 OCR",
            Font = new Font("Microsoft YaHei UI", 8.1f, FontStyle.Regular),
            AutoSize = true,
            Location = new Point(327, 141)
        };

        var actionsTitle = new Label
        {
            Text = "开始",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 177)
        };

        var captureButton = new ModernButton
        {
            Text = "截图",
            IsPrimary = true,
            CornerRadius = 8,
            Location = new Point(0, 201),
            Size = new Size(126, 38)
        };
        captureButton.Click += (_, _) => RequestCapture?.Invoke(false);

        var ocrButton = new ModernButton
        {
            Text = "读取文字",
            IsPrimary = false,
            CornerRadius = 8,
            Location = new Point(136, 201),
            Size = new Size(126, 38)
        };
        ocrButton.Click += (_, _) => RequestCapture?.Invoke(true);

        var recentTitle = new Label
        {
            Text = "最近结果",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 264)
        };

        var recentResultCard = new ModernCard
        {
            CornerRadius = 9,
            Location = new Point(0, 289),
            Size = new Size(contentWidth, 48),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _homeLatestResultLabel = new Label
        {
            Text = "还没有新的捕捉记录",
            Font = new Font("Microsoft YaHei UI", 8.3f, FontStyle.Regular),
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(14, 14),
            Size = new Size(Math.Max(1, contentWidth - 114), 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _homeOpenResultLink = new LinkLabel
        {
            Text = "显示文件",
            Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular),
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Location = new Point(contentWidth - 75, 13),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            TabStop = true,
            Visible = false
        };
        _homeOpenResultLink.LinkClicked += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_homeLatestFilePath) || !File.Exists(_homeLatestFilePath))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_homeLatestFilePath}\"",
                UseShellExecute = true
            });
        };

        recentResultCard.Controls.Add(_homeLatestResultLabel);
        recentResultCard.Controls.Add(_homeOpenResultLink);

        void PaintStatsDivider(object? _, PaintEventArgs e)
        {
            using var pen = new Pen(ThemeManager.Palette.SeparatorColor, 1f);
            e.Graphics.DrawLine(pen, 0, 0, 0, statsDividerOne.Height);
        }

        statsDividerOne.Paint += PaintStatsDivider;
        statsDividerTwo.Paint += PaintStatsDivider;
        welcomeRegion.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(ThemeManager.Palette.AccentColor);
            e.Graphics.FillRectangle(brush, 0, 3, 3, 62);
        };

        Action applyHomeTheme = () =>
        {
            ThemePalette palette = ThemeManager.Palette;
            welcomeEyebrow.ForeColor = palette.AccentColor;
            welcomeTitle.ForeColor = palette.TextPrimary;
            welcomeDescription.ForeColor = palette.TextMuted;
            statsTitle.ForeColor = palette.TextSecondary;
            _homeCaptureCountLabel.ForeColor = palette.TextPrimary;
            _homeOcrCountLabel.ForeColor = palette.TextPrimary;
            captureCountCaption.ForeColor = palette.TextMuted;
            ocrCountCaption.ForeColor = palette.TextMuted;
            readyValue.ForeColor = palette.TextPrimary;
            readyCaption.ForeColor = palette.TextMuted;
            actionsTitle.ForeColor = palette.TextSecondary;
            recentTitle.ForeColor = palette.TextSecondary;
            _homeLatestResultLabel.ForeColor = palette.TextMuted;
            _homeOpenResultLink.LinkColor = palette.AccentColor;
            _homeOpenResultLink.ActiveLinkColor = palette.AccentColor;
            _homeOpenResultLink.VisitedLinkColor = palette.AccentColor;
            welcomeRegion.Invalidate();
            statsDividerOne.Invalidate();
            statsDividerTwo.Invalidate();
            recentResultCard.Invalidate();
        };
        applyHomeTheme();
        ThemeManager.ThemeChanged += applyHomeTheme;
        panel.Disposed += (_, _) => ThemeManager.ThemeChanged -= applyHomeTheme;

        panel.Controls.Add(welcomeRegion);
        panel.Controls.Add(statsTitle);
        panel.Controls.Add(_homeCaptureCountLabel);
        panel.Controls.Add(captureCountCaption);
        panel.Controls.Add(statsDividerOne);
        panel.Controls.Add(_homeOcrCountLabel);
        panel.Controls.Add(ocrCountCaption);
        panel.Controls.Add(statsDividerTwo);
        panel.Controls.Add(readyValue);
        panel.Controls.Add(readyCaption);
        panel.Controls.Add(actionsTitle);
        panel.Controls.Add(captureButton);
        panel.Controls.Add(ocrButton);
        panel.Controls.Add(recentTitle);
        panel.Controls.Add(recentResultCard);

        return panel;
    }

    private Panel CreateCapturePageLegacy()
    {
        var panel = CreateBasePage();
        int contentWidth = panel.Width - 16;

        var titleLabel = new Label
        {
            Text = "快捷截图与 OCR 识别",
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 4)
        };
        titleLabel.Paint += (s, e) => titleLabel.ForeColor = ThemeManager.Palette.TextPrimary;

        var descLabel = new Label
        {
            Text = "截取屏幕选区，支持自动复制到剪贴板、存储到本地图片库，以及离线文字识别。",
            Font = new Font("Microsoft YaHei UI", 9f),
            AutoSize = false,
            Size = new Size(contentWidth, 24),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Location = new Point(0, 32)
        };
        descLabel.Paint += (s, e) => descLabel.ForeColor = ThemeManager.Palette.TextMuted;

        var btnCapture = new ModernButton
        {
            Text = "立即截图 (Alt+Q)",
            IsPrimary = true,
            CornerRadius = 8,
            Size = new Size(150, 36),
            Location = new Point(0, 66)
        };
        btnCapture.Click += (_, _) => RequestCapture?.Invoke(false);

        var btnOcr = new ModernButton
        {
            Text = "截图并识别 (Alt+X)",
            IsPrimary = true,
            CornerRadius = 8,
            Size = new Size(155, 36),
            Location = new Point(160, 66)
        };
        btnOcr.Click += (_, _) => RequestCapture?.Invoke(true);

        var btnOpenFolder = new ModernButton
        {
            Text = "打开保存目录",
            IsPrimary = false,
            CornerRadius = 8,
            Size = new Size(130, 36),
            Location = new Point(0, 112)
        };
        btnOpenFolder.Click += (_, _) =>
        {
            var dir = ConfigService.GetEffectiveSavePath();
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        };

        var statusCard = new ModernCard
        {
            CornerRadius = 10,
            Location = new Point(0, 160),
            Size = new Size(contentWidth, 95),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var statusTitle = new Label
        {
            Text = "系统运行状态",
            Font = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold),
            Location = new Point(14, 10),
            AutoSize = true
        };
        statusTitle.Paint += (s, e) => statusTitle.ForeColor = ThemeManager.Palette.TextPrimary;

        var statusDetail = new Label
        {
            Text = "• 全局热键: Alt+Q (截图), Alt+X (OCR 识别) 正常监听中\n• OCR 引擎: Windows 本地离线引擎 (中文/英文已就绪)\n• 保存路径: " + ConfigService.GetEffectiveSavePath(),
            Font = new Font("Microsoft YaHei UI", 8.5f),
            Location = new Point(14, 32),
            Size = new Size(statusCard.Width - 28, 52),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        statusDetail.Paint += (s, e) => statusDetail.ForeColor = ThemeManager.Palette.TextSecondary;

        statusCard.Controls.Add(statusTitle);
        statusCard.Controls.Add(statusDetail);

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(descLabel);
        panel.Controls.Add(btnCapture);
        panel.Controls.Add(btnOcr);
        panel.Controls.Add(btnOpenFolder);
        panel.Controls.Add(statusCard);

        return panel;
    }

    // 页面 2：OCR 工具与记录
    private Panel CreateOcrStudioPage()
    {
        var panel = CreateBasePage();
        int contentWidth = panel.Width - 16;
        var palette = ThemeManager.Palette;

        var titleLabel = new Label
        {
            Text = "OCR 文本工作室",
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 4)
        };
        titleLabel.Paint += (s, e) => titleLabel.ForeColor = ThemeManager.Palette.TextPrimary;

        _ocrTextBox = new ModernTextEditor
        {
            Location = new Point(0, 32),
            Size = new Size(contentWidth, panel.Height - 78),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            Font = new Font("Consolas", 10f),
            PlaceholderText = "此处显示最近一次 OCR 识别文本，可在此粘贴或整理..."
        };

        var copyBtn = new ModernButton
        {
            Text = "复制文本",
            IsPrimary = true,
            CornerRadius = 8,
            Size = new Size(100, 32),
            Location = new Point(0, panel.Height - 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_ocrTextBox.Text))
            {
                CaptureService.TryCopyTextToClipboard(_ocrTextBox.Text);
                MessageBox.Show("已复制识别文本到剪贴板！", "ZSnaper", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };

        var cleanBtn = new ModernButton
        {
            Text = "合并段落",
            IsPrimary = false,
            CornerRadius = 8,
            Size = new Size(100, 32),
            Location = new Point(108, panel.Height - 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        cleanBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_ocrTextBox.Text))
            {
                _ocrTextBox.Text = OcrTextFormatter.Clean(_ocrTextBox.Text);
            }
        };

        var clearBtn = new ModernButton
        {
            Text = "清空",
            IsPrimary = false,
            CornerRadius = 8,
            Size = new Size(75, 32),
            Location = new Point(216, panel.Height - 38),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        clearBtn.Click += (_, _) => _ocrTextBox.Clear();

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(_ocrTextBox);
        panel.Controls.Add(copyBtn);
        panel.Controls.Add(cleanBtn);
        panel.Controls.Add(clearBtn);

        return panel;
    }

    public void UpdateLatestOcrText(string text)
    {
        if (_ocrTextBox != null && !IsDisposed)
        {
            _ocrTextBox.Text = text;
        }
    }

    public void UpdateHomeOverview(int captureCount, int ocrCount, string? savedFilePath, bool wasOcr)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => UpdateHomeOverview(captureCount, ocrCount, savedFilePath, wasOcr));
            return;
        }

        _homeCaptureCountLabel.Text = captureCount.ToString();
        _homeOcrCountLabel.Text = ocrCount.ToString();

        if (!string.IsNullOrWhiteSpace(savedFilePath))
        {
            _homeLatestFilePath = savedFilePath;
            _homeLatestResultLabel.Text = savedFilePath;
            _homeOpenResultLink.Visible = true;
            return;
        }

        _homeLatestFilePath = null;
        _homeLatestResultLabel.Text = wasOcr
            ? "文字已发送到 OCR 工作台"
            : "截图已复制到剪贴板";
        _homeOpenResultLink.Visible = false;
    }

    // 页面 3：快捷键配置
    private Panel CreateHotkeysPage()
    {
        var panel = CreateBasePage();
        int contentWidth = panel.Width - 16;

        var titleLabel = new Label
        {
            Text = "快捷键",
            Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 4)
        };

        var descriptionLabel = new Label
        {
            Text = "点击快捷键框，然后按下新的组合键。修改后立即生效。",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            AutoSize = true,
            Location = new Point(1, 39)
        };

        HotkeyGesture captureGesture = HotkeyGesture.TryParse(ConfigService.Current.CaptureHotkey, out HotkeyGesture parsedCapture)
            ? parsedCapture
            : new HotkeyGesture(Keys.Q, Keys.Alt);
        HotkeyGesture ocrGesture = HotkeyGesture.TryParse(ConfigService.Current.OcrHotkey, out HotkeyGesture parsedOcr)
            ? parsedOcr
            : new HotkeyGesture(Keys.X, Keys.Alt);

        var captureCard = CreateHotkeyCard(
            "截图",
            "截取选区并按当前设置保存或复制",
            captureGesture,
            HotkeyCommand.Capture,
            68,
            out HotkeyRecorder captureRecorder,
            out Label captureName,
            out Label captureDescription);

        var ocrCard = CreateHotkeyCard(
            "读取文字",
            "截取选区并调用本地 OCR",
            ocrGesture,
            HotkeyCommand.Ocr,
            146,
            out HotkeyRecorder ocrRecorder,
            out Label ocrName,
            out Label ocrDescription);

        var feedbackLabel = new Label
        {
            Text = "点击右侧快捷键框开始修改",
            Font = new Font("Microsoft YaHei UI", 8.1f),
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(1, 223),
            Size = new Size(contentWidth, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        bool feedbackIsError = false;

        void ShowFeedback(HotkeyChangeResult result)
        {
            feedbackLabel.Text = result.Message;
            feedbackIsError = !result.Success && result.Message != "已取消修改";
            feedbackLabel.ForeColor = feedbackIsError
                ? Color.FromArgb(239, 68, 68)
                : ThemeManager.Palette.TextMuted;
        }

        captureRecorder.Feedback += ShowFeedback;
        ocrRecorder.Feedback += ShowFeedback;

        var fixedTitle = new Label
        {
            Text = "固定操作",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 253)
        };

        var fixedCard = new ModernCard
        {
            CornerRadius = 9,
            Location = new Point(0, 278),
            Size = new Size(contentWidth, 60),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var escapeKey = new Label
        {
            Text = "Esc",
            Font = new Font("Segoe UI", 8.6f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 10)
        };
        var escapeDescription = new Label
        {
            Text = "取消当前选区",
            Font = new Font("Microsoft YaHei UI", 8.2f),
            AutoSize = true,
            Location = new Point(74, 10)
        };
        var trayKey = new Label
        {
            Text = "双击托盘",
            Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(14, 34)
        };
        var trayDescription = new Label
        {
            Text = "打开工作台",
            Font = new Font("Microsoft YaHei UI", 8.2f),
            AutoSize = true,
            Location = new Point(94, 34)
        };
        fixedCard.Controls.Add(escapeKey);
        fixedCard.Controls.Add(escapeDescription);
        fixedCard.Controls.Add(trayKey);
        fixedCard.Controls.Add(trayDescription);

        Action applyHotkeyTheme = () =>
        {
            ThemePalette palette = ThemeManager.Palette;
            titleLabel.ForeColor = palette.TextPrimary;
            descriptionLabel.ForeColor = palette.TextMuted;
            captureName.ForeColor = palette.TextPrimary;
            captureDescription.ForeColor = palette.TextMuted;
            ocrName.ForeColor = palette.TextPrimary;
            ocrDescription.ForeColor = palette.TextMuted;
            feedbackLabel.ForeColor = feedbackIsError ? Color.FromArgb(239, 68, 68) : palette.TextMuted;
            fixedTitle.ForeColor = palette.TextSecondary;
            escapeKey.ForeColor = palette.AccentColor;
            trayKey.ForeColor = palette.AccentColor;
            escapeDescription.ForeColor = palette.TextMuted;
            trayDescription.ForeColor = palette.TextMuted;
            captureCard.Invalidate();
            ocrCard.Invalidate();
            fixedCard.Invalidate();
            captureRecorder.Invalidate();
            ocrRecorder.Invalidate();
        };
        applyHotkeyTheme();
        ThemeManager.ThemeChanged += applyHotkeyTheme;
        panel.Disposed += (_, _) => ThemeManager.ThemeChanged -= applyHotkeyTheme;

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(descriptionLabel);
        panel.Controls.Add(captureCard);
        panel.Controls.Add(ocrCard);
        panel.Controls.Add(feedbackLabel);
        panel.Controls.Add(fixedTitle);
        panel.Controls.Add(fixedCard);
        return panel;

        ModernCard CreateHotkeyCard(
            string name,
            string description,
            HotkeyGesture gesture,
            HotkeyCommand command,
            int top,
            out HotkeyRecorder recorder,
            out Label nameLabel,
            out Label descLabel)
        {
            var card = new ModernCard
            {
                CornerRadius = 10,
                Location = new Point(0, top),
                Size = new Size(contentWidth, 68),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };

            nameLabel = new Label
            {
                Text = name,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(14, 13)
            };
            descLabel = new Label
            {
                Text = description,
                Font = new Font("Microsoft YaHei UI", 8f),
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(14, 38),
                Size = new Size(Math.Max(1, contentWidth - 190), 19),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            recorder = new HotkeyRecorder
            {
                Gesture = gesture,
                Location = new Point(contentWidth - 156, 17),
                Size = new Size(142, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TryCommit = proposed => RequestHotkeyChange?.Invoke(command, proposed)
                    ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪")
            };

            card.Controls.Add(nameLabel);
            card.Controls.Add(descLabel);
            card.Controls.Add(recorder);
            return card;
        }
    }

    // 页面 4：偏好设置（固定标题 + 现代滚动内容区）
    private Panel CreateSettingsPage()
    {
        var panel = CreateBasePage();

        static void BindThemeColor(Label label, Func<ThemePalette, Color> resolve)
        {
            void Apply()
            {
                if (!label.IsDisposed) label.ForeColor = resolve(ThemeManager.Palette);
            }

            Apply();
            ThemeManager.ThemeChanged += Apply;
            label.Disposed += (_, _) => ThemeManager.ThemeChanged -= Apply;
        }

        var titleLabel = new Label
        {
            Text = "偏好设置",
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold),
            ForeColor = ThemeManager.Palette.TextPrimary,
            AutoSize = true,
            Location = new Point(0, 0)
        };
        BindThemeColor(titleLabel, palette => palette.TextPrimary);

        var subtitleLabel = new Label
        {
            Text = "定制外观、截图工作流与系统行为",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Regular),
            ForeColor = ThemeManager.Palette.TextMuted,
            AutoSize = true,
            Location = new Point(1, 27)
        };
        BindThemeColor(subtitleLabel, palette => palette.TextMuted);

        var scrollPanel = new ModernScrollPanel
        {
            Location = new Point(0, 54),
            Size = new Size(panel.Width, panel.Height - 54),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            ContentHeight = 690
        };
        int contentWidth = Math.Max(320, scrollPanel.Content.ClientSize.Width - 4);

        Label CreateSectionLabel(string text, int y)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
                ForeColor = ThemeManager.Palette.TextSecondary,
                AutoSize = true,
                Location = new Point(4, y)
            };
            BindThemeColor(label, palette => palette.TextSecondary);
            return label;
        }

        // 分组 1：外观与个性化 (Appearance & Customization - 4 项)
        var group1 = new ModernCard
        {
            CornerRadius = 10,
            Location = new Point(0, 28),
            Size = new Size(contentWidth, 208),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var rowAnim = new SettingItemRow
        {
            Title = "交互动效",
            Description = "三档流畅度设置",
            ShowDivider = false,
            ActionControl = new AnimationSegmentedControl()
        };

        var toggleGlow = new ModernToggleSwitch { Checked = ConfigService.Current.EnableBackgroundGlow };
        toggleGlow.CheckedChanged += (_, _) => ThemeManager.EnableGlow = toggleGlow.Checked;
        var rowGlow = new SettingItemRow
        {
            Title = "背景氛围漫反射",
            Description = "微光跟随强调色",
            ShowDivider = true,
            ActionControl = toggleGlow
        };

        var rowAccent = new SettingItemRow
        {
            Title = "系统强调色",
            Description = "自定义主色调",
            ShowDivider = true,
            ActionControl = new ModernAccentColorPicker()
        };

        var rowTheme = new SettingItemRow
        {
            Title = "主题外观模式",
            Description = "浅色 / 纯粹沉浸深色",
            ShowDivider = true,
            ActionControl = new ThemeSegmentedControl()
        };

        rowTheme.SetBounds(0, 0, group1.Width, 52);
        rowAccent.SetBounds(0, 52, group1.Width, 52);
        rowGlow.SetBounds(0, 104, group1.Width, 52);
        rowAnim.SetBounds(0, 156, group1.Width, 52);
        rowTheme.Anchor = rowAccent.Anchor = rowGlow.Anchor = rowAnim.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        group1.Controls.Add(rowTheme);
        group1.Controls.Add(rowAccent);
        group1.Controls.Add(rowGlow);
        group1.Controls.Add(rowAnim);

        // 分组 2：截图与工作流
        var group2 = new ModernCard
        {
            CornerRadius = 10,
            Location = new Point(0, 278),
            Size = new Size(contentWidth, 260),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var rowToolbarPlacement = new SettingItemRow
        {
            Title = "截图工具栏位置",
            Description = "左侧 / 居中 / 右侧 / Auto 习惯自适应",
            ShowDivider = true,
            ActionControl = new ToolbarPlacementSegmentedControl()
        };

        var btnCustomizeToolbar = new ModernButton
        {
            Text = "展开",
            IsPrimary = false,
            CornerRadius = 6,
            Icon = LucideIcon.Sliders,
            IconSize = 14,
            IconGap = 5,
            Size = new Size(82, 28)
        };
        var rowToolbarItems = new SettingItemRow
        {
            Title = "工具栏与批注样式",
            Description = "工具顺序、图标行为、颜色、字体与笔刷",
            ShowDivider = true,
            ActionControl = btnCustomizeToolbar
        };

        var toolbarEditor = new ToolbarCustomizationPanel
        {
            Visible = false,
            Location = new Point(0, 104),
            Size = new Size(group2.Width, 398),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var toggleCopy = new ModernToggleSwitch { Checked = ConfigService.Current.AutoCopyClipboard };
        toggleCopy.CheckedChanged += (_, _) => { ConfigService.Current.AutoCopyClipboard = toggleCopy.Checked; ConfigService.Save(); };
        var rowCopy = new SettingItemRow
        {
            Title = "自动写入剪贴板",
            Description = "截图或 OCR 识别完成后写入剪贴板",
            ShowDivider = true,
            ActionControl = toggleCopy
        };

        var toggleSave = new ModernToggleSwitch { Checked = ConfigService.Current.AutoSavePictures };
        toggleSave.CheckedChanged += (_, _) => { ConfigService.Current.AutoSavePictures = toggleSave.Checked; ConfigService.Save(); };
        var rowSave = new SettingItemRow
        {
            Title = "自动保存原图到本地",
            Description = "截图完成后自动归档为 PNG",
            ShowDivider = true,
            ActionControl = toggleSave
        };

        var btnOpenDir = new ModernButton
        {
            Text = "打开目录",
            IsPrimary = false,
            CornerRadius = 6,
            Size = new Size(76, 26)
        };
        btnOpenDir.Click += (_, _) =>
        {
            var dir = ConfigService.GetEffectiveSavePath();
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start("explorer.exe", dir);
        };
        var rowPath = new SettingItemRow
        {
            Title = "本地保存目录",
            Description = @"系统图片目录 \ ZSnaper",
            ShowDivider = false,
            ActionControl = btnOpenDir
        };

        rowToolbarPlacement.SetBounds(0, 0, group2.Width, 52);
        rowToolbarItems.SetBounds(0, 52, group2.Width, 52);
        rowCopy.SetBounds(0, 104, group2.Width, 52);
        rowSave.SetBounds(0, 156, group2.Width, 52);
        rowPath.SetBounds(0, 208, group2.Width, 52);
        rowToolbarPlacement.Anchor = rowToolbarItems.Anchor = rowCopy.Anchor = rowSave.Anchor = rowPath.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        group2.Controls.Add(rowToolbarPlacement);
        group2.Controls.Add(rowToolbarItems);
        group2.Controls.Add(toolbarEditor);
        group2.Controls.Add(rowCopy);
        group2.Controls.Add(rowSave);
        group2.Controls.Add(rowPath);

        // 分组 3：系统提醒
        var group3 = new ModernCard
        {
            CornerRadius = 10,
            Location = new Point(0, 580),
            Size = new Size(contentWidth, 52),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var toggleNotify = new ModernToggleSwitch { Checked = ConfigService.Current.ShowNotification };
        toggleNotify.CheckedChanged += (_, _) => { ConfigService.Current.ShowNotification = toggleNotify.Checked; ConfigService.Save(); };
        var rowNotify = new SettingItemRow
        {
            Title = "操作完成状态气泡",
            Description = string.Empty,
            ShowDivider = false,
            ActionControl = toggleNotify
        };
        rowNotify.SetBounds(0, 0, group3.Width, 52);
        rowNotify.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        group3.Controls.Add(rowNotify);

        var footerHint = new Label
        {
            Text = "设置会自动保存，并立即应用到当前窗口",
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular),
            ForeColor = ThemeManager.Palette.TextMuted,
            AutoSize = true,
            Location = new Point(4, 648)
        };
        BindThemeColor(footerHint, palette => palette.TextMuted);

        var systemSectionLabel = CreateSectionLabel("系统", 554);

        btnCustomizeToolbar.Click += (_, _) =>
        {
            bool expanded = !toolbarEditor.Visible;
            int editorHeight = expanded ? toolbarEditor.Height : 0;

            toolbarEditor.Visible = expanded;
            btnCustomizeToolbar.Text = expanded ? "收起" : "展开";
            btnCustomizeToolbar.Icon = expanded ? LucideIcon.Minus : LucideIcon.Sliders;

            rowCopy.Top = 104 + editorHeight;
            rowSave.Top = 156 + editorHeight;
            rowPath.Top = 208 + editorHeight;
            group2.Height = 260 + editorHeight;

            systemSectionLabel.Top = 554 + editorHeight;
            group3.Top = 580 + editorHeight;
            footerHint.Top = 648 + editorHeight;
            scrollPanel.ContentHeight = 690 + editorHeight;

            group2.Invalidate(true);
            scrollPanel.Content.Invalidate(true);
        };

        scrollPanel.Content.Controls.Add(CreateSectionLabel("外观与个性化", 2));
        scrollPanel.Content.Controls.Add(group1);
        scrollPanel.Content.Controls.Add(CreateSectionLabel("截图与工作流", 252));
        scrollPanel.Content.Controls.Add(group2);
        scrollPanel.Content.Controls.Add(systemSectionLabel);
        scrollPanel.Content.Controls.Add(group3);
        scrollPanel.Content.Controls.Add(footerHint);

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(scrollPanel);

        return panel;
    }

    // 页面 5：关于
    private Panel CreateAboutPage()
    {
        var panel = CreateBasePage();
        int contentWidth = panel.Width - 16;

        var titleLabel = new Label
        {
            Text = "关于 ZSnaper",
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(0, 2)
        };

        var brandSurface = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(0, 36),
            Size = new Size(contentWidth, 70),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        brandSurface.Paint += (_, e) =>
        {
            LogoRenderer.DrawFullBrandLogo(
                e.Graphics,
                0f,
                8f,
                50f,
                ThemeManager.Palette.TextPrimary,
                ThemeManager.Palette.TextPrimary);
        };

        var productLabel = new Label
        {
            Text = "Powered By ZZBuAoYe",
            Font = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular),
            Location = new Point(1, 110),
            Size = new Size(contentWidth, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var separator = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(0, 136),
            Size = new Size(contentWidth, 1),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        separator.Paint += (_, e) =>
        {
            using var pen = new Pen(ThemeManager.Palette.SeparatorColor, 1f);
            e.Graphics.DrawLine(pen, 0, 0, separator.Width, 0);
        };

        var infoItems = new List<(string Caption, string Value)>();
        infoItems.Add(("VERSION", AppVersionInfo.DisplayVersion));
        if (AppVersionInfo.ShowChannel)
        {
            infoItems.Add(("CHANNEL", AppVersionInfo.Channel));
        }
        infoItems.Add(("BUILD NUMBER", AppVersionInfo.BuildNumber));
        infoItems.Add(("BUILD DATE", AppVersionInfo.BuildDate));
        infoItems.Add(("BUILD COUNT", $"#{AppVersionInfo.BuildCount}"));
        infoItems.Add(("PLATFORM", "Windows"));
        infoItems.Add(("RUNTIME", ".NET 8"));

        var captionLabels = new List<Label>();
        var valueLabels = new List<Label>();

        int startY = 146;
        int rowStep = 25;

        for (int i = 0; i < infoItems.Count; i++)
        {
            int y = startY + i * rowStep;
            var (caption, val) = infoItems[i];

            var capLabel = new Label
            {
                Text = caption,
                Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular),
                Location = new Point(1, y),
                Size = new Size(106, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            var valLabel = new Label
            {
                Text = val,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                Location = new Point(112, y),
                Size = new Size(contentWidth - 112, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                TextAlign = ContentAlignment.MiddleLeft
            };

            captionLabels.Add(capLabel);
            valueLabels.Add(valLabel);
            panel.Controls.Add(capLabel);
            panel.Controls.Add(valLabel);
        }

        Action applyAboutTheme = () =>
        {
            titleLabel.ForeColor = ThemeManager.Palette.TextPrimary;
            productLabel.ForeColor = ThemeManager.Palette.TextMuted;
            foreach (var cap in captionLabels) cap.ForeColor = ThemeManager.Palette.TextMuted;
            foreach (var val in valueLabels) val.ForeColor = ThemeManager.Palette.TextSecondary;
            brandSurface.Invalidate();
            separator.Invalidate();
        };
        applyAboutTheme();
        ThemeManager.ThemeChanged += applyAboutTheme;
        panel.Disposed += (_, _) => ThemeManager.ThemeChanged -= applyAboutTheme;

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(brandSurface);
        panel.Controls.Add(productLabel);
        panel.Controls.Add(separator);

        return panel;
    }

    // 窗口自由拖拽
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && e.Y < 55)
        {
            NativeMethods.DragWindow(Handle);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _mainSurfaceDirty = true;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        ThemePalette palette = ThemeManager.Palette;
        Size surfaceSize = ClientSize;
        if (_mainSurfaceDirty || _mainSurface.Size != surfaceSize)
        {
            _mainSurface.Render(surfaceSize, canvas =>
            {
                DrawSkiaBackground(canvas, surfaceSize, palette);
                DrawLogoArea(canvas, palette);
            });
            _mainSurfaceDirty = false;
        }

        // Skia renders the client in one cached layer. Native WinForms child
        // controls remain responsible for input, accessibility and composition.
        _mainSurface.Draw(e.Graphics, Point.Empty);
    }

    private static void DrawSkiaBackground(SKCanvas canvas, Size size, ThemePalette palette)
    {
        canvas.Clear(SkiaDrawing.ToSkColor(palette.BackgroundColor));
        if (!ThemeManager.EnableGlow || palette.GlowColor1.A == 0) return;

        bool isLight = palette.Mode == ThemeMode.Light;
        DrawSoftGlowOrb(
            canvas,
            size.Width * 0.15f,
            size.Height * 0.20f,
            size.Width * 0.75f,
            palette.GlowColor1,
            (byte)(isLight ? 16 : 22));
        DrawSoftGlowOrb(
            canvas,
            size.Width * 0.85f,
            size.Height * 0.85f,
            size.Width * 0.85f,
            palette.GlowColor2,
            (byte)(isLight ? 14 : 18));
    }

    private static void DrawSoftGlowOrb(
        SKCanvas canvas,
        float centerX,
        float centerY,
        float radius,
        Color color,
        byte centerAlpha)
    {
        SKColor rgb = SkiaDrawing.ToSkColor(color);
        SKColor[] colors =
        [
            rgb.WithAlpha(centerAlpha),
            rgb.WithAlpha((byte)(centerAlpha * 0.92f)),
            rgb.WithAlpha((byte)(centerAlpha * 0.72f)),
            rgb.WithAlpha((byte)(centerAlpha * 0.40f)),
            rgb.WithAlpha(0)
        ];
        float[] stops = [0f, 0.15f, 0.35f, 0.65f, 1f];
        using SKShader shader = SKShader.CreateRadialGradient(
            new SKPoint(centerX, centerY),
            Math.Max(1f, radius),
            colors,
            stops,
            SKShaderTileMode.Clamp);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = shader
        };
        canvas.DrawCircle(centerX, centerY, Math.Max(1f, radius), paint);
    }

    private static void DrawLogoArea(SKCanvas canvas, ThemePalette palette)
    {
        Color logoColor = palette.Mode == ThemeMode.Light ? Color.FromArgb(24, 28, 38) : Color.FromArgb(250, 250, 255);

        SkiaDrawing.DrawLogo(canvas, 16, 14, 28, logoColor);
        SkiaDrawing.DrawText(
            canvas,
            "ZSnaper",
            "Segoe UI",
            16.7f,
            palette.TextPrimary,
            50,
            22,
            SKFontStyleWeight.Bold);
        SkiaDrawing.DrawText(
            canvas,
            "v0.1",
            "Segoe UI",
            10f,
            palette.TextMuted,
            52,
            36.5f,
            SKFontStyleWeight.Bold);
    }
}
