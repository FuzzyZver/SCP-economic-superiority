using Leopotam.Ecs;

/// <summary>
/// Размещение объектов по камерам. Сам объект в камеру не садится: игрок выбирает его в списке
/// и указывает камеру (ContainScpEvent от BuildSystem). Здесь проверка, запись и отрисовка.
/// Объект без камеры каждый день понемногу поднимает рассекречивание,
/// снесённая или размытая камера с объектом внутри — бьёт сразу и сильно.
/// </summary>
public class ContainmentSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<ContainScpEvent> _contains;
    private EcsFilter<ConnectivityChangedEvent> _connectivity;
    private EcsFilter<BuildingRemovedEvent> _removed;
    private EcsFilter<DayStartedEvent> _days;

    private EcsFilter<ScpFlag, ScpAttribute, ContainedAttribute> _scps;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    public void Run()
    {
        foreach (var r in _removed) Release(_removed.Get1(r).Id);
        foreach (var c in _contains) Contain(_contains.Get1(c));

        if (!_connectivity.IsEmpty()) Redraw();

        foreach (var _ in _days) DailyLeak();
    }

    private void Contain(ContainScpEvent request)
    {
        var scpConfig = GameConfig.ScpConfig;
        var buildings = GameConfig.BuildingsConfig;

        foreach (var i in _scps)
        {
            if (_scps.Get2(i).Id != request.Id) continue;

            ref var contained = ref _scps.Get3(i);
            var definition = scpConfig.Get(request.Id);
            if (contained.IsContained || definition == null) return;
            if (!TryRoom(request.RoomId, out int roomIndex) || Occupied(request.RoomId)) return;

            var room = _buildings.Get2(roomIndex);
            var roomDef = buildings.Def(room.Def);
            if (!ScpConfig.Fits(definition, roomDef)) return;

            contained.RoomId = room.Id;
            Draw(roomIndex, definition);
            Log($"{definition.Number} помещён в «{roomDef.FullName}» ({buildings.FloorName(room.Depth)}).");
            return;
        }
    }

    private void Release(int roomId)
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var i in _scps)
        {
            ref var contained = ref _scps.Get3(i);
            if (contained.RoomId != roomId) continue;

            contained.RoomId = ContainedAttribute.NoRoom;
            string number = Number(_scps.Get2(i).Id);
            Log($"{number} остался без камеры содержания! Выберите его в списке и укажите новую камеру.");

            ref var leak = ref _world.NewEntity().Get<DeclassChangeEvent>();
            leak.Delta = balance.RoomLostDeclass;
            leak.Reason = $"{number} вывезли из разрушенной камеры";
        }
    }

    /// <summary>Камеры перестраиваются при повороте — объекты рисуем заново.</summary>
    private void Redraw()
    {
        var scpConfig = GameConfig.ScpConfig;
        foreach (var i in _scps)
        {
            int roomId = _scps.Get3(i).RoomId;
            if (roomId == ContainedAttribute.NoRoom || !TryRoom(roomId, out int roomIndex)) continue;
            Draw(roomIndex, scpConfig.Get(_scps.Get2(i).Id));
        }
    }

    private void DailyLeak()
    {
        int loose = 0;
        foreach (var i in _scps)
            if (!_scps.Get3(i).IsContained) loose++;

        if (loose == 0) return;

        ref var leak = ref _world.NewEntity().Get<DeclassChangeEvent>();
        leak.Delta = loose * GameConfig.BalanceConfig.UncontainedDeclassPerDay;
        leak.Reason = null;   // каждодневный рост видно по шкале, журнал не засоряем
    }

    private bool Occupied(int roomId)
    {
        foreach (var i in _scps)
            if (_scps.Get3(i).RoomId == roomId) return true;
        return false;
    }

    private bool TryRoom(int roomId, out int index)
    {
        foreach (var b in _buildings)
        {
            if (_buildings.Get2(b).Id != roomId) continue;
            index = b;
            return true;
        }

        index = -1;
        return false;
    }

    private void Draw(int roomIndex, ScpDefinition definition)
    {
        var room = _buildings.GetEntity(roomIndex);
        if (room.Has<BuildingRef>() && room.Get<BuildingRef>().Value != null)
            room.Get<BuildingRef>().Value.ShowOccupant(definition);
    }

    private string Number(ScpId id)
    {
        var definition = GameConfig.ScpConfig.Get(id);
        return definition != null ? definition.Number : id.ToString();
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
