using System.IO;
using System.Text.Json;
using WinXStart.Models;
using WinXStart.Services;

namespace WinXStart.Tests;

/// <summary>
/// Tests for PinManager. Every test uses a temp directory via the
/// configDirOverride constructor so production %UserProfile%\WinXStart is never touched.
/// </summary>
public class PinManagerTests : IDisposable
{
    private readonly string _tempDir;

    public PinManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinXStart.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    private PinManager NewManager() => new PinManager(_tempDir);
    private string ConfigPath => Path.Combine(_tempDir, "pins.json");

    // ---------- Construction / default state ----------

    [Fact]
    public void New_instance_has_default_Pinned_group_with_no_tiles()
    {
        var pm = NewManager();
        pm.Groups.Should().ContainSingle().Which.Name.Should().Be("Pinned");
        pm.Groups[0].Tiles.Should().BeEmpty();
    }

    [Fact]
    public void IsPinned_returns_false_for_unknown_app()
    {
        NewManager().IsPinned("nope").Should().BeFalse();
    }

    // ---------- Pin ----------

    [Fact]
    public void Pin_adds_tile_with_Order_zero_to_default_group()
    {
        var pm = NewManager();
        pm.Pin("app1");
        pm.IsPinned("app1").Should().BeTrue();
        pm.Groups[0].Tiles.Should().ContainSingle();
        pm.Groups[0].Tiles[0].Order.Should().Be(0);
        pm.Groups[0].Tiles[0].Size.Should().Be(TileSize.Medium);
    }

    [Fact]
    public void Pin_is_case_insensitive_and_idempotent()
    {
        var pm = NewManager();
        pm.Pin("App1");
        pm.Pin("app1"); // same id different case
        pm.Groups[0].Tiles.Should().ContainSingle();
    }

    [Fact]
    public void Pin_assigns_incrementing_Order()
    {
        var pm = NewManager();
        pm.Pin("a");
        pm.Pin("b");
        pm.Pin("c");
        pm.Groups[0].Tiles.Select(t => t.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Pin_to_new_group_creates_it()
    {
        var pm = NewManager();
        pm.Pin("a", TileSize.Large, "Games");
        pm.Groups.Should().HaveCount(2);
        pm.Groups.Should().ContainSingle(g => g.Name == "Games")
            .Which.Tiles.Should().ContainSingle(t => t.AppId == "a" && t.Size == TileSize.Large);
    }

    [Fact]
    public void Pin_uses_max_plus_one_not_physical_count()
    {
        // Regression: v1.3.1 bug. Pin after drag-reorder must not collide Orders.
        var pm = NewManager();
        pm.Pin("a"); // Order=0
        pm.Pin("b"); // Order=1
        pm.Pin("c"); // Order=2

        // Simulate drag-reorder: a→end. Orders become 0→2, 1→0, 2→1 (via ReorderInGroup).
        pm.ReorderInGroup("Pinned", 0, 2);

        // Orders now: b=0, c=1, a=2 (in Order-sorted view)
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.AppId).Should().Equal("b", "c", "a");

        // Pin new tile. Must get Order=3 (max+1), not 3 (count) either - check the ID ordering.
        pm.Pin("d");
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.AppId).Should().Equal("b", "c", "a", "d");
        pm.Groups[0].Tiles.First(t => t.AppId == "d").Order.Should().Be(3);
    }

    // ---------- Unpin ----------

    [Fact]
    public void Unpin_removes_tile()
    {
        var pm = NewManager();
        pm.Pin("a");
        pm.Pin("b");
        pm.Unpin("a");
        pm.IsPinned("a").Should().BeFalse();
        pm.IsPinned("b").Should().BeTrue();
    }

    [Fact]
    public void Unpin_rewrites_Order_to_close_gap()
    {
        // Regression: v1.3.2 bug - Pin after Unpin used unbounded max+1.
        var pm = NewManager();
        pm.Pin("a"); // 0
        pm.Pin("b"); // 1
        pm.Pin("c"); // 2
        pm.Unpin("b");
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.Order).Should().Equal(0, 1);

