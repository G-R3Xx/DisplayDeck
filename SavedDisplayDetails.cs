namespace DisplayDeck;

public sealed class SavedDisplayDetails
{
    public string DisplayName { get; set; } = "";

    public string StableDisplayId { get; set; } = "";

    public string MonitorHardwareCode { get; set; } = "";

    public string Label { get; set; } = "";

    public string ResolutionText { get; set; } = "";

    public string PositionText { get; set; } = "";

    public int PositionX { get; set; }

    public int PositionY { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int Frequency { get; set; }

    public bool HasUsableMode =>
        Width > 0 &&
        Height > 0 &&
        Frequency > 0;
}