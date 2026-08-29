using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ZSnaper.Installer.Core;

namespace ZSnaper.FullInstaller;

internal sealed class InstallerForm : Form
{
    private const int WindowWidth = 920;
    private const int WindowHeight = 620;
    private const int TitleBarHeight = 52;
    private const int FooterHeight = 82;
    private const int NavigationWidth = 228;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRound = 2;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int DwmSystemBackdropMica = 2;

    private readonly InstallerService _installerService = new();
    private readonly string? _payloadDirectory;
    private readonly string _installerExecutable;
    private readonly string _version;
    private readonly Panel _pageHost = new();
    private readonly Panel _footer = new();
    private TableLayoutPanel _layout = null!;
    private WelcomeCanvas? _welcomeCanvas;
    private RoundedButton _backButton = null!;
    private RoundedButton _primaryButton = null!;
    private Label _footerStatus = null!;
    private SlimProgressBar _progress = null!;
    private TextBox _installPath = null!;
    private ModernCheckBox _agreement = null!;
    private ModernCheckBox _desktopShortcut = null!;
    private ModernCheckBox _startMenuShortcut = null!;
    private ModernCheckBox _autoStart = null!;
    private string? _installedDirectory;
    private int _pageIndex;
    private bool _busy;
    private bool _micaEnabled;

    public InstallerForm(string? payloadDirectory, string installerExecutable, string version)
    {
        _payloadDirectory = payloadDirectory;
        _installerExecutable = installerExecutable;
        _version = version;
        InitializeUi();
        ShowPage(0);
    }

