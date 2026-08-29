using System.Text.Json;
using ZSnaper.Controls;
using ZSnaper.Models;
using ZSnaper.Services;

namespace ZSnaper.Stability;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        TestConfigurationNormalization();
        TestForceHotkeyValidation();
        TestHotkeyRecordingModeSwitch();
        TestAtomicConfigurationRecovery();
        TestCaptureResourceGuards();
        Console.WriteLine("Application stability tests passed.");
        return 0;
    }

    private static void TestForceHotkeyValidation()
    {
        var delete = new HotkeyGesture(Keys.Delete, Keys.None);
        Assert(!delete.IsValid, "Standalone Delete unexpectedly became a normal hotkey.");
        Assert(delete.IsValidForForceBinding, "Standalone Delete was rejected for force binding.");
        Assert(
            !HotkeyGesture.TryParse("Delete", out _),
            "Standalone Delete unexpectedly parsed as a normal hotkey.");
        Assert(
            HotkeyGesture.TryParse("Delete", out HotkeyGesture parsedDelete, forceBinding: true) &&
            parsedDelete == delete,
            "Standalone Delete did not parse as a force-bound hotkey.");
        Assert(
            !new HotkeyGesture(Keys.Escape, Keys.None).IsValidForForceBinding,
            "Escape must remain reserved for cancelling recording.");

        var config = new AppConfig
        {
            CaptureHotkey = "Delete",
            CaptureHotkeyForceBinding = true,
            OcrHotkey = "Alt+X"
        };
        AppConfigSanitizer.Normalize(config);
        Assert(
            config.CaptureHotkey == "Delete" && config.CaptureHotkeyForceBinding,
            "Configuration normalization discarded a force-bound standalone Delete key.");
    }

    private static void TestHotkeyRecordingModeSwitch()
    {
        using var recorder = new HotkeyRecorder();
        int beginCount = 0;
        int endCount = 0;
        recorder.BeginRecordingRequest = _ =>
        {
            beginCount++;
            return new HotkeyChangeResult(true, string.Empty);
        };
        recorder.EndRecordingRequest = () =>
        {
            endCount++;
            return new HotkeyChangeResult(true, string.Empty);
        };

        recorder.StartRecording(forceBinding: false);
        Assert(recorder.IsRecording && !recorder.IsForceRecording, "Normal hotkey recording did not start.");

        recorder.StartRecording(forceBinding: true);
        Assert(recorder.IsRecording && recorder.IsForceRecording, "Recording did not switch from normal to force mode.");
        Assert(beginCount == 2 && endCount == 1, "Switching recording mode did not close the previous session exactly once.");

        recorder.CancelExternalRecording();
        Assert(!recorder.IsRecording && endCount == 2, "Cancelling force recording did not close the active session.");
    }

    private static void TestConfigurationNormalization()
    {
        var config = new AppConfig
        {
            Theme = (ThemeMode)999,
            AnimationMode = (AnimationLevel)999,
            ToolbarPlacement = (ToolbarPlacementMode)999,
            CaptureToolbarLayout = CaptureToolbarLayout.Custom,
            CaptureToolbarItems = [CaptureToolbarItem.Copy, CaptureToolbarItem.Copy, (CaptureToolbarItem)999],
            CaptureToolbarOrder = [CaptureToolbarItem.Copy, CaptureToolbarItem.Copy, (CaptureToolbarItem)999],
            ConfirmButtonBehavior = (ConfirmButtonBehavior)999,
            AnnotationToolBehavior = (AnnotationToolBehavior)999,
            AnnotationArrowStyle = (AnnotationArrowStyle)999,
            AccentColorHex = "not-a-color",
            AnnotationColorHex = "#123",
            ToolbarAutoHorizontalBias = double.NaN,
            ToolbarAutoSampleCount = int.MaxValue,
            AnnotationFontSize = float.PositiveInfinity,
            AnnotationPenWidth = -10,
            AnnotationMosaicSize = 999,
            AnnotationMosaicPixelSize = -1,
            AnnotationFontStyle = int.MaxValue,
            CaptureHotkey = "invalid",
            OcrHotkey = "Alt+Q",
            UpdateCheckIntervalHours = 1,
            LastUpdateCheckAt = DateTimeOffset.UtcNow.AddDays(3),
            TrayIconCustomPalette = ["invalid", "#ffffff", "#FFFFFF"]
        };

        AppConfigSanitizer.Normalize(config);

        Assert(config.Theme == ThemeMode.Light, "Invalid theme was not repaired.");
        Assert(config.AnimationMode == AnimationLevel.Balanced, "Invalid animation mode was not repaired.");
        Assert(config.ToolbarPlacement == ToolbarPlacementMode.Auto, "Invalid toolbar placement was not repaired.");
        Assert(config.ConfirmButtonBehavior == ConfirmButtonBehavior.Copy, "Invalid confirm behavior was not repaired.");
        Assert(config.AccentColorHex == "#10B981", "Invalid accent color was not repaired.");
        Assert(double.IsFinite(config.ToolbarAutoHorizontalBias), "Non-finite toolbar bias survived normalization.");
        Assert(config.ToolbarAutoSampleCount == 10_000, "Toolbar sample count was not bounded.");
        Assert(config.AnnotationFontSize == 18f, "Non-finite annotation font size was not repaired.");
        Assert(config.AnnotationPenWidth == 1f && config.AnnotationMosaicSize == 80f, "Annotation dimensions were not bounded.");
        Assert(config.AnnotationFontStyle == (int)FontStyle.Regular, "Invalid font style survived normalization.");
        Assert(config.CaptureHotkey == "Alt+Q" && config.OcrHotkey == "Alt+X", "Invalid or duplicate hotkeys were not repaired.");
        Assert(config.UpdateCheckIntervalHours == 24 && config.LastUpdateCheckAt is null, "Invalid update schedule survived normalization.");
        Assert(config.CaptureToolbarOrder.Distinct().Count() == CaptureToolbarDefaults.CreateItems().Count, "Toolbar order was not repaired.");
        Assert(config.CaptureToolbarItems.SequenceEqual([CaptureToolbarItem.Copy]), "Custom toolbar selection was not preserved safely.");
        Assert(config.TrayIconCustomPalette.SequenceEqual(["#FFFFFF"]), "Tray palette was not normalized and deduplicated.");
    }

    private static void TestAtomicConfigurationRecovery()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ZSnaper-Stability-" + Guid.NewGuid().ToString("N"));
        string configPath = Path.Combine(directory, "config.json");
        string backupPath = configPath + ".bak";
        var options = new JsonSerializerOptions { WriteIndented = true };
        try
        {
            string first = JsonSerializer.Serialize(new AppConfig { Theme = ThemeMode.Light }, options);
            ConfigFileStore.WriteAtomic(configPath, first, backupPath, backupExisting: false);
            string second = JsonSerializer.Serialize(new AppConfig { Theme = ThemeMode.Dark }, options);
            ConfigFileStore.WriteAtomic(configPath, second, backupPath, backupExisting: true);

            Assert(ConfigFileStore.TryRead(configPath, options, out AppConfig current) && current.Theme == ThemeMode.Dark,
                "Atomic write did not publish the new configuration.");
            Assert(ConfigFileStore.TryRead(backupPath, options, out AppConfig backup) && backup.Theme == ThemeMode.Light,
                "Atomic write did not retain the previous configuration.");

            File.WriteAllText(configPath, "{ broken json");
            Assert(!ConfigFileStore.TryRead(configPath, options, out _), "Corrupted configuration was accepted.");
            Assert(ConfigFileStore.TryRead(backupPath, options, out backup) && backup.Theme == ThemeMode.Light,
                "Backup configuration was not recoverable.");
            Assert(!Directory.EnumerateFiles(directory, "*.tmp").Any(), "Atomic writer left a temporary file behind.");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void TestCaptureResourceGuards()
    {
        bool rejected = false;
        try
        {
            using Bitmap _ = CaptureService.CaptureScreen(Rectangle.Empty);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejected = true;
        }

        Assert(rejected, "An empty screen capture was not rejected before allocating GDI resources.");

        string directory = Path.Combine(Path.GetTempPath(), "ZSnaper-Capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var bitmap = new Bitmap(2, 2);
            string first = CaptureService.SaveToDirectory(bitmap, directory);
            string second = CaptureService.SaveToDirectory(bitmap, directory);
            Assert(first != second && File.Exists(first) && File.Exists(second), "Rapid saves reused a file name or lost output.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
