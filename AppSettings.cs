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

    public List<DisplayModeProfile> Profiles { get; set; } = new();

    public string SelectedProfileId { get; set; } = "";

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
                return defaultSettings;
            }

            string json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

            settings.EnsureProfiles();

            return settings;
        }
        catch
        {
            var fallbackSettings = new AppSettings();
            fallbackSettings.EnsureProfiles();
            return fallbackSettings;
        }
    }

    public void Save()
    {
        EnsureProfiles();

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
        if (Profiles is null)
        {
            Profiles = new List<DisplayModeProfile>();
        }

        if (Profiles.Count == 0)
        {
            Profiles.Add(new DisplayModeProfile
            {
                Id = "desk-mode",
                Name = "Desk Mode",
                PrimaryDisplayName = DeskMode.PrimaryDisplayName,
                EnabledDisplays = new List<string>(DeskMode.EnabledDisplays),
                DisabledDisplays = new List<string>(DeskMode.DisabledDisplays),
                HotkeyCtrl = DeskHotkeyCtrl,
                HotkeyAlt = DeskHotkeyAlt,
                HotkeyShift = DeskHotkeyShift,
                HotkeyWin = DeskHotkeyWin,
                HotkeyKey = DeskHotkeyKey,
                LauncherPath = LauncherPath,
                LauncherProcessName = LauncherProcessName,
                LaunchAppAfterSwitch = false,
                CloseLauncherAfterSwitch = CloseAppOnDeskMode
            });

            Profiles.Add(new DisplayModeProfile
            {
                Id = "tv-gaming-mode",
                Name = "TV Gaming Mode",
                PrimaryDisplayName = TvGamingMode.PrimaryDisplayName,
                EnabledDisplays = new List<string>(TvGamingMode.EnabledDisplays),
                DisabledDisplays = new List<string>(TvGamingMode.DisabledDisplays),
                HotkeyCtrl = TvHotkeyCtrl,
                HotkeyAlt = TvHotkeyAlt,
                HotkeyShift = TvHotkeyShift,
                HotkeyWin = TvHotkeyWin,
                HotkeyKey = TvHotkeyKey,
                LauncherPath = LauncherPath,
                LauncherProcessName = LauncherProcessName,
                LaunchAppAfterSwitch = LaunchAppOnTvMode,
                CloseLauncherAfterSwitch = false
            });
        }

        for (int i = 0; i < Profiles.Count; i++)
        {
            Profiles[i].EnsureDefaults($"Profile {i + 1}");
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileId) ||
            !Profiles.Exists(profile => string.Equals(profile.Id, SelectedProfileId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedProfileId = Profiles.Count > 0 ? Profiles[0].Id : "";
        }
    }
}