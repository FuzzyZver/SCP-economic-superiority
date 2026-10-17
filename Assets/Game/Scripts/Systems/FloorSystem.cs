using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Какой этаж на экране. Z — глубже, C — выше, плюс стрелки в меню стройки.
/// Видно только текущий этаж: верхние иначе закрывают обзор, нижние путают.
/// Камера плавно уезжает на высоту этажа (это делает CameraSystem по TargetHeight).
/// </summary>
public class FloorSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private EcsWorld _world;

    private EcsFilter<FloorInputEvent> _inputs;
    private EcsFilter<SiteFlag, ViewFloorAttribute, BuildToolAttribute> _sites;
    private EcsFilter<CameraFlag, CameraAttribute> _cameras;

    public void Init()
    {
        Subscribe(true);
        foreach (var s in _sites) Apply(_sites.Get2(s).Depth);
    }

    public void Destroy() => Subscribe(false);

    public void Run()
    {
        if (_inputs.IsEmpty()) return;

        int max = GameConfig.BuildingsConfig.MaxDepth;

        foreach (var s in _sites)
        {
            ref var view = ref _sites.Get2(s);
            int depth = view.Depth;

            foreach (var i in _inputs) depth = Mathf.Clamp(depth + _inputs.Get1(i).Delta, 0, max);
            if (depth == view.Depth) continue;

            view.Depth = depth;
            _sites.Get3(s).Dragging = false;   // протянутый коридор не переезжает на другой этаж
            Apply(depth);
        }
    }

    private void Apply(int depth)
    {
        if (SceneData.Floors != null)
            for (int i = 0; i < SceneData.Floors.Length; i++)
                if (SceneData.Floors[i] != null) SceneData.Floors[i].SetVisible(i == depth);

        foreach (var c in _cameras)
            _cameras.Get2(c).TargetHeight = GameConfig.BuildingsConfig.FloorY(depth);

        if (SceneData.Ghost != null) SceneData.Ghost.Hide();
    }

    private void Subscribe(bool on)
    {
        var view = UI.Build;
        if (view == null) return;

        if (view.FloorUpButton != null)
        {
            if (on) view.FloorUpButton.onClick.AddListener(Up);
            else view.FloorUpButton.onClick.RemoveListener(Up);
        }

        if (view.FloorDownButton != null)
        {
            if (on) view.FloorDownButton.onClick.AddListener(Down);
            else view.FloorDownButton.onClick.RemoveListener(Down);
        }
    }

    private void Up() => _world.NewEntity().Get<FloorInputEvent>().Delta = -1;
    private void Down() => _world.NewEntity().Get<FloorInputEvent>().Delta = 1;
}
