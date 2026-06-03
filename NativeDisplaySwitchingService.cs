using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DisplayDeck;

public sealed class NativeDisplaySwitchingService
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int ENUM_REGISTRY_SETTINGS = -2;

    private const int DM_POSITION = 0x00000020;
    private const int DM_BITSPERPEL = 0x00040000;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;

    private const int CDS_UPDATEREGISTRY = 0x00000001;
    private const int CDS_SET_PRIMARY = 0x00000010;
    private const int CDS_NORESET = 0x10000000;

    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_RESTART = 1;
    private const int DISP_CHANGE_FAILED = -1;
    private const int DISP_CHANGE_BADMODE = -2;
    private const int DISP_CHANGE_NOTUPDATED = -3;
    private const int DISP_CHANGE_BADFLAGS = -4;
    private const int DISP_CHANGE_BADPARAM = -5;

    private readonly NativeDisplayDiscoveryService _displayDiscoveryService = new();
    private readonly CcdDisplayConfigService _ccdDisplayConfigService = new();

    public async Task ApplyProfileAsync(DisplayModeProfile profile, int delayMs)
    {
        profile.EnsureDefaults(profile.Name);

        if (profile.EnabledDisplays.Count == 0)
        {
            throw new InvalidOperationException("At least one display must be enabled for this profile.");
        }

        if (string.IsNullOrWhiteSpace(profile.PrimaryDisplayName))
        {
            throw new InvalidOperationException("A primary display must be selected for this profile.");
        }

        if (!profile.EnabledDisplays.Contains(profile.PrimaryDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The primary display must also be enabled for this profile.");
        }

        if (profile.EnabledDisplays.Count == 1)
        {
            await ApplySingleDisplayProfileAsync(profile, delayMs);
            return;
        }

        await ApplyMultiDisplayProfileAsync(profile, delayMs);
    }

    private async Task ApplySingleDisplayProfileAsync(DisplayModeProfile profile, int delayMs)
    {
        Dictionary<string, DisplayInfo> displayLookup = BuildFreshDisplayLookup();

        string enabledKey = profile.EnabledDisplays[0];

        DisplayInfo targetDisplay = ResolveDisplay(
            profile,
            enabledKey,
            displayLookup,
            "enable"
        );

        // Step 1:
        // Bring the target display online and make it primary using the saved profile mode.
        EnableDisplay(targetDisplay, profile, enabledKey);
        ApplyAllChanges();
        await Task.Delay(Math.Max(delayMs, 2000));

        displayLookup = BuildFreshDisplayLookup();

        targetDisplay = ResolveDisplay(
            profile,
            enabledKey,
            displayLookup,
            "set primary"
        );

        SetPrimaryDisplay(targetDisplay, profile, enabledKey);
        ApplyAllChanges();
        await Task.Delay(Math.Max(delayMs, 2500));

        // Step 2:
        // Use Windows' built-in "second screen only" pathway.
        // On this setup, /external gets us very close: Sim + Drawing active, Sim primary.
        // That is better than trying to detach from the full 3-monitor extended layout.
        await WindowsDisplaySwitchService.ExternalOnlyAsync(delayMs);

        // Step 3:
        // Re-resolve the target after Windows has changed the topology.
        displayLookup = BuildFreshDisplayLookup();

        targetDisplay = ResolveDisplay(
            profile,
            enabledKey,
            displayLookup,
            "finalise single-display profile"
        );

        SetPrimaryDisplay(targetDisplay, profile, enabledKey);
        ApplyAllChanges();
        await Task.Delay(Math.Max(delayMs, 2500));

        // Step 4:
        // Ask CCD to keep only the profile target active.
        string ccdError = "";
        var targetHardwareCodes = new List<string>();

        if (!string.IsNullOrWhiteSpace(targetDisplay.MonitorHardwareCode))
        {
            targetHardwareCodes.Add(targetDisplay.MonitorHardwareCode);
        }

        if (!string.IsNullOrWhiteSpace(targetDisplay.StableDisplayId))
        {
            targetHardwareCodes.Add(targetDisplay.StableDisplayId);
        }

        bool ccdSucceeded = _ccdDisplayConfigService.TryKeepOnlyDisplayHardwareCodesEnabled(
            targetHardwareCodes,
            out ccdError
        );

        if (!ccdSucceeded)
        {
            ccdSucceeded = _ccdDisplayConfigService.TryKeepOnlyDisplaysEnabled(
                new List<string> { targetDisplay.DisplayName },
                out ccdError
            );
        }

        if (ccdSucceeded)
        {
            ApplyAllChanges();
            await Task.Delay(Math.Max(delayMs, 2500));

            // Do not trust a successful CCD return blindly.
            // Some Windows/GPU combinations report success but still leave one extra
            // display attached. Verify the actual active displays before returning.
            displayLookup = BuildFreshDisplayLookup();

            bool onlyTargetIsActive = displayLookup
                .Values
                .GroupBy(display => display.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Where(display => display.IsActive)
                .All(display => DisplayLooksLikeProfileDisplay(profile, enabledKey, display));

            if (onlyTargetIsActive)
            {
                return;
            }
        }

        // Step 5:
        // Final fallback: after /external/CCD, disconnect any remaining active display that is not the target.
        displayLookup = BuildFreshDisplayLookup();

        var failedDisables = new List<string>();

        IReadOnlyList<DisplayInfo> activeDisplays = displayLookup
            .Values
            .GroupBy(display => display.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(display => display.IsActive)
            .ToList();

        foreach (DisplayInfo activeDisplay in activeDisplays)
        {
            if (DisplayLooksLikeProfileDisplay(profile, enabledKey, activeDisplay))
            {
                continue;
            }

            int disableResult = TryDisableDisplayWithFallbacks(activeDisplay.DisplayName);

            if (!IsSuccessfulResult(disableResult))
            {
                ApplyAllChanges();
                await Task.Delay(Math.Max(delayMs, 2000));
                disableResult = TryDisableDisplayWithFallbacks(activeDisplay.DisplayName);
            }

            if (!IsSuccessfulResult(disableResult))
            {
                string label = !string.IsNullOrWhiteSpace(activeDisplay.DisplayLabel)
                    ? activeDisplay.DisplayLabel
                    : activeDisplay.DisplayName;

                failedDisables.Add(label);
            }
            else
            {
                ApplyAllChanges();
                await Task.Delay(delayMs);
            }
        }

        ApplyAllChanges();
        await Task.Delay(Math.Max(delayMs, 2000));

        if (failedDisables.Count > 0)
        {
            throw new InvalidOperationException(
                "DisplayDeck switched to the single-display profile as far as Windows allowed, but one or more extra displays stayed on:\n\n" +
                string.Join("\n", failedDisables.Select(name => "- " + name)) +
                "\n\nWindows /external worked partially on this PC, but the final detach step was refused. You can manually disconnect the remaining display in Windows Display Settings, or test whether changing the GPU/Windows display numbering makes /external select only the Sim display."
            );
        }
    }
    private async Task ApplyMultiDisplayProfileAsync(DisplayModeProfile profile, int delayMs)
    {
        // If coming back from a single-display profile such as Sim Mode,
        // Windows may have fully disconnected the desk displays.
        // Ask Windows to extend first, wait, then apply the saved profile layout.
        await WindowsDisplaySwitchService.ExtendDesktopAsync(delayMs);

        Dictionary<string, DisplayInfo> displayLookup = BuildFreshDisplayLookup();

        DisplayInfo primaryDisplay = ResolveDisplay(profile, profile.PrimaryDisplayName, displayLookup, "enable primary");
        EnableDisplay(primaryDisplay, profile, profile.PrimaryDisplayName);
        ApplyAllChanges();
        await Task.Delay(delayMs);

        displayLookup = BuildFreshDisplayLookup();

        primaryDisplay = ResolveDisplay(profile, profile.PrimaryDisplayName, displayLookup, "set primary");
        SetPrimaryDisplay(primaryDisplay, profile, profile.PrimaryDisplayName);
        ApplyAllChanges();
        await Task.Delay(delayMs);

        displayLookup = BuildFreshDisplayLookup();

        foreach (string displayKey in profile.EnabledDisplays)
        {
            if (string.Equals(displayKey, profile.PrimaryDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DisplayInfo display = ResolveDisplay(profile, displayKey, displayLookup, "enable");
            EnableDisplay(display, profile, displayKey);
            ApplyAllChanges();
            await Task.Delay(delayMs);
            displayLookup = BuildFreshDisplayLookup();
        }

        ApplyAllChanges();
        await Task.Delay(Math.Max(delayMs, 2500));
        displayLookup = BuildFreshDisplayLookup();

        var failedDisables = new List<string>();

        foreach (string displayKey in profile.DisabledDisplays)
        {
            if (string.Equals(displayKey, profile.PrimaryDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryResolveDisplay(profile, displayKey, displayLookup, out DisplayInfo? displayToDisable))
            {
                continue;
            }

            int disableResult = TryDisableDisplayWithFallbacks(displayToDisable.DisplayName);

            if (!IsSuccessfulResult(disableResult))
            {
                ApplyAllChanges();
                await Task.Delay(Math.Max(delayMs, 2500));
                displayLookup = BuildFreshDisplayLookup();

                if (TryResolveDisplay(profile, displayKey, displayLookup, out DisplayInfo? retryDisplayToDisable))
                {
                    disableResult = TryDisableDisplayWithFallbacks(retryDisplayToDisable.DisplayName);
                    displayToDisable = retryDisplayToDisable;
                }
            }

            if (!IsSuccessfulResult(disableResult))
            {
                failedDisables.Add(GetDisplayLabelForError(profile, displayKey, displayToDisable));
                continue;
            }

            ApplyAllChanges();
            await Task.Delay(delayMs);
            displayLookup = BuildFreshDisplayLookup();
        }

        ApplyAllChanges();
        await Task.Delay(delayMs);

        if (failedDisables.Count > 0)
        {
            throw new InvalidOperationException(
                "DisplayDeck switched the main profile display, but Windows refused to turn off one or more old displays:\n\n" +
                string.Join("\n", failedDisables.Select(name => "- " + name)) +
                "\n\nTry clicking the profile button once more after Windows settles. If it still happens, open Windows Display Settings, confirm the layout, click Refresh in DisplayDeck, and save the profile again."
            );
        }
    }

    private Dictionary<string, DisplayInfo> BuildFreshDisplayLookup()
    {
        IReadOnlyList<DisplayInfo> allDisplays = _displayDiscoveryService.GetDisplays();
        return BuildDisplayLookup(allDisplays);
    }

    private static Dictionary<string, DisplayInfo> BuildDisplayLookup(IReadOnlyList<DisplayInfo> displays)
    {
        var lookup = new Dictionary<string, DisplayInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (DisplayInfo display in displays)
        {
            AddLookupKey(lookup, display.DisplayName, display);
            AddLookupKey(lookup, display.StableDisplayId, display);
            AddLookupKey(lookup, display.MonitorHardwareCode, display);
            AddLookupKey(lookup, display.IdentityKey, display);
        }

        return lookup;
    }

    private static void AddLookupKey(Dictionary<string, DisplayInfo> lookup, string key, DisplayInfo display)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!lookup.ContainsKey(key))
        {
            lookup[key] = display;
        }
    }

    private static DisplayInfo ResolveDisplay(DisplayModeProfile profile, string displayKey, Dictionary<string, DisplayInfo> lookup, string action)
    {
        if (TryResolveDisplay(profile, displayKey, lookup, out DisplayInfo? display))
        {
            return display;
        }

        throw new InvalidOperationException(
            $"DisplayDeck could not {action} {GetDisplayLabelForError(profile, displayKey)}.\n\n" +
            "Windows is not currently reporting this saved monitor as available, and there is no saved Windows device path to fall back to."
        );
    }

    private static bool TryResolveDisplay(DisplayModeProfile profile, string displayKey, Dictionary<string, DisplayInfo> lookup, out DisplayInfo display)
    {
        display = null!;

        if (string.IsNullOrWhiteSpace(displayKey))
        {
            return false;
        }

        SavedDisplayDetails? savedDetails = profile.GetSavedDisplayDetails(displayKey);

        if (lookup.TryGetValue(displayKey, out DisplayInfo? directMatch) && IsReasonableDisplayMatch(directMatch, savedDetails))
        {
            display = directMatch;
            return true;
        }

        if (savedDetails is not null)
        {
            if (!string.IsNullOrWhiteSpace(savedDetails.StableDisplayId) && lookup.TryGetValue(savedDetails.StableDisplayId, out DisplayInfo? stableMatch) && IsReasonableDisplayMatch(stableMatch, savedDetails))
            {
                display = stableMatch;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(savedDetails.MonitorHardwareCode) && lookup.TryGetValue(savedDetails.MonitorHardwareCode, out DisplayInfo? hardwareMatch) && IsReasonableDisplayMatch(hardwareMatch, savedDetails))
            {
                display = hardwareMatch;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(savedDetails.DisplayName) && lookup.TryGetValue(savedDetails.DisplayName, out DisplayInfo? displayNameMatch) && IsReasonableDisplayMatch(displayNameMatch, savedDetails))
            {
                display = displayNameMatch;
                return true;
            }

            DisplayInfo? modeMatch = FindBestModeMatch(lookup.Values, savedDetails);

            if (modeMatch is not null)
            {
                display = modeMatch;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(savedDetails.DisplayName))
            {
                display = new DisplayInfo
                {
                    DisplayName = savedDetails.DisplayName,
                    StableDisplayId = savedDetails.StableDisplayId,
                    MonitorHardwareCode = savedDetails.MonitorHardwareCode,
                    FriendlyName = savedDetails.Label,
                    MonitorName = savedDetails.Label,
                    Width = savedDetails.Width,
                    Height = savedDetails.Height,
                    Frequency = savedDetails.Frequency,
                    PositionX = savedDetails.PositionX,
                    PositionY = savedDetails.PositionY,
                    IsActive = false,
                    IsPrimary = false
                };

                return true;
            }
        }

        return false;
    }

    private static bool IsReasonableDisplayMatch(DisplayInfo display, SavedDisplayDetails? savedDetails)
    {
        if (savedDetails is null || !savedDetails.HasUsableMode)
        {
            return true;
        }

        if (display.Width <= 0 || display.Height <= 0)
        {
            return true;
        }

        if (display.Width != savedDetails.Width || display.Height != savedDetails.Height)
        {
            return false;
        }

        if (display.Frequency > 0 && savedDetails.Frequency > 0 && Math.Abs(display.Frequency - savedDetails.Frequency) > 2)
        {
            return false;
        }

        return true;
    }

    private static DisplayInfo? FindBestModeMatch(IEnumerable<DisplayInfo> displays, SavedDisplayDetails savedDetails)
    {
        if (!savedDetails.HasUsableMode)
        {
            return null;
        }

        return displays
            .Where(display =>
                display.Width == savedDetails.Width &&
                display.Height == savedDetails.Height &&
                (display.Frequency <= 0 || savedDetails.Frequency <= 0 || Math.Abs(display.Frequency - savedDetails.Frequency) <= 2)
            )
            .OrderBy(display => Math.Abs(display.PositionX - savedDetails.PositionX) + Math.Abs(display.PositionY - savedDetails.PositionY))
            .FirstOrDefault();
    }

    private static bool DisplayLooksLikeProfileDisplay(DisplayModeProfile profile, string profileDisplayKey, DisplayInfo activeDisplay)
    {
        if (string.Equals(profileDisplayKey, activeDisplay.IdentityKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profileDisplayKey, activeDisplay.StableDisplayId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profileDisplayKey, activeDisplay.MonitorHardwareCode, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(profileDisplayKey, activeDisplay.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        SavedDisplayDetails? savedDetails = profile.GetSavedDisplayDetails(profileDisplayKey);

        if (savedDetails is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(savedDetails.DisplayName) && string.Equals(savedDetails.DisplayName, activeDisplay.DisplayName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (savedDetails.HasUsableMode && activeDisplay.Width == savedDetails.Width && activeDisplay.Height == savedDetails.Height && (activeDisplay.Frequency <= 0 || savedDetails.Frequency <= 0 || Math.Abs(activeDisplay.Frequency - savedDetails.Frequency) <= 2))
        {
            return true;
        }

        return false;
    }

    private static SavedDisplayDetails? GetSavedDetailsForDisplay(DisplayModeProfile profile, string profileDisplayKey, DisplayInfo resolvedDisplay)
    {
        return profile.GetSavedDisplayDetailsForAnyKey(
            profileDisplayKey,
            resolvedDisplay.IdentityKey,
            resolvedDisplay.StableDisplayId,
            resolvedDisplay.MonitorHardwareCode,
            resolvedDisplay.DisplayName
        );
    }

    private static void EnableDisplay(DisplayInfo display, DisplayModeProfile profile, string profileDisplayKey)
    {
        SavedDisplayDetails? savedDetails = GetSavedDetailsForDisplay(profile, profileDisplayKey, display);
        DEVMODE baseMode = ReadBestDisplayMode(display, savedDetails);
        var attempts = new List<DEVMODE>();

        if (savedDetails is not null && savedDetails.HasUsableMode)
        {
            DEVMODE savedWithRefresh = baseMode;
            savedWithRefresh.dmPelsWidth = savedDetails.Width;
            savedWithRefresh.dmPelsHeight = savedDetails.Height;
            savedWithRefresh.dmDisplayFrequency = savedDetails.Frequency;
            savedWithRefresh.dmPosition.x = savedDetails.PositionX;
            savedWithRefresh.dmPosition.y = savedDetails.PositionY;
            EnsureUsableModeValues(ref savedWithRefresh);
            savedWithRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
            attempts.Add(savedWithRefresh);

            DEVMODE savedNoRefresh = savedWithRefresh;
            savedNoRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
            attempts.Add(savedNoRefresh);
        }

        DEVMODE currentWithPosition = baseMode;
        EnsureUsableModeValues(ref currentWithPosition);

        if (savedDetails is not null)
        {
            currentWithPosition.dmPosition.x = savedDetails.PositionX;
            currentWithPosition.dmPosition.y = savedDetails.PositionY;
        }

        currentWithPosition.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
        attempts.Add(currentWithPosition);

        DEVMODE currentNoRefresh = currentWithPosition;
        currentNoRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
        attempts.Add(currentNoRefresh);

        int lastResult = DISP_CHANGE_FAILED;

        foreach (DEVMODE attempt in attempts)
        {
            DEVMODE mode = attempt;
            int result = TryApplyDisplayMode(display.DisplayName, mode, setPrimary: false);

            if (IsSuccessfulResult(result))
            {
                return;
            }

            lastResult = result;
        }

        EnsureSuccess(lastResult, "enable", GetDisplayLabelForError(profile, profileDisplayKey, display));
    }

    private static void SetPrimaryDisplay(DisplayInfo display, DisplayModeProfile profile, string profileDisplayKey)
    {
        SavedDisplayDetails? savedDetails = GetSavedDetailsForDisplay(profile, profileDisplayKey, display);
        DEVMODE baseMode = ReadBestDisplayMode(display, savedDetails);
        var attempts = new List<DEVMODE>();

        if (savedDetails is not null && savedDetails.HasUsableMode)
        {
            DEVMODE savedWithRefresh = baseMode;
            savedWithRefresh.dmPelsWidth = savedDetails.Width;
            savedWithRefresh.dmPelsHeight = savedDetails.Height;
            savedWithRefresh.dmDisplayFrequency = savedDetails.Frequency;
            savedWithRefresh.dmPosition.x = 0;
            savedWithRefresh.dmPosition.y = 0;
            EnsureUsableModeValues(ref savedWithRefresh);
            savedWithRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
            attempts.Add(savedWithRefresh);

            DEVMODE savedNoRefresh = savedWithRefresh;
            savedNoRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
            attempts.Add(savedNoRefresh);
        }

        DEVMODE currentPrimary = baseMode;
        EnsureUsableModeValues(ref currentPrimary);
        currentPrimary.dmPosition.x = 0;
        currentPrimary.dmPosition.y = 0;
        currentPrimary.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY;
        attempts.Add(currentPrimary);

        DEVMODE currentPrimaryNoRefresh = currentPrimary;
        currentPrimaryNoRefresh.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
        attempts.Add(currentPrimaryNoRefresh);

        DEVMODE primaryPositionOnly = CreateDevMode();
        primaryPositionOnly.dmPosition.x = 0;
        primaryPositionOnly.dmPosition.y = 0;
        primaryPositionOnly.dmFields = DM_POSITION;
        attempts.Add(primaryPositionOnly);

        int lastResult = DISP_CHANGE_FAILED;

        foreach (DEVMODE attempt in attempts)
        {
            DEVMODE mode = attempt;
            int result = TryApplyDisplayMode(display.DisplayName, mode, setPrimary: true);

            if (IsSuccessfulResult(result))
            {
                return;
            }

            lastResult = result;
        }

        EnsureSuccess(lastResult, "set primary", GetDisplayLabelForError(profile, profileDisplayKey, display));
    }

    private static DEVMODE ReadBestDisplayMode(DisplayInfo display, SavedDisplayDetails? savedDetails)
    {
        DEVMODE devMode = CreateDevMode();

        bool gotSettings =
            EnumDisplaySettingsEx(display.DisplayName, ENUM_CURRENT_SETTINGS, ref devMode, 0) ||
            EnumDisplaySettingsEx(display.DisplayName, ENUM_REGISTRY_SETTINGS, ref devMode, 0);

        if (!gotSettings && savedDetails is not null)
        {
            devMode.dmPelsWidth = savedDetails.Width > 0 ? savedDetails.Width : 1920;
            devMode.dmPelsHeight = savedDetails.Height > 0 ? savedDetails.Height : 1080;
            devMode.dmDisplayFrequency = savedDetails.Frequency > 0 ? savedDetails.Frequency : 60;
            devMode.dmPosition.x = savedDetails.PositionX;
            devMode.dmPosition.y = savedDetails.PositionY;
            devMode.dmBitsPerPel = 32;
        }

        return devMode;
    }

    private static void EnsureUsableModeValues(ref DEVMODE devMode)
    {
        if (devMode.dmPelsWidth <= 0 || devMode.dmPelsHeight <= 0)
        {
            devMode.dmPelsWidth = 1920;
            devMode.dmPelsHeight = 1080;
        }

        if (devMode.dmDisplayFrequency <= 0)
        {
            devMode.dmDisplayFrequency = 60;
        }

        if (devMode.dmBitsPerPel <= 0)
        {
            devMode.dmBitsPerPel = 32;
        }
    }

    private static int TryApplyDisplayMode(string displayName, DEVMODE devMode, bool setPrimary)
    {
        int flags = CDS_UPDATEREGISTRY | CDS_NORESET;

        if (setPrimary)
        {
            flags |= CDS_SET_PRIMARY;
        }

        return ChangeDisplaySettingsEx(displayName, ref devMode, IntPtr.Zero, flags, IntPtr.Zero);
    }

    private static int TryDisableDisplayWithFallbacks(string displayName)
    {
        var attempts = new List<(DEVMODE Mode, int Flags)>();

        DEVMODE fullDisableQueued = CreateDevMode();
        fullDisableQueued.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT;
        fullDisableQueued.dmPosition.x = 0;
        fullDisableQueued.dmPosition.y = 0;
        fullDisableQueued.dmPelsWidth = 0;
        fullDisableQueued.dmPelsHeight = 0;
        attempts.Add((fullDisableQueued, CDS_UPDATEREGISTRY | CDS_NORESET));

        DEVMODE sizeOnlyQueued = CreateDevMode();
        sizeOnlyQueued.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
        sizeOnlyQueued.dmPelsWidth = 0;
        sizeOnlyQueued.dmPelsHeight = 0;
        attempts.Add((sizeOnlyQueued, CDS_UPDATEREGISTRY | CDS_NORESET));

        DEVMODE fullDisableImmediate = fullDisableQueued;
        attempts.Add((fullDisableImmediate, CDS_UPDATEREGISTRY));

        DEVMODE sizeOnlyImmediate = sizeOnlyQueued;
        attempts.Add((sizeOnlyImmediate, CDS_UPDATEREGISTRY));

        DEVMODE fullDisableTemporary = fullDisableQueued;
        attempts.Add((fullDisableTemporary, 0));

        int lastResult = DISP_CHANGE_FAILED;

        for (int i = 0; i < attempts.Count; i++)
        {
            DEVMODE mode = attempts[i].Mode;
            int flags = attempts[i].Flags;
            int result = ChangeDisplaySettingsEx(displayName, ref mode, IntPtr.Zero, flags, IntPtr.Zero);

            if (IsSuccessfulResult(result))
            {
                return result;
            }

            lastResult = result;
        }

        return lastResult;
    }

    private static void ApplyAllChanges()
    {
        int result = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        EnsureSuccess(result, "apply display changes", "");
    }

    private static bool IsSuccessfulResult(int result)
    {
        return result == DISP_CHANGE_SUCCESSFUL || result == DISP_CHANGE_RESTART;
    }

    private static void EnsureSuccess(int result, string action, string displayName)
    {
        if (IsSuccessfulResult(result))
        {
            return;
        }

        string displayText = string.IsNullOrWhiteSpace(displayName) ? "the selected display setup" : displayName;

        if (result == DISP_CHANGE_FAILED)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not {action} {displayText}.\n\n" +
                "Windows failed the display change request. Try again after Windows settles, or confirm the layout in Windows Display Settings."
            );
        }

        if (result == DISP_CHANGE_BADMODE)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not {action} {displayText}.\n\n" +
                "Windows rejected the display mode for this monitor."
            );
        }

        if (result == DISP_CHANGE_NOTUPDATED)
        {
            throw new InvalidOperationException($"DisplayDeck could not {action} {displayText}. Windows could not update the display registry settings.");
        }

        if (result == DISP_CHANGE_BADFLAGS)
        {
            throw new InvalidOperationException($"DisplayDeck could not {action} {displayText}. Windows rejected the display command flags.");
        }

        if (result == DISP_CHANGE_BADPARAM)
        {
            throw new InvalidOperationException($"DisplayDeck could not {action} {displayText}. Windows rejected the display command.");
        }

        throw new InvalidOperationException(
            $"DisplayDeck could not {action} {displayText}.\n\n" +
            $"Windows display API returned error code {result}."
        );
    }

    private static string GetDisplayLabelForError(DisplayModeProfile profile, string displayKey)
    {
        return profile.GetSavedOrRawDisplayLabel(displayKey);
    }

    private static string GetDisplayLabelForError(DisplayModeProfile profile, string displayKey, DisplayInfo display)
    {
        string savedLabel = profile.GetSavedOrRawDisplayLabel(displayKey);

        if (!string.IsNullOrWhiteSpace(savedLabel) && !string.Equals(savedLabel, displayKey, StringComparison.OrdinalIgnoreCase))
        {
            return savedLabel;
        }

        if (!string.IsNullOrWhiteSpace(display.DisplayLabel))
        {
            return display.DisplayLabel;
        }

        return displayKey;
    }

    private static DEVMODE CreateDevMode()
    {
        var devMode = new DEVMODE();
        devMode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return devMode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;

        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public POINTL dmPosition;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;

        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
}




