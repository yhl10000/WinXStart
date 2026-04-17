namespace WinXStart.Models;

public class PinnedTile
{
    public string AppId { get; set; } = "";
    public TileSize Size { get; set; } = TileSize.Medium;
    public int Order { get; set; }
}