        pm.Pin("d");
        pm.Groups[0].Tiles.First(t => t.AppId == "d").Order.Should().Be(2);
    }

    [Fact]
    public void Unpin_last_tile_keeps_default_Pinned_group()
    {
        var pm = NewManager();
        pm.Pin("a");
        pm.Unpin("a");
        pm.Groups.Should().ContainSingle().Which.Name.Should().Be("Pinned");
    }

    [Fact]
    public void Unpin_drops_empty_non_default_group()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Games");
        pm.Unpin("a");
        pm.Groups.Should().NotContain(g => g.Name == "Games");
    }

    // ---------- Resize ----------

    [Fact]
    public void ResizeTile_changes_size()
    {
        var pm = NewManager();
        pm.Pin("a");
        pm.ResizeTile("a", TileSize.Large);
        pm.Groups[0].Tiles[0].Size.Should().Be(TileSize.Large);
    }

    [Fact]
    public void ResizeTile_unknown_app_is_noop()
    {
        var pm = NewManager();
        Action act = () => pm.ResizeTile("ghost", TileSize.Large);
        act.Should().NotThrow();
    }

    // ---------- Reorder ----------

    [Fact]
    public void ReorderInGroup_moves_tile_and_reindexes_densely()
    {
        var pm = NewManager();
        pm.Pin("a"); pm.Pin("b"); pm.Pin("c"); pm.Pin("d");
        pm.ReorderInGroup("Pinned", 0, 3); // move a→end

        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.AppId)
            .Should().Equal("b", "c", "d", "a");
        pm.Groups[0].Tiles.Select(t => t.Order).OrderBy(o => o).Should().Equal(0, 1, 2, 3);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(5, 0)]
    [InlineData(0, 5)]
    [InlineData(1, 1)]
    public void ReorderInGroup_invalid_indices_noop(int from, int to)
    {
        var pm = NewManager();
        pm.Pin("a"); pm.Pin("b");
        pm.ReorderInGroup("Pinned", from, to);
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.AppId).Should().Equal("a", "b");
    }

    [Fact]
    public void ReorderInGroup_unknown_group_noop()
    {
        var pm = NewManager();
        pm.Pin("a");
        Action act = () => pm.ReorderInGroup("Nope", 0, 0);
        act.Should().NotThrow();
    }

    // ---------- Groups ----------

    [Fact]
    public void CreateGroup_adds_named_group()
    {
        var pm = NewManager();
        pm.CreateGroup("Dev");
        pm.Groups.Should().Contain(g => g.Name == "Dev");
    }

    [Fact]
    public void CreateGroup_is_case_insensitive_idempotent()
    {
        var pm = NewManager();
        pm.CreateGroup("Dev");
        pm.CreateGroup("dev");
        pm.Groups.Count(g => g.Name.Equals("Dev", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public void RenameGroup_changes_name()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Old");
        pm.RenameGroup("Old", "New");
        pm.Groups.Should().Contain(g => g.Name == "New");
        pm.Groups.Should().NotContain(g => g.Name == "Old");
    }

    [Fact]
    public void RenameGroup_rejects_blank()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Old");
        pm.RenameGroup("Old", "   ");
        pm.Groups.Should().Contain(g => g.Name == "Old");
    }

    [Fact]
    public void DeleteGroup_removes_it()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Games");
        pm.DeleteGroup("Games");
        pm.Groups.Should().NotContain(g => g.Name == "Games");
    }

    [Fact]
    public void DeleteGroup_preserves_at_least_one_group()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Only");
        pm.DeleteGroup("Only");
        pm.Groups.Should().ContainSingle().Which.Name.Should().Be("Pinned");
    }

    // ---------- MoveToGroup ----------

    [Fact]
    public void MoveToGroup_same_group_reorders()
    {
        var pm = NewManager();
        pm.Pin("a"); pm.Pin("b"); pm.Pin("c");
        pm.MoveToGroup("a", "Pinned", 2); // move a to Order=2
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.AppId)
            .Should().Equal("b", "c", "a");
    }

    [Fact]
    public void MoveToGroup_cross_group_moves_and_reindexes_both()
    {
        var pm = NewManager();
        pm.Pin("a"); pm.Pin("b"); pm.Pin("c");      // Pinned: a=0,b=1,c=2
        pm.Pin("x", groupName: "Games");             // Games: x=0
        pm.Pin("y", groupName: "Games");             // Games: x=0,y=1

        pm.MoveToGroup("b", "Games", 1);             // insert b at index 1 of Games

        var pinned = pm.Groups.Single(g => g.Name == "Pinned");
        var games = pm.Groups.Single(g => g.Name == "Games");

        pinned.Tiles.OrderBy(t => t.Order).Select(t => t.AppId).Should().Equal("a", "c");
        pinned.Tiles.OrderBy(t => t.Order).Select(t => t.Order).Should().Equal(0, 1);

        games.Tiles.OrderBy(t => t.Order).Select(t => t.AppId).Should().Equal("x", "b", "y");
        games.Tiles.OrderBy(t => t.Order).Select(t => t.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public void MoveToGroup_to_new_group_creates_it()
    {
        var pm = NewManager();
        pm.Pin("a");
        pm.MoveToGroup("a", "NewG", 0);
        pm.Groups.Should().Contain(g => g.Name == "NewG");
    }

    [Fact]
    public void MoveToGroup_cross_group_drops_empty_source()
    {
        var pm = NewManager();
        pm.Pin("a", groupName: "Src");
        pm.MoveToGroup("a", "Dst", 0);
        pm.Groups.Should().NotContain(g => g.Name == "Src");
    }

    [Fact]
    public void MoveToGroup_unknown_app_noop()
    {
        var pm = NewManager();
        pm.Pin("a");
        Action act = () => pm.MoveToGroup("ghost", "Pinned", 0);
        act.Should().NotThrow();
    }

    // ---------- AddCustomApp ----------

    [Fact]
    public void AddCustomApp_persists_app_and_pins_it()
    {
        var pm = NewManager();
        var app = new AppInfo { Name = "Custom", TargetPath = @"C:\foo\custom.exe" };
        pm.AddCustomApp(app);

        pm.CustomApps.Should().ContainSingle();
        pm.IsPinned(app.Id).Should().BeTrue();
    }

    [Fact]
    public void AddCustomApp_dedupes_by_TargetPath_case_insensitive()
    {
        var pm = NewManager();
        pm.AddCustomApp(new AppInfo { Name = "A", TargetPath = @"C:\foo\a.exe" });
        pm.AddCustomApp(new AppInfo { Name = "A copy", TargetPath = @"c:\FOO\A.EXE" });
        pm.CustomApps.Should().ContainSingle();
    }

    // ---------- Save / Load round-trip ----------

    [Fact]
    public void Pin_persists_to_disk_across_instances()
    {
        var pm1 = NewManager();
        pm1.Pin("a");
        pm1.Pin("b", groupName: "G2");

        var pm2 = NewManager(); // new instance reads file
        pm2.IsPinned("a").Should().BeTrue();
        pm2.IsPinned("b").Should().BeTrue();
        pm2.Groups.Should().Contain(g => g.Name == "G2");
    }

    [Fact]
    public void Corrupt_json_falls_back_to_default()
    {
        File.WriteAllText(Path.Combine(_tempDir, "pins.json"), "{ not valid json");
        var pm = new PinManager(_tempDir);
        pm.Groups.Should().ContainSingle().Which.Name.Should().Be("Pinned");
    }

    // ---------- NormalizeOrders (startup) ----------

    [Fact]
    public void Startup_normalizes_duplicate_Orders()
    {
        // Regression: v1.3.2 bug - Orders could have duplicates on disk.
        // Write a pre-corrupted file: two tiles with Order=0.
        var json = @"{
          ""groups"": [{
            ""name"": ""Pinned"",
            ""tiles"": [
              { ""appId"": ""a"", ""size"": ""Medium"", ""order"": 0 },
              { ""appId"": ""b"", ""size"": ""Medium"", ""order"": 0 },
              { ""appId"": ""c"", ""size"": ""Medium"", ""order"": 5 }
            ]
          }],
          ""customApps"": [],
          ""doubleTapMs"": 400,
          ""autoStart"": false
        }";
        File.WriteAllText(Path.Combine(_tempDir, "pins.json"), json);

        var pm = new PinManager(_tempDir);
        var orders = pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.Order).ToList();
        orders.Should().Equal(0, 1, 2);
    }

    [Fact]
    public void Startup_normalizes_sparse_Orders()
    {
        var json = @"{
          ""groups"": [{
            ""name"": ""Pinned"",
            ""tiles"": [
              { ""appId"": ""a"", ""size"": ""Medium"", ""order"": 10 },
              { ""appId"": ""b"", ""size"": ""Medium"", ""order"": 99 }
            ]
          }],
          ""customApps"": [],
          ""doubleTapMs"": 400
        }";
        File.WriteAllText(Path.Combine(_tempDir, "pins.json"), json);

        var pm = new PinManager(_tempDir);
        pm.Groups[0].Tiles.OrderBy(t => t.Order).Select(t => t.Order).Should().Equal(0, 1);
    }

    [Fact]
    public void Startup_writes_back_normalized_file_when_dirty()
    {
        var json = @"{
          ""groups"": [{ ""name"": ""Pinned"", ""tiles"": [
            { ""appId"": ""a"", ""size"": ""Medium"", ""order"": 7 }
          ]}],
          ""customApps"": [],
          ""doubleTapMs"": 400
        }";
        File.WriteAllText(Path.Combine(_tempDir, "pins.json"), json);

        _ = new PinManager(_tempDir);

        var persisted = File.ReadAllText(Path.Combine(_tempDir, "pins.json"));
        persisted.Should().Contain("\"order\": 0").And.NotContain("\"order\": 7");
    }
}
