using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DisplayDeck;

public sealed class DisplayModeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Profile";

    public string PrimaryDisplayName { get; set; } = "";

    public List<string> EnabledDisplays { get; set; } = new();

    public List<string> DisabledDisplays { get; set; } = new();

    public bool HotkeyCtrl { get; set; } = true;

    public bool HotkeyAlt { get; set; } = true;

    public bool HotkeyShift { get; set; } = false;

    public bool HotkeyWin { get; set; } = false;

    public string HotkeyKey { get; set; } = "";

    public string LauncherPath { get; set; } = "";

    public string LauncherProcessName { get; set; } = "";

    public bool LaunchAppAfterSwitch { get; set; } = false;

    public bool CloseLauncherAfterSwitch { get; set; } = false;

    public string HotkeyDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HotkeyKey))
            {
                return "No hotkey set";
            }

            var parts = new List<string>();

            if (HotkeyCtrl)
            {
                parts.Add("Ctrl");
            }

            if (HotkeyAlt)
            {
                parts.Add("Alt");
            }

            if (HotkeyShift)
            {
                parts.Add("Shift");
            }

            if (HotkeyWin)
            {
                parts.Add("Win");
            }

            parts.Add(HotkeyKey.Trim().ToUpperInvariant());

            return string.Join(" + ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }
    }

    public void EnsureDefaults(string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            Name = fallbackName;
        }

        if (EnabledDisplays is null)
        {
            EnabledDisplays = new List<string>();
        }

        if (DisabledDisplays is null)
        {
            DisabledDisplays = new List<string>();
        }

        if (!HotkeyCtrl && !HotkeyAlt && !HotkeyShift && !HotkeyWin && !string.IsNullOrWhiteSpace(HotkeyKey))
        {
            HotkeyCtrl = true;
            HotkeyAlt = true;
        }

        HotkeyKey = (HotkeyKey ?? "").Trim().ToUpperInvariant();

        if (!string.IsNullOrWhiteSpace(LauncherProcessName))
        {
            LauncherProcessName = LauncherProcessName
                .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
                .Trim();
        }
        else if (!string.IsNullOrWhiteSpace(LauncherPath))
        {
            LauncherProcessName = Path.GetFileNameWithoutExtension(LauncherPath);
        }
    }
}