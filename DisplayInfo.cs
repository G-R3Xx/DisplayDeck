namespace DisplayDeck;

public sealed class DisplayInfo
{
    public string DisplayName { get; set; } = "";

    public string StableDisplayId { get; set; } = "";

    public string MonitorHardwareCode { get; set; } = "";

    public string DeviceString { get; set; } = "";

    public string MonitorName { get; set; } = "";

    public string FriendlyName { get; set; } = "";

    public string ResolutionText { get; set; } = "";

    public bool IsActive { get; set; }

    public bool IsPrimary { get; set; }

    public int PositionX { get; set; }

    public int PositionY { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Frequency { get; set; }

    public string ActiveText => IsActive ? "Active" : "Inactive";

    public string PrimaryText => IsPrimary ? "Yes" : "No";

    public string PositionText => $"{PositionX}, {PositionY}";

    public string IdentityKey
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(StableDisplayId))
            {
                return StableDisplayId;
            }

            if (!string.IsNullOrWhiteSpace(MonitorHardwareCode))
            {
                return MonitorHardwareCode;
            }

            return DisplayName;
        }
    }

    public string ResolutionDisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(ResolutionText))
            {
                return ResolutionText;
            }

            if (Width > 0 && Height > 0 && Frequency > 0)
            {
                return $"{Width} × {Height} @ {Frequency}Hz";
            }

            if (Width > 0 && Height > 0)
            {
                return $"{Width} × {Height}";
            }

            return "Resolution unknown";
        }
    }

    public string DisplayNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                return "";
            }

            int index = DisplayName.LastIndexOf("DISPLAY", System.StringComparison.OrdinalIgnoreCase);

            if (index < 0)
            {
                return DisplayName;
            }

            return DisplayName[index..].Replace("DISPLAY", "Display ", System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public string BestDetectedName
    {
        get
        {
            if (IsGoodDisplayName(MonitorName))
            {
                return MonitorName.Trim();
            }

            if (IsGoodDisplayName(DeviceString))
            {
                return DeviceString.Trim();
            }

            return "";
        }
    }

    public string AutoFallbackLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayNumber))
            {
                return $"{DisplayNumber} — {ResolutionDisplayText}";
            }

            return DisplayName;
        }
    }

    public string DisplayLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(FriendlyName))
            {
                return FriendlyName;
            }

            if (!string.IsNullOrWhiteSpace(BestDetectedName))
            {
                return BestDetectedName;
            }

            return AutoFallbackLabel;
        }
    }

    private static bool IsGoodDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();

        if (value.Contains("Generic", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains("PnP", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.StartsWith(@"\\.\DISPLAY", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}