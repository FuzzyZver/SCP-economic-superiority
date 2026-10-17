using Leopotam.Ecs;

/// <summary>
/// Клик по миру без инструмента стройки: попали в камеру содержания с объектом —
/// открываем его документ. Клетка под курсором уже посчитана GridPointerSystem.
/// </summary>
public class SelectionSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<ClickInputEvent> _clicks;
    private EcsFilter<SiteFlag, GridAttribute, BuildToolAttribute, PointerAttribute, ViewFloorAttribute> _sites;
    private EcsFilter<ScpFlag, ScpAttribute, ContainedAttribute> _scps;

    public void Run()
    {
        if (_clicks.IsEmpty()) return;

        foreach (var s in _sites)
        {
            if (_sites.Get3(s).Tool != BuildTool.None) continue;

            ref var pointer = ref _sites.Get4(s);
            if (!pointer.HasCell) continue;

            int depth = _sites.Get5(s).Depth;
            int id = _sites.Get2(s).Floors[depth].BuildingAt(pointer.Cell);
            if (id == FloorGrid.Empty) continue;

            foreach (var i in _scps)
            {
                if (_scps.Get3(i).RoomId != id) continue;
                _world.NewEntity().Get<ScpClickedEvent>().Id = _scps.Get2(i).Id;
                break;
            }
        }
    }
}
