namespace WinXStart.Models;

public class UserConfig
{
    public List<TileGroup> Groups { get; set; } = new();
    public List<AppInfo> CustomApps { get; set; } = new();
    public int DoubleTapMs { get; set; } = 400;
    public bool AutoStart { get; set; }
}
