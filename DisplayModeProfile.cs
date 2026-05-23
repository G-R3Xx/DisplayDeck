using System.Collections.Generic;

namespace DisplayDeck;

public sealed class DisplayModeProfile
{
    public string Name { get; set; } = "";

    public string PrimaryDisplayName { get; set; } = "";

    public List<string> EnabledDisplays { get; set; } = new();

    public List<string> DisabledDisplays { get; set; } = new();
}