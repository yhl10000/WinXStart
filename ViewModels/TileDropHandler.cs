using System.Windows;
using GongSolutions.Wpf.DragDrop;

namespace WinXStart.ViewModels;

/// <summary>
/// Combined Gong DragDrop <see cref="IDropTarget"/> + <see cref="IDragSource"/>.
///
/// We implement IDragSource only to hook into drag start/end so the custom
/// cursor-centered ghost (a Popup owned by MainWindow) can be shown/hidden.
/// Everything else delegates to Gong's default behaviour via <see cref="DefaultDragHandler"/>.
/// </summary>
public class TileDropHandler : GongSolutions.Wpf.DragDrop.IDropTarget, GongSolutions.Wpf.DragDrop.IDragSource
{
    private readonly MainViewModel _vm;
    private readonly DefaultDragHandler _defaultDragHandler = new();

    // Throttle + dedupe state for live reflow. DragOver fires on every mousemove
    // (hundreds per second); without this, we'd Move/Insert on every event and
    // kick FluidMoveBehavior into a re-layout storm.
    private TileGroupViewModel? _lastTargetGroup;
    private int _lastInsertIndex = -1;
    private int _lastReflowTick;
    private const int ReflowThrottleMs = 40;

    /// <summary>Fires when a tile drag begins. Main window listens to show the ghost Popup.</summary>
    public event Action<TileViewModel>? DragStarted;

    /// <summary>Fires when a tile drag ends (drop, cancel, or abort). Main window hides the ghost.</summary>
    public event Action? DragEnded;

    public TileDropHandler(MainViewModel vm)
    {
        _vm = vm;
    }

    // ── IDropTarget ──────────────────────────────────────────

    public void DragOver(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not TileViewModel tile) return;

        dropInfo.Effects = DragDropEffects.Move;
        // Adorner disabled: we rely on live-reflow (tiles physically move out of
        // the way) to show the drop slot. Skipping adorner rendering reduces
        // DragOver cost significantly on every mousemove.

        var sourceGroup = FindGroupOf(tile);
        var targetGroup = dropInfo.TargetCollection as System.Collections.IList;
        if (sourceGroup == null || targetGroup == null) return;

        var targetGroupVm = _vm.TileGroups.FirstOrDefault(g => ReferenceEquals(g.Tiles, targetGroup));
        if (targetGroupVm == null) return;

        int insertIndex = dropInfo.InsertIndex;
        if (insertIndex < 0) insertIndex = 0;
        if (insertIndex > targetGroupVm.Tiles.Count) insertIndex = targetGroupVm.Tiles.Count;

        // Dedupe: skip if target slot hasn't changed since last reflow.
        if (ReferenceEquals(targetGroupVm, _lastTargetGroup) && insertIndex == _lastInsertIndex)
            return;

        // Throttle: ignore events that arrive too fast, unless the target group changed
        // (switching groups should always reflow immediately for responsive feel).
        int now = Environment.TickCount;
        bool groupChanged = !ReferenceEquals(targetGroupVm, _lastTargetGroup);
        if (!groupChanged && now - _lastReflowTick < ReflowThrottleMs)
            return;

        _lastTargetGroup = targetGroupVm;
        _lastInsertIndex = insertIndex;
        _lastReflowTick = now;

        // Live-reflow: mutate the ObservableCollections so WrapPanel + FluidMoveBehavior
        // animate tiles out of the way.
        if (ReferenceEquals(sourceGroup, targetGroupVm))
        {
            int fromIdx = sourceGroup.Tiles.IndexOf(tile);
            if (fromIdx < 0) return;
            // Gong's InsertIndex counts positions in the ORIGINAL list; adjust
            // when moving forward so we land at the intended slot.
            int toIdx = insertIndex;
            if (toIdx > fromIdx) toIdx--;
            if (fromIdx == toIdx) return;
            sourceGroup.Tiles.Move(fromIdx, toIdx);
        }
        else
        {
            int fromIdx = sourceGroup.Tiles.IndexOf(tile);
            if (fromIdx < 0) return;
            sourceGroup.Tiles.RemoveAt(fromIdx);
            if (insertIndex > targetGroupVm.Tiles.Count)
                insertIndex = targetGroupVm.Tiles.Count;
            targetGroupVm.Tiles.Insert(insertIndex, tile);
        }
    }

    public void Drop(IDropInfo dropInfo)
    {
        if (dropInfo.Data is not TileViewModel tile) return;

        // The collections were already mutated in DragOver; just persist the
        // tile's final position to PinManager.
        var finalGroup = FindGroupOf(tile);
        if (finalGroup == null) return;
        int finalIndex = finalGroup.Tiles.IndexOf(tile);
        if (finalIndex < 0) return;

        _vm.PersistTilePosition(tile, finalGroup, finalIndex);
    }

    private TileGroupViewModel? FindGroupOf(TileViewModel tile) =>
        _vm.TileGroups.FirstOrDefault(g => g.Tiles.Contains(tile));

    // ── IDragSource ──────────────────────────────────────────

    public void StartDrag(IDragInfo dragInfo)
    {
        _defaultDragHandler.StartDrag(dragInfo);
        if (dragInfo.SourceItem is TileViewModel tile)
            DragStarted?.Invoke(tile);
    }

    public bool CanStartDrag(IDragInfo dragInfo) => _defaultDragHandler.CanStartDrag(dragInfo);

    public void Dropped(IDropInfo dropInfo) => _defaultDragHandler.Dropped(dropInfo);

    public void DragDropOperationFinished(DragDropEffects operationResult, IDragInfo dragInfo)
    {
        _defaultDragHandler.DragDropOperationFinished(operationResult, dragInfo);
        DragEnded?.Invoke();
    }

    public void DragCancelled()
    {
        _defaultDragHandler.DragCancelled();
        DragEnded?.Invoke();
    }

    public bool TryCatchOccurredException(Exception exception) =>
        _defaultDragHandler.TryCatchOccurredException(exception);
}
