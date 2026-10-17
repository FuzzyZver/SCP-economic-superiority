using System;
using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Кто с кем связан. Пересчитывается только когда сетка изменилась.
///
/// 1. От клетки перед главным въездом заливаем коридоры этажа −1.
/// 2. Лифт связывает этажи: если коридор у его входа связан на одном этаже,
///    связанным становится и коридор у станции на другом. Повторяем, пока что-то меняется.
/// 3. Постройка работает, если клетка перед каждым её входом — связанный коридор подходящей ширины
///    (техтоннель годится только инфраструктуре; модули коридора — если стоят на связанном коридоре;
///    автопарк и вертолётная — если ворота выходят в зал главного въезда).
/// 4. Этаж ниже открывается, когда на этаже выше есть связанные пассажирский и грузовой (или тяжёлый) лифты.
/// 5. Выходов на поверхность должно быть минимум два, иначе персоналу некуда эвакуироваться.
/// </summary>
public class ConnectivitySystem : Injects, IEcsRunSystem
{
    private const int RequiredExits = 2;

    private EcsWorld _world;

    private EcsFilter<GridChangedEvent> _changes;
    private EcsFilter<SiteFlag, GridAttribute> _sites;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    private readonly Queue<Vector2Int> _queue = new Queue<Vector2Int>();
    private readonly List<Vector2Int> _cells = new List<Vector2Int>();
    private bool[] _wasOpen;
    private int _lastExits = -1;

    private BuildingsConfig Config => GameConfig.BuildingsConfig;

    public void Run()
    {
        if (_changes.IsEmpty()) return;

        foreach (var s in _sites) Recompute(ref _sites.Get2(s));
        _world.NewEntity().Get<ConnectivityChangedEvent>();
    }

    private void Recompute(ref GridAttribute grid)
    {
        foreach (var floor in grid.Floors) Array.Clear(floor.Reached, 0, floor.Reached.Length);

        // 1. главный въезд
        int entranceId = FloorGrid.Empty;
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            var def = Config.Def(building.Def);
            if (def == null || def.Role != BuildingRole.MainEntrance) continue;

            entranceId = building.Id;
            Flood(grid.Floors[building.Depth], def.DoorOutside(building.Origin, building.Rotation));
        }

        // 2. лифты между этажами
        bool changed = true;
        for (int guard = 0; changed && guard < 32; guard++)
        {
            changed = false;
            foreach (var i in _buildings)
            {
                ref var building = ref _buildings.Get2(i);
                var def = Config.Def(building.Def);
                if (def == null || !BuildingsConfig.IsElevator(def.Role) || building.Depth + 1 >= grid.Floors.Length) continue;

                var door = def.DoorOutside(building.Origin, building.Rotation);
                var top = grid.Floors[building.Depth];
                var bottom = grid.Floors[building.Depth + 1];
                if (!top.IsReached(door) && !bottom.IsReached(door)) continue;

                changed |= Flood(top, door);
                changed |= Flood(bottom, door);
            }
        }

        // 3. какие постройки работают
        int exits = 0;
        var personnel = new bool[grid.Floors.Length];
        var freight = new bool[grid.Floors.Length];

        foreach (var i in _buildings)
        {
            var entity = _buildings.GetEntity(i);
            ref var building = ref _buildings.Get2(i);
            var def = Config.Def(building.Def);
            if (def == null) continue;

            var floor = grid.Floors[building.Depth];
            bool mainOk = true, controlOk = true, lowerOk = false;
            var issue = ConnectionIssue.None;

            if (def.Role == BuildingRole.MainEntrance) { }
            else if (def.CorridorModule)
            {
                if (!AnyReached(def, building, floor)) issue = ConnectionIssue.NoCorridor;
            }
            else if (def.AttachToEntrance)
            {
                mainOk = floor.BuildingAt(def.DoorOutside(building.Origin, building.Rotation)) == entranceId;
                if (!mainOk) issue = ConnectionIssue.NotAtEntrance;
            }
            else
            {
                issue = DoorIssue(floor, def, def.DoorOutside(building.Origin, building.Rotation), true);
                mainOk = issue == ConnectionIssue.None;

                if (BuildingsConfig.IsElevator(def.Role) && building.Depth + 1 < grid.Floors.Length)
                {
                    lowerOk = grid.Floors[building.Depth + 1].IsReached(def.DoorOutside(building.Origin, building.Rotation));
                    if (lowerOk && issue == ConnectionIssue.NoCorridor) issue = ConnectionIssue.None;   // лифт работает и снизу
                }

                if (def.HasControlRoom)
                {
                    controlOk = DoorIssue(floor, def, def.ControlOutside(building.Origin, building.Rotation), false) == ConnectionIssue.None;
                    if (issue == ConnectionIssue.None && !controlOk) issue = ConnectionIssue.NoControlAccess;
                }
            }

            bool connected = issue == ConnectionIssue.None;
            building.Issue = issue;

            if (connected) entity.Get<ConnectedFlag>();
            else if (entity.Has<ConnectedFlag>()) entity.Del<ConnectedFlag>();

            if (entity.Has<BuildingRef>() && entity.Get<BuildingRef>().Value != null)
                entity.Get<BuildingRef>().Value.SetState(connected ? null : IssueLabel(issue, def), mainOk, controlOk);
            if (entity.Has<LowerStopRef>() && entity.Get<LowerStopRef>().Value != null)
                entity.Get<LowerStopRef>().Value.SetState(lowerOk ? null : "НЕТ ДОСТУПА\nк коридору", lowerOk, true);

            if (!connected) continue;
            if (BuildingsConfig.IsExit(def.Role)) exits++;
            if (def.Role == BuildingRole.PersonnelElevator) personnel[building.Depth] = true;
            if (def.Role == BuildingRole.FreightElevator || def.Role == BuildingRole.HeavyElevator) freight[building.Depth] = true;
        }

