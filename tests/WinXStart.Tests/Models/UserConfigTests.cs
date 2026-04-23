using WinXStart.Models;

namespace WinXStart.Tests.Models;

public class UserConfigTests
{
    [Fact]
    public void Defaults_are_empty_lists_and_400ms()
    {
        var cfg = new UserConfig();
        cfg.Groups.Should().BeEmpty();
        cfg.CustomApps.Should().BeEmpty();
        cfg.DoubleTapMs.Should().Be(400);
        cfg.AutoStart.Should().BeFalse();
    }

    [Fact]
    public void PinnedTile_defaults_to_Medium_size_Order_zero()
    {
        var t = new PinnedTile();
        t.AppId.Should().Be("");
        t.Size.Should().Be(TileSize.Medium);
        t.Order.Should().Be(0);
    }

    [Fact]
    public void TileGroup_defaults_to_empty_Tiles_and_blank_name()
    {
        var g = new TileGroup();
        g.Name.Should().Be("");
        g.Tiles.Should().BeEmpty();
    }
}
