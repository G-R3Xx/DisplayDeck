namespace DisplayDeck;

public sealed class ProfileDisplaySelection
{
    public string DisplayName { get; set; } = "";

    public string MonitorName { get; set; } = "";

    public string ResolutionText { get; set; } = "";

    public bool Enabled { get; set; }

    public bool Primary { get; set; }
}