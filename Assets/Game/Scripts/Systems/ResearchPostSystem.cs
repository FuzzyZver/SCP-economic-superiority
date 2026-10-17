using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Исследовательские посты. Каждая камера с объектом привязана к одному посту:
/// там сидит учёный, который ведёт объект (второй — в пультовой самой камеры).
/// В посту мест по числу учёных: 1 учёный — 1 объект (малый 2, средний 6, большой 12).
///
/// Камера с объектом без поста привязывается сама к ближайшему посту со свободным местом.
/// Клик по посту — выбрать его: дальше клик по камере привязывает или отвязывает её.
/// Для каждой привязки считается путь по коридорам от входа поста до пультовой камеры
/// (через лифты — с наценкой): чем дальше, тем медленнее исследования (BalanceConfig.ResearchFactor).
/// </summary>
public class ResearchPostSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<ClickInputEvent> _clicks;
    private EcsFilter<ConnectivityChangedEvent> _connectivity;
    private EcsFilter<SiteFlag, GridAttribute, BuildToolAttribute, PointerAttribute, ViewFloorAttribute> _sites;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;
    private EcsFilter<ScpFlag, ContainedAttribute> _scps;

    private readonly Dictionary<int, int[][]> _distances = new Dictionary<int, int[][]>();
    private readonly SortedSet<long> _open = new SortedSet<long>();
    private readonly List<Vector2Int> _cells = new List<Vector2Int>();
    private readonly List<Vector2Int> _zone = new List<Vector2Int>();
    private readonly List<Vector3> _noDoors = new List<Vector3>();
    private bool _dirty = true;

    private BuildingsConfig Config => GameConfig.BuildingsConfig;

    public void Run()
    {
        if (!_connectivity.IsEmpty()) _dirty = true;

        foreach (var s in _sites)
        {
            ref var grid = ref _sites.Get2(s);
            ref var tool = ref _sites.Get3(s);
            var pointer = _sites.Get4(s);
            int depth = _sites.Get5(s).Depth;
            bool clicked = !_clicks.IsEmpty();

            if (tool.Tool == BuildTool.None && clicked && pointer.HasCell && !pointer.OverUI && depth < grid.Floors.Length)
            {
                int id = grid.Floors[depth].BuildingAt(pointer.Cell);
                if (id != FloorGrid.Empty && IsPost(id))
                {
                    tool.Tool = BuildTool.Bind;
                    tool.BindPost = id;
                    clicked = false;   // этот клик уже выбрал пост
                }
            }

            Validate();
            if (_dirty)
            {
                _dirty = false;
                ComputeDistances(ref grid);
            }

            AutoBind();
            if (_dirty)
            {
                _dirty = false;
                ComputeDistances(ref grid);
            }

            if (tool.Tool == BuildTool.Bind) BindTool(ref grid, ref tool, pointer, depth, clicked);
        }
    }

    // ─────────────────────── инструмент: привязка ───────────────────────

    private void BindTool(ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked)
    {
        if (!TryFind(tool.BindPost, out int postIndex))
        {
            tool.Tool = BuildTool.None;
            return;
        }

        var post = _buildings.Get2(postIndex);
        var postDef = Config.Def(post.Def);
        int used = Bound(post.Id);

        ShowBinding(post, depth);
        string head = $"{postDef.FullName}: учёных {used}/{postDef.Capacity}. Клик по камере — привязать или отвязать, Esc — готово.";

        if (!pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length)
        {
            Status(ref tool, head, false);
            return;
        }

        int id = grid.Floors[depth].BuildingAt(pointer.Cell);
        if (id == FloorGrid.Empty || !TryFind(id, out int index) || id == post.Id)
        {
            Status(ref tool, head, false);
            if (clicked && id != post.Id) tool.Tool = BuildTool.None;   // клик мимо — выйти из привязки
            return;
        }

        if (IsPost(id))
        {
            Status(ref tool, "Клик — выбрать этот пост.", false);
            if (clicked) tool.BindPost = id;
            return;
        }

        ref var cell = ref _buildings.Get2(index);
        var def = Config.Def(cell.Def);
        if (def == null || def.Role != BuildingRole.Containment)
        {
            Status(ref tool, $"«{def?.FullName}» — не камера содержания. {head}", true);
            if (clicked) tool.Tool = BuildTool.None;
            return;
        }

        bool mine = cell.PostId == post.Id;
        bool full = !mine && used >= postDef.Capacity;
        Status(ref tool, mine ? $"Отвязать «{def.FullName}» от поста." : full
            ? $"Мест нет: учёных {used}/{postDef.Capacity}. Отвяжи другую камеру или поставь пост больше."
            : $"Привязать «{def.FullName}» к посту" + (cell.PostId != BuildingAttribute.NoPost ? " (сейчас — к другому)." : "."), full);

        if (!clicked || full) return;

        cell.PostId = mine ? BuildingAttribute.NoPost : post.Id;
        cell.PostDistance = -1;
        _dirty = true;
        Log(mine ? $"«{def.FullName}» отвязана от поста." : $"«{def.FullName}» привязана к посту «{postDef.FullName}».");
    }

    /// <summary>Подсветка: клетки поста и привязанных к нему камер на текущем этаже.</summary>
    private void ShowBinding(BuildingAttribute post, int depth)
    {
        var ghost = SceneData.Ghost;
        if (ghost == null) return;

        _zone.Clear();
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            if (building.Depth != depth || (building.Id != post.Id && building.PostId != post.Id)) continue;
            var def = Config.Def(building.Def);
            def.Cells(building.Origin, building.Rotation, _cells);
            _zone.AddRange(_cells);
        }

        ghost.Show(_zone, Config, depth, Config.GhostZone);
        ghost.ShowDoors(_noDoors, Config.GhostZone);
        ghost.HideZone();
    }

    // ─────────────────────── привязки ───────────────────────

    /// <summary>Снесённый пост — его камеры снова ничьи.</summary>
    private void Validate()
    {
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            if (building.PostId == BuildingAttribute.NoPost || IsPost(building.PostId)) continue;

            building.PostId = BuildingAttribute.NoPost;
            building.PostDistance = -1;
            _dirty = true;
        }
    }

    /// <summary>Камера с объектом без поста — к ближайшему посту со свободным местом.</summary>
    private void AutoBind()
    {
        foreach (var i in _buildings)
        {
            ref var cell = ref _buildings.Get2(i);
            if (cell.PostId != BuildingAttribute.NoPost || !Occupied(cell.Id)) continue;

            var def = Config.Def(cell.Def);
            if (def == null || def.Role != BuildingRole.Containment) continue;

            int bestPost = BuildingAttribute.NoPost, bestDistance = int.MaxValue;
            foreach (var p in _buildings)
            {
                ref var post = ref _buildings.Get2(p);
                var postDef = Config.Def(post.Def);
                if (postDef == null || postDef.Role != BuildingRole.ResearchPost || Bound(post.Id) >= postDef.Capacity) continue;

                int distance = Distance(post, cell);
                if (distance < 0) distance = int.MaxValue - 1;   // недостижимый пост — только если других нет
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                bestPost = post.Id;
            }

            if (bestPost == BuildingAttribute.NoPost) continue;
            cell.PostId = bestPost;
            _dirty = true;
        }
    }

    // ─────────────────────── расстояния ───────────────────────

    private void ComputeDistances(ref GridAttribute grid)
    {
        _distances.Clear();

        foreach (var i in _buildings)
        {
            ref var post = ref _buildings.Get2(i);
            var def = Config.Def(post.Def);
            if (def == null || def.Role != BuildingRole.ResearchPost) continue;
            if (!_buildings.GetEntity(i).Has<ConnectedFlag>()) continue;
            _distances[post.Id] = Dijkstra(ref grid, post.Depth, def.DoorOutside(post.Origin, post.Rotation));
        }

        foreach (var i in _buildings)
        {
            ref var cell = ref _buildings.Get2(i);
            if (cell.PostId == BuildingAttribute.NoPost) continue;
            cell.PostDistance = TryFind(cell.PostId, out int postIndex) ? Distance(_buildings.Get2(postIndex), cell) : -1;
        }
    }

    /// <summary>Путь от поста до камеры по коридорам; −1 — не дойти.</summary>
    private int Distance(BuildingAttribute post, BuildingAttribute cell)
    {
        if (!_distances.TryGetValue(post.Id, out var map)) return -1;

        var def = Config.Def(cell.Def);
        if (def == null || cell.Depth >= map.Length) return -1;

        var target = def.HasControlRoom ? def.ControlOutside(cell.Origin, cell.Rotation) : def.DoorOutside(cell.Origin, cell.Rotation);
        int size = Config.GridSize;
        if (target.x < 0 || target.y < 0 || target.x >= size || target.y >= size) return -1;

        int value = map[cell.Depth][target.y * size + target.x];
        return value == int.MaxValue ? -1 : value;
    }

    /// <summary>Дейкстра по связанным клеткам коридоров всех этажей; лифт — ребро между этажами.</summary>
    private int[][] Dijkstra(ref GridAttribute grid, int depth, Vector2Int start)
    {
        int floors = grid.Floors.Length;
        int size = grid.Floors[0].Size;
        var map = new int[floors][];
        for (int f = 0; f < floors; f++)
        {
            map[f] = new int[size * size];
            for (int k = 0; k < map[f].Length; k++) map[f][k] = int.MaxValue;
        }

        var startFloor = grid.Floors[depth];
        if (!startFloor.IsReached(start)) return map;

        var links = ElevatorLinks(ref grid);
        int travel = GameConfig.BalanceConfig.ElevatorTravelCost;

        _open.Clear();
        map[depth][startFloor.Index(start)] = 0;
        _open.Add(Pack(0, depth, startFloor.Index(start)));

        while (_open.Count > 0)
        {
            long top = _open.Min;
            _open.Remove(top);
            int distance = (int)(top >> 32);
            int node = (int)(top & 0xffffffff);
            int f0 = node / (size * size), index = node % (size * size);
            if (distance > map[f0][index]) continue;

            var floor = grid.Floors[f0];
            int x = index % size, z = index / size;
            Relax(floor, map, f0, x + 1, z, distance + 1);
            Relax(floor, map, f0, x - 1, z, distance + 1);
            Relax(floor, map, f0, x, z + 1, distance + 1);
            Relax(floor, map, f0, x, z - 1, distance + 1);

            foreach (var link in links)
            {
                if (link.x != index) continue;
                if (link.y == f0 && link.y + 1 < floors) Relax(grid.Floors[f0 + 1], map, f0 + 1, x, z, distance + travel);
                if (link.y + 1 == f0) Relax(grid.Floors[f0 - 1], map, f0 - 1, x, z, distance + travel);
            }
        }

        return map;
    }

    private void Relax(FloorGrid floor, int[][] map, int depth, int x, int z, int distance)
    {
        if (x < 0 || z < 0 || x >= floor.Size || z >= floor.Size) return;
        int index = z * floor.Size + x;
        if (!floor.Reached[index] || map[depth][index] <= distance) return;

        map[depth][index] = distance;
        _open.Add(Pack(distance, depth, index));
    }

    private long Pack(int distance, int depth, int index) => ((long)distance << 32) | (uint)(depth * Config.GridSize * Config.GridSize + index);

    /// <summary>Лифты: x — индекс клетки перед входом, y — верхний этаж.</summary>
    private List<Vector2Int> ElevatorLinks(ref GridAttribute grid)
    {
        var links = new List<Vector2Int>();
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            var def = Config.Def(building.Def);
            if (def == null || !BuildingsConfig.IsElevator(def.Role) || !_buildings.GetEntity(i).Has<ConnectedFlag>()) continue;
            var door = def.DoorOutside(building.Origin, building.Rotation);
            if (!grid.Floors[building.Depth].InBounds(door)) continue;
            links.Add(new Vector2Int(grid.Floors[building.Depth].Index(door), building.Depth));
        }
        return links;
    }

    // ─────────────────────── служебное ───────────────────────

    private bool IsPost(int id)
    {
        if (!TryFind(id, out int index)) return false;
        var def = Config.Def(_buildings.Get2(index).Def);
        return def != null && def.Role == BuildingRole.ResearchPost;
    }

    private bool Occupied(int roomId)
    {
        foreach (var i in _scps)
            if (_scps.Get2(i).RoomId == roomId) return true;
        return false;
    }

    private int Bound(int postId)
    {
        int count = 0;
        foreach (var i in _buildings)
            if (_buildings.Get2(i).PostId == postId) count++;
        return count;
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

    private static void Status(ref BuildToolAttribute tool, string text, bool error)
    {
        tool.Status = text;
        tool.StatusIsError = error;
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
