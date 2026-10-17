using UnityEngine;
using Leopotam.Ecs;

/// <summary>Переводит курсор в клетку сетки на текущем этаже: луч из камеры в плоскость пола.</summary>
public class GridPointerSystem : Injects, IEcsRunSystem
{
    private EcsFilter<SiteFlag, PointerAttribute, ViewFloorAttribute> _sites;

    public void Run()
    {
        var camera = SceneData.Camera;
        var config = GameConfig.BuildingsConfig;

        foreach (var s in _sites)
        {
            ref var pointer = ref _sites.Get2(s);
            int depth = _sites.Get3(s).Depth;
            pointer.HasCell = false;

            if (camera == null) continue;

            var ray = camera.ScreenPointToRay(pointer.Screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, config.FloorY(depth), 0f));
            if (!plane.Raycast(ray, out float distance)) continue;

            var cell = config.WorldToCell(ray.GetPoint(distance));
            pointer.Cell = cell;
            pointer.HasCell = cell.x >= 0 && cell.y >= 0 && cell.x < config.GridSize && cell.y < config.GridSize;
        }
    }
}
