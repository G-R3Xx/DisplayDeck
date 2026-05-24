using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DisplayDeck;

public sealed class DisplayAliasEditorItem : INotifyPropertyChanged
{
    private string _alias = "";

    public string DisplayKey { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string StableDisplayId { get; set; } = "";

    public string DetectedName { get; set; } = "";

    public string ResolutionText { get; set; } = "";

    public string PositionText { get; set; } = "";

    public string Alias
    {
        get => _alias;
        set
        {
            if (_alias == value)
            {
                return;
            }

            _alias = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}