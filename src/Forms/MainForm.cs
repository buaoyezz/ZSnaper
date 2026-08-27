using System.Drawing.Drawing2D;
using System.Text.Json;
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
    private ModernButton _updateButton = null!;
    private SettingItemRow _lastUpdateRow = null!;
    private string? _homeLatestFilePath;
    private string? _latestUpdateUrl;
    private Action<HotkeyCommand, HotkeyGesture>? _recordedHotkeyHandler;
    private Action? _cancelRecordedHotkeyHandler;
    private readonly CancellationTokenSource _welcomeQuoteCancellation = new();
    private string _welcomeQuote = "保持好奇，保持创造。";
    private string? _welcomeQuoteSource = "ZSnaper";

    // 事件
    public event Action<bool>? RequestCapture;
    public event Func<HotkeyCommand, HotkeyGesture, bool, HotkeyChangeResult>? RequestHotkeyChange;
    public event Func<HotkeyCommand, bool, HotkeyChangeResult>? RequestHotkeyRecordingStart;
    public event Func<HotkeyCommand, HotkeyChangeResult>? RequestHotkeyRecordingStop;
    public event Action? RequestUpdateCheck;
    public event Action<string>? RequestOpenUpdate;

    public void ApplyRecordedHotkey(HotkeyCommand command, HotkeyGesture gesture) =>
        _recordedHotkeyHandler?.Invoke(command, gesture);

    public void CancelRecordedHotkey() => _cancelRecordedHotkeyHandler?.Invoke();

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
            _welcomeQuoteCancellation.Cancel();
            _welcomeQuoteCancellation.Dispose();
            _mainSurface.Dispose();
        };

        // 加载时启用 Windows 11 原生圆角与 DWM 阴影
        Load += (_, _) =>
        {
            UpdateDwmEffect();
            _ = LoadReleaseQuoteAsync();
        };
    }

    private async Task LoadReleaseQuoteAsync()
    {
        if (!AppVersionInfo.IsReleaseBuild)
        {
            return;
        }

        try
        {
            HitokotoSentence? quote = await HitokotoService.FetchAsync(_welcomeQuoteCancellation.Token);
            if (quote is null || IsDisposed || Disposing)
            {
                return;
            }

            _welcomeQuote = quote.Text;
            _welcomeQuoteSource = quote.Source;
            _mainSurfaceDirty = true;
            Invalidate();
        }
        catch (OperationCanceledException)
        {
            // Closing the form cancels the optional network request.
        }
        catch (HttpRequestException)
        {
            // Keep the local sentence when the public service is unavailable.
        }
        catch (JsonException)
        {
            // Ignore malformed responses and keep the local sentence.
        }
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
            Text = "普通绑定不会抢占其他软件快捷键；按键被占用时点击“强力绑定”。强力绑定会拦截该按键或组合键，需要权限时会弹出 UAC 请求。",
            Font = new Font("Microsoft YaHei UI", 8.5f),
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(1, 39)
        };
        descriptionLabel.Size = new Size(contentWidth, 18);

        HotkeyGesture captureGesture = HotkeyGesture.TryParse(ConfigService.Current.CaptureHotkey, out HotkeyGesture parsedCapture)
            ? parsedCapture
            : new HotkeyGesture(Keys.Q, Keys.Alt);
        HotkeyGesture ocrGesture = HotkeyGesture.TryParse(ConfigService.Current.OcrHotkey, out HotkeyGesture parsedOcr)
            ? parsedOcr
            : new HotkeyGesture(Keys.X, Keys.Alt);
        Label feedbackLabel = null!;
        bool feedbackIsError = false;

        var captureCard = CreateHotkeyCard(
            "截图",
            "截取选区并按当前设置保存或复制",
            captureGesture,
            HotkeyCommand.Capture,
            ConfigService.Current.CaptureHotkeyForceBinding,
            68,
            out HotkeyRecorder captureRecorder,
            out Label captureName,
            out Label captureDescription,
            out ModernButton captureForceButton);

        var ocrCard = CreateHotkeyCard(
            "读取文字",
            "截取选区并调用本地 OCR",
            ocrGesture,
            HotkeyCommand.Ocr,
            ConfigService.Current.OcrHotkeyForceBinding,
            146,
            out HotkeyRecorder ocrRecorder,
            out Label ocrName,
            out Label ocrDescription,
            out ModernButton ocrForceButton);

        feedbackLabel = new Label
        {
            Text = "点击右侧快捷键框开始修改",
            Font = new Font("Microsoft YaHei UI", 8.1f),
            AutoSize = false,
            AutoEllipsis = true,
            Location = new Point(1, 223),
            Size = new Size(contentWidth, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
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

        _recordedHotkeyHandler = (command, gesture) =>
        {
            if (command == HotkeyCommand.Capture)
            {
                captureRecorder.CommitRecordedGesture(gesture);
            }
            else
            {
                ocrRecorder.CommitRecordedGesture(gesture);
            }
        };
        _cancelRecordedHotkeyHandler = () =>
        {
            captureRecorder.CancelExternalRecording();
            ocrRecorder.CancelExternalRecording();
        };

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
        panel.Disposed += (_, _) =>
        {
            _recordedHotkeyHandler = null;
            _cancelRecordedHotkeyHandler = null;
        };

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
            bool forceBinding,
            int top,
            out HotkeyRecorder recorder,
            out Label nameLabel,
            out Label descLabel,
            out ModernButton forceButton)
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
                Size = new Size(Math.Max(1, contentWidth - 302), 19),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            HotkeyRecorder createdRecorder = new()
            {
                Gesture = gesture,
                Location = new Point(contentWidth - 156, 17),
                Size = new Size(142, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            recorder = createdRecorder;
            createdRecorder.BeginRecordingRequest = useForceBinding => RequestHotkeyRecordingStart?.Invoke(command, useForceBinding)
                ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪");
            createdRecorder.EndRecordingRequest = () => RequestHotkeyRecordingStop?.Invoke(command)
                ?? new HotkeyChangeResult(true, string.Empty);

            ModernButton createdForceButton = new()
            {
                Text = forceBinding ? "解除强力" : "强力绑定",
                Icon = LucideIcon.ShieldCheck,
                IsPrimary = false,
                Font = new Font("Microsoft YaHei UI", 8.1f),
                Size = new Size(118, 30),
                Location = new Point(contentWidth - 282, 19),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AccessibleRole = AccessibleRole.PushButton,
                AccessibleName = forceBinding ? "解除强力绑定" : "强力绑定",
                AccessibleDescription = "强力绑定会拦截已被其他程序占用的按键或组合键，需要权限时会弹出 UAC 请求"
            };
            forceButton = createdForceButton;
            void RefreshForceButton()
            {
                createdForceButton.Text = forceBinding ? "解除强力" : "强力绑定";
                createdForceButton.AccessibleName = forceBinding ? "解除强力绑定" : "强力绑定";
                createdForceButton.Invalidate();
            }

            createdRecorder.TryCommit = (proposed, useForceBinding) =>
            {
                HotkeyChangeResult result = RequestHotkeyChange?.Invoke(command, proposed, useForceBinding)
                    ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪");
                if (result.Success)
                {
                    forceBinding = useForceBinding;
                    RefreshForceButton();
                }

                return result;
            };
            createdForceButton.Click += (_, _) =>
            {
                if (forceBinding)
                {
                    HotkeyChangeResult result = RequestHotkeyChange?.Invoke(command, createdRecorder.Gesture, false)
                        ?? new HotkeyChangeResult(false, "快捷键服务尚未就绪");
                    if (result.Success)
                    {
                        forceBinding = false;
                        RefreshForceButton();
                    }

                    ShowFeedback(result);
                    return;
                }

                createdRecorder.StartRecording(forceBinding: true);
            };

            card.Controls.Add(nameLabel);
            card.Controls.Add(descLabel);
            card.Controls.Add(forceButton);
            card.Controls.Add(recorder);
            return card;
        }
    }

    // 页面 4：偏好设置
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
            Text = "按类别管理外观、截图、工具栏与更新",
            Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Regular),
            ForeColor = ThemeManager.Palette.TextMuted,
            AutoSize = true,
            Location = new Point(1, 27)
        };
        BindThemeColor(subtitleLabel, palette => palette.TextMuted);

        var tabBar = new SettingsTabBar
        {
            Location = new Point(0, 52),
            Size = new Size(panel.Width, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var scrollPanel = new ModernScrollPanel
        {
            Location = new Point(0, 94),
            Size = new Size(panel.Width, panel.Height - 94),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        int contentWidth = Math.Max(320, scrollPanel.Content.ClientSize.Width - 4);

        Label CreateSectionLabel(string text)
        {
            var label = new Label
            {
                Text = text,
                Font = new Font("Microsoft YaHei UI", 8.4f, FontStyle.Bold),
                ForeColor = ThemeManager.Palette.TextSecondary,
                AutoSize = false,
                Size = new Size(contentWidth, 20),
                TextAlign = ContentAlignment.MiddleLeft,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            BindThemeColor(label, palette => palette.TextSecondary);
            return label;
        }

        ModernCard CreateSettingsCard(int height) => new()
        {
            CornerRadius = 10,
            Size = new Size(contentWidth, height),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // 外观：只放影响窗口视觉的选项。
        var appearanceCard = CreateSettingsCard(520);
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

        var trayStyleOptions = new[]
        {
            (Style: TrayIconStyle.FollowTheme, Label: "跟随主题"),
            (Style: TrayIconStyle.Light, Label: "浅色图标"),
            (Style: TrayIconStyle.Dark, Label: "深色图标"),
            (Style: TrayIconStyle.CustomSvg, Label: "自定义 SVG"),
            (Style: TrayIconStyle.LegacyBlack, Label: "黑底图标")
        };
        var trayStyleDropdown = new ModernDropdown
        {
            Font = new Font("Microsoft YaHei UI", 8f),
            Size = new Size(150, 28),
            AccessibleName = "托盘图标样式",
            AccessibleDescription = "选择托盘图标的主题和 SVG 来源"
        };
        trayStyleDropdown.SetItems(trayStyleOptions.Select(option => option.Label));
        int trayStyleIndex = 0;
        for (int index = 0; index < trayStyleOptions.Length; index++)
        {
            if (trayStyleOptions[index].Style != ConfigService.Current.TrayIconStyle) continue;
            trayStyleIndex = index;
            break;
        }
        trayStyleDropdown.SelectedIndex = trayStyleIndex;

        static Color ReadTrayIconColor(string value, Color fallback)
        {
            try
            {
                return Color.FromArgb(255, ColorTranslator.FromHtml(value));
            }
            catch
            {
                return fallback;
            }
        }

        static string WriteTrayIconColor(Color color) =>
            $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        Color[] configuredTrayIconColors = ConfigService.Current.TrayIconCustomPalette
            .Select(value => ReadTrayIconColor(value, Color.White))
            .ToArray();
        var lightTrayIconColor = new TrayIconColorButton
        {
            Color = ReadTrayIconColor(ConfigService.Current.TrayIconLightColorHex, Color.FromArgb(56, 60, 64)),
            CustomColors = configuredTrayIconColors
        };
        var darkTrayIconColor = new TrayIconColorButton
        {
            Color = ReadTrayIconColor(ConfigService.Current.TrayIconDarkColorHex, Color.White),
            CustomColors = configuredTrayIconColors
        };
        bool paletteTargetsDark = false;
        var trayIconPalette = new TrayIconPaletteControl();
        trayIconPalette.SetColors(configuredTrayIconColors);
        var trayIconScale = new TrayIconScaleControl
        {
            Value = ConfigService.Current.TrayIconScalePercent,
            Enabled = ConfigService.Current.TrayIconStyle != TrayIconStyle.LegacyBlack
        };

        void RefreshTrayIconCustomColors()
        {
            Color[] colors = trayIconPalette.Colors.ToArray();
            lightTrayIconColor.CustomColors = colors;
            darkTrayIconColor.CustomColors = colors;
        }

        void SaveTrayIconColor(bool dark, Color color)
        {
            if (dark)
            {
                ConfigService.Current.TrayIconDarkColorHex = WriteTrayIconColor(color);
            }
            else
            {
                ConfigService.Current.TrayIconLightColorHex = WriteTrayIconColor(color);
            }
            ConfigService.Save();
        }

        lightTrayIconColor.MouseDown += (_, _) => paletteTargetsDark = false;
        darkTrayIconColor.MouseDown += (_, _) => paletteTargetsDark = true;
        lightTrayIconColor.ColorChanged += (_, _) =>
            SaveTrayIconColor(false, lightTrayIconColor.Color);
        darkTrayIconColor.ColorChanged += (_, _) =>
            SaveTrayIconColor(true, darkTrayIconColor.Color);
        trayIconPalette.ColorSelected += color =>
        {
            if (paletteTargetsDark)
            {
                darkTrayIconColor.Color = color;
                SaveTrayIconColor(true, color);
            }
            else
            {
                lightTrayIconColor.Color = color;
                SaveTrayIconColor(false, color);
            }
        };
        trayIconPalette.PaletteChanged += colors =>
        {
            ConfigService.Current.TrayIconCustomPalette = colors
                .Select(WriteTrayIconColor)
                .ToList();
            RefreshTrayIconCustomColors();
            ConfigService.Save();
        };

        trayIconScale.ValueCommitted += (_, _) =>
        {
            ConfigService.Current.TrayIconScalePercent = trayIconScale.Value;
            ConfigService.Save();
        };

        trayStyleDropdown.SelectedIndexChanged += (_, _) =>
        {
            int selected = trayStyleDropdown.SelectedIndex;
            if (selected < 0 || selected >= trayStyleOptions.Length) return;
            ConfigService.Current.TrayIconStyle = trayStyleOptions[selected].Style;
            trayIconScale.Enabled = ConfigService.Current.TrayIconStyle != TrayIconStyle.LegacyBlack;
            ConfigService.Save();
        };

        var btnChooseTrayIconSvg = new ModernButton
        {
            Text = "选择 SVG",
            IsPrimary = false,
            CornerRadius = 6,
            Icon = LucideIcon.Folder,
            IconSize = 14,
            IconGap = 5,
            Size = new Size(104, 28)
        };
        var rowTrayIconSvg = new SettingItemRow
        {
            Title = "自定义 SVG 文件",
            Description = string.IsNullOrWhiteSpace(ConfigService.Current.TrayIconSvgPath)
                ? "未选择时使用内置 Logo SVG"
                : Path.GetFileName(ConfigService.Current.TrayIconSvgPath),
            ShowDivider = true,
            ActionControl = btnChooseTrayIconSvg
        };
        btnChooseTrayIconSvg.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择托盘图标 SVG",
                Filter = "SVG 文件 (*.svg)|*.svg|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(panel.FindForm()) != DialogResult.OK) return;

            ConfigService.Current.TrayIconSvgPath = dialog.FileName;
            rowTrayIconSvg.Description = Path.GetFileName(dialog.FileName);
            ConfigService.Current.TrayIconStyle = TrayIconStyle.CustomSvg;
            trayStyleDropdown.SelectedIndex = 3;
            ConfigService.Save();
        };

        var rowTrayIconStyle = new SettingItemRow
        {
            Title = "托盘图标样式",
            Description = "自动切换 Light / Dark SVG，也可固定或使用自定义文件",
            ShowDivider = true,
            ActionControl = trayStyleDropdown
        };
        var rowTrayIconLightColor = new SettingItemRow
        {
            Title = "浅色模式图标颜色",
            Description = "内置和自定义 SVG 的填充/描边颜色",
            ShowDivider = true,
            ActionControl = lightTrayIconColor
        };
        var rowTrayIconDarkColor = new SettingItemRow
        {
            Title = "深色模式图标颜色",
            Description = "内置和自定义 SVG 的填充/描边颜色",
            ShowDivider = true,
            ActionControl = darkTrayIconColor
        };
        var rowTrayIconPalette = new SettingItemRow
        {
            Title = "SVG 调色板",
            Description = "左键应用到最近选择的颜色，右键编辑色块",
            ShowDivider = true,
            ActionControl = trayIconPalette
        };
        var rowTrayIconScale = new SettingItemRow
        {
            Title = "SVG 托盘图标大小",
            Description = "可调 80% - 160%；黑底图标不支持缩放",
            ShowDivider = false,
            ActionControl = trayIconScale
        };

        rowTheme.SetBounds(0, 0, appearanceCard.Width, 52);
        rowAccent.SetBounds(0, 52, appearanceCard.Width, 52);
        rowGlow.SetBounds(0, 104, appearanceCard.Width, 52);
        rowAnim.SetBounds(0, 156, appearanceCard.Width, 52);
        rowTrayIconStyle.SetBounds(0, 208, appearanceCard.Width, 52);
        rowTrayIconSvg.SetBounds(0, 260, appearanceCard.Width, 52);
        rowTrayIconLightColor.SetBounds(0, 312, appearanceCard.Width, 52);
        rowTrayIconDarkColor.SetBounds(0, 364, appearanceCard.Width, 52);
        rowTrayIconPalette.SetBounds(0, 416, appearanceCard.Width, 52);
        rowTrayIconScale.SetBounds(0, 468, appearanceCard.Width, 52);
        rowTheme.Anchor = rowAccent.Anchor = rowGlow.Anchor = rowAnim.Anchor =
            rowTrayIconStyle.Anchor = rowTrayIconSvg.Anchor = rowTrayIconLightColor.Anchor =
            rowTrayIconDarkColor.Anchor = rowTrayIconPalette.Anchor = rowTrayIconScale.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        appearanceCard.Controls.Add(rowTheme);
        appearanceCard.Controls.Add(rowAccent);
        appearanceCard.Controls.Add(rowGlow);
        appearanceCard.Controls.Add(rowAnim);
        appearanceCard.Controls.Add(rowTrayIconStyle);
        appearanceCard.Controls.Add(rowTrayIconSvg);
        appearanceCard.Controls.Add(rowTrayIconLightColor);
        appearanceCard.Controls.Add(rowTrayIconDarkColor);
        appearanceCard.Controls.Add(rowTrayIconPalette);
        appearanceCard.Controls.Add(rowTrayIconScale);

        // 工具栏与批注：编辑器单独成组，展开时不会把其他设置挤在同一张卡片里。
        var toolbarCard = CreateSettingsCard(104);
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
            Description = "工具顺序、完成行为、颜色、字体与笔刷",
            ShowDivider = false,
            ActionControl = btnCustomizeToolbar
        };

        var toolbarEditor = new ToolbarCustomizationPanel
        {
            Visible = false,
            Location = new Point(0, 104),
            Size = new Size(toolbarCard.Width, 398),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        rowToolbarPlacement.SetBounds(0, 0, toolbarCard.Width, 52);
        rowToolbarItems.SetBounds(0, 52, toolbarCard.Width, 52);
        rowToolbarPlacement.Anchor = rowToolbarItems.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        toolbarCard.Controls.Add(rowToolbarPlacement);
        toolbarCard.Controls.Add(rowToolbarItems);
        toolbarCard.Controls.Add(toolbarEditor);

        // 截图行为：完成后的默认去向和保存位置。
        var workflowCard = CreateSettingsCard(156);
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

        rowCopy.SetBounds(0, 0, workflowCard.Width, 52);
        rowSave.SetBounds(0, 52, workflowCard.Width, 52);
        rowPath.SetBounds(0, 104, workflowCard.Width, 52);
        rowCopy.Anchor = rowSave.Anchor = rowPath.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        workflowCard.Controls.Add(rowCopy);
        workflowCard.Controls.Add(rowSave);
        workflowCard.Controls.Add(rowPath);

        // 更新与系统：与截图行为分开，避免设置页尾部出现过多不同类型的控件。
        var systemCard = CreateSettingsCard(364);
        var rowChannel = new SettingItemRow
        {
            Title = "更新与发布通道",
            Description = "正式版 (稳定) / 公测版 / 内测版",
            ShowDivider = true,
            ActionControl = new ChannelSegmentedControl()
        };
        rowChannel.SetBounds(0, 0, systemCard.Width, 52);

        _updateButton = new ModernButton
        {
            Text = "检查更新",
            IsPrimary = false,
            CornerRadius = 6,
            Icon = LucideIcon.RotateCcw,
            IconSize = 14,
            IconGap = 5,
            Size = new Size(102, 28)
        };
        _updateButton.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_latestUpdateUrl))
            {
                RequestOpenUpdate?.Invoke(_latestUpdateUrl);
            }
            else
            {
                RequestUpdateCheck?.Invoke();
            }
        };

        var rowUpdate = new SettingItemRow
        {
            Title = "检查应用更新",
            Description = $"当前版本 {AppVersionInfo.DisplayVersion}",
            ShowDivider = true,
            ActionControl = _updateButton
        };
        rowUpdate.SetBounds(0, 52, systemCard.Width, 52);

        _lastUpdateRow = new SettingItemRow
        {
            Title = "上次检查",
            Description = FormatLastUpdateCheck(),
            ShowDivider = true
        };
        _lastUpdateRow.SetBounds(0, 104, systemCard.Width, 52);

        var toggleAutoUpdate = new ModernToggleSwitch
        {
            Checked = ConfigService.Current.AutoCheckUpdates
        };
        toggleAutoUpdate.CheckedChanged += (_, _) =>
        {
            ConfigService.Current.AutoCheckUpdates = toggleAutoUpdate.Checked;
            ConfigService.Save();
        };
        var rowAutoUpdate = new SettingItemRow
        {
            Title = "自动检查更新",
            Description = "应用启动后按设定频率自动检查",
            ShowDivider = true,
            ActionControl = toggleAutoUpdate
        };
        rowAutoUpdate.SetBounds(0, 156, systemCard.Width, 52);

        var updateIntervalOptions = new[]
        {
            (Hours: 6, Label: "每 6 小时"),
            (Hours: 12, Label: "每 12 小时"),
            (Hours: 24, Label: "每天"),
            (Hours: 168, Label: "每周")
        };
        var updateIntervalDropdown = new ModernDropdown
        {
            Font = new Font("Microsoft YaHei UI", 8f),
            Size = new Size(112, 28),
            AccessibleName = "自动检查更新间隔",
            AccessibleDescription = "设置自动检查更新的时间间隔"
        };
        updateIntervalDropdown.SetItems(updateIntervalOptions.Select(option => option.Label));
        int updateIntervalIndex = Array.FindIndex(
            updateIntervalOptions,
            option => option.Hours == ConfigService.Current.UpdateCheckIntervalHours);
        updateIntervalDropdown.SelectedIndex = updateIntervalIndex >= 0 ? updateIntervalIndex : 2;
        updateIntervalDropdown.SelectedIndexChanged += (_, _) =>
        {
            int selectedIndex = updateIntervalDropdown.SelectedIndex;
            if (selectedIndex < 0) return;
            ConfigService.Current.UpdateCheckIntervalHours = updateIntervalOptions[selectedIndex].Hours;
            ConfigService.Save();
        };

        var rowUpdateInterval = new SettingItemRow
        {
            Title = "检查频率",
            Description = "自动检查更新的时间间隔",
            ShowDivider = true,
            ActionControl = updateIntervalDropdown
        };
        rowUpdateInterval.SetBounds(0, 208, systemCard.Width, 52);

        var toggleAutoStart = new ModernToggleSwitch
        {
            Checked = ConfigService.IsAutoStartEnabled()
        };
        bool restoringAutoStart = false;
        toggleAutoStart.CheckedChanged += (_, _) =>
        {
            if (restoringAutoStart) return;

            bool enabled = toggleAutoStart.Checked;
            if (ConfigService.SetAutoStart(enabled)) return;

            restoringAutoStart = true;
            toggleAutoStart.Checked = ConfigService.IsAutoStartEnabled();
            restoringAutoStart = false;
            MessageBox.Show(
                "无法更新开机自启动设置，请检查当前用户的注册表权限。",
                "ZSnaper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        };
        var rowAutoStart = new SettingItemRow
        {
            Title = "开机自启动",
            Description = "登录 Windows 后在后台启动并驻留托盘",
            ShowDivider = true,
            ActionControl = toggleAutoStart
        };
        rowAutoStart.SetBounds(0, 260, systemCard.Width, 52);

        var toggleNotify = new ModernToggleSwitch { Checked = ConfigService.Current.ShowNotification };
        toggleNotify.CheckedChanged += (_, _) => { ConfigService.Current.ShowNotification = toggleNotify.Checked; ConfigService.Save(); };
        var rowNotify = new SettingItemRow
        {
            Title = "操作完成状态气泡",
            Description = string.Empty,
            ShowDivider = false,
            ActionControl = toggleNotify
        };
        rowNotify.SetBounds(0, 312, systemCard.Width, 52);

        rowChannel.Anchor = rowUpdate.Anchor = _lastUpdateRow.Anchor = rowAutoUpdate.Anchor =
            rowUpdateInterval.Anchor = rowNotify.Anchor =
            AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        rowAutoStart.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        systemCard.Controls.Add(rowChannel);
        systemCard.Controls.Add(rowUpdate);
        systemCard.Controls.Add(_lastUpdateRow);
        systemCard.Controls.Add(rowAutoUpdate);
        systemCard.Controls.Add(rowUpdateInterval);
        systemCard.Controls.Add(rowAutoStart);
        systemCard.Controls.Add(rowNotify);

        var footerHint = new Label
        {
            Text = "设置会自动保存，并立即应用到当前窗口",
            Font = new Font("Microsoft YaHei UI", 8f, FontStyle.Regular),
            ForeColor = ThemeManager.Palette.TextMuted,
            AutoSize = true,
            Location = new Point(4, 0)
        };
        BindThemeColor(footerHint, palette => palette.TextMuted);

        var appearanceLabel = CreateSectionLabel("外观");
        var toolbarLabel = CreateSectionLabel("工具栏与批注");
        var workflowLabel = CreateSectionLabel("截图行为");
        var systemLabel = CreateSectionLabel("更新与系统");

        void LayoutSettings()
        {
            (Label Label, ModernCard Card)[] sections =
            {
                (appearanceLabel, appearanceCard),
                (workflowLabel, workflowCard),
                (toolbarLabel, toolbarCard),
                (systemLabel, systemCard)
            };

            int selectedIndex = tabBar.SelectedIndex;
            for (int index = 0; index < sections.Length; index++)
            {
                bool visible = index == selectedIndex;
                sections[index].Label.Visible = visible;
                sections[index].Card.Visible = visible;
            }

            (Label selectedLabel, ModernCard selectedCard) = sections[selectedIndex];
            selectedLabel.Top = 4;
            selectedCard.Top = selectedLabel.Bottom + 2;

            footerHint.Visible = true;
            footerHint.Top = selectedCard.Bottom + 16;
            scrollPanel.FitContentHeight(bottomPadding: 18);
        }

        tabBar.SelectedIndexChanged += (_, _) =>
        {
            LayoutSettings();
            scrollPanel.ScrollToTop();
            scrollPanel.Content.Invalidate(true);
        };

        btnCustomizeToolbar.Click += (_, _) =>
        {
            bool expanded = !toolbarEditor.Visible;

            toolbarEditor.Visible = expanded;
            btnCustomizeToolbar.Text = expanded ? "收起" : "展开";
            btnCustomizeToolbar.Icon = expanded ? LucideIcon.Minus : LucideIcon.Sliders;

            toolbarCard.Height = 104 + (expanded ? toolbarEditor.Height : 0);
            LayoutSettings();
            toolbarCard.Invalidate(true);
            scrollPanel.Content.Invalidate(true);
        };

        scrollPanel.Content.Controls.Add(appearanceLabel);
        scrollPanel.Content.Controls.Add(appearanceCard);
        scrollPanel.Content.Controls.Add(toolbarLabel);
        scrollPanel.Content.Controls.Add(toolbarCard);
        scrollPanel.Content.Controls.Add(workflowLabel);
        scrollPanel.Content.Controls.Add(workflowCard);
        scrollPanel.Content.Controls.Add(systemLabel);
        scrollPanel.Content.Controls.Add(systemCard);
        scrollPanel.Content.Controls.Add(footerHint);
        ConfigService.ConfigChanged += RefreshUpdateCheckInfo;
        panel.Disposed += (_, _) => ConfigService.ConfigChanged -= RefreshUpdateCheckInfo;
        LayoutSettings();

        panel.Controls.Add(titleLabel);
        panel.Controls.Add(subtitleLabel);
        panel.Controls.Add(tabBar);
        panel.Controls.Add(scrollPanel);

        return panel;
    }

    public void SetUpdateStatus(string text, bool isBusy, string? releaseUrl = null)
    {
        if (IsDisposed || _updateButton is null) return;

        if (InvokeRequired)
        {
            BeginInvoke(() => SetUpdateStatus(text, isBusy, releaseUrl));
            return;
        }

        _latestUpdateUrl = releaseUrl;
        _updateButton.Text = text;
        _updateButton.Enabled = !isBusy;
        _updateButton.Icon = !isBusy && !string.IsNullOrWhiteSpace(releaseUrl)
            ? LucideIcon.ArrowUpRight
            : LucideIcon.RotateCcw;
        _updateButton.IsPrimary = !isBusy && !string.IsNullOrWhiteSpace(releaseUrl);
    }

    public void RefreshUpdateCheckInfo()
    {
        if (IsDisposed || _lastUpdateRow is null) return;

        if (InvokeRequired)
        {
            BeginInvoke(RefreshUpdateCheckInfo);
            return;
        }

        _lastUpdateRow.Description = FormatLastUpdateCheck();
    }

    private static string FormatLastUpdateCheck()
    {
        return ConfigService.Current.LastUpdateCheckAt is { } lastCheck
            ? $"{lastCheck.LocalDateTime:yyyy-MM-dd HH:mm}"
            : "尚未检查更新";
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

        var infoContainer = new Panel
        {
            BackColor = Color.Transparent,
            Location = new Point(0, 146),
            Size = new Size(contentWidth, 180),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        var captionLabels = new List<Label>();
        var valueLabels = new List<Label>();

        void RefreshInfoRows()
        {
            infoContainer.Controls.Clear();
            captionLabels.Clear();
            valueLabels.Clear();

            var infoItems = new List<(string Caption, string Value)>();
            infoItems.Add(("VERSION", AppVersionInfo.DisplayVersion));
            if (AppVersionInfo.ShowChannel)
            {
                infoItems.Add(("CHANNEL", AppVersionInfo.BuildChannel));
            }
            infoItems.Add(("BUILD NUMBER", AppVersionInfo.BuildNumber));
            infoItems.Add(("BUILD DATE", AppVersionInfo.BuildDate));
            infoItems.Add(("BUILD COUNT", $"#{AppVersionInfo.BuildCount}"));
            infoItems.Add(("PLATFORM", "Windows"));
            infoItems.Add(("RUNTIME", ".NET 8"));

            int rowStep = 25;
            for (int i = 0; i < infoItems.Count; i++)
            {
                int y = i * rowStep;
                var (caption, val) = infoItems[i];

                var capLabel = new Label
                {
                    Text = caption,
                    Font = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Regular),
                    ForeColor = ThemeManager.Palette.TextMuted,
                    Location = new Point(1, y),
                    Size = new Size(106, 20),
                    TextAlign = ContentAlignment.MiddleLeft
                };

                var valLabel = new Label
                {
                    Text = val,
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = ThemeManager.Palette.TextSecondary,
                    Location = new Point(112, y),
                    Size = new Size(contentWidth - 112, 20),
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                    TextAlign = ContentAlignment.MiddleLeft
                };

                captionLabels.Add(capLabel);
                valueLabels.Add(valLabel);
                infoContainer.Controls.Add(capLabel);
                infoContainer.Controls.Add(valLabel);
            }
        }

        RefreshInfoRows();
        ConfigService.ConfigChanged += RefreshInfoRows;
        panel.Disposed += (_, _) => ConfigService.ConfigChanged -= RefreshInfoRows;

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
        panel.Controls.Add(infoContainer);

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

    private void DrawLogoArea(SKCanvas canvas, ThemePalette palette)
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
        string? channelLabel = AppVersionInfo.WelcomeChannelLabel;
        if (!string.IsNullOrEmpty(channelLabel))
        {
            SkiaDrawing.DrawText(
                canvas,
                channelLabel,
                "Segoe UI",
                10f,
                palette.TextMuted,
                52,
                36.5f,
                SKFontStyleWeight.Bold);
        }
        else if (AppVersionInfo.IsReleaseBuild)
        {
            SkiaDrawing.DrawText(
                canvas,
                LimitHeaderText($"“{_welcomeQuote}”", 42),
                "Microsoft YaHei UI",
                9.2f,
                palette.TextMuted,
                52,
                37.5f);

            if (!string.IsNullOrWhiteSpace(_welcomeQuoteSource))
            {
                SkiaDrawing.DrawText(
                    canvas,
                    LimitHeaderText($"— {_welcomeQuoteSource}", 28),
                    "Microsoft YaHei UI",
                    8.2f,
                    palette.TextMuted,
                    52,
                    50.5f);
            }
        }
    }

    private static string LimitHeaderText(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, Math.Max(1, maxLength - 1)), "…");
    }
}
