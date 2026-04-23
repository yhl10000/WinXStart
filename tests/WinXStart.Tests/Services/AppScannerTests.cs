using WinXStart.Services;

namespace WinXStart.Tests.Services;

public class AppScannerTests
{
    [Theory]
    [InlineData("Microsoft Edge", true)]
    [InlineData("Visual Studio Code", true)]
    [InlineData("Notepad", true)]
    [InlineData("7-Zip File Manager", true)]
    public void IsUserFriendlyName_accepts_normal_display_names(string name, bool expected)
    {
        AppScanner.IsUserFriendlyName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{b5292708-a791-47c3-b5fb-ec3f9becf31f}")]
    [InlineData("b5292708-a791-47c3-b5fb-ec3f9becf31f")]
    [InlineData("com.adobe.reader")]
    [InlineData("org.videolan.vlc")]
    [InlineData("net.example.app")]
    [InlineData("Microsoft.WindowsTerminal")]
    [InlineData("windows.immersivecontrolpanel")]
    [InlineData("foo.bar.baz")]
    public void IsUserFriendlyName_rejects_identifiers(string name)
    {
        AppScanner.IsUserFriendlyName(name).Should().BeFalse();
    }

    [Fact]
    public void IsUserFriendlyName_accepts_Microsoft_with_space()
    {
        // "Microsoft Store" has a space, so it's a display name, not Microsoft.xxx package id
        AppScanner.IsUserFriendlyName("Microsoft Store").Should().BeTrue();
    }
}
