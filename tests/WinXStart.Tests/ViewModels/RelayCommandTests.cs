using WinXStart.ViewModels;

namespace WinXStart.Tests.ViewModels;

public class RelayCommandTests
{
    [Fact]
    public void Execute_invokes_action_with_parameter()
    {
        object? received = null;
        var cmd = new RelayCommand(p => received = p);
        cmd.Execute("hello");
        received.Should().Be("hello");
    }

    [Fact]
    public void Execute_parameterless_ctor_runs_action()
    {
        int calls = 0;
        var cmd = new RelayCommand(() => calls++);
        cmd.Execute(null);
        calls.Should().Be(1);
    }

    [Fact]
    public void CanExecute_default_true_when_no_predicate()
    {
        new RelayCommand(_ => { }).CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void CanExecute_respects_predicate()
    {
        var cmd = new RelayCommand(_ => { }, p => p is int n && n > 0);
        cmd.CanExecute(5).Should().BeTrue();
        cmd.CanExecute(-1).Should().BeFalse();
        cmd.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Ctor_throws_on_null_execute()
    {
        Action act = () => new RelayCommand((Action<object?>)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
