using System.Drawing.Drawing2D;
using ZSnaper.Installer.Core;

namespace ZSnaper.UpdateInstaller;

internal sealed class UpdateForm : Form
{
    private readonly InstallerService _installerService = new();
    private readonly UpdatePackageService _packageService;
    private readonly TextBox _packagePath = new();
    private readonly Label _details = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private Button _applyButton = null!;
    private bool _busy;

    public UpdateForm(string? packagePath)
    {
        _packageService = new UpdatePackageService(_installerService);
        InitializeUi();
        if (!string.IsNullOrWhiteSpace(packagePath))
        {
            _packagePath.Text = packagePath;
            LoadPackageDetails();
        }
    }

    private void InitializeUi()
    {
        Text = "ZSnaper Update";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(650, 390);
        BackColor = Color.FromArgb(247, 248, 250);
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        Panel header = new()
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = Color.FromArgb(28, 31, 38)
        };
        header.Paint += (_, eventArgs) =>
        {
            using LinearGradientBrush brush = new(header.ClientRectangle, Color.FromArgb(28, 31, 38), Color.FromArgb(61, 67, 83), 0F);
            eventArgs.Graphics.FillRectangle(brush, header.ClientRectangle);
        };
        header.Controls.Add(new Label
        {
            Text = "ZSnaper Update",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 23F, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(31, 21)
        });
        header.Controls.Add(new Label
        {
            Text = "Apply a checksum-verified update package",
            ForeColor = Color.FromArgb(202, 208, 220),
            AutoSize = true,
            Location = new Point(34, 61)
        });
        Controls.Add(header);

        Controls.Add(new Label
        {
            Text = "Update package",
            AutoSize = true,
            Location = new Point(34, 126),
            ForeColor = Color.FromArgb(35, 39, 46)
        });
        _packagePath.Location = new Point(34, 151);
        _packagePath.Size = new Size(475, 31);
        _packagePath.BorderStyle = BorderStyle.FixedSingle;
        _packagePath.TextChanged += (_, _) => LoadPackageDetails();
        Controls.Add(_packagePath);

        Button browseButton = CreateButton("Browse...", new Point(520, 149), new Size(96, 35));
        browseButton.Click += BrowseButtonOnClick;
        Controls.Add(browseButton);

        _details.AutoSize = false;
        _details.Location = new Point(34, 202);
        _details.Size = new Size(582, 58);
        _details.ForeColor = Color.FromArgb(87, 93, 104);
        _details.Text = "Select a .zup package to inspect its target version.";
        Controls.Add(_details);

        _status.AutoSize = false;
        _status.Location = new Point(34, 285);
        _status.Size = new Size(390, 30);
        _status.ForeColor = Color.FromArgb(87, 93, 104);
        _status.Text = "Ready.";
        Controls.Add(_status);

        _progress.Location = new Point(34, 326);
        _progress.Size = new Size(390, 12);
        Controls.Add(_progress);

        _applyButton = CreateButton("Update", new Point(477, 286), new Size(139, 52));
        _applyButton.BackColor = Color.FromArgb(38, 105, 210);
        _applyButton.ForeColor = Color.White;
        _applyButton.Enabled = false;
        _applyButton.Click += ApplyButtonOnClick;
        Controls.Add(_applyButton);
    }

    private static Button CreateButton(string text, Point location, Size size) => new()
    {
        Text = text,
        Location = location,
        Size = size,
        FlatStyle = FlatStyle.Flat,
        FlatAppearance = { BorderSize = 0 },
        BackColor = Color.FromArgb(230, 233, 239),
        ForeColor = Color.FromArgb(35, 39, 46),
        Cursor = Cursors.Hand
    };

    private void BrowseButtonOnClick(object? sender, EventArgs e)
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "ZSnaper update package (*.zup)|*.zup|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _packagePath.Text = dialog.FileName;
        }
    }

    private void LoadPackageDetails()
    {
        if (_busy || string.IsNullOrWhiteSpace(_packagePath.Text) || !File.Exists(_packagePath.Text))
        {
            if (!_busy)
            {
                _details.Text = "Select a .zup package to inspect its target version.";
            }

            return;
        }

        try
        {
            UpdateManifest manifest = _packageService.ReadManifest(_packagePath.Text);
            InstallationInfo? installation = _installerService.GetInstalled();
            string installed = installation is null ? "No registered installation" : installation.Version;
            _details.Text = $"Installed: {installed}    Update: {manifest.From} → {manifest.To}\r\nFiles: {manifest.Files.Count} changed, {manifest.Delete.Count} removed";
            _applyButton.Enabled = installation is not null;
            _status.Text = installation is null ? "Install ZSnaper before applying an update." : "Ready.";
        }
        catch (Exception exception)
        {
            _details.Text = exception.Message;
            _applyButton.Enabled = false;
        }
    }

    private async void ApplyButtonOnClick(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            InstallationInfo installation = _installerService.GetInstalled()
                ?? throw new InvalidOperationException("ZSnaper is not installed.");
            if (!File.Exists(_packagePath.Text))
            {
                throw new FileNotFoundException("Select a valid .zup package first.");
            }

            Progress<InstallProgress> progress = new(UpdateProgress);
            await Task.Run(() => _packageService.Apply(_packagePath.Text, installation, progress));
            _progress.Value = 100;
            _status.Text = "Update completed.";
            MessageBox.Show(this, "ZSnaper was updated successfully.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception exception)
        {
            _status.Text = "Update failed.";
            MessageBox.Show(this, exception.Message, "Update failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetBusy(false);
        }
    }

    private void UpdateProgress(InstallProgress progress)
    {
        _status.Text = progress.Stage;
        _progress.Value = progress.Total <= 0
            ? 0
            : Math.Clamp(progress.Completed * 100 / progress.Total, 0, 100);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _applyButton.Enabled = !busy;
        _packagePath.Enabled = !busy;
        _progress.Value = busy ? 0 : _progress.Value;
    }
}
