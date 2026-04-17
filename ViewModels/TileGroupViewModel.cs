using System.Collections.ObjectModel;

namespace WinXStart.ViewModels;

public class TileGroupViewModel : ViewModelBase
{
    private string _name;
    private bool _isEditingName;

    public string Name
    {
        get => _name;
        set
        {
            var old = _name;
            if (SetField(ref _name, value) && old != value)
                NameChanged?.Invoke(this, old, value);
        }
    }

    public bool IsEditingName
    {
        get => _isEditingName;
        set => SetField(ref _isEditingName, value);
    }

    public ObservableCollection<TileViewModel> Tiles { get; } = new();

    /// <summary>Raised after Name is committed (old → new). Used by MainViewModel to persist.</summary>
    public event Action<TileGroupViewModel, string, string>? NameChanged;

    public TileGroupViewModel(string name)
    {
        _name = name;
    }
}
