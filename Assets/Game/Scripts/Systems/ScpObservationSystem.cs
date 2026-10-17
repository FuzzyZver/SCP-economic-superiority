using Leopotam.Ecs;

/// <summary>
/// Пассивное изучение: каждый день объект в работающей камере немного раскрывается,
/// пока не упрётся в ObservationCap. Дальше — только испытания.
/// Изучение идёт, только если камера привязана к исследовательскому посту и до поста можно дойти:
/// чем дальше пост по коридорам, тем медленнее (BalanceConfig.ResearchFactor).
/// </summary>
public class ScpObservationSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<DayStartedEvent> _days;
    private EcsFilter<ScpFlag, ScpAttribute, ResearchAttribute, ContainedAttribute> _scps;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    public void Run()
    {
        if (_days.IsEmpty()) return;

        var balance = GameConfig.BalanceConfig;
        var config = GameConfig.ScpConfig;

        foreach (var _ in _days)
        {
            foreach (var i in _scps)
            {
                int roomId = _scps.Get4(i).RoomId;
                if (roomId == ContainedAttribute.NoRoom || !TryRoom(roomId, out var room, out bool connected) || !connected) continue;
                if (room.PostId == BuildingAttribute.NoPost || room.PostDistance < 0 || !PostWorks(room.PostId)) continue;

                var id = _scps.Get2(i).Id;
                float progress = _scps.Get3(i).Progress;
                if (progress >= balance.ObservationCap) continue;

                var definition = config.Get(id);
                float rate = (definition != null ? definition.ObservationRate : 1f) * balance.ResearchFactor(room.PostDistance);

                float amount = balance.ObservationPerDay * rate;
                if (progress + amount > balance.ObservationCap) amount = balance.ObservationCap - progress;
                if (amount <= 0f) continue;

                ref var gain = ref _world.NewEntity().Get<ResearchGainEvent>();
                gain.Id = id;
                gain.Amount = amount;
                gain.Source = "наблюдения";
            }
        }
    }

    private bool PostWorks(int postId)
    {
        foreach (var b in _buildings)
            if (_buildings.Get2(b).Id == postId) return _buildings.GetEntity(b).Has<ConnectedFlag>();
        return false;
    }

    private bool TryRoom(int roomId, out BuildingAttribute room, out bool connected)
    {
        foreach (var b in _buildings)
        {
            if (_buildings.Get2(b).Id != roomId) continue;

            room = _buildings.Get2(b);
            connected = _buildings.GetEntity(b).Has<ConnectedFlag>();
            return true;
        }

        room = default(BuildingAttribute);
        connected = false;
        return false;
    }
}
