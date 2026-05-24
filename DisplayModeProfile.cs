using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DisplayDeck;

public sealed class DisplayModeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Profile";

    // These keep their old property names for compatibility, but after migration
    // they store the stable display identity where available, not only \\.\DISPLAYx.
    public string PrimaryDisplayName { get; set; } = "";

    public List<string> EnabledDisplays { get; set; } = new();

    public List<string> DisabledDisplays { get; set; } = new();

    public Dictionary<string, string> SavedDisplayLabels { get; set; } = new();

    public Dictionary<string, SavedDisplayDetails> SavedDisplayDetails { get; set; } = new();

    public bool HotkeyCtrl { get; set; } = true;

    public bool HotkeyAlt { get; set; } = true;

    public bool HotkeyShift { get; set; } = false;

    public bool HotkeyWin { get; set; } = false;

    public string HotkeyKey { get; set; } = "";

    public string LauncherPath { get; set; } = "";

    public string LauncherProcessName { get; set; } = "";

    public bool LaunchAppAfterSwitch { get; set; } = false;

    public bool CloseLauncherAfterSwitch { get; set; } = false;

    public string PrimaryDisplayLabel { get; set; } = "";

    public string EnabledDisplaysLabel { get; set; } = "";

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

        EnabledDisplays ??= new List<string>();
        DisabledDisplays ??= new List<string>();
        SavedDisplayLabels ??= new Dictionary<string, string>();
        SavedDisplayDetails ??= new Dictionary<string, SavedDisplayDetails>();

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

        PrimaryDisplayLabel = GetSavedOrRawDisplayLabel(PrimaryDisplayName);

        EnabledDisplaysLabel = EnabledDisplays.Count == 0
            ? "None"
            : string.Join(", ", EnabledDisplays.Select(GetSavedOrRawDisplayLabel));
    }

    public string GetSavedOrRawDisplayLabel(string displayKey)
    {
        if (string.IsNullOrWhiteSpace(displayKey))
        {
            return "Not set";
        }

        if (SavedDisplayLabels is not null &&
            SavedDisplayLabels.TryGetValue(displayKey, out string? savedLabel) &&
            !string.IsNullOrWhiteSpace(savedLabel))
        {
            return savedLabel;
        }

        if (SavedDisplayDetails is not null &&
            SavedDisplayDetails.TryGetValue(displayKey, out SavedDisplayDetails? details) &&
            !string.IsNullOrWhiteSpace(details.Label))
        {
            return details.Label;
        }

        return displayKey;
    }

    public SavedDisplayDetails? GetSavedDisplayDetails(string displayKey)
    {
        if (string.IsNullOrWhiteSpace(displayKey))
        {
            return null;
        }

        if (SavedDisplayDetails is not null &&
            SavedDisplayDetails.TryGetValue(displayKey, out SavedDisplayDetails? details))
        {
            return details;
        }

        return null;
    }

    public SavedDisplayDetails? GetSavedDisplayDetailsForAnyKey(params string[] possibleKeys)
    {
        if (SavedDisplayDetails is null)
        {
            return null;
        }

        foreach (string possibleKey in possibleKeys)
        {
            if (string.IsNullOrWhiteSpace(possibleKey))
            {
                continue;
            }

            if (SavedDisplayDetails.TryGetValue(possibleKey, out SavedDisplayDetails? details))
            {
                return details;
            }
        }

        return null;
    }
}