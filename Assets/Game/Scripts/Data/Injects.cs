using Leopotam.Ecs;

/// <summary>
/// Всё, что EcsInclude раздаёт системам через Inject. Системы наследуются от этого класса
/// и получают поля без объявления в каждой.
/// </summary>
public class Injects
{
    public EcsWorld EcsWorld;
    public GameConfig GameConfig;
    public SceneData SceneData;
    public UI UI;
    public RealtimeData RealtimeData;
}
