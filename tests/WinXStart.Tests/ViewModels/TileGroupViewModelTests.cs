using WinXStart.ViewModels;

namespace WinXStart.Tests.ViewModels;

public class TileGroupViewModelTests
{
    [Fact]
    public void Ctor_sets_name_and_empty_tiles()
    {
        var vm = new TileGroupViewModel("Pinned");
        vm.Name.Should().Be("Pinned");
        vm.Tiles.Should().BeEmpty();
        vm.IsEditingName.Should().BeFalse();
    }

    [Fact]
    public void NameChanged_fires_with_old_and_new_on_rename()
    {
        var vm = new TileGroupViewModel("Old");
        string? oldN = null, newN = null;
        vm.NameChanged += (_, o, n) => { oldN = o; newN = n; };

        vm.Name = "New";

        oldN.Should().Be("Old");
        newN.Should().Be("New");
    }

    [Fact]
    public void NameChanged_does_not_fire_when_same_value()
    {
        var vm = new TileGroupViewModel("X");
        int calls = 0;
        vm.NameChanged += (_, _, _) => calls++;
        vm.Name = "X";
        calls.Should().Be(0);
    }

    [Fact]
    public void IsEditingName_raises_PropertyChanged()
    {
        var vm = new TileGroupViewModel("X");
        string? raised = null;
        ((System.ComponentModel.INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised = e.PropertyName;
        vm.IsEditingName = true;
        raised.Should().Be(nameof(vm.IsEditingName));
    }
}
