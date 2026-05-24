using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace DisplayDeck;

public sealed class NativeDisplayDiscoveryService
{
    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004;

    public IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var monitorNamesByCode = GetWmiMonitorNamesByHardwareCode();
        var results = new List<DisplayInfo>();

        uint displayIndex = 0;

        while (true)
        {
            var displayDevice = CreateDisplayDevice();

            bool found = EnumDisplayDevices(null, displayIndex, ref displayDevice, 0);

            if (!found)
            {
                break;
            }

            bool isActive = (displayDevice.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            bool isPrimary = (displayDevice.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;

            var monitorDevice = CreateDisplayDevice();
            bool foundMonitor = EnumDisplayDevices(displayDevice.DeviceName, 0, ref monitorDevice, 0);

            string monitorName = "";
            string stableDisplayId = "";
            string monitorHardwareCode = "";

            if (foundMonitor)
            {
                stableDisplayId = BuildStableDisplayId(monitorDevice.DeviceID, monitorDevice.DeviceKey);
                monitorHardwareCode = ExtractMonitorHardwareCode(monitorDevice.DeviceID);

                if (!string.IsNullOrWhiteSpace(monitorHardwareCode) &&
                    monitorNamesByCode.TryGetValue(monitorHardwareCode, out string? wmiName) &&
                    !string.IsNullOrWhiteSpace(wmiName))
                {
                    monitorName = wmiName;
                }
                else if (!string.IsNullOrWhiteSpace(monitorDevice.DeviceString))
                {
                    monitorName = monitorDevice.DeviceString;
                }
            }

            if (string.IsNullOrWhiteSpace(stableDisplayId))
            {
                stableDisplayId = BuildStableDisplayId(displayDevice.DeviceID, displayDevice.DeviceKey);
            }

            if (string.IsNullOrWhiteSpace(stableDisplayId))
            {
                stableDisplayId = displayDevice.DeviceName;
            }

            if (string.IsNullOrWhiteSpace(monitorName))
            {
                monitorName = displayDevice.DeviceString;
            }

            var info = new DisplayInfo
            {
                DisplayName = displayDevice.DeviceName,
                StableDisplayId = stableDisplayId,
                MonitorHardwareCode = monitorHardwareCode,
                DeviceString = foundMonitor
                    ? $"{displayDevice.DeviceString} / {monitorDevice.DeviceString}"
                    : displayDevice.DeviceString,
                MonitorName = monitorName,
                IsActive = isActive,
                IsPrimary = isPrimary
            };

            if (isActive)
            {
                var devMode = CreateDevMode();

                bool gotSettings = EnumDisplaySettingsEx(
                    displayDevice.DeviceName,
                    ENUM_CURRENT_SETTINGS,
                    ref devMode,
                    0
                );

                if (gotSettings)
                {
                    info.PositionX = devMode.dmPosition.x;
                    info.PositionY = devMode.dmPosition.y;
                    info.Width = devMode.dmPelsWidth;
                    info.Height = devMode.dmPelsHeight;
                    info.Frequency = devMode.dmDisplayFrequency;
                }
            }

            results.Add(info);
            displayIndex++;
        }

        return results;
    }

    private static string BuildStableDisplayId(string deviceId, string deviceKey)
    {
        string cleanDeviceId = (deviceId ?? "").Trim();
        string cleanDeviceKey = (deviceKey ?? "").Trim();

        if (!string.IsNullOrWhiteSpace(cleanDeviceId))
        {
            return cleanDeviceId.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(cleanDeviceKey))
        {
            return cleanDeviceKey.ToUpperInvariant();
        }

        return "";
    }

    private static Dictionary<string, string> GetWmiMonitorNamesByHardwareCode()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"
            );

            foreach (ManagementObject monitor in searcher.Get())
            {
                string instanceName = monitor["InstanceName"]?.ToString() ?? "";
                string hardwareCode = ExtractWmiHardwareCode(instanceName);

                string friendlyName = DecodeWmiString(monitor["UserFriendlyName"]);

                if (!string.IsNullOrWhiteSpace(hardwareCode) &&
                    !string.IsNullOrWhiteSpace(friendlyName))
                {
                    result[hardwareCode] = friendlyName;
                }
            }
        }
        catch
        {
            // Some systems restrict WMI monitor info.
            // If this fails, we fall back to EnumDisplayDevices.
        }

        return result;
    }

    private static string DecodeWmiString(object? value)
    {
        if (value is not ushort[] raw)
        {
            return "";
        }

        var builder = new StringBuilder();

        foreach (ushort character in raw)
        {
            if (character == 0)
            {
                break;
            }

            builder.Append((char)character);
        }

        return builder.ToString().Trim();
    }

    private static string ExtractMonitorHardwareCode(string deviceId)
    {
        // Example:
        // MONITOR\SAM73A3\{...}
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return "";
        }

        string[] parts = deviceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return parts[1];
        }

        return "";
    }

    private static string ExtractWmiHardwareCode(string instanceName)
    {
        // Example:
        // DISPLAY\SAM73A3\5&123456&0&UID4352_0
        if (string.IsNullOrWhiteSpace(instanceName))
        {
            return "";
        }

        string[] parts = instanceName.Split('\\', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            return parts[1];
        }

        return "";
    }

    private static DISPLAY_DEVICE CreateDisplayDevice()
    {
        var displayDevice = new DISPLAY_DEVICE();
        displayDevice.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        return displayDevice;
    }

    private static DEVMODE CreateDevMode()
    {
        var devMode = new DEVMODE();
        devMode.dmSize = (short)Marshal.SizeOf<DEVMODE>();
        return devMode;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(
        string? lpDevice,
        uint iDevNum,
        ref DISPLAY_DEVICE lpDisplayDevice,
        uint dwFlags
    );

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsEx(
        string lpszDeviceName,
        int iModeNum,
        ref DEVMODE lpDevMode,
        int dwFlags
    );

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

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