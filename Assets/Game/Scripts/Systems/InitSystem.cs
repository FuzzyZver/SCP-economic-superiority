using System.Collections.Generic;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Старт партии: сущность Зоны со всеми шкалами и сеткой, камера, объекты, этажи,
/// главный вход со стартовым коридором и стартовые окна.
/// Объекты приезжают без камер — их ещё нужно построить. Пока камеры нет, растёт рассекречивание.
/// </summary>
public class InitSystem : Injects, IEcsInitSystem
{
    public void Init()
    {
        EnsureMaterial();
        CreateSite();
        CreateCamera();
        CreateScps();
        InitFloors();
        PlaceStart();

        UI.Hud.SetVisible(true);
        UI.Document.SetVisible(false);
        UI.GameOver.SetVisible(false);
        if (UI.Build != null) UI.Build.SetVisible(true);
        if (UI.ScpList != null) UI.ScpList.SetVisible(true);

        int clearance = GameConfig.BalanceConfig.ClearanceFor(GameConfig.BalanceConfig.StartDirectorLevel);
        Log($"Вы назначены директором Зоны. Допуск {clearance}. Совет O5 ждёт первого отчёта.");
        Log("Объекты доставлены, но камер для них нет. Выберите объект в списке слева и укажите или постройте ему камеру.");
        Log("Строительство — кнопка внизу справа или B. Для исследований нужен исследовательский пост.");
    }

    private void EnsureMaterial()
    {
        var buildings = GameConfig.BuildingsConfig;
        if (buildings.BaseMaterial != null) return;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        buildings.BaseMaterial = new Material(shader) { name = "Base (runtime)" };
        Debug.LogWarning("В BuildingsConfig не назначен BaseMaterial — создан временный.", buildings);
    }

    private void CreateSite()
    {
        var balance = GameConfig.BalanceConfig;
        var buildings = GameConfig.BuildingsConfig;
        var site = EcsWorld.NewEntity();

        site.Get<SiteFlag>();
        site.Get<CalendarAttribute>().Hours = balance.StartHour;

        ref var time = ref site.Get<TimeScaleAttribute>();
        time.SpeedIndex = Mathf.Clamp(balance.DefaultSpeedIndex, 0, Mathf.Max(0, balance.Speeds.Length - 1));
        time.Paused = false;

        site.Get<TreasuryAttribute>().Money = balance.StartMoney;

        ref var director = ref site.Get<DirectorAttribute>();
        director.Level = balance.StartDirectorLevel;
        director.Xp = balance.XpToReach(director.Level);
        director.Clearance = balance.ClearanceFor(director.Level);

        ref var trust = ref site.Get<TrustAttribute>();
        trust.Floor = balance.TrustFloor;
        trust.Ceiling = balance.CeilingFor(director.Level);
        trust.Value = Mathf.Clamp(balance.StartTrust, trust.Floor, trust.Ceiling);
        trust.Level = trust.BestLevel = balance.TrustLevelFor(trust.Value);
        trust.LastIncidentDay = 0;

        site.Get<DeclassificationAttribute>().Value = 0f;
        site.Get<SelectedScpAttribute>().HasTarget = false;
        site.Get<LogAttribute>().Lines = new List<string>();

        ref var grid = ref site.Get<GridAttribute>();
        grid.Size = buildings.GridSize;
        grid.Floors = new FloorGrid[buildings.MaxDepth + 1];
        for (int depth = 0; depth <= buildings.MaxDepth; depth++)
            grid.Floors[depth] = new FloorGrid(buildings.GridSize, depth);
        grid.Floors[1].Open = true;   // этаж −1 открыт всегда: туда ведёт главный вход
        grid.NextId = 0;

        ref var tool = ref site.Get<BuildToolAttribute>();
        tool.Tool = BuildTool.None;
        tool.Corridor = Mathf.Clamp(2, 0, buildings.Corridors.Length - 1);   // обычный коридор
        tool.Building = -1;
        tool.Status = string.Empty;

        ref var menu = ref site.Get<BuildMenuAttribute>();
        menu.Open = false;
        menu.Tab = BuildingCategory.Containment;

        site.Get<PointerAttribute>().HasCell = false;
        site.Get<ViewFloorAttribute>().Depth = Mathf.Clamp(balance.StartViewDepth, 0, buildings.MaxDepth);
    }

