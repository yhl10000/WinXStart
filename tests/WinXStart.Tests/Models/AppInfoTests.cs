using WinXStart.Models;

namespace WinXStart.Tests.Models;

public class AppInfoTests
{
    [Fact]
    public void IsStoreApp_true_when_AppUserModelId_set()
    {
        new AppInfo { AppUserModelId = "Microsoft.Foo_8wekyb!App" }.IsStoreApp.Should().BeTrue();
    }

    [Fact]
    public void IsStoreApp_false_when_AppUserModelId_empty()
    {
        new AppInfo { TargetPath = @"C:\foo.exe" }.IsStoreApp.Should().BeFalse();
    }

    [Fact]
    public void Id_prefers_AppUserModelId_lowercased()
    {
        var app = new AppInfo { AppUserModelId = "Microsoft.Foo!App", TargetPath = @"C:\other.exe", Name = "Foo" };
        app.Id.Should().Be("microsoft.foo!app");
    }

    [Fact]
    public void Id_falls_back_to_TargetPath_filename_without_ext()
    {
        var app = new AppInfo { TargetPath = @"C:\Program Files\Foo\Bar.exe", Name = "Bar App" };
        app.Id.Should().Be("bar");
    }

    [Fact]
    public void Id_falls_back_to_Name_with_spaces_as_underscore()
    {
        var app = new AppInfo { Name = "My Cool App" };
        app.Id.Should().Be("my_cool_app");
    }

    [Theory]
    [InlineData("Acrobat", 'A')]
    [InlineData("bar", 'B')]
    [InlineData("zoom", 'Z')]
    [InlineData("", '#')]
    [InlineData("1Password", '#')]
    [InlineData("_hidden", '#')]
    [InlineData("中文", '#')]
    public void FirstLetter_returns_uppercase_letter_or_hash(string name, char expected)
    {
        new AppInfo { Name = name }.FirstLetter.Should().Be(expected);
    }
}
