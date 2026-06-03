using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DisplayDeck;

public sealed class CcdDisplayConfigService
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;

    private const uint DISPLAYCONFIG_PATH_ACTIVE = 0x00000001;

    private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;
    private const uint SDC_APPLY = 0x00000080;
    private const uint SDC_SAVE_TO_DATABASE = 0x00000200;
    private const uint SDC_ALLOW_CHANGES = 0x00000400;
    private const uint SDC_ALLOW_PATH_ORDER_CHANGES = 0x00002000;

    private const int ERROR_SUCCESS = 0;

    public bool TryKeepOnlyDisplayHardwareCodesEnabled(
        IReadOnlyCollection<string> enabledHardwareCodes,
        out string errorMessage
    )
    {
        errorMessage = "";

        var wantedCodes = enabledHardwareCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => code.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (wantedCodes.Count == 0)
        {
            errorMessage = "No enabled monitor hardware codes were supplied.";
            return false;
        }

        if (!TryQueryActivePaths(out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes, out uint pathCount, out uint modeCount, out errorMessage))
        {
            return false;
        }

        var activeDescriptions = new List<string>();
        bool matchedAtLeastOneEnabledPath = false;

        for (int i = 0; i < pathCount; i++)
        {
            DISPLAYCONFIG_PATH_INFO path = paths[i];

            string gdiName = GetSourceGdiDeviceName(path.sourceInfo.adapterId, path.sourceInfo.id);
            string monitorPath = GetTargetMonitorDevicePath(path.targetInfo.adapterId, path.targetInfo.id);
            string friendlyName = GetTargetMonitorFriendlyName(path.targetInfo.adapterId, path.targetInfo.id);

            activeDescriptions.Add($"{gdiName} | {friendlyName} | {monitorPath}");

            bool shouldKeep =
                wantedCodes.Any(code =>
                    (!string.IsNullOrWhiteSpace(monitorPath) &&
                     monitorPath.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(friendlyName) &&
                     friendlyName.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrWhiteSpace(gdiName) &&
                     string.Equals(gdiName, code, StringComparison.OrdinalIgnoreCase))
                );

            if (shouldKeep)
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                matchedAtLeastOneEnabledPath = true;
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
            }
        }

        if (!matchedAtLeastOneEnabledPath)
        {
            errorMessage =
                "None of the requested monitor hardware codes matched Windows' active display paths." +
                Environment.NewLine +
                "Requested hardware codes: " + string.Join(", ", wantedCodes) +
                Environment.NewLine +
                "Active paths:" + Environment.NewLine +
                string.Join(Environment.NewLine, activeDescriptions);

            return false;
        }

        int setResult = SetDisplayConfig(
            pathCount,
            paths,
            modeCount,
            modes,
            SDC_USE_SUPPLIED_DISPLAY_CONFIG |
            SDC_APPLY |
            SDC_SAVE_TO_DATABASE |
            SDC_ALLOW_CHANGES |
            SDC_ALLOW_PATH_ORDER_CHANGES
        );

        if (setResult != ERROR_SUCCESS)
        {
            errorMessage =
                $"SetDisplayConfig failed with code {setResult}." +
                Environment.NewLine +
                "Requested hardware codes: " + string.Join(", ", wantedCodes) +
                Environment.NewLine +
                "Active paths:" + Environment.NewLine +
                string.Join(Environment.NewLine, activeDescriptions);

            return false;
        }

        return true;
    }

    public bool TryKeepOnlyDisplaysEnabled(
        IReadOnlyCollection<string> enabledDisplayNames,
        out string errorMessage
    )
    {
        errorMessage = "";

        if (enabledDisplayNames.Count == 0)
        {
            errorMessage = "No enabled displays were supplied.";
            return false;
        }

        var enabledSet = new HashSet<string>(enabledDisplayNames, StringComparer.OrdinalIgnoreCase);

        if (!TryQueryActivePaths(out DISPLAYCONFIG_PATH_INFO[] paths, out DISPLAYCONFIG_MODE_INFO[] modes, out uint pathCount, out uint modeCount, out errorMessage))
        {
            return false;
        }

        var activeNames = new List<string>();
        bool matchedAtLeastOneEnabledPath = false;

        for (int i = 0; i < pathCount; i++)
        {
            string gdiName = GetSourceGdiDeviceName(paths[i].sourceInfo.adapterId, paths[i].sourceInfo.id);

            if (!string.IsNullOrWhiteSpace(gdiName))
            {
                activeNames.Add(gdiName);
            }

            if (enabledSet.Contains(gdiName))
            {
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                matchedAtLeastOneEnabledPath = true;
            }
            else
            {
                paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
            }
        }

        if (!matchedAtLeastOneEnabledPath)
        {
            errorMessage =
                "None of the requested enabled displays matched Windows' active display paths." +
                Environment.NewLine +
                "Requested: " + string.Join(", ", enabledDisplayNames) +
                Environment.NewLine +
                "Active paths: " + string.Join(", ", activeNames);

            return false;
        }

        int setResult = SetDisplayConfig(
            pathCount,
            paths,
            modeCount,
            modes,
            SDC_USE_SUPPLIED_DISPLAY_CONFIG |
            SDC_APPLY |
            SDC_SAVE_TO_DATABASE |
            SDC_ALLOW_CHANGES |
            SDC_ALLOW_PATH_ORDER_CHANGES
        );

        if (setResult != ERROR_SUCCESS)
        {
            errorMessage =
                $"SetDisplayConfig failed with code {setResult}." +
                Environment.NewLine +
                "Requested: " + string.Join(", ", enabledDisplayNames) +
                Environment.NewLine +
                "Active paths: " + string.Join(", ", activeNames);

            return false;
        }

        return true;
    }

    private static bool TryQueryActivePaths(
        out DISPLAYCONFIG_PATH_INFO[] paths,
        out DISPLAYCONFIG_MODE_INFO[] modes,
        out uint pathCount,
        out uint modeCount,
        out string errorMessage
    )
    {
        errorMessage = "";
        paths = Array.Empty<DISPLAYCONFIG_PATH_INFO>();
        modes = Array.Empty<DISPLAYCONFIG_MODE_INFO>();
        pathCount = 0;
        modeCount = 0;

        int sizeResult = GetDisplayConfigBufferSizes(
            QDC_ONLY_ACTIVE_PATHS,
            out pathCount,
            out modeCount
        );

        if (sizeResult != ERROR_SUCCESS)
        {
            errorMessage = $"GetDisplayConfigBufferSizes failed with code {sizeResult}.";
            return false;
        }

        if (pathCount == 0)
        {
            errorMessage = "Windows reported no active display paths.";
            return false;
        }

        paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

        int queryResult = QueryDisplayConfig(
            QDC_ONLY_ACTIVE_PATHS,
            ref pathCount,
            paths,
            ref modeCount,
            modes,
            IntPtr.Zero
        );

        if (queryResult != ERROR_SUCCESS)
        {
            errorMessage = $"QueryDisplayConfig failed with code {queryResult}.";
            return false;
        }

        return true;
    }

    private static string GetSourceGdiDeviceName(LUID adapterId, uint sourceId)
    {
        var sourceName = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                adapterId = adapterId,
                id = sourceId
            },
            viewGdiDeviceName = ""
        };

        int result = DisplayConfigGetDeviceInfo(ref sourceName);

        if (result != ERROR_SUCCESS)
        {
            return "";
        }

        return sourceName.viewGdiDeviceName ?? "";
    }

    private static string GetTargetMonitorDevicePath(LUID adapterId, uint targetId)
    {
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = CreateTargetDeviceNameRequest(adapterId, targetId);

        int result = DisplayConfigGetDeviceInfo(ref targetName);

        if (result != ERROR_SUCCESS)
        {
            return "";
        }

        return targetName.monitorDevicePath ?? "";
    }

    private static string GetTargetMonitorFriendlyName(LUID adapterId, uint targetId)
    {
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = CreateTargetDeviceNameRequest(adapterId, targetId);

        int result = DisplayConfigGetDeviceInfo(ref targetName);

        if (result != ERROR_SUCCESS)
        {
            return "";
        }

        return targetName.monitorFriendlyDeviceName ?? "";
    }

    private static DISPLAYCONFIG_TARGET_DEVICE_NAME CreateTargetDeviceNameRequest(LUID adapterId, uint targetId)
    {
        return new DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = DISPLAYCONFIG_DEVICE_INFO_TYPE.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId
            },
            monitorFriendlyDeviceName = "",
            monitorDevicePath = ""
        };
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(
        uint flags,
        out uint numPathArrayElements,
        out uint numModeInfoArrayElements
    );

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [Out] DISPLAYCONFIG_PATH_INFO[] pathInfoArray,
        ref uint numModeInfoArrayElements,
        [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId
    );

    [DllImport("user32.dll")]
    private static extern int SetDisplayConfig(
        uint numPathArrayElements,
        [In] DISPLAYCONFIG_PATH_INFO[] pathArray,
        uint numModeInfoArrayElements,
        [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        uint flags
    );

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket
    );

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(
        ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket
    );

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_2DREGION
    {
        public uint cx;
        public uint cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public DISPLAYCONFIG_ROTATION rotation;
        public DISPLAYCONFIG_SCALING scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool targetAvailable;

        public uint statusFlags;
    }

    private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY : uint
    {
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFF,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16,
        DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000
    }

    private enum DISPLAYCONFIG_ROTATION : uint
    {
        DISPLAYCONFIG_ROTATION_IDENTITY = 1,
        DISPLAYCONFIG_ROTATION_ROTATE90 = 2,
        DISPLAYCONFIG_ROTATION_ROTATE180 = 3,
        DISPLAYCONFIG_ROTATION_ROTATE270 = 4
    }

    private enum DISPLAYCONFIG_SCALING : uint
    {
        DISPLAYCONFIG_SCALING_IDENTITY = 1,
        DISPLAYCONFIG_SCALING_CENTERED = 2,
        DISPLAYCONFIG_SCALING_STRETCHED = 3,
        DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4,
        DISPLAYCONFIG_SCALING_CUSTOM = 5,
        DISPLAYCONFIG_SCALING_PREFERRED = 128
    }

    private enum DISPLAYCONFIG_SCANLINE_ORDERING : uint
    {
        DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
        DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = 2,
        DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3
    }

    private enum DISPLAYCONFIG_MODE_INFO_TYPE : uint
    {
        DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
        DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
        DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)]
        public DISPLAYCONFIG_MODE_INFO_TYPE infoType;

        [FieldOffset(4)]
        public uint id;

        [FieldOffset(8)]
        public LUID adapterId;

        [FieldOffset(16)]
        public DISPLAYCONFIG_TARGET_MODE targetMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_SOURCE_MODE sourceMode;

        [FieldOffset(16)]
        public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_MODE
    {
        public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
    {
        public ulong pixelRate;
        public DISPLAYCONFIG_RATIONAL hSyncFreq;
        public DISPLAYCONFIG_RATIONAL vSyncFreq;
        public DISPLAYCONFIG_2DREGION activeSize;
        public DISPLAYCONFIG_2DREGION totalSize;
        public uint videoStandard;
        public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE
    {
        public uint width;
        public uint height;
        public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
        public POINTL position;
    }

    private enum DISPLAYCONFIG_PIXELFORMAT : uint
    {
        DISPLAYCONFIG_PIXELFORMAT_8BPP = 1,
        DISPLAYCONFIG_PIXELFORMAT_16BPP = 2,
        DISPLAYCONFIG_PIXELFORMAT_24BPP = 3,
        DISPLAYCONFIG_PIXELFORMAT_32BPP = 4,
        DISPLAYCONFIG_PIXELFORMAT_NONGDI = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECTL
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
    {
        public POINTL PathSourceSize;
        public RECTL DesktopImageRegion;
        public RECTL DesktopImageClip;
    }

    private enum DISPLAYCONFIG_DEVICE_INFO_TYPE : uint
    {
        DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1,
        DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public DISPLAYCONFIG_DEVICE_INFO_TYPE type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
        public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
    {
        public uint value;
    }
}
