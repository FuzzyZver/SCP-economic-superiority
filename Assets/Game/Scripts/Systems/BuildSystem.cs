using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Стройка построек: выбор инструмента, призрак под курсором, проверки, постановка, снос, поворот,
/// размещение объекта («объект в руках») и подсказка под курсором без инструмента.
/// Коридоры прокладывает и сносит CorridorSystem, привязку камер к постам — ResearchPostSystem.
///
/// Комната работает, только если клетка перед каждым её входом — связанный коридор нужной ширины
/// (это считает ConnectivitySystem). R крутит постройку при выборе или ту, что под курсором.
/// Лифт занимает те же клетки этажом ниже — так появляется станция и путь на следующий этаж.
///
/// Стартовые постройки приходят событием PlaceBuildingEvent с Free = true и идут тем же путём.
/// Размытые водой постройки (CollapseFlag) сносятся здесь же, без возврата денег.
/// </summary>
public class BuildSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<SelectToolEvent> _selects;
    private EcsFilter<CancelInputEvent> _cancels;
    private EcsFilter<RotateInputEvent> _rotations;
    private EcsFilter<ClickInputEvent> _clicks;
    private EcsFilter<PlaceBuildingEvent> _placeBuildings;

    private EcsFilter<SiteFlag, GridAttribute, BuildToolAttribute, PointerAttribute, ViewFloorAttribute> _sites;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;
    private EcsFilter<BuildingFlag, BuildingAttribute, CollapseFlag> _collapsed;
    private EcsFilter<ScpFlag, ScpAttribute, ContainedAttribute, NicknameAttribute> _scps;

    private readonly List<Vector2Int> _cells = new List<Vector2Int>();
    private readonly List<Vector2Int> _check = new List<Vector2Int>();
    private readonly List<Vector2Int> _doors = new List<Vector2Int>();
    private readonly List<Vector3> _doorPoints = new List<Vector3>();
    private readonly List<Vector2Int> _zone = new List<Vector2Int>();
    private readonly List<Vector2Int> _openings = new List<Vector2Int>();
    private bool _openingsDirty;   // пристроили, повернули или снесли что-то у главного въезда — прорезать ворота в зале

    private BuildingsConfig Config => GameConfig.BuildingsConfig;

    public void Run()
    {
        foreach (var s in _sites)
        {
            var site = _sites.GetEntity(s);
            ref var grid = ref _sites.Get2(s);
            ref var tool = ref _sites.Get3(s);
            var pointer = _sites.Get4(s);
            int depth = _sites.Get5(s).Depth;

            foreach (var c in _collapsed)
            {
                var building = _collapsed.Get2(c);
                var def = Config.Def(building.Def);
                Log($"Обрушение: «{(def != null ? def.FullName : "постройка")}» размыло грунтовыми водами ({Config.FloorName(building.Depth)}).");
                Demolish(ref grid, _collapsed.GetEntity(c), building, false);
            }

            foreach (var p in _placeBuildings)
            {
                ref var place = ref _placeBuildings.Get1(p);
                PlaceBuilding(site, ref grid, place.Def, place.Depth, place.Origin, place.Rotation, place.Free);
            }

            foreach (var e in _selects) Select(ref tool, _selects.Get1(e));

            if (!_cancels.IsEmpty())
            {
                if (tool.Dragging) tool.Dragging = false;
                else tool.Tool = BuildTool.None;
            }

            if (!_rotations.IsEmpty()) Rotate(site, ref grid, ref tool, pointer, depth);

            bool clicked = !_clicks.IsEmpty();
            switch (tool.Tool)
            {
                case BuildTool.Building: BuildingTool(site, ref grid, ref tool, pointer, depth, clicked); break;
                case BuildTool.PlaceScp: ScpTool(site, ref grid, ref tool, pointer, depth, clicked); break;
                case BuildTool.Demolish: DemolishTool(ref grid, ref tool, pointer, depth, clicked); break;
                case BuildTool.None: Hover(ref grid, ref tool, pointer, depth); break;
            }
        }

        if (_openingsDirty) UpdateEntranceOpenings();
    }

    /// <summary>Ворота пристроек (автопарк, вертолётная) выходят прямо в зал главного въезда — убираем там стену зала.</summary>
    private void UpdateEntranceOpenings()
    {
        _openingsDirty = false;
        _openings.Clear();
        RoomActor hall = null;

        foreach (var b in _buildings)
        {
            var building = _buildings.Get2(b);
            var def = Config.Def(building.Def);
            if (def == null) continue;

            if (def.Role == BuildingRole.MainEntrance)
            {
                var entity = _buildings.GetEntity(b);
                if (entity.Has<BuildingRef>()) hall = entity.Get<BuildingRef>().Value;
                continue;
            }

            if (def.AttachToEntrance) def.DoorSpan(building.Origin, building.Rotation, _openings);
        }

        if (hall != null) hall.SetOpenings(_openings);
    }

    // ─────────────────────── выбор инструмента ───────────────────────

    private void Select(ref BuildToolAttribute tool, SelectToolEvent request)
    {
        bool same = tool.Tool == request.Tool
                    && (request.Tool != BuildTool.Building || tool.Building == request.Building)
                    && (request.Tool != BuildTool.Corridor || tool.Corridor == request.Corridor)
                    && (request.Tool != BuildTool.PlaceScp || tool.Scp == request.Scp);

        tool.Dragging = false;
        if (same)
        {
            tool.Tool = BuildTool.None;
            return;
        }

        tool.Tool = request.Tool;
        if (request.Tool == BuildTool.Building) tool.Building = request.Building;
        if (request.Tool == BuildTool.Corridor) tool.Corridor = request.Corridor;
        if (request.Tool == BuildTool.PlaceScp) tool.Scp = request.Scp;
    }

    // ─────────────────────── инструмент: постройка ───────────────────────

    private void BuildingTool(EcsEntity site, ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked)
    {
        var def = Config.Def(tool.Building);
        if (def == null)
        {
            tool.Tool = BuildTool.None;
            return;
        }

        string hint = $"{def.FullName} — {def.Cost:N0}, содержание {def.MonthlyUpkeep:N0}/мес. {def.Description} R — повернуть, ПКМ — отмена.";
        if (!pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length)
        {
            HideGhost();
            Status(ref tool, hint, false);
            return;
        }

        bool ok = Preview(site, ref grid, def, depth, pointer.Cell, tool.Rotation, out var origin, out string text);
        Status(ref tool, text ?? hint, !ok);

        if (clicked && ok) PlaceBuilding(site, ref grid, tool.Building, depth, origin, tool.Rotation, false);
    }

    /// <summary>Призрак постройки под курсором: клетки, входы, зона насосной. text — что не так (null — всё хорошо).</summary>
    private bool Preview(EcsEntity site, ref GridAttribute grid, BuildingDefinition def, int depth, Vector2Int cell, int rotation,
        out Vector2Int origin, out string text)
    {
        var footprint = def.Footprint(rotation);
        origin = cell - new Vector2Int(footprint.x / 2, footprint.y / 2);

        bool ok = CanPlace(site, ref grid, def, depth, origin, rotation, false, out string reason);
        var issue = ok ? WillConnect(ref grid, def, depth, origin, rotation) : ConnectionIssue.None;
        // работающая насосная осушает и себя — предупреждаем только если её не подключить
        bool wet = ok && Wet(grid.Floors[depth], def, origin, rotation) && (def.Role != BuildingRole.Pump || issue != ConnectionIssue.None);

        var color = !ok ? Config.GhostInvalid : issue != ConnectionIssue.None || wet ? Config.GhostUnconnected : Config.GhostValid;
        def.Cells(origin, rotation, _cells);
        def.DoorsOutside(origin, rotation, _doors);
        ShowGhost(depth, color, _doors);

        if (def.Role == BuildingRole.Pump) ShowPumpZone(depth, origin, footprint, def.PumpRadius);
        else if (SceneData.Ghost != null) SceneData.Ghost.HideZone();

        text = null;
        if (!ok) text = $"{def.FullName}: {reason}";
        else if (issue != ConnectionIssue.None) text = $"{def.FullName} — {def.Cost:N0}. {Capital(IssueText(issue, def))}: работать не будет.";
        if (ok && wet) text = (text ?? $"{def.FullName} — {def.Cost:N0}.") + " Стоит в воде — без насосной размоет.";
        return ok;
    }

    // ─────────────────────── инструмент: объект в руках ───────────────────────

    private void ScpTool(EcsEntity site, ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked)
    {
        var scpDef = GameConfig.ScpConfig.Get(tool.Scp);
        if (scpDef == null || IsContained(tool.Scp))
        {
            tool.Tool = BuildTool.None;
            HideGhost();
            return;
        }

        // особому объекту — его особая камера, стабильному гуманоиду — комфортная камера без постоянной слежки
        int roomIndex = scpDef.Containment == ContainmentType.Special ? Config.IndexOf(scpDef.SpecialRoom)
            : scpDef.Stable ? Config.CellFor(ContainmentType.StableHumanoid)
            : Config.CellFor(scpDef.Containment);
        if (roomIndex < 0 && scpDef.Containment != ContainmentType.Special) roomIndex = Config.CellFor(scpDef.Containment);
        var roomDef = Config.Def(roomIndex);
        string number = scpDef.Number;
        string roomName = roomDef != null ? roomDef.Name : GameConfig.ScpConfig.ContainmentName(scpDef.Containment);
        string hint = $"{number} в руках: наведи на свободную камеру ({roomName.ToLowerInvariant()}) или поставь новую. R — повернуть, ПКМ — отмена.";

        if (!pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length)
        {
            HideGhost();
            Status(ref tool, hint, false);
            return;
        }

        var floor = grid.Floors[depth];
        int id = floor.BuildingAt(pointer.Cell);
        if (id != FloorGrid.Empty && TryFind(id, out int index))
        {
            var building = _buildings.Get2(index);
            var def = Config.Def(building.Def);
            bool fits = ScpConfig.Fits(scpDef, def);
            bool occupied = Occupant(building.Id) != null;
            bool connected = _buildings.GetEntity(index).Has<ConnectedFlag>();

            def.Cells(building.Origin, building.Rotation, _cells);
            _doors.Clear();
            ShowGhost(depth, !fits || occupied ? Config.GhostInvalid : connected ? Config.GhostValid : Config.GhostUnconnected, _doors);

            if (!fits) Status(ref tool, $"«{def.FullName}» не подходит: {number} нужна камера «{roomName}».", true);
            else if (occupied) Status(ref tool, $"«{def.FullName}» занята.", true);
            else Status(ref tool, $"Поместить {number} в «{def.FullName}»" + (connected ? "." : " — камера пока без доступа к коридору."), false);

            if (!clicked || !fits || occupied) return;
            Contain(tool.Scp, building.Id);
            tool.Tool = BuildTool.None;
            HideGhost();
            return;
        }

        if (roomDef == null)
        {
            HideGhost();
            Status(ref tool, $"Для {number} в каталоге нет камеры «{roomName}».", true);
            return;
        }

        bool ok = Preview(site, ref grid, roomDef, depth, pointer.Cell, tool.Rotation, out var origin, out string text);
        Status(ref tool, text != null ? $"{number}: {text}" : $"{number}: новая «{roomDef.Name}» — {roomDef.Cost:N0}. Или наведи на свободную камеру. R — повернуть.", !ok);
        if (!clicked || !ok) return;

        int placed = PlaceBuilding(site, ref grid, roomIndex, depth, origin, tool.Rotation, false);
        if (placed < 0) return;

        Contain(tool.Scp, placed);
        tool.Tool = BuildTool.None;
        HideGhost();
    }

    private void Contain(ScpId id, int roomId)
    {
        ref var contain = ref _world.NewEntity().Get<ContainScpEvent>();
        contain.Id = id;
        contain.RoomId = roomId;
    }

    // ─────────────────────── инструмент: снос ───────────────────────

    private void DemolishTool(ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked)
    {
        string hint = "Снос: клик по постройке или пролёту коридора, рамка — участок коридоров. Вернётся " + Config.RefundPercent + "% цены.";

        if (!pointer.HasCell || pointer.OverUI || depth <= 0 || depth >= grid.Floors.Length)
        {
            if (!tool.Dragging) HideGhost();
            Status(ref tool, hint, false);
            return;
        }

        if (tool.Dragging) return;   // протянутую рамку ведёт CorridorSystem

        var floor = grid.Floors[depth];
        int id = floor.BuildingAt(pointer.Cell);
        if (id == FloorGrid.Empty || !TryFind(id, out int index))
        {
            if (!floor.IsCorridor(pointer.Cell))
            {
                HideGhost();
                Status(ref tool, hint, false);
            }
            return;   // коридор под курсором — это CorridorSystem
        }

        var building = _buildings.Get2(index);
        var def = Config.Def(building.Def);
        def.Cells(building.Origin, building.Rotation, _cells);
        _doors.Clear();
        ShowGhost(depth, Config.GhostInvalid, _doors);

        bool locked = def.Role == BuildingRole.MainEntrance;
        int refund = building.Paid * Config.RefundPercent / 100;
        string occupant = Occupant(building.Id);
        string text = locked ? "Главный въезд снести нельзя." : $"Снести «{def.FullName}»: вернётся {refund:N0}.";
        if (!locked && occupant != null) text += $" {occupant} останется без камеры!";
        Status(ref tool, text, locked || occupant != null);

        if (clicked && !locked) Demolish(ref grid, _buildings.GetEntity(index), building, true);
    }

    // ─────────────────────── без инструмента: подсказка ───────────────────────

    private void Hover(ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth)
    {
        HideGhost();

        if (!pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length)
        {
            Status(ref tool, string.Empty, false);
            return;
        }

        var floor = grid.Floors[depth];
        int id = floor.BuildingAt(pointer.Cell);

        if (id != FloorGrid.Empty && TryFind(id, out int index))
        {
            var building = _buildings.Get2(index);
            var entity = _buildings.GetEntity(index);
            var def = Config.Def(building.Def);
            bool connected = entity.Has<ConnectedFlag>();

            string text = def.FullName + (connected ? " · работает" : " · " + IssueText(building.Issue, def));
            if (def.Planned) text += " · заготовка, функция позже";

            if (def.Role == BuildingRole.Containment)
            {
                string occupant = Occupant(building.Id);
                text += occupant != null ? $" · {occupant} (клик — документ)" : " · пустая";
                if (building.PostId == BuildingAttribute.NoPost) text += " · не привязана к посту — исследования стоят";
                else if (building.PostDistance < 0) text += " · до поста не дойти по коридорам";
                else text += $" · пост в {building.PostDistance} м, скорость {Mathf.RoundToInt(GameConfig.BalanceConfig.ResearchFactor(building.PostDistance) * 100f)}%";
            }
            else if (def.Role == BuildingRole.ResearchPost)
            {
                text += $" · учёных занято {Bound(building.Id)}/{def.Capacity} · клик — привязать камеры";
            }
            else if (def.Role == BuildingRole.Pump)
            {
                text += $" · откачка в радиусе {def.PumpRadius} м";
            }

            bool wet = Wet(floor, def, building.Origin, building.Rotation);
            if (wet || building.Condition < 0.999f)
                text += $" · прочность {Mathf.RoundToInt(building.Condition * 100f)}%" + (wet ? ", в воде — нужна насосная" : "");

            if (def.Role != BuildingRole.MainEntrance) text += " · R — повернуть";
            Status(ref tool, text, !connected || wet);
            return;
        }

        int index2 = floor.Index(pointer.Cell);
        int tier = floor.Corridor[index2];
        if (tier > 0)
        {
            var def = Config.Corridor(tier - 1);
            bool reached = floor.Reached[index2];
            Status(ref tool, (def != null ? $"{def.Name} ({def.Width} м)" : "Коридор") +
                             (reached ? " · связан с главным въездом" : " · обрыв: не связан с главным въездом"), !reached);
            return;
        }

        byte water = floor.Water[index2];
        if (water > 0)
        {
            bool drained = floor.Drain[index2] >= water;
            Status(ref tool, (water >= 2 ? "Подземная река" : "Грунтовые воды") +
                             (drained ? " · откачано насосной" : water >= 2 ? " · строить можно только с большой насосной" : " · нужна насосная"), !drained);
            return;
        }

        Status(ref tool, string.Empty, false);
    }

    // ─────────────────────── поворот ───────────────────────

    private void Rotate(EcsEntity site, ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth)
    {
        if (tool.Tool == BuildTool.Building || tool.Tool == BuildTool.PlaceScp)
        {
            tool.Rotation = (tool.Rotation + 1) & 3;
            return;
        }

        if (tool.Tool != BuildTool.None || !pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length) return;

        int id = grid.Floors[depth].BuildingAt(pointer.Cell);
        if (id == FloorGrid.Empty || !TryFind(id, out int index)) return;

        ref var building = ref _buildings.Get2(index);
        var def = Config.Def(building.Def);
        if (def.Role == BuildingRole.MainEntrance)
        {
            Log("Главный въезд не поворачивается.");
            return;
        }

        int rotation = (building.Rotation + 1) & 3;
        var oldSize = def.Footprint(building.Rotation);
        var newSize = def.Footprint(rotation);
        var origin = building.Origin + new Vector2Int((oldSize.x - newSize.x) / 2, (oldSize.y - newSize.y) / 2);

        Vacate(ref grid, def, building);
        bool ok = CanPlace(site, ref grid, def, building.Depth, origin, rotation, true, out string reason);

        if (!ok)
        {
            Occupy(ref grid, def, building.Id, building.Depth, building.Origin, building.Rotation);
            Log($"«{def.FullName}» не повернуть: {reason}");
            return;
        }

        building.Origin = origin;
        building.Rotation = rotation;
        Occupy(ref grid, def, building.Id, building.Depth, origin, rotation);
        Rebuild(_buildings.GetEntity(index), building, def);
        if (def.AttachToEntrance) _openingsDirty = true;
        _world.NewEntity().Get<GridChangedEvent>();
    }

    // ─────────────────────── проверки ───────────────────────

    private bool CanPlace(EcsEntity site, ref GridAttribute grid, BuildingDefinition def, int depth, Vector2Int origin, int rotation, bool free, out string reason)
    {
        reason = null;
        int maxDepth = grid.Floors.Length - 1;

        if (depth <= 0) { reason = "на поверхности строить пока нельзя"; return false; }
        if (depth > maxDepth) { reason = "такого этажа нет"; return false; }

        var floor = grid.Floors[depth];
        bool elevator = BuildingsConfig.IsElevator(def.Role);

        if (!free)
        {
            if (!def.Buildable) { reason = "это не строится"; return false; }
            if (!floor.Open) { reason = $"этаж закрыт — нужны пассажирский и грузовой лифты на −{depth - 1}"; return false; }
            if (site.Get<DirectorAttribute>().Level < def.RequiredDirectorLevel) { reason = $"нужен уровень директора {def.RequiredDirectorLevel}"; return false; }
        }

        if (def.OnlyDepth > 0 && depth != def.OnlyDepth) { reason = $"только на этаже −{def.OnlyDepth}"; return false; }
        if (elevator && depth >= maxDepth) { reason = "ниже этажей нет"; return false; }

        def.Cells(origin, rotation, _check);
        var lower = elevator ? grid.Floors[depth + 1] : null;

        foreach (var cell in _check)
        {
            if (!floor.InBounds(cell)) { reason = "выходит за границу этажа"; return false; }

            int index = floor.Index(cell);
            if (def.CorridorModule)
            {
                if (floor.Corridor[index] == 0) { reason = "ставится только на коридор"; return false; }
                if (floor.Building[index] != FloorGrid.Empty) { reason = "место занято"; return false; }
                int width = Config.WidthOfTier(floor.Corridor[index]);
                if (width < def.MinCorridorWidth) { reason = $"нужен коридор от {def.MinCorridorWidth} м, здесь {width} м"; return false; }
            }
            else
            {
                if (floor.Building[index] != FloorGrid.Empty) { reason = "место занято"; return false; }
                if (floor.Corridor[index] != 0) { reason = "мешает коридор"; return false; }
            }

            if (lower != null && !lower.IsFree(cell)) { reason = "шахта упирается в постройку этажом ниже"; return false; }
        }

        if (def.HasDoor)
        {
            def.DoorsOutside(origin, rotation, _doors);
            foreach (var door in _doors)
                if (!floor.InBounds(door)) { reason = "вход смотрит за границу этажа"; return false; }

            if (def.AttachToEntrance && !IsMainEntrance(floor.BuildingAt(def.DoorOutside(origin, rotation))))
            { reason = "ворота должны выходить прямо в зал главного въезда"; return false; }
        }

        if (!free && site.Get<TreasuryAttribute>().Money < def.Cost) { reason = $"не хватает денег ({def.Cost:N0})"; return false; }
        return true;
    }

    /// <summary>Заработает ли постройка на этом месте — те же правила, что у ConnectivitySystem.</summary>
    private ConnectionIssue WillConnect(ref GridAttribute grid, BuildingDefinition def, int depth, Vector2Int origin, int rotation)
    {
        var floor = grid.Floors[depth];

        if (def.Role == BuildingRole.MainEntrance) return ConnectionIssue.None;

        if (def.CorridorModule)
        {
            def.Cells(origin, rotation, _check);
            foreach (var cell in _check)
                if (floor.IsReached(cell)) return ConnectionIssue.None;
            return ConnectionIssue.NoCorridor;
        }

        if (def.AttachToEntrance)
            return IsMainEntrance(floor.BuildingAt(def.DoorOutside(origin, rotation))) ? ConnectionIssue.None : ConnectionIssue.NotAtEntrance;

        var issue = DoorIssue(floor, def, def.DoorOutside(origin, rotation), true);
        if (issue == ConnectionIssue.NoCorridor && BuildingsConfig.IsElevator(def.Role) && depth + 1 < grid.Floors.Length
            && grid.Floors[depth + 1].IsReached(def.DoorOutside(origin, rotation)))
            issue = ConnectionIssue.None;

        if (issue == ConnectionIssue.None && def.HasControlRoom && DoorIssue(floor, def, def.ControlOutside(origin, rotation), false) != ConnectionIssue.None)
            issue = ConnectionIssue.NoControlAccess;

        return issue;
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

    private string IssueText(ConnectionIssue issue, BuildingDefinition def)
    {
        switch (issue)
        {
            case ConnectionIssue.NoCorridor: return "нет доступа к коридору";
            case ConnectionIssue.NoControlAccess: return "к пультовой не подведён коридор";
            case ConnectionIssue.NarrowCorridor: return $"нужен коридор от {def.MinCorridorWidth} м";
            case ConnectionIssue.ServiceTunnel: return "техтоннель — только для инфраструктуры";
            case ConnectionIssue.NotAtEntrance: return "ворота должны выходить в зал главного въезда";
            default: return "не работает";
        }
    }

    private static string Capital(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0]) + text.Substring(1);

    private bool Wet(FloorGrid floor, BuildingDefinition def, Vector2Int origin, int rotation)
    {
        def.Cells(origin, rotation, _check);
        foreach (var cell in _check)
            if (floor.IsWet(cell)) return true;
        return false;
    }

    private bool IsMainEntrance(int buildingId)
    {
        if (buildingId == FloorGrid.Empty || !TryFind(buildingId, out int index)) return false;
        var def = Config.Def(_buildings.Get2(index).Def);
        return def != null && def.Role == BuildingRole.MainEntrance;
    }

    // ─────────────────────── постановка и снос ───────────────────────

    /// <summary>Ставит постройку и возвращает её id; −1 — не встала.</summary>
    private int PlaceBuilding(EcsEntity site, ref GridAttribute grid, int defIndex, int depth, Vector2Int origin, int rotation, bool free)
    {
        var def = Config.Def(defIndex);
        if (def == null) return -1;

        if (!CanPlace(site, ref grid, def, depth, origin, rotation, free, out string reason))
        {
            if (free) Debug.LogWarning($"Стартовая постройка «{def.FullName}» не встала: {reason}");
            return -1;
        }

        int id = grid.NextId++;
        Occupy(ref grid, def, id, depth, origin, rotation);

        var actor = CreateActor(depth, def.FullName);
        actor.Init(_world);
        var entity = actor.GetEntity();

        ref var building = ref entity.Get<BuildingAttribute>();
        building.Id = id;
        building.Def = defIndex;
        building.Depth = depth;
        building.Origin = origin;
        building.Rotation = rotation;
        building.Paid = free ? 0 : def.Cost;
        building.Condition = 1f;
        building.Issue = ConnectionIssue.None;
        building.PostId = BuildingAttribute.NoPost;
        building.PostDistance = -1;

        actor.Build(def, Config, origin, rotation, depth, false);
        if (def.AttachToEntrance) _openingsDirty = true;

        if (BuildingsConfig.IsElevator(def.Role))
        {
            var stop = CreateActor(depth + 1, def.Name + " (станция)");
            stop.Build(def, Config, origin, rotation, depth + 1, true);
            entity.Get<LowerStopRef>().Value = stop;
        }

        if (!free)
        {
            Money(-def.Cost, "Стройка: " + def.FullName);
            Log($"Построено: {def.FullName} ({Config.FloorName(depth)}).");
        }

        _world.NewEntity().Get<GridChangedEvent>();
        return id;
    }

    private void Demolish(ref GridAttribute grid, EcsEntity entity, BuildingAttribute building, bool refund)
    {
        var def = Config.Def(building.Def);
        if (def != null) Vacate(ref grid, def, building);
        if (def != null && def.AttachToEntrance) _openingsDirty = true;

        if (entity.Has<BuildingRef>() && entity.Get<BuildingRef>().Value != null) Object.Destroy(entity.Get<BuildingRef>().Value.gameObject);
        if (entity.Has<LowerStopRef>() && entity.Get<LowerStopRef>().Value != null) Object.Destroy(entity.Get<LowerStopRef>().Value.gameObject);

        if (refund && def != null)
        {
            int money = building.Paid * Config.RefundPercent / 100;
            if (money > 0) Money(money, "Снос: " + def.FullName);
            Log($"Снесено: {def.FullName} ({Config.FloorName(building.Depth)}).");
        }

        ref var removed = ref _world.NewEntity().Get<BuildingRemovedEvent>();
        removed.Id = building.Id;
        removed.Def = building.Def;

        entity.Destroy();
        _world.NewEntity().Get<GridChangedEvent>();
    }

    private void Rebuild(EcsEntity entity, BuildingAttribute building, BuildingDefinition def)
    {
        if (entity.Has<BuildingRef>() && entity.Get<BuildingRef>().Value != null)
            entity.Get<BuildingRef>().Value.Build(def, Config, building.Origin, building.Rotation, building.Depth, false);

        if (entity.Has<LowerStopRef>() && entity.Get<LowerStopRef>().Value != null)
            entity.Get<LowerStopRef>().Value.Build(def, Config, building.Origin, building.Rotation, building.Depth + 1, true);

        // объект внутри перестроенной камеры заново рисует ContainmentSystem по ConnectivityChangedEvent
    }

    private void Occupy(ref GridAttribute grid, BuildingDefinition def, int id, int depth, Vector2Int origin, int rotation)
    {
        def.Cells(origin, rotation, _check);
        var floor = grid.Floors[depth];
        var lower = BuildingsConfig.IsElevator(def.Role) && depth + 1 < grid.Floors.Length ? grid.Floors[depth + 1] : null;

        foreach (var cell in _check)
        {
            floor.Building[floor.Index(cell)] = id;
            if (lower != null) lower.Building[lower.Index(cell)] = id;
        }
    }

    private void Vacate(ref GridAttribute grid, BuildingDefinition def, BuildingAttribute building)
    {
        def.Cells(building.Origin, building.Rotation, _check);
        var floor = grid.Floors[building.Depth];
        var lower = BuildingsConfig.IsElevator(def.Role) && building.Depth + 1 < grid.Floors.Length ? grid.Floors[building.Depth + 1] : null;

        foreach (var cell in _check)
        {
            if (floor.InBounds(cell) && floor.Building[floor.Index(cell)] == building.Id) floor.Building[floor.Index(cell)] = FloorGrid.Empty;
            if (lower != null && lower.InBounds(cell) && lower.Building[lower.Index(cell)] == building.Id) lower.Building[lower.Index(cell)] = FloorGrid.Empty;
        }
    }

    // ─────────────────────── служебное ───────────────────────

    private RoomActor CreateActor(int depth, string title)
    {
        var floors = SceneData.Floors;
        var floor = floors != null && depth < floors.Length ? floors[depth] : null;
        return floor != null ? floor.CreateRoom(title) : new GameObject(title).AddComponent<RoomActor>();
    }

    private bool TryFind(int id, out int index)
    {
        foreach (var i in _buildings)
        {
            if (_buildings.Get2(i).Id != id) continue;
            index = i;
            return true;
        }

        index = -1;
        return false;
    }

    private string Occupant(int roomId)
    {
        foreach (var i in _scps)
        {
            if (_scps.Get3(i).RoomId != roomId) continue;
            var definition = GameConfig.ScpConfig.Get(_scps.Get2(i).Id);
            return definition != null ? definition.Number : _scps.Get2(i).Id.ToString();
        }
        return null;
    }

    private bool IsContained(ScpId id)
    {
        foreach (var i in _scps)
            if (_scps.Get2(i).Id == id) return _scps.Get3(i).IsContained;
        return false;
    }

    /// <summary>Сколько камер привязано к посту.</summary>
    private int Bound(int postId)
    {
        int count = 0;
        foreach (var i in _buildings)
            if (_buildings.Get2(i).PostId == postId) count++;
        return count;
    }

    private void ShowGhost(int depth, Color color, List<Vector2Int> doors)
    {
        var ghost = SceneData.Ghost;
        if (ghost == null) return;

        ghost.Show(_cells, Config, depth, color);
        _doorPoints.Clear();
        foreach (var door in doors) _doorPoints.Add(Config.CellCenter(door, depth));
        ghost.ShowDoors(_doorPoints, color);
    }

    private void ShowPumpZone(int depth, Vector2Int origin, Vector2Int footprint, int radius)
    {
        if (SceneData.Ghost == null) return;

        _zone.Clear();
        var center = new Vector2(origin.x + footprint.x * 0.5f, origin.y + footprint.y * 0.5f);
        int size = Config.GridSize;
        for (int z = Mathf.Max(0, (int)(center.y - radius) - 1); z <= Mathf.Min(size - 1, (int)(center.y + radius) + 1); z++)
            for (int x = Mathf.Max(0, (int)(center.x - radius) - 1); x <= Mathf.Min(size - 1, (int)(center.x + radius) + 1); x++)
                if ((new Vector2(x + 0.5f, z + 0.5f) - center).sqrMagnitude <= radius * radius) _zone.Add(new Vector2Int(x, z));

        SceneData.Ghost.ShowZone(_zone, Config, depth, Config.GhostZone);
    }

    private void HideGhost()
    {
        if (SceneData.Ghost != null) SceneData.Ghost.Hide();
    }

    private static void Status(ref BuildToolAttribute tool, string text, bool error)
    {
        tool.Status = text;
        tool.StatusIsError = error;
    }

    private void Money(int delta, string reason)
    {
        ref var change = ref _world.NewEntity().Get<MoneyChangeEvent>();
        change.Delta = delta;
        change.Reason = reason;
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
