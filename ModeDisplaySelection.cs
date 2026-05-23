namespace DisplayDeck;

public sealed class ModeDisplaySelection
{
    public string DisplayName { get; set; } = "";

    public string MonitorName { get; set; } = "";

    public string ResolutionText { get; set; } = "";

    public bool DeskEnabled { get; set; }

    public bool DeskPrimary { get; set; }

    public bool TvEnabled { get; set; }

    public bool TvPrimary { get; set; }
}