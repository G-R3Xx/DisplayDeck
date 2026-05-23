namespace DisplayDeck;

public sealed class DisplayInfo
{
    public string DisplayName { get; set; } = "";

    public string MonitorName { get; set; } = "";

    public string DeviceString { get; set; } = "";

    public bool IsActive { get; set; }

    public bool IsPrimary { get; set; }

    public int PositionX { get; set; }

    public int PositionY { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Frequency { get; set; }

    public string ActiveText => IsActive ? "Active" : "Inactive";

    public string PrimaryText => IsPrimary ? "Yes" : "No";

    public string ResolutionText =>
        Width > 0 && Height > 0
            ? $"{Width} × {Height} @ {Frequency}Hz"
            : "Not active";

    public string PositionText =>
        IsActive ? $"{PositionX}, {PositionY}" : "-";
}