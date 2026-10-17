using System;
using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Коридоры — артерии Зоны. Хранятся отрезками (FloorGrid.Segments), как дороги в градостроителе,
/// а клетки FloorGrid.Corridor — их растр для связности и комнат.
///
/// Прокладка: тянешь мышью буквой «Г» — первая часть идёт туда, куда сначала повёл мышь.
/// Конец рядом с другим коридором прилипает к его оси — так получаются перекрёстки.
/// Параллельные коридоры не ближе ParallelGap клеток: иначе они слипаются в один зал.
/// Коридор по той же оси — продолжение или улучшение: шире перекрывает уже, плата только за новые клетки.
/// Вдоль стены коридор сам сдвигает ширину от постройки, если ось остаётся внутри.
///
/// Снос: клик по коридору убирает пролёт между перекрёстками, рамка режет всё, что в неё попало.
/// Под модулем (КПП, Тесла…) коридор не убрать, пока стоит модуль.
/// </summary>
public class CorridorSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<PlaceCorridorEvent> _places;
    private EcsFilter<ClickInputEvent> _clicks;
    private EcsFilter<ReleaseInputEvent> _releases;
    private EcsFilter<SiteFlag, GridAttribute, BuildToolAttribute, PointerAttribute, ViewFloorAttribute> _sites;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    private readonly List<CorridorSegment> _legs = new List<CorridorSegment>();
    private readonly List<CorridorSegment> _sim = new List<CorridorSegment>();
    private readonly List<CorridorSegment> _group = new List<CorridorSegment>();
    private readonly List<Vector2Int> _cells = new List<Vector2Int>();
    private readonly List<Vector2Int> _doors = new List<Vector2Int>();
    private readonly List<Vector3> _noDoors = new List<Vector3>();
    private readonly List<Vector2Int> _junctions = new List<Vector2Int>();   // x — начало, y — конец вдоль отрезка
    private readonly HashSet<long> _seen = new HashSet<long>();
    private readonly HashSet<long> _doorCells = new HashSet<long>();
    private byte[] _scratch = new byte[0];
    private int _fresh;
    private int _refund;
    private string _reason;

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

            foreach (var p in _places)
            {
                ref var place = ref _places.Get1(p);
                if (place.Depth < 0 || place.Depth >= grid.Floors.Length) continue;

                var floor = grid.Floors[place.Depth];
                if (Plan(site, floor, place.From, place.To, place.Corridor, false, place.Free, out int cost))
                    Apply(ref grid, floor, place.Corridor, cost, place.Free);
                else if (place.Free) Debug.LogWarning("Стартовый коридор не проложен: " + _reason);
            }

            bool clicked = !_clicks.IsEmpty();
            bool released = !_releases.IsEmpty();

            if (tool.Tool == BuildTool.Corridor) CorridorTool(site, ref grid, ref tool, pointer, depth, clicked, released);
            else if (tool.Tool == BuildTool.Demolish) DemolishTool(ref grid, ref tool, pointer, depth, clicked, released);
        }
    }

    // ─────────────────────── инструмент: прокладка ───────────────────────

    private void CorridorTool(EcsEntity site, ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked, bool released)
    {
        var def = Config.Corridor(tool.Corridor);
        if (def == null)
        {
            tool.Tool = BuildTool.None;
            return;
        }

        string hint = $"{def.Name} ({def.Width} м) — {def.CostPerCell:N0} за м². {def.Description} Зажми ЛКМ и тяни; у другого коридора конец прилипнет.";

        if (!pointer.HasCell || pointer.OverUI || depth >= grid.Floors.Length)
        {
            if (released) tool.Dragging = false;
            if (!tool.Dragging) HideGhost();
            Status(ref tool, hint, false);
            return;
        }

        var floor = grid.Floors[depth];

        if (!tool.Dragging)
        {
            CollectDoors(depth);
            Square(Snap(floor, pointer.Cell, out _), def.Width);
            ShowGhost(depth, Config.GhostValid);
            Status(ref tool, hint, false);

            if (!clicked) return;
            tool.Dragging = true;
            tool.DragStart = pointer.Cell;
            tool.DragAxisChosen = false;
            if (!released) return;   // щелчок без протяжки в одном кадре — не оставляем протяжку висеть
        }

        if (!tool.DragAxisChosen && pointer.Cell != tool.DragStart)
        {
            tool.DragAxisChosen = true;
            tool.DragAlongZFirst = Mathf.Abs(pointer.Cell.y - tool.DragStart.y) > Mathf.Abs(pointer.Cell.x - tool.DragStart.x);
        }

        bool ok = Plan(site, floor, tool.DragStart, pointer.Cell, tool.Corridor, tool.DragAlongZFirst, false, out int cost);

        if (_legs.Count == 0) Square(Snap(floor, tool.DragStart, out _), def.Width);
        else LegCells(floor);
        ShowGhost(depth, !ok ? Config.GhostInvalid : _fresh > 0 ? Config.GhostValid : Config.GhostUnconnected);

        if (!ok) Status(ref tool, $"{def.Name}: {_reason}", _legs.Count > 0);
        else if (_fresh == 0) Status(ref tool, $"{def.Name}: здесь уже есть коридор не хуже.", false);
        else Status(ref tool, $"{def.Name}: {_fresh} м² новых, {cost:N0}. Отпусти, чтобы проложить.", false);

        if (!released) return;

        tool.Dragging = false;
        if (ok && _fresh > 0) Apply(ref grid, floor, tool.Corridor, cost, false);
    }

    // ─────────────────────── план ───────────────────────

    /// <summary>Собирает отрезки будущего коридора в _legs и проверяет их. _fresh — сколько клеток станет лучше.</summary>
    private bool Plan(EcsEntity site, FloorGrid floor, Vector2Int from, Vector2Int to, int type, bool zFirst, bool free, out int cost)
    {
        cost = 0;
        _fresh = 0;
        _reason = null;
        _legs.Clear();

        var def = Config.Corridor(type);
        if (def == null) { _reason = "нет такого коридора"; return false; }
        if (floor.Depth <= 0) { _reason = "на поверхности строить пока нельзя"; return false; }
        if (!free && !floor.Open) { _reason = $"этаж закрыт — нужны пассажирский и грузовой лифты на −{floor.Depth - 1}"; return false; }
        if (!free && site.Get<DirectorAttribute>().Level < def.RequiredDirectorLevel) { _reason = $"нужен уровень директора {def.RequiredDirectorLevel}"; return false; }

        CollectDoors(floor.Depth);
        var a = Snap(floor, from, out var snapA);
        var b = Snap(floor, to, out var snapB);
        if (a == b) { _reason = "протяни мышью дальше"; return false; }

        int width = def.Width;
        bool alongX = a.x != b.x, alongZ = a.y != b.y;

        // ось у прилипшего конца не двигаем, иначе собьётся перекрёсток
        bool lockFirst = snapA != null, lockSecond = snapB != null;
        int bestS1 = 0, bestS2 = 0;
        bool found = false;

        int low = CorridorSegment.Low(width), high = CorridorSegment.High(width);
        foreach (int s1 in Shifts(low, high, lockFirst))
        {
            foreach (int s2 in Shifts(low, high, lockSecond || !(alongX && alongZ)))
            {
                BuildLegs(a, b, alongX, alongZ, zFirst, width, s1, s2, type);
                if (Blocked(floor, width) != null) continue;
                bestS1 = s1;
                bestS2 = s2;
                found = true;
                break;
            }
            if (found) break;
        }

        BuildLegs(a, b, alongX, alongZ, zFirst, width, bestS1, bestS2, type);

        string blocked = Blocked(floor, width);
        if (blocked != null) { _reason = blocked; return false; }

        foreach (var leg in _legs)
        {
            if (!Parallel(floor, leg, width)) continue;
            _reason = $"слишком близко к соседнему коридору — оставь зазор {Config.ParallelGap} м или подведи коридор к нему";
            return false;
        }

        byte tier = (byte)(type + 1);
        LegCells(floor);
        foreach (var cell in _cells)
            if (floor.Corridor[floor.Index(cell)] < tier) _fresh++;

        cost = free ? 0 : _fresh * def.CostPerCell;
        if (!free && _fresh > 0 && site.Get<TreasuryAttribute>().Money < cost) { _reason = $"не хватает денег ({cost:N0})"; return false; }
        return true;
    }

    /// <summary>Сдвиги оси поперёк хода: 0, +1, −1, +2… так, чтобы исходная ось осталась внутри ширины.</summary>
    private static IEnumerable<int> Shifts(int low, int high, bool locked)
    {
        yield return 0;
        if (locked) yield break;
        for (int k = 1; k <= high - low; k++)
        {
            if (k <= -low) yield return k;
            if (k <= high) yield return -k;
        }
    }

    private void BuildLegs(Vector2Int a, Vector2Int b, bool alongX, bool alongZ, bool zFirst, int width, int s1, int s2, int type)
    {
        _legs.Clear();
        int low = CorridorSegment.Low(width), high = CorridorSegment.High(width);

        if (!(alongX && alongZ))
        {
            if (alongX) _legs.Add(Leg(true, a.y + s1, a.x, b.x, type));
            else _legs.Add(Leg(false, a.x + s1, a.y, b.y, type));
            return;
        }

        // «Г»: первый ход доходит до угла и заходит за ось второго на полширины, второй начинается с оси первого
        CorridorSegment first, second;
        if (!zFirst)
        {
            int c1 = a.y + s1, c2 = b.x + s2;
            first = Leg(true, c1, a.x, c2, type);
            second = Leg(false, c2, c1, b.y, type);
            Extend(first, c2 > a.x, c2, low, high);
            Begin(second, b.y > c1, c1, low, high);
        }
        else
        {
            int c1 = a.x + s1, c2 = b.y + s2;
            first = Leg(false, c1, a.y, c2, type);
            second = Leg(true, c2, c1, b.x, type);
            Extend(first, c2 > a.y, c2, low, high);
            Begin(second, b.x > c1, c1, low, high);
        }

        _legs.Add(first);
        _legs.Add(second);
    }

    private static CorridorSegment Leg(bool alongX, int center, int start, int end, int type) =>
        new CorridorSegment { AlongX = alongX, Center = center, From = Mathf.Min(start, end), To = Mathf.Max(start, end), Type = type };

    /// <summary>Конец хода у угла заходит за ось следующего хода, чтобы угол был залит целиком.</summary>
    private static void Extend(CorridorSegment leg, bool forward, int corner, int low, int high)
    {
        if (forward) leg.To = Mathf.Max(leg.To, corner + high);
        else leg.From = Mathf.Min(leg.From, corner + low);
    }

    private static void Begin(CorridorSegment leg, bool forward, int corner, int low, int high)
    {
        if (forward) leg.From = Mathf.Min(leg.From, corner + low);
        else leg.To = Mathf.Max(leg.To, corner + high);
    }

    /// <summary>Что мешает ходам: граница этажа или постройка (модуль на коридоре не мешает).</summary>
    private string Blocked(FloorGrid floor, int width)
    {
        int low = CorridorSegment.Low(width), high = CorridorSegment.High(width);
        foreach (var leg in _legs)
        {
            for (int along = leg.From; along <= leg.To; along++)
            {
                for (int across = leg.Center + low; across <= leg.Center + high; across++)
                {
                    var cell = leg.Cell(along, across);
                    if (!floor.InBounds(cell)) return "выходит за границу этажа";
                    int index = floor.Index(cell);
                    if (floor.Building[index] != FloorGrid.Empty && floor.Corridor[index] == 0) return "упирается в постройку";
                }
            }
        }
        return null;
    }

    /// <summary>Идёт ли ход параллельно соседнему коридору ближе ParallelGap. По той же оси — можно.</summary>
    private bool Parallel(FloorGrid floor, CorridorSegment leg, int width)
    {
        int gap = Config.ParallelGap;
        int lLo = leg.Center + CorridorSegment.Low(width), lHi = leg.Center + CorridorSegment.High(width);

        foreach (var other in floor.Segments)
        {
            if (other.AlongX != leg.AlongX || other.Center == leg.Center) continue;

            int w = Width(other);
            int sLo = other.Center + CorridorSegment.Low(w), sHi = other.Center + CorridorSegment.High(w);
            int lateral = Mathf.Max(lLo - sHi, sLo - lHi) - 1;          // свободных клеток поперёк
            int lengthwise = Mathf.Max(leg.From - other.To, other.From - leg.To) - 1;   // и вдоль
            if (lateral < gap && lengthwise < gap) return true;
        }
        return false;
    }

    /// <summary>Конец рядом с коридором — на его ось. Клетки перед входами в комнаты не прилипают.</summary>
    private Vector2Int Snap(FloorGrid floor, Vector2Int point, out CorridorSegment snapped)
    {
        snapped = null;
        if (_doorCells.Contains(Key(point))) return point;

        int best = int.MaxValue;
        foreach (var segment in floor.Segments)
        {
            int w = Width(segment);
            int along = segment.AlongX ? point.x : point.y;
            int across = segment.AlongX ? point.y : point.x;
            int lo = segment.Center + CorridorSegment.Low(w), hi = segment.Center + CorridorSegment.High(w);

            int da = along < segment.From ? segment.From - along : along > segment.To ? along - segment.To : 0;
            int dc = across < lo ? lo - across : across > hi ? across - hi : 0;
            int distance = Mathf.Max(da, dc);
            if (distance > Config.SnapDistance || distance >= best) continue;

            best = distance;
            snapped = segment;
        }

        if (snapped == null) return point;

        int a = Mathf.Clamp(snapped.AlongX ? point.x : point.y, snapped.From, snapped.To);
        return snapped.Cell(a, snapped.Center);
    }

    // ─────────────────────── применение ───────────────────────

    private void Apply(ref GridAttribute grid, FloorGrid floor, int type, int cost, bool free)
    {
        foreach (var leg in _legs)
        {
            leg.Type = type;
            Merge(ref grid, floor.Segments, leg);
        }

        Rasterize(floor.Segments, floor.Corridor, floor.Size);

        if (!free && cost > 0)
        {
            var def = Config.Corridor(type);
            Money(-cost, "Стройка: " + def.Name);
            Log($"Проложен {def.Name.ToLowerInvariant()}: {_fresh} м² ({Config.FloorName(floor.Depth)}).");
        }

        _world.NewEntity().Get<GridChangedEvent>();
    }

    /// <summary>Ход сливается с отрезками на той же оси, которых касается: по каждой клетке берётся лучший тип.</summary>
    private void Merge(ref GridAttribute grid, List<CorridorSegment> segments, CorridorSegment leg)
    {
        _group.Clear();
        _group.Add(leg);
        int from = leg.From, to = leg.To;

        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = segments.Count - 1; i >= 0; i--)
            {
                var other = segments[i];
                if (other.AlongX != leg.AlongX || other.Center != leg.Center) continue;
                if (Mathf.Max(from - other.To, other.From - to) > 1) continue;   // не касаются

                _group.Add(other);
                segments.RemoveAt(i);
                from = Mathf.Min(from, other.From);
                to = Mathf.Max(to, other.To);
                grew = true;
            }
        }

        int runStart = from, runType = TypeAt(from);
        for (int position = from + 1; position <= to + 1; position++)
        {
            int type = position <= to ? TypeAt(position) : int.MinValue;
            if (type == runType) continue;

            if (runType >= 0)
                segments.Add(new CorridorSegment { Id = grid.NextCorridorId++, AlongX = leg.AlongX, Center = leg.Center,
                    From = runStart, To = position - 1, Type = runType });

            runStart = position;
            runType = type;
        }
    }

    private int TypeAt(int position)
    {
        int best = -1;
        foreach (var segment in _group)
            if (position >= segment.From && position <= segment.To && segment.Type > best) best = segment.Type;
        return best;
    }

    private void Rasterize(List<CorridorSegment> segments, byte[] into, int size)
    {
        Array.Clear(into, 0, into.Length);
        foreach (var segment in segments)
        {
            int w = Width(segment);
            byte tier = (byte)(segment.Type + 1);
            for (int along = segment.From; along <= segment.To; along++)
            {
                for (int across = segment.Center + CorridorSegment.Low(w); across <= segment.Center + CorridorSegment.High(w); across++)
                {
                    var cell = segment.Cell(along, across);
                    if (cell.x < 0 || cell.y < 0 || cell.x >= size || cell.y >= size) continue;
                    int index = cell.y * size + cell.x;
                    if (into[index] < tier) into[index] = tier;
                }
            }
        }
    }

    // ─────────────────────── инструмент: снос коридора ───────────────────────

    private void DemolishTool(ref GridAttribute grid, ref BuildToolAttribute tool, PointerAttribute pointer, int depth, bool clicked, bool released)
    {
        if (!pointer.HasCell || pointer.OverUI || depth <= 0 || depth >= grid.Floors.Length)
        {
            if (released) tool.Dragging = false;
            return;
        }

        var floor = grid.Floors[depth];
        var cell = pointer.Cell;

        if (!tool.Dragging)
        {
            if (floor.BuildingAt(cell) != FloorGrid.Empty || !floor.IsCorridor(cell)) return;   // постройки сносит BuildSystem

            bool ok = SimulateSpan(floor, cell, out string text);
            ShowGhost(depth, Config.GhostInvalid);
            Status(ref tool, text, !ok);

            if (!clicked) return;
            tool.Dragging = true;
            tool.DragStart = cell;
            if (!released) return;   // быстрый щелчок: нажали и отпустили в одном кадре — сносим пролёт сразу
        }

        bool span = cell == tool.DragStart;
        bool allowed = span ? SimulateSpan(floor, cell, out string status) : SimulateBox(floor, tool.DragStart, cell, out status);
        ShowGhost(depth, Config.GhostInvalid);
        Status(ref tool, status, !allowed);

        if (!released) return;

        tool.Dragging = false;
        if (!allowed || _cells.Count == 0) return;

        floor.Segments.Clear();
        foreach (var segment in _sim) floor.Segments.Add(segment);
        Rasterize(floor.Segments, floor.Corridor, floor.Size);

        if (_refund > 0) Money(_refund, "Снос коридора");
        Log($"Убран коридор: {_cells.Count} м² ({Config.FloorName(floor.Depth)}).");
        _world.NewEntity().Get<GridChangedEvent>();
    }

    /// <summary>Клик: пролёт отрезка под курсором между соседними перекрёстками.</summary>
    private bool SimulateSpan(FloorGrid floor, Vector2Int cell, out string text)
    {
        Copy(floor.Segments, _sim);
        _cells.Clear();
        _refund = 0;

        var segment = SegmentAt(_sim, cell);
        if (segment == null)
        {
            text = "Снос: клик по постройке или по коридору, рамка — участок коридоров.";
            return false;
        }

        int along = segment.AlongX ? cell.x : cell.y;
        Junctions(_sim, segment);
        foreach (var j in _junctions)
        {
            if (along < j.x || along > j.y) continue;
            text = "Это перекрёсток — кликни по пролёту рядом или протяни рамку.";
            return false;
        }

        int lo = segment.From, hi = segment.To;
        foreach (var j in _junctions)
        {
            if (j.y < along) lo = Mathf.Max(lo, j.y + 1);
            if (j.x > along) hi = Mathf.Min(hi, j.x - 1);
        }

        Cut(_sim, segment, lo, hi);
        return Diff(floor, "Убрать пролёт: {0} м², вернётся {1}. Рамкой — участок.", out text);
    }

    /// <summary>Рамка: каждый отрезок, попавший в неё, режется по её границам.</summary>
    private bool SimulateBox(FloorGrid floor, Vector2Int a, Vector2Int b, out string text)
    {
        Copy(floor.Segments, _sim);
        int x0 = Mathf.Min(a.x, b.x), x1 = Mathf.Max(a.x, b.x);
        int z0 = Mathf.Min(a.y, b.y), z1 = Mathf.Max(a.y, b.y);

        _group.Clear();
        foreach (var segment in _sim)
        {
            int w = Width(segment);
            int lo = segment.Center + CorridorSegment.Low(w), hi = segment.Center + CorridorSegment.High(w);
            int a0 = segment.AlongX ? x0 : z0, a1 = segment.AlongX ? x1 : z1;
            int c0 = segment.AlongX ? z0 : x0, c1 = segment.AlongX ? z1 : x1;
            if (segment.To < a0 || segment.From > a1 || hi < c0 || lo > c1) continue;
            _group.Add(segment);
        }

        foreach (var segment in _group)
        {
            int a0 = segment.AlongX ? x0 : z0, a1 = segment.AlongX ? x1 : z1;
            Cut(_sim, segment, Mathf.Max(segment.From, a0), Mathf.Min(segment.To, a1));
        }

        return Diff(floor, "Убрать коридоры в рамке: {0} м², вернётся {1}.", out text);
    }

    /// <summary>Вырезает [lo, hi] из отрезка; обрубки, целиком лежащие в перекрёстках, тоже убираются.</summary>
    private void Cut(List<CorridorSegment> segments, CorridorSegment segment, int lo, int hi)
    {
        segments.Remove(segment);
        if (lo > segment.From) Keep(segments, segment, segment.From, lo - 1);
        if (hi < segment.To) Keep(segments, segment, hi + 1, segment.To);
    }

    private void Keep(List<CorridorSegment> segments, CorridorSegment source, int from, int to)
    {
        var piece = source.Copy();
        piece.From = from;
        piece.To = to;

        Junctions(segments, piece);
        bool covered = true;
        for (int along = from; along <= to && covered; along++)
        {
            bool inside = false;
            foreach (var j in _junctions)
                if (along >= j.x && along <= j.y) { inside = true; break; }
            covered = inside;
        }

        if (!covered) segments.Add(piece);
    }

    /// <summary>Где поперечные коридоры пересекают отрезок или упираются в него: диапазоны вдоль отрезка.</summary>
    private void Junctions(List<CorridorSegment> segments, CorridorSegment segment)
    {
        _junctions.Clear();
        int w = Width(segment);
        int lo = segment.Center + CorridorSegment.Low(w), hi = segment.Center + CorridorSegment.High(w);

        foreach (var other in segments)
        {
            if (other == segment || other.AlongX == segment.AlongX) continue;
            if (other.To < lo - 1 || other.From > hi + 1) continue;   // не доходит до отрезка

            int ow = Width(other);
            int jLo = other.Center + CorridorSegment.Low(ow), jHi = other.Center + CorridorSegment.High(ow);
            if (jHi < segment.From || jLo > segment.To) continue;
            _junctions.Add(new Vector2Int(jLo, jHi));
        }
    }

    /// <summary>Сравнивает растр симуляции с этажом: что уйдёт, сколько вернётся и не стоит ли там модуль.</summary>
    private bool Diff(FloorGrid floor, string format, out string text)
    {
        if (_scratch.Length != floor.Corridor.Length) _scratch = new byte[floor.Corridor.Length];
        Rasterize(_sim, _scratch, floor.Size);

        _cells.Clear();
        _refund = 0;
        string module = null;

        for (int i = 0; i < _scratch.Length; i++)
        {
            byte before = floor.Corridor[i], after = _scratch[i];
            if (before <= after) continue;

            var cell = new Vector2Int(i % floor.Size, i / floor.Size);
            _cells.Add(cell);

            var was = Config.CorridorOfTier(before);
            var now = Config.CorridorOfTier(after);
            _refund += (was != null ? was.CostPerCell : 0) - (now != null ? now.CostPerCell : 0);

            if (after == 0 && floor.Building[i] != FloorGrid.Empty && module == null) module = ModuleName(floor.Building[i]);
        }

        _refund = _refund * Config.RefundPercent / 100;

        if (module != null)
        {
            text = $"Под коридором стоит «{module}» — сначала снеси его.";
            return false;
        }

        text = string.Format(format, _cells.Count, _refund.ToString("N0"));
        return _cells.Count > 0;
    }

    private CorridorSegment SegmentAt(List<CorridorSegment> segments, Vector2Int cell)
    {
        CorridorSegment best = null;
        int bestOffset = int.MaxValue;

        foreach (var segment in segments)
        {
            if (!segment.Contains(cell, Width(segment))) continue;
            int offset = Mathf.Abs((segment.AlongX ? cell.y : cell.x) - segment.Center);
            if (offset > bestOffset || (offset == bestOffset && best != null && segment.Length <= best.Length)) continue;
            best = segment;
            bestOffset = offset;
        }

        return best;
    }

    // ─────────────────────── служебное ───────────────────────

    private int Width(CorridorSegment segment)
    {
        var def = Config.Corridor(segment.Type);
        return def != null ? def.Width : 1;
    }

    private static void Copy(List<CorridorSegment> from, List<CorridorSegment> into)
    {
        into.Clear();
        foreach (var segment in from) into.Add(segment.Copy());
    }

    /// <summary>Клетки перед входами построек этажа: туда тянут коридор, к соседнему не прилипаем.</summary>
    private void CollectDoors(int depth)
    {
        _doorCells.Clear();
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            if (building.Depth != depth) continue;

            var def = Config.Def(building.Def);
            if (def == null) continue;
            def.DoorsOutside(building.Origin, building.Rotation, _doors);
            foreach (var door in _doors) _doorCells.Add(Key(door));
        }
    }

    private string ModuleName(int buildingId)
    {
        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            if (building.Id != buildingId) continue;
            var def = Config.Def(building.Def);
            return def != null ? def.Name : "модуль";
        }
        return "модуль";
    }

    private void Square(Vector2Int center, int width)
    {
        _cells.Clear();
        int low = CorridorSegment.Low(width), high = CorridorSegment.High(width);
        for (int dx = low; dx <= high; dx++)
            for (int dz = low; dz <= high; dz++)
                _cells.Add(new Vector2Int(center.x + dx, center.y + dz));
    }

    private void LegCells(FloorGrid floor)
    {
        _cells.Clear();
        _seen.Clear();
        foreach (var leg in _legs)
        {
            int w = Width(leg);
            for (int along = leg.From; along <= leg.To; along++)
            {
                for (int across = leg.Center + CorridorSegment.Low(w); across <= leg.Center + CorridorSegment.High(w); across++)
                {
                    var cell = leg.Cell(along, across);
                    if (!floor.InBounds(cell) || !_seen.Add(Key(cell))) continue;
                    _cells.Add(cell);
                }
            }
        }
    }

    private static long Key(Vector2Int cell) => ((long)cell.x << 32) ^ (uint)cell.y;

    private void ShowGhost(int depth, Color color)
    {
        var ghost = SceneData.Ghost;
        if (ghost == null) return;

        ghost.Show(_cells, Config, depth, color);
        ghost.ShowDoors(_noDoors, color);
        ghost.HideZone();
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
