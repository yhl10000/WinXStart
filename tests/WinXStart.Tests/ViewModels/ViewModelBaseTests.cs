using System.ComponentModel;
using WinXStart.ViewModels;

namespace WinXStart.Tests.ViewModels;

public class ViewModelBaseTests
{
    private class Probe : ViewModelBase
    {
        private int _n;
        public int N
        {
            get => _n;
            set => SetField(ref _n, value);
        }
        public bool SetNDirect(int v) => SetField(ref _n, v);
    }

    [Fact]
    public void SetField_changes_value_raises_PropertyChanged_returns_true()
    {
        var p = new Probe();
        string? raised = null;
        ((INotifyPropertyChanged)p).PropertyChanged += (_, e) => raised = e.PropertyName;

        // Use the property setter path (SetField default uses CallerMemberName = setter)
        p.N = 5;
        p.N.Should().Be(5);
        raised.Should().Be(nameof(Probe.N));
    }

    [Fact]
    public void SetField_same_value_returns_false_no_event()
    {
        var p = new Probe { N = 5 };
        bool raised = false;
        ((INotifyPropertyChanged)p).PropertyChanged += (_, _) => raised = true;

        p.SetNDirect(5).Should().BeFalse();
        raised.Should().BeFalse();
    }

    [Fact]
    public void Setter_uses_property_name_via_CallerMemberName()
    {
        var p = new Probe();
        string? name = null;
        ((INotifyPropertyChanged)p).PropertyChanged += (_, e) => name = e.PropertyName;
        p.N = 7;
        name.Should().Be(nameof(Probe.N));
    }
}
