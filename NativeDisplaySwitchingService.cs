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
    private const int CDS_NORESET = 0x10000000;

    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const int DISP_CHANGE_BADMODE = -2;
    private const int DISP_CHANGE_BADPARAM = -5;

    private readonly NativeDisplayDiscoveryService _displayDiscoveryService = new();

    public async Task ApplyProfileAsync(DisplayModeProfile profile, int delayMs)
    {
        profile.EnsureDefaults(profile.Name);

        if (profile.EnabledDisplays.Count == 0)
        {
            throw new InvalidOperationException("At least one display must be enabled for this mode.");
        }

        if (string.IsNullOrWhiteSpace(profile.PrimaryDisplayName))
        {
            throw new InvalidOperationException("A primary display must be selected for this mode.");
        }

        if (!profile.EnabledDisplays.Contains(profile.PrimaryDisplayName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The primary display must also be enabled for this mode.");
        }

        IReadOnlyList<DisplayInfo> allDisplays = _displayDiscoveryService.GetDisplays();
        Dictionary<string, DisplayInfo> displayLookup = BuildDisplayLookup(allDisplays);

        foreach (string displayKey in profile.EnabledDisplays)
        {
            DisplayInfo display = ResolveDisplay(displayKey, displayLookup, "enable");
            EnableDisplay(display, profile, displayKey);
            await Task.Delay(delayMs);
        }

        DisplayInfo primaryDisplay = ResolveDisplay(profile.PrimaryDisplayName, displayLookup, "set primary");
        SetPrimaryDisplay(primaryDisplay, profile, profile.PrimaryDisplayName);
        await Task.Delay(delayMs);

        foreach (string displayKey in profile.DisabledDisplays)
        {
            if (string.Equals(displayKey, profile.PrimaryDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryResolveDisplay(displayKey, displayLookup, out DisplayInfo? displayToDisable))
            {
                continue;
            }

            DisableDisplay(displayToDisable.DisplayName);
            await Task.Delay(delayMs);
        }

        ApplyAllChanges();
        await Task.Delay(delayMs);
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

    private static DisplayInfo ResolveDisplay(
        string displayKey,
        Dictionary<string, DisplayInfo> lookup,
        string action
    )
    {
        if (TryResolveDisplay(displayKey, lookup, out DisplayInfo? display))
        {
            return display;
        }

        throw new InvalidOperationException(
            $"DisplayDeck could not {action} {displayKey}.\n\n" +
            "The saved display is not currently available. Turn the display on, make sure it is connected, then click Refresh and try again."
        );
    }

    private static bool TryResolveDisplay(
        string displayKey,
        Dictionary<string, DisplayInfo> lookup,
        out DisplayInfo display
    )
    {
        display = null!;

        if (string.IsNullOrWhiteSpace(displayKey))
        {
            return false;
        }

        if (lookup.TryGetValue(displayKey, out DisplayInfo? directMatch))
        {
            display = directMatch;
            return true;
        }

        return false;
    }

    private static SavedDisplayDetails? GetSavedDetailsForDisplay(
        DisplayModeProfile profile,
        string profileDisplayKey,
        DisplayInfo resolvedDisplay
    )
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
        DEVMODE devMode = CreateDevMode();

        bool gotSettings =
            EnumDisplaySettingsEx(display.DisplayName, ENUM_CURRENT_SETTINGS, ref devMode, 0) ||
            EnumDisplaySettingsEx(display.DisplayName, ENUM_REGISTRY_SETTINGS, ref devMode, 0);

        if (!gotSettings)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not read settings for {display.DisplayLabel}.\n\n" +
                "Make sure the display is powered on, connected, and visible to Windows before switching modes."
            );
        }

        SavedDisplayDetails? savedDetails = GetSavedDetailsForDisplay(profile, profileDisplayKey, display);

        if (savedDetails is not null && savedDetails.HasUsableMode)
        {
            devMode.dmPelsWidth = savedDetails.Width;
            devMode.dmPelsHeight = savedDetails.Height;
            devMode.dmDisplayFrequency = savedDetails.Frequency;
            devMode.dmPosition.x = savedDetails.PositionX;
            devMode.dmPosition.y = savedDetails.PositionY;
        }
        else
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
        }

        if (devMode.dmBitsPerPel <= 0)
        {
            devMode.dmBitsPerPel = 32;
        }

        devMode.dmFields =
            DM_POSITION |
            DM_PELSWIDTH |
            DM_PELSHEIGHT |
            DM_BITSPERPEL |
            DM_DISPLAYFREQUENCY;

        int result = ChangeDisplaySettingsEx(
            display.DisplayName,
            ref devMode,
            IntPtr.Zero,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            IntPtr.Zero
        );

        EnsureSuccess(result, "enable", display.DisplayLabel);
    }

    private static void SetPrimaryDisplay(DisplayInfo display, DisplayModeProfile profile, string profileDisplayKey)
    {
        DEVMODE devMode = CreateDevMode();

        bool gotSettings =
            EnumDisplaySettingsEx(display.DisplayName, ENUM_CURRENT_SETTINGS, ref devMode, 0) ||
            EnumDisplaySettingsEx(display.DisplayName, ENUM_REGISTRY_SETTINGS, ref devMode, 0);

        if (!gotSettings)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not read settings for the primary display {display.DisplayLabel}.\n\n" +
                "Make sure the display is powered on, connected, and visible to Windows before switching modes."
            );
        }

        SavedDisplayDetails? savedDetails = GetSavedDetailsForDisplay(profile, profileDisplayKey, display);

        if (savedDetails is not null && savedDetails.HasUsableMode)
        {
            devMode.dmPelsWidth = savedDetails.Width;
            devMode.dmPelsHeight = savedDetails.Height;
            devMode.dmDisplayFrequency = savedDetails.Frequency;
        }

        if (devMode.dmBitsPerPel <= 0)
        {
            devMode.dmBitsPerPel = 32;
        }

        if (devMode.dmPelsWidth <= 0 || devMode.dmPelsHeight <= 0)
        {
            devMode.dmPelsWidth = 1920;
            devMode.dmPelsHeight = 1080;
        }

        if (devMode.dmDisplayFrequency <= 0)
        {
            devMode.dmDisplayFrequency = 60;
        }

        devMode.dmFields =
            DM_POSITION |
            DM_PELSWIDTH |
            DM_PELSHEIGHT |
            DM_BITSPERPEL |
            DM_DISPLAYFREQUENCY;

        devMode.dmPosition.x = 0;
        devMode.dmPosition.y = 0;

        int result = ChangeDisplaySettingsEx(
            display.DisplayName,
            ref devMode,
            IntPtr.Zero,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            IntPtr.Zero
        );

        EnsureSuccess(result, "set primary", display.DisplayLabel);
    }

    private static void DisableDisplay(string displayName)
    {
        DEVMODE devMode = CreateDevMode();

        devMode.dmFields =
            DM_POSITION |
            DM_PELSWIDTH |
            DM_PELSHEIGHT;

        devMode.dmPosition.x = 0;
        devMode.dmPosition.y = 0;
        devMode.dmPelsWidth = 0;
        devMode.dmPelsHeight = 0;

        int result = ChangeDisplaySettingsEx(
            displayName,
            ref devMode,
            IntPtr.Zero,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            IntPtr.Zero
        );

        EnsureSuccess(result, "disable", displayName);
    }

    private static void ApplyAllChanges()
    {
        int result = ChangeDisplaySettingsEx(
            null,
            IntPtr.Zero,
            IntPtr.Zero,
            0,
            IntPtr.Zero
        );

        EnsureSuccess(result, "apply display changes", "");
    }

    private static void EnsureSuccess(int result, string action, string displayName)
    {
        if (result == DISP_CHANGE_SUCCESSFUL)
        {
            return;
        }

        string displayText = string.IsNullOrWhiteSpace(displayName)
            ? "the selected display setup"
            : displayName;

        if (result == DISP_CHANGE_BADMODE)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not {action} {displayText}.\n\n" +
                "Windows rejected the display mode. This usually happens when the display is disconnected, powered off, asleep, or not currently available over HDMI.\n\n" +
                "Turn the TV on, make sure it is on the correct HDMI input, then try the profile again."
            );
        }

        if (result == DISP_CHANGE_BADPARAM)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not {action} {displayText}.\n\n" +
                "Windows rejected the display command. Refresh displays, confirm the profile setup, then save settings again."
            );
        }

        throw new InvalidOperationException(
            $"DisplayDeck could not {action} {displayText}.\n\n" +
            $"Windows display API returned error code {result}."
        );
    }

    private static DEVMODE CreateDevMode()
    {
        var devMode = new DEVMODE();
        devMode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return devMode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        int dwFlags
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        ref DEVMODE lpDevMode,
        IntPtr hwnd,
        int dwflags,
        IntPtr lParam
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(
        string? lpszDeviceName,
        IntPtr lpDevMode,
        IntPtr hwnd,
        int dwflags,
        IntPtr lParam
    );

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