using System.Collections;
using System.Linq;
using System.Windows;
using GongSolutions.Wpf.DragDrop;

namespace WinXStart.ViewModels;

/// <summary>
/// Handles tile drag/drop using GongSolutions.Wpf.DragDrop.
/// Mutates ObservableCollections directly (no RefreshTileGroups) so WPF
/// item-container animations stay smooth.
/// </summary>
public class TileDropHandler : GongSolutions.Wpf.DragDrop.IDropTarget
{
    private readonly MainViewModel _mainVm;

    public TileDropHandler(MainViewModel mainVm) => _mainVm = mainVm;

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is TileViewModel && dropInfo.TargetCollection is IList)
        {
            dropInfo.Effects = DragDropEffects.Move;
            dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not TileViewModel tile) return;

        var sourceCollection = dropInfo.DragInfo.SourceCollection;
        var targetCollection = dropInfo.TargetCollection;
        int insertIndex = dropInfo.UnfilteredInsertIndex;

        var sourceGroup = _mainVm.TileGroups.FirstOrDefault(
            g => ReferenceEquals(g.Tiles, sourceCollection));
        var targetGroup = _mainVm.TileGroups.FirstOrDefault(
            g => ReferenceEquals(g.Tiles, targetCollection));

        if (sourceGroup == null || targetGroup == null) return;

        if (ReferenceEquals(sourceGroup, targetGroup))
        {
            int fromIdx = sourceGroup.Tiles.IndexOf(tile);
            if (fromIdx < 0) return;

            int toIdx = insertIndex;
            // Gong reports the insert slot BEFORE removal; if moving forward,
            // the destination shifts left by one.
            if (fromIdx < toIdx) toIdx--;
            if (toIdx < 0) toIdx = 0;
            if (toIdx >= sourceGroup.Tiles.Count) toIdx = sourceGroup.Tiles.Count - 1;
            if (fromIdx == toIdx) return;

            _mainVm.MoveTileInGroup(sourceGroup, fromIdx, toIdx);
        }
        else
        {
            _mainVm.MoveTileToGroupAt(tile, sourceGroup, targetGroup, insertIndex);
        }
    }
}
