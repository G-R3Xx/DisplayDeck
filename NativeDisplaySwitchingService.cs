using System;
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

    public async Task ApplyProfileAsync(DisplayModeProfile profile, int delayMs)
    {
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

        foreach (string displayName in profile.EnabledDisplays)
        {
            EnableDisplay(displayName, profile.PrimaryDisplayName);
            await Task.Delay(delayMs);
        }

        SetPrimaryDisplay(profile.PrimaryDisplayName);
        await Task.Delay(delayMs);

        foreach (string displayName in profile.DisabledDisplays)
        {
            if (string.Equals(displayName, profile.PrimaryDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DisableDisplay(displayName);
            await Task.Delay(delayMs);
        }

        ApplyAllChanges();
        await Task.Delay(delayMs);
    }

    private static void EnableDisplay(string displayName, string primaryDisplayName)
    {
        DEVMODE devMode = CreateDevMode();

        bool gotSettings =
            EnumDisplaySettingsEx(displayName, ENUM_REGISTRY_SETTINGS, ref devMode, 0) ||
            EnumDisplaySettingsEx(displayName, ENUM_CURRENT_SETTINGS, ref devMode, 0);

        if (!gotSettings)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not read settings for {displayName}.\n\n" +
                "Make sure the display is powered on, connected, and visible to Windows before switching modes."
            );
        }

        if (devMode.dmPelsWidth <= 0 || devMode.dmPelsHeight <= 0)
        {
            devMode.dmPelsWidth = 1920;
            devMode.dmPelsHeight = 1080;
        }

        if (devMode.dmBitsPerPel <= 0)
        {
            devMode.dmBitsPerPel = 32;
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

        if (string.Equals(displayName, primaryDisplayName, StringComparison.OrdinalIgnoreCase))
        {
            devMode.dmPosition.x = 0;
            devMode.dmPosition.y = 0;
        }

        int result = ChangeDisplaySettingsEx(
            displayName,
            ref devMode,
            IntPtr.Zero,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            IntPtr.Zero
        );

        EnsureSuccess(result, "enable", displayName);
    }

    private static void SetPrimaryDisplay(string displayName)
    {
        DEVMODE devMode = CreateDevMode();

        bool gotSettings =
            EnumDisplaySettingsEx(displayName, ENUM_CURRENT_SETTINGS, ref devMode, 0) ||
            EnumDisplaySettingsEx(displayName, ENUM_REGISTRY_SETTINGS, ref devMode, 0);

        if (!gotSettings)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not read settings for the primary display {displayName}.\n\n" +
                "Make sure the display is powered on, connected, and visible to Windows before switching modes."
            );
        }

        devMode.dmFields = DM_POSITION;
        devMode.dmPosition.x = 0;
        devMode.dmPosition.y = 0;

        int result = ChangeDisplaySettingsEx(
            displayName,
            ref devMode,
            IntPtr.Zero,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            IntPtr.Zero
        );

        EnsureSuccess(result, "set primary", displayName);
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
                "Turn the TV on, make sure it is on the correct HDMI input, then try TV Gaming Mode again."
            );
        }

        if (result == DISP_CHANGE_BADPARAM)
        {
            throw new InvalidOperationException(
                $"DisplayDeck could not {action} {displayText}.\n\n" +
                "Windows rejected the display command. Refresh displays, confirm the mode setup, then save settings again."
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