    public int ExitCode { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            CreateParams parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    private void InitializeUi()
    {
        Text = "ZSnaper 安装向导";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(WindowWidth, WindowHeight);
        MinimumSize = MaximumSize = Size;
        BackColor = InstallerPalette.Window;
        Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        Icon = LoadWindowIcon();

        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = InstallerPalette.Window
        };
        _layout = layout;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, TitleBarHeight));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, FooterHeight));
        Controls.Add(layout);

        Panel titleBar = CreateTitleBar();
        titleBar.Dock = DockStyle.Fill;
        layout.Controls.Add(titleBar, 0, 0);

        _footer.Dock = DockStyle.Fill;
        _footer.BackColor = InstallerPalette.Window;
        _footer.Margin = Padding.Empty;
        layout.Controls.Add(_footer, 0, 2);

        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = Color.Transparent;
        _pageHost.Margin = Padding.Empty;
        layout.Controls.Add(_pageHost, 0, 1);

        BuildFooterControls();
        FormClosing += (_, e) =>
        {
            if (_busy)
            {
                e.Cancel = true;
                _footerStatus.Text = "正在写入程序文件，请稍候…";
            }
        };
    }

    private Panel CreateTitleBar()
    {
        Panel titleBar = new()
        {
            Dock = DockStyle.Fill,
            BackColor = InstallerPalette.Window,
            Margin = Padding.Empty
        };

        Image? logo = LoadLogo();
        if (logo is not null)
        {
            titleBar.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(18, 13),
                Size = new Size(26, 26),
                BackColor = Color.Transparent
            });
        }

        Label title = new()
        {
            Text = "ZSnaper  安装向导",
            AutoSize = true,
            ForeColor = Color.FromArgb(232, 236, 244),
            Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular),
            Location = new Point(53, 16),
            BackColor = Color.Transparent
        };
        titleBar.Controls.Add(title);

        Label version = new()
        {
            Text = _version,
            AutoSize = true,
            ForeColor = Color.FromArgb(119, 130, 151),
            Font = new Font("Segoe UI", 8.5F),
            Location = new Point(184, 17),
            BackColor = Color.Transparent
        };
        titleBar.Controls.Add(version);

        Panel windowButtons = new()
        {
            Dock = DockStyle.Right,
            Width = 96,
            BackColor = InstallerPalette.Window,
            Margin = Padding.Empty
        };
        titleBar.Controls.Add(windowButtons);

        ChromeButton close = new(ChromeGlyph.Close) { Location = new Point(48, 0) };
        close.Click += (_, _) => Close();
        windowButtons.Controls.Add(close);

        ChromeButton minimize = new(ChromeGlyph.Minimize) { Location = Point.Empty };
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        windowButtons.Controls.Add(minimize);

        AttachWindowDrag(titleBar);
        AttachWindowDrag(title);
        AttachWindowDrag(version);
        return titleBar;
    }

    private void BuildFooterControls()
    {
        _backButton = CreateButton("返回", new Point(260, 18), new Size(108, 46), accent: false);
        _backButton.AccessibleName = "返回上一步";
        _backButton.Click += (_, _) =>
        {
            if (!_busy && _pageIndex > 0)
            {
                ShowPage(_pageIndex - 1);
            }
        };
        _footer.Controls.Add(_backButton);

        _footerStatus = new Label
        {
            AutoSize = false,
            Location = new Point(390, 18),
            Size = new Size(310, 24),
            ForeColor = InstallerPalette.MutedText,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };
        _footer.Controls.Add(_footerStatus);

        _progress = new SlimProgressBar
        {
            Location = new Point(390, 51),
            Size = new Size(310, 4),
            Visible = false
        };
        _footer.Controls.Add(_progress);

        _primaryButton = CreateButton("开始安装", new Point(742, 15), new Size(144, 52), accent: true);
        _primaryButton.AccessibleName = "继续";
        _primaryButton.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Regular);
        _primaryButton.Click += PrimaryButtonOnClick;
        _footer.Controls.Add(_primaryButton);
    }

    private void ShowPage(int pageIndex)
    {
        _pageIndex = pageIndex;
        _pageHost.Controls.Clear();
        _welcomeCanvas = null;
        _progress.Visible = false;
        _progress.Value = 0;
        _backButton.Visible = pageIndex is 1 or 2;
        _primaryButton.Enabled = true;
        _primaryButton.Visible = true;

        ConfigureFooter(pageIndex);
        switch (pageIndex)
        {
            case 0:
                _pageHost.Controls.Add(CreateWelcomePage());
                _footerStatus.Text = $"版本 {_version}  ·  Windows x64";
                _primaryButton.Text = "开始安装";
                break;
            case 1:
                _pageHost.Controls.Add(CreateAgreementPage());
                _footerStatus.Text = "阅读并同意协议后继续";
                _primaryButton.Text = "下一步";
                _primaryButton.Enabled = _agreement.Checked;
                break;
            case 2:
                _pageHost.Controls.Add(CreateInstallPage());
                _footerStatus.Text = "确认安装位置";
                _primaryButton.Text = "立即安装";
                break;
            case 3:
                _pageHost.Controls.Add(CreateCompletionPage());
                _footerStatus.Text = "最后选择常用入口";
                _primaryButton.Text = "完成并启动";
                _backButton.Visible = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pageIndex));
        }
    }

    internal void ShowPreviewPage(int pageIndex) => ShowPage(pageIndex);

    private void ConfigureFooter(int pageIndex)
    {
        bool welcome = pageIndex == 0;
        _footer.Visible = !welcome;
        _layout.RowStyles[2].Height = welcome ? 0F : FooterHeight;
        _footer.BackColor = welcome ? InstallerPalette.Window : Color.White;
        _footerStatus.Location = welcome ? new Point(36, 27) : new Point(390, 18);
        _footerStatus.Size = welcome ? new Size(500, 24) : new Size(310, 24);
        _footerStatus.ForeColor = welcome ? Color.FromArgb(126, 138, 158) : InstallerPalette.MutedText;
        _progress.Location = welcome ? new Point(36, 56) : new Point(390, 51);
        _progress.Size = welcome ? new Size(500, 4) : new Size(310, 4);

        Control? navigationFooter = _footer.Controls["NavigationFooter"];
        if (navigationFooter is not null)
        {
            _footer.Controls.Remove(navigationFooter);
            navigationFooter.Dispose();
        }

        if (!welcome)
        {
            Panel darkStrip = new()
            {
                Name = "NavigationFooter",
                BackColor = InstallerPalette.WindowRaised,
                Location = Point.Empty,
                Size = new Size(NavigationWidth, FooterHeight)
            };
            _footer.Controls.Add(darkStrip);
            darkStrip.SendToBack();
        }
    }

    private Control CreateWelcomePage()
    {
        WelcomeCanvas page = new()
        {
            Dock = DockStyle.Fill,
            UseMicaBackdrop = _micaEnabled
        };
        _welcomeCanvas = page;

        Image? logo = LoadLogo();
        if (logo is not null)
        {
            page.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(401, 76),
                Size = new Size(118, 118),
                BackColor = Color.Transparent
            });
        }
        else
        {
            page.Controls.Add(CreateFallbackLogo(new Point(405, 80), 110));
        }

        page.Controls.Add(new Label
        {
            Text = "ZSNAPER",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 28F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(260, 242),
            Size = new Size(400, 52),
            BackColor = Color.Transparent
        });
        NextButton next = new()
        {
            Location = new Point(372, 366),
            Size = new Size(176, 56),
            AccessibleName = "Next"
        };
        next.Click += (_, _) =>
        {
            if (!_busy)
            {
                ShowPage(1);
            }
        };
        page.Controls.Add(next);
        return page;
    }

    private Control CreateAgreementPage()
    {
        (Panel page, Panel content) = CreatePageShell(1, "使用协议", "请在继续安装前阅读以下内容");
        Panel agreementCard = CreateCard(new Point(48, 147), new Size(596, 226));
        content.Controls.Add(agreementCard);
        agreementCard.Controls.Add(new RichTextBox
        {
            Location = new Point(18, 15),
            Size = new Size(560, 196),
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(74, 82, 97),
            Font = new Font("Microsoft YaHei UI", 9F),
            DetectUrls = false,
            TabStop = true,
            Text = "ZSnaper 使用协议\r\n\r\n" +
                   "1. ZSnaper 是面向 Windows 的截图、OCR 与效率辅助工具。\r\n" +
                   "2. 本安装器只会将程序文件写入你选择的安装目录，并仅在你选择时创建快捷方式或开机启动项。\r\n" +
                   "3. 用户配置位于 %APPDATA%\\ZSnaper。卸载程序默认保留这些配置，便于后续恢复使用。\r\n" +
                   "4. 请在遵守当地法律法规以及目标应用服务条款的前提下使用 ZSnaper。"
        });

        _agreement = new ModernCheckBox
        {
            Text = "我已阅读并同意 ZSnaper 使用协议",
            Location = new Point(49, 393),
            Size = new Size(400, 32),
            Checked = false,
            AccessibleName = "同意 ZSnaper 使用协议"
        };
        _agreement.CheckedChanged += (_, _) =>
        {
            if (!_busy && _pageIndex == 1)
            {
                _primaryButton.Enabled = _agreement.Checked;
            }
        };
        content.Controls.Add(_agreement);
        return page;
    }

    private Control CreateInstallPage()
    {
        (Panel page, Panel content) = CreatePageShell(2, "选择安装位置", "程序文件将写入下方目录，现有用户配置不会被覆盖");
        Panel card = CreateCard(new Point(48, 157), new Size(596, 126));
        content.Controls.Add(card);
        card.Controls.Add(new Label
        {
            Text = "安装目录",
            AutoSize = true,
            Location = new Point(20, 16),
            ForeColor = InstallerPalette.Text,
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
        });

        Panel pathBorder = new()
        {
            BackColor = InstallerPalette.Border,
            Location = new Point(20, 49),
            Size = new Size(430, 45),
            Padding = new Padding(1)
        };
        card.Controls.Add(pathBorder);
        Panel pathSurface = new()
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            Padding = new Padding(12, 10, 12, 8)
        };
        pathBorder.Controls.Add(pathSurface);
        _installPath = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = InstallerPalette.Text,
            Font = new Font("Microsoft YaHei UI", 9.5F),
            Text = _installerService.GetInstalled()?.InstallDirectory ?? InstallerPaths.DefaultInstallDirectory,
            Margin = Padding.Empty
        };
        pathSurface.Controls.Add(_installPath);

        RoundedButton browse = CreateButton("浏览", new Point(466, 49), new Size(108, 45), accent: false);
        browse.AccessibleName = "浏览安装目录";
        browse.Click += BrowseButtonOnClick;
        card.Controls.Add(browse);

        _installedDirectory = _installerService.GetInstalled()?.InstallDirectory;
        string description = _installedDirectory is null
            ? "首次安装会注册卸载信息；卸载时默认保留你的截图偏好和快捷键配置。"
            : "已检测到现有安装。继续会安全更新程序文件，并保留你的全部用户配置。";
        content.Controls.Add(new Label
        {
            Text = description,
            AutoSize = false,
            Location = new Point(50, 311),
            Size = new Size(590, 28),
            ForeColor = InstallerPalette.MutedText,
            Font = new Font("Microsoft YaHei UI", 8.7F)
        });

        Panel tip = new()
        {
            BackColor = Color.FromArgb(235, 242, 255),
            Location = new Point(48, 355),
            Size = new Size(596, 58)
        };
        tip.Controls.Add(new Label
        {
            Text = "i",
            ForeColor = InstallerPalette.Accent,
            Font = new Font("Segoe UI Semibold", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(15, 17),
            Size = new Size(22, 22)
        });
        tip.Controls.Add(new Label
        {
            Text = "安装完成后再选择桌面快捷方式、开始菜单入口和开机自启动。",
            ForeColor = Color.FromArgb(77, 96, 132),
            AutoSize = true,
            Location = new Point(45, 19),
            Font = new Font("Microsoft YaHei UI", 8.5F)
        });
        content.Controls.Add(tip);
        return page;
    }

    private Control CreateCompletionPage()
    {
        (Panel page, Panel content) = CreatePageShell(3, "安装完成", "程序文件已就绪，再选择你习惯的启动方式");
        Panel successCard = CreateCard(new Point(48, 150), new Size(596, 82));
        content.Controls.Add(successCard);
        successCard.Controls.Add(new SuccessMark
        {
            Location = new Point(20, 15),
            Size = new Size(52, 52)
        });
        successCard.Controls.Add(new Label
        {
            Text = "ZSnaper 已安装到你的电脑",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            ForeColor = InstallerPalette.Text,
            Location = new Point(88, 18)
        });
        successCard.Controls.Add(new Label
        {
            Text = "点击完成后会应用以下设置并启动应用",
            AutoSize = true,
            ForeColor = InstallerPalette.MutedText,
            Font = new Font("Microsoft YaHei UI", 8.5F),
            Location = new Point(89, 47)
        });

        _desktopShortcut = CreateOptionCheckBox("创建桌面快捷方式", new Point(50, 259), true);
        _startMenuShortcut = CreateOptionCheckBox("创建开始菜单快捷方式", new Point(50, 303), true);
        _autoStart = CreateOptionCheckBox("开机时最小化启动 ZSnaper", new Point(50, 347), false);
        content.Controls.Add(_desktopShortcut);
        content.Controls.Add(_startMenuShortcut);
        content.Controls.Add(_autoStart);
        content.Controls.Add(new Label
        {
            Text = "开机启动会使用 --startup 参数，应用将直接进入托盘，不打扰桌面。",
            AutoSize = true,
            ForeColor = InstallerPalette.MutedText,
            Font = new Font("Microsoft YaHei UI", 8.3F),
            Location = new Point(50, 405)
        });
        return page;
    }

    private (Panel Page, Panel Content) CreatePageShell(int activeStep, string title, string subtitle)
    {
        Panel page = new() { Dock = DockStyle.Fill, BackColor = InstallerPalette.Surface };
        Panel navigation = new()
        {
            Dock = DockStyle.Left,
            Width = NavigationWidth,
            BackColor = InstallerPalette.WindowRaised
        };
        page.Controls.Add(navigation);
        AddNavigationBrand(navigation);
        AddStep(navigation, 1, "使用协议", 157, activeStep);
        AddStep(navigation, 2, "安装位置", 215, activeStep);
        AddStep(navigation, 3, "完成设置", 273, activeStep);
        navigation.Controls.Add(new Label
        {
            Text = "ZSNAPER  /  SETUP",
            AutoSize = true,
            ForeColor = Color.FromArgb(78, 91, 115),
            Font = new Font("Segoe UI Semibold", 7.5F),
            Location = new Point(30, 430)
        });

        Panel content = new() { Dock = DockStyle.Fill, BackColor = InstallerPalette.Surface };
        page.Controls.Add(content);
        content.BringToFront();
        content.Controls.Add(new Label
        {
            Text = $"0{activeStep + 1}  /  04",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold),
            ForeColor = InstallerPalette.Accent,
            Location = new Point(48, 39)
        });
        content.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 23F, FontStyle.Bold),
            ForeColor = InstallerPalette.Text,
            Location = new Point(44, 66)
        });
        content.Controls.Add(new Label
        {
            Text = subtitle,
            AutoSize = true,
            ForeColor = InstallerPalette.MutedText,
            Font = new Font("Microsoft YaHei UI", 9F),
            Location = new Point(49, 116)
        });
        return (page, content);
    }

    private static void AddNavigationBrand(Control navigation)
    {
        Image? logo = LoadLogo();
        if (logo is not null)
        {
            navigation.Controls.Add(new PictureBox
            {
                Image = logo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(28, 29),
                Size = new Size(42, 42),
                BackColor = Color.Transparent
            });
        }

        navigation.Controls.Add(new Label
        {
            Text = "ZSnaper",
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold),
            Location = new Point(80, 30)
        });
        navigation.Controls.Add(new Label
        {
            Text = "轻快、纯净、专注",
            AutoSize = true,
            ForeColor = Color.FromArgb(112, 126, 151),
            Font = new Font("Microsoft YaHei UI", 8F),
            Location = new Point(82, 61)
        });
    }

    private static void AddStep(Control navigation, int step, string text, int y, int activeStep)
    {
        bool active = step == activeStep;
        bool complete = step < activeStep;
        navigation.Controls.Add(new Label
        {
            Text = complete ? "✓" : step.ToString(),
            ForeColor = active || complete ? Color.White : Color.FromArgb(113, 126, 149),
            BackColor = active || complete ? InstallerPalette.Accent : Color.FromArgb(36, 43, 58),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(30, y),
            Size = new Size(28, 28)
        });
        navigation.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = active ? Color.White : complete ? Color.FromArgb(173, 185, 205) : Color.FromArgb(105, 118, 141),
            Font = new Font("Microsoft YaHei UI", 9F, active ? FontStyle.Bold : FontStyle.Regular),
            Location = new Point(72, y + 4)
        });
    }

    private static Panel CreateCard(Point location, Size size) => new()
    {
        Location = location,
        Size = size,
        BackColor = Color.White
    };

    private static ModernCheckBox CreateOptionCheckBox(string text, Point location, bool isChecked) => new()
    {
        Text = text,
        Location = location,
        Size = new Size(430, 32),
        Checked = isChecked,
        AccessibleName = text
    };

    private static RoundedButton CreateButton(string text, Point location, Size size, bool accent) => new()
    {
        Text = text,
        Location = location,
        Size = size,
        Accent = accent,
        AccessibleRole = AccessibleRole.PushButton,
        AccessibleName = text
    };

    private static Control CreateFallbackLogo(Point location, int size) => new FallbackLogo
    {
        Location = location,
        Size = new Size(size, size)
    };

    private void BrowseButtonOnClick(object? sender, EventArgs e)
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "选择 ZSnaper 安装目录",
            SelectedPath = _installPath.Text,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _installPath.Text = dialog.SelectedPath;
        }
    }

    private async void PrimaryButtonOnClick(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        switch (_pageIndex)
        {
            case 0:
                ShowPage(1);
                break;
            case 1 when _agreement.Checked:
                ShowPage(2);
                break;
            case 2:
                await InstallApplicationAsync();
                break;
            case 3:
                await FinishInstallationAsync();
                break;
        }
    }

    private async Task InstallApplicationAsync()
    {
        if (_payloadDirectory is null)
        {
            MessageBox.Show(this, "当前是未打包的开发构建。请运行 Build-Installers.ps1 生成包含应用文件的发布安装器。", "需要发布安装器", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!InstallerPaths.IsUsableInstallDirectory(_installPath.Text, out string error))
        {
            MessageBox.Show(this, error, "安装目录无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _installedDirectory = InstallerPaths.Normalize(_installPath.Text);
        _busy = true;
        SetNavigationBusy(true);
        _progress.Visible = true;
        try
        {
            InstallOptions options = new(_installedDirectory, _version, false, false, false, false);
            Progress<InstallProgress> progress = new(UpdateProgress);
            await Task.Run(() => _installerService.Install(_payloadDirectory, _installerExecutable, options, progress));
            ExitCode = 0;
            SetNavigationBusy(false);
            _busy = false;
            ShowPage(3);
        }
        catch (Exception exception)
        {
            _busy = false;
            _progress.Visible = false;
            SetNavigationBusy(false);
            MessageBox.Show(this, exception.Message, "安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task FinishInstallationAsync()
    {
        if (string.IsNullOrWhiteSpace(_installedDirectory))
        {
            Close();
            return;
        }

        _busy = true;
        SetNavigationBusy(true);
        try
        {
            string installDirectory = _installedDirectory;
            await Task.Run(() => _installerService.ApplyOptionalSettings(installDirectory, _desktopShortcut.Checked, _startMenuShortcut.Checked, _autoStart.Checked));
            string applicationPath = Path.Combine(installDirectory, InstallerPaths.ProductExecutableName);
            if (File.Exists(applicationPath))
            {
                Process.Start(new ProcessStartInfo { FileName = applicationPath, WorkingDirectory = installDirectory, UseShellExecute = true });
            }

            ExitCode = 0;
            Close();
        }
        catch (Exception exception)
        {
            _busy = false;
            SetNavigationBusy(false);
            MessageBox.Show(this, exception.Message, "完成设置失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateProgress(InstallProgress progress)
    {
        _footerStatus.Text = progress.Stage;
        _progress.Value = progress.Total <= 0 ? 0 : Math.Clamp(progress.Completed * 100 / progress.Total, 0, 100);
    }

    private void SetNavigationBusy(bool busy)
    {
        _backButton.Enabled = !busy;
        _primaryButton.Enabled = !busy && (_pageIndex != 1 || _agreement.Checked);
        _installPath.Enabled = !busy;
    }

    private void AttachWindowDrag(Control control)
    {
        control.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            ReleaseCapture();
            SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyWindowBackdrop();
    }

    private void ApplyWindowBackdrop()
    {
        _micaEnabled = false;
        try
        {
            int darkMode = 1;
            _ = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));

            int preference = DwmRound;
            _ = DwmSetWindowAttribute(Handle, DwmWindowCornerPreference, ref preference, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int backdrop = DwmSystemBackdropMica;
                int result = DwmSetWindowAttribute(Handle, DwmSystemBackdropType, ref backdrop, sizeof(int));
                _micaEnabled = result == 0;
            }
        }
        catch (DllNotFoundException)
        {
            _micaEnabled = false;
        }
        catch (EntryPointNotFoundException)
        {
            _micaEnabled = false;
        }

        // WinForms Form does not support Color.Transparent. Keep the top-level
        // surface opaque and let the child layout controls handle transparency.
        BackColor = InstallerPalette.Window;
        _layout.BackColor = _micaEnabled ? Color.Transparent : InstallerPalette.Window;
        if (_micaEnabled)
        {
            Region?.Dispose();
            Region = null;
        }
        else
        {
            ApplyFallbackRegion();
        }

        if (_welcomeCanvas is not null)
        {
            _welcomeCanvas.UseMicaBackdrop = _micaEnabled;
            _welcomeCanvas.Invalidate();
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsHandleCreated && !_micaEnabled)
        {
            ApplyFallbackRegion();
        }
    }

    private void ApplyFallbackRegion()
    {
        using GraphicsPath path = InstallerPalette.RoundedRectangle(new Rectangle(0, 0, Width, Height), 12);
        Region?.Dispose();
        Region = new Region(path);
    }

    private static Image? LoadLogo()
    {
        using Stream? stream = typeof(InstallerForm).Assembly.GetManifestResourceStream("ZSnaper.Installer.ZSnaper.ico");
        if (stream is null)
        {
            return null;
        }

        using Icon icon = new(stream);
        return icon.ToBitmap();
    }

    private static Icon? LoadWindowIcon()
    {
        using Stream? stream = typeof(InstallerForm).Assembly.GetManifestResourceStream("ZSnaper.Installer.ZSnaper.ico");
        return stream is null ? null : new Icon(stream);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, int wordParameter, int longParameter);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

    private sealed class SuccessMark : Control
    {
        public SuccessMark() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using SolidBrush brush = new(InstallerPalette.Success);
            e.Graphics.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
            using Pen pen = new(Color.White, 3F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            e.Graphics.DrawLines(pen, new[]
            {
                new Point(15, 27),
                new Point(23, 35),
                new Point(38, 18)
            });
        }
    }

    private sealed class FallbackLogo : Control
    {
        public FallbackLogo() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new(1, 1, Width - 2, Height - 2);
            using GraphicsPath path = InstallerPalette.RoundedRectangle(bounds, Math.Max(12, Width / 5));
            using LinearGradientBrush brush = new(bounds, InstallerPalette.Accent, Color.FromArgb(93, 90, 245), 45F);
            e.Graphics.FillPath(brush, path);
            using Font font = new("Segoe UI Semibold", Width * 0.48F, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, "Z", font, bounds, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