        // 4. этажи
        bool first = _wasOpen == null || _wasOpen.Length != grid.Floors.Length;
        if (first) _wasOpen = new bool[grid.Floors.Length];

        for (int depth = 2; depth < grid.Floors.Length; depth++)
            grid.Floors[depth].Open = grid.Floors[depth - 1].Open && personnel[depth - 1] && freight[depth - 1];

        for (int depth = 0; depth < grid.Floors.Length; depth++)
        {
            bool open = grid.Floors[depth].Open;
            if (!first && depth > 1 && open != _wasOpen[depth])
                Log(open ? $"Открыт {Config.FloorName(depth).ToLowerInvariant()}: лифты на месте, можно строить."
                         : $"{Config.FloorName(depth)} закрыт: этажом выше не хватает связанных лифтов.");
            _wasOpen[depth] = open;
        }

        // 5. выходы
        grid.Exits = exits;
        if (exits < RequiredExits && (_lastExits < 0 || _lastExits >= RequiredExits))
            Log("Выход на поверхность только один. Постройте аварийный выход на −1: без него персоналу некуда эвакуироваться.");
        else if (exits >= RequiredExits && _lastExits >= 0 && _lastExits < RequiredExits)
            Log("Второй выход на поверхность готов. Эвакуация возможна.");
        _lastExits = exits;

        // вид
        if (SceneData.Floors == null) return;
        for (int depth = 0; depth < grid.Floors.Length && depth < SceneData.Floors.Length; depth++)
        {
            var actor = SceneData.Floors[depth];
            if (actor == null) continue;
            actor.RebuildCorridors(grid.Floors[depth]);
            actor.SetOpen(grid.Floors[depth].Open);
        }
    }

    private ConnectionIssue DoorIssue(FloorGrid floor, BuildingDefinition def, Vector2Int door, bool main)
    {
        if (!floor.IsReached(door)) return ConnectionIssue.NoCorridor;

        var corridor = Config.CorridorOfTier(floor.TierAt(door));
        if (corridor == null) return ConnectionIssue.NoCorridor;
        if (corridor.ServiceOnly && def.Category != BuildingCategory.Infrastructure) return ConnectionIssue.ServiceTunnel;
        if (main && corridor.Width < def.MinCorridorWidth) return ConnectionIssue.NarrowCorridor;
        return ConnectionIssue.None;
    }

    private static string IssueLabel(ConnectionIssue issue, BuildingDefinition def)
    {
        switch (issue)
        {
            case ConnectionIssue.NoControlAccess: return "НЕТ ДОСТУПА\nк пультовой";
            case ConnectionIssue.NarrowCorridor: return $"НУЖЕН КОРИДОР\nот {def.MinCorridorWidth} м";
            case ConnectionIssue.ServiceTunnel: return "ТЕХТОННЕЛЬ\nне подходит";
            case ConnectionIssue.NotAtEntrance: return "НЕ У ВЪЕЗДА";
            default: return "НЕТ ДОСТУПА\nк коридору";
        }
    }

    /// <summary>Заливка коридора от клетки. true — если что-то новое стало связанным.</summary>
    private bool Flood(FloorGrid floor, Vector2Int start)
    {
        if (!floor.IsCorridor(start) || floor.IsReached(start)) return false;

        _queue.Clear();
        _queue.Enqueue(start);
        floor.Reached[floor.Index(start)] = true;

        while (_queue.Count > 0)
        {
            var cell = _queue.Dequeue();
            Visit(floor, cell + Vector2Int.right);
            Visit(floor, cell + Vector2Int.left);
            Visit(floor, cell + Vector2Int.up);
            Visit(floor, cell + Vector2Int.down);
        }

        return true;
    }

    private void Visit(FloorGrid floor, Vector2Int cell)
    {
        if (!floor.IsCorridor(cell) || floor.IsReached(cell)) return;
        floor.Reached[floor.Index(cell)] = true;
        _queue.Enqueue(cell);
    }

    private bool AnyReached(BuildingDefinition def, BuildingAttribute building, FloorGrid floor)
    {
        def.Cells(building.Origin, building.Rotation, _cells);
        foreach (var cell in _cells)
            if (floor.IsReached(cell)) return true;
        return false;
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