    private void CreateCamera()
    {
        var settings = GameConfig.SettingsConfig;
        var buildings = GameConfig.BuildingsConfig;
        var entity = EcsWorld.NewEntity();
        entity.Get<CameraFlag>();

        ref var camera = ref entity.Get<CameraAttribute>();
        float yaw = SceneData.CameraRig != null ? SceneData.CameraRig.eulerAngles.y : 45f;
        camera.Yaw = camera.TargetYaw = yaw;
        camera.Zoom = settings.StartZoom;
        camera.TargetHeight = buildings.FloorY(Mathf.Clamp(GameConfig.BalanceConfig.StartViewDepth, 0, buildings.MaxDepth));

        if (SceneData.CameraRig == null) return;
        var entrance = buildings.Def(buildings.IndexOfRole(BuildingRole.MainEntrance));
        var size = entrance != null ? entrance.Footprint(buildings.MainEntranceRotation) : Vector2Int.zero;
        var corner = buildings.CellCorner(buildings.MainEntranceOrigin, 1);
        var position = SceneData.CameraRig.position;
        position.y = camera.TargetHeight;
        position.x = corner.x + size.x * 0.5f;
        position.z = corner.z + size.y + 6f;   // смотрим на поле перед главным въездом
        SceneData.CameraRig.position = position;
    }

    private void CreateScps()
    {
        var config = GameConfig.ScpConfig;
        if (config.Objects == null) return;

        foreach (var definition in config.Objects)
        {
            var entity = EcsWorld.NewEntity();
            entity.Get<ScpFlag>();
            entity.Get<ScpAttribute>().Id = definition.Id;
            entity.Get<NicknameAttribute>().Value = definition.Title;
            entity.Get<ResearchAttribute>().Progress = 0f;
            entity.Get<ClassificationAttribute>().IsAssigned = false;
            entity.Get<ContainedAttribute>().RoomId = ContainedAttribute.NoRoom;
        }
    }

    private void InitFloors()
    {
        var buildings = GameConfig.BuildingsConfig;

        if (SceneData.Floors != null)
            foreach (var floor in SceneData.Floors)
                if (floor != null) floor.Init(buildings);

        if (SceneData.Ghost != null) SceneData.Ghost.Init(buildings.BaseMaterial);
    }

    /// <summary>Главный въезд и коридор от него — бесплатно, через те же события, что и обычная стройка.</summary>
    private void PlaceStart()
    {
        var buildings = GameConfig.BuildingsConfig;
        int index = buildings.IndexOfRole(BuildingRole.MainEntrance);
        var entrance = buildings.Def(index);
        if (entrance == null)
        {
            Debug.LogError("В BuildingsConfig нет главного въезда.", buildings);
            return;
        }

        var origin = buildings.MainEntranceOrigin;
        int rotation = buildings.MainEntranceRotation;

        ref var place = ref EcsWorld.NewEntity().Get<PlaceBuildingEvent>();
        place.Def = index;
        place.Depth = 1;
        place.Origin = origin;
        place.Rotation = rotation;
        place.Free = true;

        var outside = entrance.DoorOutside(origin, rotation);
        var forward = outside - entrance.DoorInside(origin, rotation);

        ref var corridor = ref EcsWorld.NewEntity().Get<PlaceCorridorEvent>();
        corridor.Depth = 1;
        corridor.From = outside;
        corridor.To = outside + forward * Mathf.Max(0, buildings.StartCorridorLength - 1);
        corridor.Corridor = buildings.StartCorridor;
        corridor.Free = true;
    }

    private void Log(string text) => EcsWorld.NewEntity().Get<LogEvent>().Text = text;
}
