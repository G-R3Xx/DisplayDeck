using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DisplayDeck;

public sealed class ProfileDisplaySelection : INotifyPropertyChanged
{
    private bool _enabled;
    private bool _primary;

    public string DisplayKey { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string MonitorName { get; set; } = "";
    public string ResolutionText { get; set; } = "";
    public string PositionText { get; set; } = "";
    public string ActiveStateText { get; set; } = "";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            OnPropertyChanged();
        }
    }

    public bool Primary
    {
        get => _primary;
        set
        {
            if (_primary == value) return;
            _primary = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
