using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DisplayDeck;

public sealed class AppSettings
{
    public string LauncherPath { get; set; } = @"C:\Program Files\Winhanced\Winhanced.exe";

    public string LauncherProcessName { get; set; } = "Winhanced";

    public bool LaunchAppOnTvMode { get; set; } = true;

    public bool CloseAppOnDeskMode { get; set; } = true;

    public bool StartWithWindows { get; set; } = false;

    public bool TvHotkeyCtrl { get; set; } = true;

    public bool TvHotkeyAlt { get; set; } = true;

    public bool TvHotkeyShift { get; set; } = false;

    public bool TvHotkeyWin { get; set; } = false;

    public string TvHotkeyKey { get; set; } = "G";

    public bool DeskHotkeyCtrl { get; set; } = true;

    public bool DeskHotkeyAlt { get; set; } = true;

    public bool DeskHotkeyShift { get; set; } = false;

    public bool DeskHotkeyWin { get; set; } = false;

    public string DeskHotkeyKey { get; set; } = "D";

    public int CommandDelayMs { get; set; } = 1500;

    public string TvDisplayName { get; set; } = @"\\.\DISPLAY2";

    public string MainDisplayName { get; set; } = @"\\.\DISPLAY1";

    public string SecondaryDisplayName { get; set; } = @"\\.\DISPLAY3";

    public DisplayModeProfile DeskMode { get; set; } = new()
    {
        Name = "Desk Mode",
        PrimaryDisplayName = @"\\.\DISPLAY1",
        EnabledDisplays = new List<string> { @"\\.\DISPLAY1", @"\\.\DISPLAY3" },
        DisabledDisplays = new List<string> { @"\\.\DISPLAY2" }
    };

    public DisplayModeProfile TvGamingMode { get; set; } = new()
    {
        Name = "TV Gaming Mode",
        PrimaryDisplayName = @"\\.\DISPLAY2",
        EnabledDisplays = new List<string> { @"\\.\DISPLAY2" },
        DisabledDisplays = new List<string> { @"\\.\DISPLAY1", @"\\.\DISPLAY3" }
    };

    public static string SettingsFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DisplayDeck"
        );

    public static string SettingsFile =>
        Path.Combine(SettingsFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
            {
                var defaultSettings = new AppSettings();
                defaultSettings.EnsureProfiles();
                defaultSettings.EnsureLauncherProcessName();
                defaultSettings.EnsureHotkeys();
                return defaultSettings;
            }

            string json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            settings.EnsureProfiles();
            settings.EnsureLauncherProcessName();
            settings.EnsureHotkeys();

            return settings;
        }
        catch
        {
            var fallbackSettings = new AppSettings();

            fallbackSettings.EnsureProfiles();
            fallbackSettings.EnsureLauncherProcessName();
            fallbackSettings.EnsureHotkeys();

            return fallbackSettings;
        }
    }

    public void Save()
    {
        EnsureProfiles();
        EnsureLauncherProcessName();
        EnsureHotkeys();

        Directory.CreateDirectory(SettingsFolder);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        string json = JsonSerializer.Serialize(this, options);
        File.WriteAllText(SettingsFile, json);
    }

    public void EnsureProfiles()
    {
        if (DeskMode is null)
        {
            DeskMode = new DisplayModeProfile();
        }

        if (TvGamingMode is null)
        {
            TvGamingMode = new DisplayModeProfile();
        }

        if (DeskMode.EnabledDisplays.Count == 0 && DeskMode.DisabledDisplays.Count == 0)
        {
            DeskMode = new DisplayModeProfile
            {
                Name = "Desk Mode",
                PrimaryDisplayName = MainDisplayName,
                EnabledDisplays = new List<string> { MainDisplayName, SecondaryDisplayName },
                DisabledDisplays = new List<string> { TvDisplayName }
            };
        }

        if (TvGamingMode.EnabledDisplays.Count == 0 && TvGamingMode.DisabledDisplays.Count == 0)
        {
            TvGamingMode = new DisplayModeProfile
            {
                Name = "TV Gaming Mode",
                PrimaryDisplayName = TvDisplayName,
                EnabledDisplays = new List<string> { TvDisplayName },
                DisabledDisplays = new List<string> { MainDisplayName, SecondaryDisplayName }
            };
        }
    }

    public void EnsureLauncherProcessName()
    {
        if (!string.IsNullOrWhiteSpace(LauncherProcessName))
        {
            LauncherProcessName = LauncherProcessName
                .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
                .Trim();

            return;
        }

        if (!string.IsNullOrWhiteSpace(LauncherPath))
        {
            LauncherProcessName = Path.GetFileNameWithoutExtension(LauncherPath);
        }
    }

    public void EnsureHotkeys()
    {
        if (string.IsNullOrWhiteSpace(TvHotkeyKey))
        {
            TvHotkeyKey = "G";
        }

        if (!TvHotkeyCtrl && !TvHotkeyAlt && !TvHotkeyShift && !TvHotkeyWin)
        {
            TvHotkeyCtrl = true;
            TvHotkeyAlt = true;
        }

        TvHotkeyKey = TvHotkeyKey.Trim().ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(DeskHotkeyKey))
        {
            DeskHotkeyKey = "D";
        }

        if (!DeskHotkeyCtrl && !DeskHotkeyAlt && !DeskHotkeyShift && !DeskHotkeyWin)
        {
            DeskHotkeyCtrl = true;
            DeskHotkeyAlt = true;
        }

        DeskHotkeyKey = DeskHotkeyKey.Trim().ToUpperInvariant();
    }
}