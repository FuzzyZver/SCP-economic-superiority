using UnityEngine;
using Leopotam.Ecs;

public abstract class Actor : MonoBehaviour
{
    private EcsWorld _world;
    private EcsEntity _entity;

    public void Init(EcsWorld world)
    {
        _world = world;
        _entity = _world.NewEntity();
        ExpandEntity(_entity);
    }

    public abstract void ExpandEntity(EcsEntity entity);

    public EcsWorld GetWorld() => _world;
    public EcsEntity GetEntity() => _entity;
}
