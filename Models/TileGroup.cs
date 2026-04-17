namespace WinXStart.Models;

public class TileGroup
{
    public string Name { get; set; } = "";
    public List<PinnedTile> Tiles { get; set; } = new();
}
