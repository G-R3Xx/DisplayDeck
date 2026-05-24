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

    public string TvHotkeyKey { get; set; } = "T";

    public bool DeskHotkeyCtrl { get; set; } = true;

    public bool DeskHotkeyAlt { get; set; } = true;

    public bool DeskHotkeyShift { get; set; } = false;

    public bool DeskHotkeyWin { get; set; } = false;

    public string DeskHotkeyKey { get; set; } = "D";

    public int CommandDelayMs { get; set; } = 1500;

    public Dictionary<string, string> DisplayAliases { get; set; } = new();

    public Dictionary<string, SavedDisplayDetails> DisplayDetails { get; set; } = new();

    public List<DisplayModeProfile> Profiles { get; set; } = new();

    public string SelectedProfileId { get; set; } = "";

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
        DisplayAliases ??= new Dictionary<string, string>();
        DisplayDetails ??= new Dictionary<string, SavedDisplayDetails>();

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
                HotkeyCtrl = DeskHotkeyCtrl,
                HotkeyAlt = DeskHotkeyAlt,
                HotkeyShift = DeskHotkeyShift,
                HotkeyWin = DeskHotkeyWin,
                HotkeyKey = DeskHotkeyKey,
                LaunchAppAfterSwitch = false,
                CloseLauncherAfterSwitch = CloseAppOnDeskMode
            });

            Profiles.Add(new DisplayModeProfile
            {
                Id = "tv-gaming-mode",
                Name = "TV Gaming Mode",
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