using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Подземная вода. При старте на каждом этаже раскладываются пятна грунтовых вод и подземные реки
/// (у главного въезда всегда сухо). Насосная откачивает воду в своём радиусе: малая — грунтовые воды,
/// большая — ещё и реки. Работает только связанная с коридорами насосная.
///
/// Каждый день постройка, стоящая в неоткачанной воде, теряет прочность; на нуле её размывает
/// (CollapseFlag — сносит BuildSystem). На сухом месте прочность понемногу возвращается.
/// Снесли насосную — вода вернулась, и постройки в её зоне снова начинают разрушаться.
/// </summary>
public class WaterSystem : Injects, IEcsInitSystem, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<GridChangedEvent> _gridChanges;
    private EcsFilter<ConnectivityChangedEvent> _connectivity;
    private EcsFilter<DayStartedEvent> _days;
    private EcsFilter<SiteFlag, GridAttribute> _sites;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    private readonly List<Vector2Int> _cells = new List<Vector2Int>();

    private BuildingsConfig Config => GameConfig.BuildingsConfig;

    public void Init()
    {
        foreach (var s in _sites)
        {
            ref var grid = ref _sites.Get2(s);
            for (int depth = 1; depth < grid.Floors.Length; depth++) Generate(grid.Floors[depth]);
            Recompute(ref grid);
        }
    }

    public void Run()
    {
        foreach (var s in _sites)
        {
            ref var grid = ref _sites.Get2(s);
            if (!_gridChanges.IsEmpty() || !_connectivity.IsEmpty()) Recompute(ref grid);
            foreach (var _ in _days) Wear(ref grid);
        }
    }

    // ─────────────────────── карта воды ───────────────────────

    private void Generate(FloorGrid floor)
    {
        var random = new System.Random(Config.WaterSeed + floor.Depth * 7919);
        int size = floor.Size;

        for (int k = 0; k < Config.GroundwaterBlobs(floor.Depth); k++)
        {
            int cx = random.Next(6, size - 6), cz = random.Next(6, size - 6);
            float radius = random.Next(Config.GroundwaterRadius.x, Config.GroundwaterRadius.y + 1);
            float stretch = 0.6f + (float)random.NextDouble() * 0.8f;
            float rx = radius * stretch, rz = radius / stretch;
            float phase = (float)random.NextDouble() * 6.28f;

            for (int z = Mathf.Max(0, (int)(cz - rz - 2)); z <= Mathf.Min(size - 1, (int)(cz + rz + 2)); z++)
            {
                for (int x = Mathf.Max(0, (int)(cx - rx - 2)); x <= Mathf.Min(size - 1, (int)(cx + rx + 2)); x++)
                {
                    float dx = (x - cx) / rx, dz = (z - cz) / rz;
                    float edge = 1f + 0.18f * Mathf.Sin(Mathf.Atan2(dz, dx) * 3f + phase);   // неровный край
                    if (dx * dx + dz * dz > edge * edge) continue;
                    int index = z * size + x;
                    if (floor.Water[index] < 1) floor.Water[index] = 1;
                }
            }
        }

        for (int k = 0; k < Config.Rivers(floor.Depth); k++)
        {
            bool alongX = random.Next(2) == 0;
            float lane = random.Next(15, size - 15);
            float amplitude = random.Next(4, 12);
            float frequency = 0.04f + (float)random.NextDouble() * 0.05f;
            float phase = (float)random.NextDouble() * 6.28f;
            int half = Config.RiverWidth / 2;

            for (int along = 0; along < size; along++)
            {
                int center = Mathf.RoundToInt(lane + amplitude * Mathf.Sin(along * frequency + phase));
                for (int across = center - half; across <= center - half + Config.RiverWidth - 1; across++)
                {
                    if (across < 0 || across >= size) continue;
                    int index = alongX ? across * size + along : along * size + across;
                    floor.Water[index] = 2;
                }
            }
        }

        if (floor.Depth != 1) return;

        // у главного въезда сухо: иначе первая же стройка начинается с насосной
        var entrance = Config.Def(Config.IndexOfRole(BuildingRole.MainEntrance));
        var footprint = entrance != null ? entrance.Footprint(Config.MainEntranceRotation) : Vector2Int.one;
        var center2 = new Vector2(Config.MainEntranceOrigin.x + footprint.x * 0.5f, Config.MainEntranceOrigin.y + footprint.y * 0.5f);
        float dry = Config.DryRadiusAroundEntrance + Mathf.Max(footprint.x, footprint.y) * 0.5f;

        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                if ((new Vector2(x + 0.5f, z + 0.5f) - center2).sqrMagnitude <= dry * dry) floor.Water[z * size + x] = 0;
    }

    /// <summary>Какую воду где откачивают: по каждой связанной насосной в её радиусе.</summary>
    private void Recompute(ref GridAttribute grid)
    {
        foreach (var floor in grid.Floors) System.Array.Clear(floor.Drain, 0, floor.Drain.Length);

        foreach (var i in _buildings)
        {
            ref var building = ref _buildings.Get2(i);
            var def = Config.Def(building.Def);
            if (def == null || def.Role != BuildingRole.Pump || !_buildings.GetEntity(i).Has<ConnectedFlag>()) continue;
            if (building.Depth >= grid.Floors.Length) continue;

            var floor = grid.Floors[building.Depth];
            var footprint = def.Footprint(building.Rotation);
            var center = new Vector2(building.Origin.x + footprint.x * 0.5f, building.Origin.y + footprint.y * 0.5f);
            int radius = def.PumpRadius;
            byte power = (byte)Mathf.Clamp(def.PumpPower, 0, 255);

            for (int z = Mathf.Max(0, (int)(center.y - radius) - 1); z <= Mathf.Min(floor.Size - 1, (int)(center.y + radius) + 1); z++)
            {
                for (int x = Mathf.Max(0, (int)(center.x - radius) - 1); x <= Mathf.Min(floor.Size - 1, (int)(center.x + radius) + 1); x++)
                {
                    if ((new Vector2(x + 0.5f, z + 0.5f) - center).sqrMagnitude > radius * radius) continue;
                    int index = z * floor.Size + x;
                    if (floor.Drain[index] < power) floor.Drain[index] = power;
                }
            }
        }

        if (SceneData.Floors == null) return;
        for (int depth = 0; depth < grid.Floors.Length && depth < SceneData.Floors.Length; depth++)
            if (SceneData.Floors[depth] != null) SceneData.Floors[depth].RebuildWater(grid.Floors[depth]);
    }

    // ─────────────────────── износ ───────────────────────

    private void Wear(ref GridAttribute grid)
    {
        foreach (var i in _buildings)
        {
            var entity = _buildings.GetEntity(i);
            ref var building = ref _buildings.Get2(i);
            var def = Config.Def(building.Def);
            if (def == null || def.Role == BuildingRole.MainEntrance || entity.Has<CollapseFlag>()) continue;

            bool wet = Wet(grid, def, building);
            float before = building.Condition;

            building.Condition = wet
                ? Mathf.Max(0f, building.Condition - Config.WearPerDay)
                : Mathf.Min(1f, building.Condition + Config.RepairPerDay);

            if (wet && before >= 0.999f)
                Log($"Подтопление: «{def.FullName}» ({Config.FloorName(building.Depth)}) стоит в воде — нужна насосная, иначе постройку размоет.");

            if (entity.Has<BuildingRef>() && entity.Get<BuildingRef>().Value != null)
                entity.Get<BuildingRef>().Value.SetFlood(wet, building.Condition);

            if (building.Condition <= 0f) entity.Get<CollapseFlag>();
        }
    }

    private bool Wet(GridAttribute grid, BuildingDefinition def, BuildingAttribute building)
    {
        def.Cells(building.Origin, building.Rotation, _cells);
        var floor = grid.Floors[building.Depth];
        var lower = BuildingsConfig.IsElevator(def.Role) && building.Depth + 1 < grid.Floors.Length ? grid.Floors[building.Depth + 1] : null;

        foreach (var cell in _cells)
            if (floor.IsWet(cell) || (lower != null && lower.IsWet(cell))) return true;
        return false;
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
