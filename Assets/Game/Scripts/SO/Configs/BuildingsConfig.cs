using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Всё про стройку: сетка и этажи, коридоры, каталог построек по 7 категориям, вода.
/// Сетка: 1 клетка = 1 м = 1 юнит. Этаж depth лежит на высоте −depth × FloorSpacing.
///
/// Постройка описывается в локальных клетках: Size.x — ширина вдоль фасада, Size.y — глубина.
/// Входы стоят на переднем ряду (lz = 0): основной в клетке DoorX, у камер ещё вход в пультовую
/// (ControlDoorX). Клетка перед каждым входом (lz = −1) должна быть связанным коридором.
/// Поворот 0..3 крутит постройку по часовой: фасад смотрит на юг, запад, север, восток.
///
/// В коде постройки различаются по Role, а не по номеру: новая комната — это строчка каталога.
/// В игре постройка хранит индекс записи в Buildings.
/// </summary>
[CreateAssetMenu(fileName = "BuildingsConfig", menuName = "Configs/BuildingsConfig")]
public class BuildingsConfig : ScriptableObject
{
    [Header("Сетка и этажи")]
    public int GridSize = 100;              // клеток по стороне каждого этажа
    public int MaxDepth = 5;                // сколько этажей вниз
    public float FloorSpacing = 8f;

    [Header("Вид")]
    public Material BaseMaterial;           // белый Lit, цвет задаётся каждой детали отдельно
    public float WallHeight = 1.6f;
    public float CorridorWallHeight = 1.1f;
    public float WallThickness = 0.15f;
    public Color SurfaceColor = new Color(0.30f, 0.34f, 0.28f);
    public Color UndergroundColor = new Color(0.15f, 0.16f, 0.18f);
    public Color GridLineColor = new Color(0.23f, 0.24f, 0.27f);
    public Color LockedColor = new Color(0.22f, 0.11f, 0.11f);
    public Color UnreachedCorridorColor = new Color(0.55f, 0.30f, 0.26f);
    public Color CorridorWallColor = new Color(0.46f, 0.48f, 0.52f);
    public Color GhostValid = new Color(0.30f, 0.90f, 0.40f);
    public Color GhostUnconnected = new Color(0.95f, 0.80f, 0.30f);
    public Color GhostInvalid = new Color(0.95f, 0.30f, 0.30f);
    public Color GhostZone = new Color(0.30f, 0.60f, 0.95f);

    [Header("Коридоры")]
    [Min(1)] public int ParallelGap = 2;    // столько свободных клеток между параллельными коридорами
    [Min(0)] public int SnapDistance = 2;   // конец ближе этого к коридору прилипает к его оси

    [Header("Экономика")]
    [Range(0, 100)] public int RefundPercent = 50;

    [Header("Старт")]
    public Vector2Int MainEntranceOrigin = new Vector2Int(41, 14);   // зал на −1 под порталом тоннеля
    [Range(0, 3)] public int MainEntranceRotation = 2;               // фасадом на север, в поле стройки
    public int StartCorridor = 3;                                    // широкий: по нему везут крупные объекты
    public int StartCorridorLength = 14;

    [Header("Вода")]
    public int WaterSeed = 1977;
    public int[] GroundwaterByDepth = { 0, 3, 6, 8, 10, 12 };   // пятен грунтовых вод на этаже
    public int[] RiversByDepth = { 0, 0, 1, 1, 2, 2 };          // подземных рек на этаже
    public Vector2Int GroundwaterRadius = new Vector2Int(4, 9);
    [Min(1)] public int RiverWidth = 3;
    public float DryRadiusAroundEntrance = 16f;
    public float WearPerDay = 0.1f;          // прочность в воде: 0.1 — рухнет за 10 дней
    public float RepairPerDay = 0.05f;       // на сухом месте медленно восстанавливается
    public Color GroundwaterColor = new Color(0.18f, 0.36f, 0.62f);
    public Color RiverColor = new Color(0.10f, 0.24f, 0.55f);
    public Color DrainedColor = new Color(0.16f, 0.22f, 0.26f);

    public CorridorDefinition[] Corridors = DefaultCorridors();
    public BuildingDefinition[] Buildings = DefaultBuildings();

    // ─────────────────────── доступ ───────────────────────

    public BuildingDefinition Def(int index) =>
        Buildings != null && index >= 0 && index < Buildings.Length ? Buildings[index] : null;

    public int IndexOf(string id) => Buildings == null ? -1 : Array.FindIndex(Buildings, b => b.Id == id);

    public int IndexOfRole(BuildingRole role) => Buildings == null ? -1 : Array.FindIndex(Buildings, b => b.Role == role);

    /// <summary>Обычная камера под тип объекта (не особая).</summary>
    public int CellFor(ContainmentType type) =>
        Buildings == null ? -1 : Array.FindIndex(Buildings, b => b.Role == BuildingRole.Containment && b.Containment == type && b.Buildable);

    public CorridorDefinition Corridor(int index) =>
        Corridors != null && index >= 0 && index < Corridors.Length ? Corridors[index] : null;

    /// <summary>Тип коридора по значению клетки сетки (0 — коридора нет).</summary>
    public CorridorDefinition CorridorOfTier(byte tier) => tier == 0 ? null : Corridor(tier - 1);

    public int WidthOfTier(byte tier) => CorridorOfTier(tier) != null ? CorridorOfTier(tier).Width : 0;

    public static bool IsElevator(BuildingRole role) =>
        role == BuildingRole.PersonnelElevator || role == BuildingRole.FreightElevator || role == BuildingRole.HeavyElevator;

    public static bool IsExit(BuildingRole role) => role == BuildingRole.MainEntrance || role == BuildingRole.SurfaceExit;

    public string FloorName(int depth) => depth == 0 ? "Поверхность" : "Этаж −" + depth;

    public static string CategoryName(BuildingCategory category)
    {
        switch (category)
        {
            case BuildingCategory.Containment: return "Содержание";
            case BuildingCategory.Research: return "Исследования";
            case BuildingCategory.Security: return "Безопасность";
            case BuildingCategory.Personnel: return "Персонал";
            case BuildingCategory.Infrastructure: return "Инфраструктура";
            case BuildingCategory.Administration: return "Администрация";
            default: return "Коммуникации";
        }
    }

    public static string BranchName(ScienceBranch branch)
    {
        switch (branch)
        {
            case ScienceBranch.Physics: return "аномальная физика";
            case ScienceBranch.Biomedicine: return "биомедицина";
            case ScienceBranch.Chemistry: return "химия";
            case ScienceBranch.Memetics: return "меметика и психология";
            case ScienceBranch.Engineering: return "инженерия";
            case ScienceBranch.Computing: return "вычисления";
            case ScienceBranch.SpaceTime: return "пространство-время";
            case ScienceBranch.Thaumaturgy: return "тауматургия";
            default: return "—";
        }
    }

    public int GroundwaterBlobs(int depth) => Pick(GroundwaterByDepth, depth);
    public int Rivers(int depth) => Pick(RiversByDepth, depth);

    private static int Pick(int[] values, int index) =>
        values == null || values.Length == 0 ? 0 : values[Mathf.Clamp(index, 0, values.Length - 1)];

    // ─────────────────────── координаты ───────────────────────

    public float FloorY(int depth) => -depth * FloorSpacing;

    /// <summary>Левый нижний угол клетки в мире.</summary>
    public Vector3 CellCorner(Vector2Int cell, int depth) =>
        new Vector3(cell.x - GridSize * 0.5f, FloorY(depth), cell.y - GridSize * 0.5f);

    public Vector3 CellCenter(Vector2Int cell, int depth) => CellCorner(cell, depth) + new Vector3(0.5f, 0f, 0.5f);

    public Vector2Int WorldToCell(Vector3 point) =>
        new Vector2Int(Mathf.FloorToInt(point.x + GridSize * 0.5f), Mathf.FloorToInt(point.z + GridSize * 0.5f));

    // ─────────────────────── редактор ───────────────────────

    [ContextMenu("Добавить недостающие записи")]
    private void FillMissing()
    {
        var corridors = new List<CorridorDefinition>(Corridors ?? new CorridorDefinition[0]);
        var defaults = DefaultCorridors();
        for (int i = corridors.Count; i < defaults.Length; i++) corridors.Add(defaults[i]);
        Corridors = corridors.ToArray();

        var buildings = new List<BuildingDefinition>(Buildings ?? new BuildingDefinition[0]);
        foreach (var def in DefaultBuildings())
            if (!buildings.Exists(b => b.Id == def.Id)) buildings.Add(def);
        Buildings = buildings.ToArray();

        Debug.Log($"{name}: проверено, недостающее дописано.", this);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Каталог, коридоры, старт и вода — заново из кода. Зовёт сборщик сцены при новой итерации.</summary>
    [ContextMenu("Пересобрать с нуля")]
    public void RebuildDefaults()
    {
        var fresh = CreateInstance<BuildingsConfig>();
        Corridors = DefaultCorridors();
        Buildings = DefaultBuildings();
        MainEntranceOrigin = fresh.MainEntranceOrigin;
        MainEntranceRotation = fresh.MainEntranceRotation;
        StartCorridor = fresh.StartCorridor;
        StartCorridorLength = fresh.StartCorridorLength;
        ParallelGap = fresh.ParallelGap;
        SnapDistance = fresh.SnapDistance;
        CorridorWallHeight = fresh.CorridorWallHeight;
        CorridorWallColor = fresh.CorridorWallColor;
        GhostZone = fresh.GhostZone;
        WaterSeed = fresh.WaterSeed;
        GroundwaterByDepth = fresh.GroundwaterByDepth;
        RiversByDepth = fresh.RiversByDepth;
        GroundwaterRadius = fresh.GroundwaterRadius;
        RiverWidth = fresh.RiverWidth;
        DryRadiusAroundEntrance = fresh.DryRadiusAroundEntrance;
        WearPerDay = fresh.WearPerDay;
        RepairPerDay = fresh.RepairPerDay;
        GroundwaterColor = fresh.GroundwaterColor;
        RiverColor = fresh.RiverColor;
        DrainedColor = fresh.DrainedColor;
        DestroyImmediate(fresh);

        Debug.LogWarning($"{name}: каталог, коридоры, старт и вода перезаписаны значениями из кода.", this);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ─────────────────────── коридоры ───────────────────────
    // Индекс — номер типа. Шире открываются уровнем директора, позже — исследованиями и по секторам.

    private static CorridorDefinition[] DefaultCorridors() => new[]
    {
        new CorridorDefinition { Name = "Техтоннель", Width = 1, CostPerCell = 25, UpkeepPerCell = 1f, ServiceOnly = true,
            Color = new Color(0.40f, 0.37f, 0.30f), RequiredDirectorLevel = 1,
            Description = "Служебный ход. Комнаты к нему не подключаются, кроме инфраструктуры." },
        new CorridorDefinition { Name = "Узкий коридор", Width = 2, CostPerCell = 40, UpkeepPerCell = 2f,
            Color = new Color(0.50f, 0.52f, 0.56f), RequiredDirectorLevel = 1, Description = "Дешёвый, для офисов и быта." },
        new CorridorDefinition { Name = "Коридор", Width = 3, CostPerCell = 50, UpkeepPerCell = 2f,
            Color = new Color(0.60f, 0.62f, 0.66f), RequiredDirectorLevel = 1, Description = "Основной, под большинство модулей." },
        new CorridorDefinition { Name = "Широкий коридор", Width = 5, CostPerCell = 70, UpkeepPerCell = 3f,
            Color = new Color(0.70f, 0.72f, 0.76f), RequiredDirectorLevel = 3, Description = "К крупным камерам и тяжёлому лифту." },
        new CorridorDefinition { Name = "Магистраль", Width = 7, CostPerCell = 90, UpkeepPerCell = 4f,
            Color = new Color(0.66f, 0.70f, 0.62f), RequiredDirectorLevel = 5, Description = "Электрокары и монорельс." },
    };

    // ─────────────────────── каталог ───────────────────────

    private static BuildingDefinition[] DefaultBuildings()
    {
        var c = BuildingCategory.Containment;
        var r = BuildingCategory.Research;
        var s = BuildingCategory.Security;
        var p = BuildingCategory.Personnel;
        var i = BuildingCategory.Infrastructure;
        var a = BuildingCategory.Administration;
        var m = BuildingCategory.Communications;
        var list = new List<BuildingDefinition>();

        // ── Содержание: камеры по типам объектов, особые — под конкретный SCP ──
        list.Add(Cell("cell_item", "Камера: предметы", "Предметы", 6, 4, 2, 3, 0, 7000, 600, ContainmentType.Item,
            new Color(0.35f, 0.55f, 0.40f), "Небольшие объекты. Камера и пультовая за стеклом, два входа."));
        list.Add(Cell("cell_humanoid", "Камера: гуманоиды", "Гуманоиды", 8, 5, 2, 4, 0, 10000, 900, ContainmentType.Humanoid,
            new Color(0.45f, 0.55f, 0.72f), "Жилая камера под наблюдением. С пультовой."));
        list.Add(B("cell_stable", "Камера стабильных гуманоидов", "Стабильные", c, 6, 5, 9000, 1000, BuildingRole.Containment,
            BuildingShape.Room, ContainmentType.StableHumanoid, color: new Color(0.62f, 0.66f, 0.80f),
            description: "Комфортные условия без постоянной слежки. Без пультовой, один вход."));
        list.Add(Cell("cell_creature", "Камера: существа", "Существа", 10, 7, 3, 6, 1, 14000, 1300, ContainmentType.Creature,
            new Color(0.78f, 0.60f, 0.25f), "Усиленные стены, кормление через шлюз. С пультовой."));
        list.Add(Cell("cell_large", "Камера: крупные", "Крупные", 15, 12, 3, 9, 1, 32000, 2600, ContainmentType.Large,
            new Color(0.70f, 0.40f, 0.30f), "Ангар под объекты размером с машину. Вход только с широкого коридора.", 3, 5));
        list.Add(B("intake", "Приёмник-карантин", "Приёмник", c, 6, 5, 6000, 500, description: "Новый объект сначала проверяют здесь."));
        list.Add(B("neutralized", "Склад нейтрализованных объектов", "Нейтрализованные", c, 5, 4, 4000, 300,
            description: "Освобождает камеры, остаются образцы."));
        list.Add(B("cell_service", "Пункт обслуживания камер", "Обслуживание", c, 4, 4, 3500, 300,
            description: "Кормление, уборка, ремонт камер."));
        list.Add(B("block_control", "Пост контроля блока", "Контроль блока", c, 4, 3, 5000, 400,
            description: "Мониторы камер блока: нарушение замечают раньше."));

        // ── Исследования ──
        list.AddRange(PostSizes("post", "Исследовательский пост", "Иссл. пост", r, BuildingRole.ResearchPost,
            "Учёные ведут привязанные камеры: 1 учёный — 1 объект. Отсюда все команды по объектам.",
            new[] { 6000, 500, 14000, 1100, 26000, 2000 }, false));
        list.Add(B("lab_samples", "Лаборатория образцов", "Образцы", r, 6, 5, 9000, 700, description: "Анализ проб, взятых у объектов."));
        list.Add(B("vivarium", "Виварий", "Виварий", r, 6, 5, 7000, 600, description: "Испытания на животных: дешевле класса D."));
        list.Add(B("autopsy", "Прозекторская", "Прозекторская", r, 5, 4, 6000, 500, description: "Вскрытия и биоматериалы после испытаний."));
        list.Add(Lab("lab_physics", "Лаборатория аномальной физики", "Физика", ScienceBranch.Physics, 1));
        list.Add(Lab("lab_biomed", "Биомедицинская лаборатория", "Биомедицина", ScienceBranch.Biomedicine, 1));
        list.Add(Lab("lab_chem", "Химико-материаловедческая лаборатория", "Химия", ScienceBranch.Chemistry, 1));
        list.Add(Lab("lab_meme", "Лаборатория меметики и психологии", "Меметика", ScienceBranch.Memetics, 2));
        list.Add(Lab("design_bureau", "Инженерное КБ", "КБ", ScienceBranch.Engineering, 1));
        list.Add(Lab("computing", "Вычислительный центр", "Вычисления", ScienceBranch.Computing, 2));
        list.Add(Lab("lab_spacetime", "Лаборатория пространства-времени", "Пространство-время", ScienceBranch.SpaceTime, 3));
        list.Add(Lab("lab_thaum", "Тауматургическая лаборатория", "Тауматургия", ScienceBranch.Thaumaturgy, 5));
        list.Add(B("lab_hot", "«Горячая» радиолаборатория", "Радиолаборатория", r, 6, 5, 16000, 1400, level: 3,
            description: "Объекты с излучением, работа манипуляторами."));
        list.Add(B("lab_amnestic", "Лаборатория амнезиаков", "Амнезиаки", r, 6, 5, 14000, 1100, level: 2,
            description: "Производство амнезиаков против рассекречивания."));
        list.Add(B("library", "Научная библиотека", "Библиотека", r, 6, 5, 6000, 400, description: "Документы объектов заполняются быстрее."));
        list.Add(B("council", "Зал учёного совета", "Учёный совет", r, 7, 5, 12000, 800, level: 2, description: "Бонус к исследованиям, новые ветки."));
        list.Add(B("pilot_prod", "Опытное производство", "Опытное пр-во", r, 9, 7, 30000, 2000, level: 3,
            description: "Коммерческое применение аномалий — источник денег."));
        list.Add(B("cross_test", "Зал перекрёстных испытаний", "Перекрёстные", r, 10, 8, 60000, 4000, level: 5,
            description: "На будущее: объект против объекта."));

        // ── Безопасность ──
        list.AddRange(PostSizes("guard", "Пост охраны", "Пост охраны", s, BuildingRole.GuardPost,
            "Смена охраны. Чем ближе к баракам D и камерам, тем быстрее сопровождение испытаний и реакция на побеги.",
            new[] { 4000, 400, 9000, 900, 18000, 1700 }, true));
        list.Add(Module("checkpoint", "КПП", "КПП", s, 3, 2, 4000, 300, BuildingShape.Checkpoint, 1, 2,
            new Color(0.65f, 0.62f, 0.30f), "Пропускной пункт прямо в коридоре. Ставится поперёк коридора."));
        list.Add(Module("tesla", "Ворота Теслы", "Тесла", s, 3, 1, 12000, 900, BuildingShape.TeslaGate, 3, 3,
            new Color(0.30f, 0.72f, 0.92f), "Смертельный разряд для всех без допуска."));
        list.Add(Module("blast_door", "Гермодверь сектора", "Гермодверь", s, 3, 1, 6000, 300, BuildingShape.Checkpoint, 2, 2,
            new Color(0.52f, 0.52f, 0.56f), "При нарушении отсекает участок коридора.", true));
        list.Add(Module("clearance_door", "Дверь по допуску", "Допуск", s, 3, 1, 3000, 150, BuildingShape.Checkpoint, 1, 2,
            new Color(0.80f, 0.68f, 0.30f), "Пускает только нужный уровень. Без неё посторонние ходят в закрытые зоны — растёт рассекречивание.",
            true));
        list.Add(Module("turret", "Автоматическая турель", "Турель", s, 1, 1, 7000, 400, BuildingShape.Device, 3, 2,
            new Color(0.40f, 0.40f, 0.44f), "Огонь по беглецам.", true));
        list.Add(Module("sleep_gas", "Усыпляющий газ", "Газ", s, 1, 1, 5000, 300, BuildingShape.Device, 2, 2,
            new Color(0.55f, 0.75f, 0.55f), "Нейтрализует беглецов без потерь.", true));
        list.Add(Module("cctv", "Камера видеонаблюдения", "Камера", s, 1, 1, 800, 40, BuildingShape.Device, 1, 1,
            new Color(0.25f, 0.25f, 0.28f), "Работает в паре с центром видеонаблюдения.", true));
        list.Add(B("cctv_center", "Центр видеонаблюдения", "Видеонаблюдение", s, 6, 4, 9000, 700,
            description: "Смотрит через камеры, раньше видит нарушения и чужаков."));
        list.Add(B("arsenal", "Арсенал", "Арсенал", s, 6, 5, 12000, 600,
            description: "Закупка снаряжения на каждого охранника. При нарушении и сборе МОГ забегают за спецснаряжением."));
        list.Add(B("mtf_base", "База МОГ", "База МОГ", s, 10, 8, 40000, 3000, level: 3, description: "Штаб и сбор мобильных оперативных групп."));
        list.Add(B("shooting_range", "Стрельбище", "Стрельбище", s, 12, 4, 10000, 500, description: "Тренировка охраны."));
        list.Add(B("assault_range", "Штурмовой полигон", "Штурм. полигон", s, 10, 8, 20000, 900, level: 2,
            description: "Отработка повторного захвата объектов."));
        list.Add(B("guardhouse", "Гауптвахта", "Гауптвахта", s, 5, 4, 5000, 300, description: "Провинившиеся сотрудники."));
        list.Add(B("interrogation", "Допросная", "Допросная", s, 4, 4, 4000, 200, description: "Пойманные агенты ГОИ дают разведданные."));
        list.Add(B("counterintel", "Отдел контрразведки", "Контрразведка", s, 5, 4, 12000, 900, level: 2,
            description: "Меньше шанс проникновения Повстанцев Хаоса и других ГОИ."));
        list.Add(B("infosec", "Отдел информационной безопасности", "Инфобез", s, 5, 4, 12000, 900, level: 2,
            description: "Защита сетей, данных и документов от утечек и взлома."));
        list.Add(B("amnestic_store", "Склад амнезиаков", "Амнезиаки", s, 4, 4, 5000, 300,
            description: "Амнезиаки для свидетелей и персонала после инцидентов."));
        list.Add(B("lockdown", "Пульт карантина", "Карантин", s, 4, 3, 15000, 600, level: 2, description: "Локдаун комплекса одной кнопкой."));
        list.Add(B("kennel", "Кинологическая служба", "Кинологи", s, 6, 5, 8000, 600, description: "Собаки чуют аномалии и нарушителей."));
        list.Add(B("shelter", "Убежище", "Убежище", s, 8, 6, 14000, 400, description: "Персонал пережидает нарушение."));
        list.Add(B("nuke", "Пункт ядерного самоуничтожения", "Самоуничтожение", s, 5, 5, 120000, 3000, level: 5,
            description: "Последний рубеж, решение вместе с O5."));

        // ── Персонал: бытовые комнаты на 10 / 25 / 40 человек ──
        list.AddRange(Sized("dorm", "Общежитие", "Общежитие", "Сон рядового персонала и охраны.", 1f, false));
        list.Add(B("officers", "Квартиры старшего состава", "Квартиры", p, 8, 6, 14000, 900,
            description: "Без них не нанять ведущих учёных и офицеров."));
        list.AddRange(Sized("dclass", "Блок D-класса", "Блок D", "Бараки класса D под охраной.", 1f, false));
        list.AddRange(Sized("canteen", "Столовая с кухней", "Столовая", "Еда.", 1.1f, false));
        list.AddRange(Sized("medbay", "Медпункт", "Медпункт", "Первая помощь.", 1.2f, true));
        list.Add(B("hospital", "Госпиталь", "Госпиталь", p, 10, 8, 30000, 2200, level: 2, description: "Хирургия и реанимация после инцидентов."));
        list.Add(B("med_isolation", "Медицинский изолятор", "Изолятор", p, 6, 4, 9000, 700,
            description: "Заражённые и попавшие под воздействие объекта."));
        list.Add(B("morgue", "Морг", "Морг", p, 5, 4, 5000, 300, description: "Тела после инцидентов и испытаний."));
        list.Add(B("psych", "Психологическая служба", "Психологи", p, 6, 4, 8000, 600,
            description: "Стресс, оценка перед повышением допуска, шкала послушания Милгрэма."));
        list.AddRange(Sized("rec", "Комната отдыха", "Отдых", "Настроение.", 0.8f, true));
        list.Add(B("gym", "Спортзал", "Спортзал", p, 8, 6, 9000, 500, description: "Настроение и форма."));
        list.Add(B("banya", "Баня", "Баня", p, 6, 5, 8000, 500, description: "Настроение, военный колорит."));
        list.Add(B("club", "Клуб-кинозал", "Клуб", p, 8, 7, 11000, 600, description: "Досуг."));
        list.Add(B("px", "Военторг", "Военторг", p, 5, 4, 6000, 200, description: "Сотрудники тратят зарплату, часть денег возвращается."));
        list.AddRange(Sized("training", "Учебный центр", "Учебный центр", "Обучение и рост по карьерной лестнице.", 1.1f, true));
        list.AddRange(Sized("sanblock", "Санблок", "Санблок", "Душевые и прачечная.", 0.8f, true));

        // ── Инфраструктура ──
        list.Add(B("substation", "Подстанция", "Подстанция", i, 4, 4, 6000, 300, description: "Распределяет питание по этажу."));
        list.Add(B("diesel", "Дизель-генераторная", "Генераторы", i, 6, 5, 12000, 1200, description: "Основное и резервное питание."));
        list.Add(B("batteries", "Аккумуляторная (ИБП)", "ИБП", i, 4, 4, 8000, 400,
            description: "Держит камеры при аварии; без подпитки сама разряжается."));
        list.Add(B("reactor", "Малая АЭС", "АЭС", i, 12, 10, 250000, 5000, level: 5, description: "Полная энергонезависимость."));
        list.Add(B("fvu", "ФВУ", "ФВУ", i, 5, 5, 10000, 800, description: "Фильтровентиляция: воздух, защита от газов и биоугроз."));
        list.Add(B("water_plant", "Скважина и водоочистка", "Водоочистка", i, 6, 5, 11000, 700, description: "Питьевая вода."));
        list.Add(B("pump_s", "Насосная", "Насосная", i, 4, 4, 8000, 500, BuildingRole.Pump, BuildingShape.Pump,
            variant: "малая", pumpRadius: 12, pumpPower: 1, color: new Color(0.30f, 0.50f, 0.72f),
            description: "Откачивает грунтовые воды в радиусе 12 м."));
        list.Add(B("pump_l", "Насосная", "Насосная", i, 6, 6, 20000, 1100, BuildingRole.Pump, BuildingShape.Pump,
            variant: "большая", pumpRadius: 18, pumpPower: 2, level: 2, color: new Color(0.24f, 0.42f, 0.70f),
            description: "Откачивает грунтовые воды и подземные реки в радиусе 18 м."));
        list.Add(B("boiler", "Котельная", "Котельная", i, 5, 5, 9000, 700, description: "Тепло."));
        list.Add(B("sewage", "Очистные сооружения", "Очистные", i, 7, 5, 10000, 600, description: "Канализация."));
        list.Add(B("incinerator", "Инсинератор", "Инсинератор", i, 5, 5, 9000, 600, description: "Уничтожение опасных отходов и материалов."));
        list.Add(B("warehouse", "Общий склад", "Склад", i, 8, 6, 7000, 300, description: "Хранение."));
        list.Add(B("food_store", "Продовольственный склад", "Продсклад", i, 6, 5, 6000, 400, description: "Запасы еды с холодильниками."));
        list.Add(B("hydroponics", "Гидропонная ферма", "Гидропоника", i, 10, 7, 18000, 900, level: 2, description: "Своя еда, автономия."));
        list.Add(B("construction_store", "Склад стройматериалов", "Стройматериалы", i, 6, 6, 8000, 300,
            description: "У грузового лифта: стройка дешевле и быстрее."));
        list.Add(B("workshop", "Ремонтная мастерская", "Мастерская", i, 6, 5, 8000, 500, description: "Техники обслуживают оборудование."));
        list.Add(B("factory", "Производственный цех", "Цех", i, 10, 8, 30000, 1600, level: 3, description: "Свои запчасти и оборудование."));
        list.Add(B("servers", "Серверная", "Серверная", i, 4, 4, 9000, 700, description: "Держит связь, видеонаблюдение и данные."));
        list.Add(B("fuel", "Склад ГСМ", "ГСМ", i, 5, 4, 6000, 300, description: "Топливо для генераторов и техники."));
        list.Add(B("garage", "Подземный автопарк", "Автопарк", i, 12, 10, 24000, 1200, shape: BuildingShape.Pad, onlyDepth: 1,
            attach: true, doorWidth: 4, description: "Машины для логистики и МОГ. Ставится только у главного въезда, воротами в зал."));
        list.Add(B("helipad", "Подземная вертолётная площадка", "Вертолётная", i, 12, 12, 60000, 2500, shape: BuildingShape.Pad,
            onlyDepth: 1, attach: true, doorWidth: 4, level: 3,
            description: "Доставка объектов и МОГ. Только у главного въезда, воротами в зал."));

        // ── Администрация ──
        list.Add(B("director_office", "Кабинет директора", "Директор", a, 5, 4, 8000, 400, description: "Центр управления Зоной."));
        list.Add(B("staff_hq", "Штаб-канцелярия", "Канцелярия", a, 6, 5, 8000, 600, description: "Документооборот."));
        list.Add(B("meeting", "Зал совещаний", "Совещания", a, 7, 5, 10000, 500, description: "Визиты и голосования Совета O5."));
        list.Add(B("finance", "Финансовый отдел", "Финансы", a, 5, 4, 9000, 700, description: "Бюджет, инвестиции, проценты."));
        list.Add(B("hr", "Отдел кадров", "Кадры", a, 5, 4, 7000, 600, description: "Найм и карьерная лестница."));
        list.Add(B("supply", "Отдел снабжения", "Снабжение", a, 5, 4, 8000, 600, description: "Дешевле закупки и материалы."));
        list.Add(B("legal", "Юридический отдел", "Юристы", a, 5, 4, 10000, 800, level: 2, description: "Договоры, коммерция, сделки с ГОИ."));
        list.Add(B("ethics", "Представительство Комитета по этике", "Комитет по этике", a, 5, 4, 9000, 500, level: 2,
            description: "Согласует спорные испытания."));
        list.Add(B("o5_link", "Узел спецсвязи с O5", "Спецсвязь O5", a, 4, 4, 20000, 900, level: 2,
            description: "Доверие и более быстрое финансирование."));
        list.Add(B("archive", "Архив документации", "Архив", a, 6, 5, 9000, 500, description: "Хранение по допускам, защита от утечек."));
        list.Add(B("coverup", "Отдел прикрытия", "Прикрытие", a, 5, 4, 12000, 900, description: "Легенды для СМИ, снижает рассекречивание."));
        list.Add(B("front_office", "Офис компании-прикрытия", "Компания-прикрытие", a, 5, 4, 15000, 600, level: 2,
            description: "Фасад Зоны, немного денег."));
        list.Add(B("analytics", "Аналитический отдел", "Аналитика", a, 5, 4, 15000, 1000, level: 3, description: "Продажа данных, источник денег."));
        list.Add(B("grants", "Отдел грантов", "Гранты", a, 5, 4, 10000, 700, level: 2, description: "Гранты и научные связи."));
        list.Add(B("guests", "Гостевые апартаменты", "Гости", a, 7, 5, 16000, 900, level: 3,
            description: "Инспекторы O5, делегации других Зон."));
        list.Add(B("dept_heads", "Кабинеты начальников отделов", "Начальники", a, 7, 5, 14000, 900, level: 2,
            description: "Открывают должности глав науки, безопасности, медицины."));

        // ── Коммуникации (коридоры — отдельным списком) ──
        list.Add(B("main_entrance", "Главный въезд", "Главный въезд", m, 18, 16, 0, 1500, BuildingRole.MainEntrance, BuildingShape.Entrance,
            buildable: false, onlyDepth: 1, doorWidth: 5, color: new Color(0.30f, 0.32f, 0.36f),
            description: "Подземная дорога с поверхности. Отсюда начинается вся Зона."));
        list.Add(B("elev_personnel", "Пассажирский лифт", "Пасс. лифт", m, 3, 3, 15000, 600, BuildingRole.PersonnelElevator,
            BuildingShape.Elevator, color: new Color(0.35f, 0.50f, 0.72f),
            description: "Персонал между этажами. Шахта занимает те же клетки этажом ниже."));
        list.Add(B("elev_freight", "Грузовой лифт", "Груз. лифт", m, 5, 5, 25000, 1000, BuildingRole.FreightElevator,
            BuildingShape.Elevator, color: new Color(0.55f, 0.47f, 0.30f),
            description: "Стройматериалы, объекты и коммуникации. Без него этаж ниже не открыть."));
        list.Add(B("elev_heavy", "Тяжёлый лифт", "Тяж. лифт", m, 8, 8, 60000, 2200, BuildingRole.HeavyElevator, BuildingShape.Elevator,
            level: 3, minWidth: 5, color: new Color(0.62f, 0.44f, 0.26f),
            description: "Крупные объекты и техника. Вход с широкого коридора."));
        list.Add(B("emergency_stairs", "Аварийная лестница", "Лестница", m, 3, 4, 6000, 200,
            description: "Эвакуация между этажами без лифта."));
        list.Add(B("emergency_exit", "Аварийный выход", "Авар. выход", m, 4, 4, 10000, 300, BuildingRole.SurfaceExit, BuildingShape.Exit,
            onlyDepth: 1, color: new Color(0.75f, 0.33f, 0.28f),
            description: "Ещё один путь на поверхность. Без второго выхода персоналу некуда эвакуироваться."));
        list.Add(B("vehicle_entrance", "Транспортный въезд", "Трансп. въезд", m, 8, 10, 45000, 1200, BuildingRole.SurfaceExit,
            BuildingShape.Exit, onlyDepth: 1, level: 2, doorWidth: 3, color: new Color(0.36f, 0.38f, 0.42f),
            description: "Ещё один въезд с поверхности — продолжение дороги."));
        list.Add(B("evac_tunnel", "Эвакуационный туннель", "Эвак. туннель", m, 3, 8, 20000, 400, BuildingRole.SurfaceExit,
            BuildingShape.Exit, onlyDepth: 1, level: 2, color: new Color(0.70f, 0.40f, 0.30f),
            description: "Выход далеко за периметр."));
        list.Add(Module("sanitary_lock", "Санпропускник сектора", "Санпропускник", m, 3, 2, 7000, 400, BuildingShape.Checkpoint, 2, 2,
            new Color(0.55f, 0.80f, 0.75f), "Дезинфекция между секторами.", true));
        list.Add(B("monorail", "Станция монорельса", "Монорельс", m, 8, 4, 50000, 1800, level: 5, minWidth: 7,
            description: "Быстрая перевозка по магистрали."));
        list.Add(B("pneumo", "Пневмопочта", "Пневмопочта", m, 2, 2, 3000, 100, description: "Документы ходят быстрее."));
        list.Add(B("telephone", "Узел связи (АТС)", "АТС", m, 4, 4, 6000, 300, description: "Телефония."));
        list.Add(B("radio", "Радиоцентр", "Радиоцентр", m, 5, 4, 9000, 500, description: "Радиосвязь с поверхностью и МОГ."));
        list.Add(B("alert_center", "Центр оповещения", "Оповещение", m, 4, 4, 8000, 400, description: "Сирены и громкая связь, эвакуация быстрее."));
        list.Add(B("vent_shaft", "Вентиляционная шахта", "Вентшахта", m, 2, 2, 3000, 100,
            description: "Нужна ФВУ, но по ней может сбежать объект."));

        return list.ToArray();
    }

    // ─────────────────────── заготовки записей ───────────────────────

    private static BuildingDefinition B(string id, string name, string shortName, BuildingCategory category, int width, int depth,
        int cost, int upkeep, BuildingRole role = BuildingRole.Stub, BuildingShape shape = BuildingShape.Room,
        ContainmentType containment = ContainmentType.None, int level = 1, bool buildable = true, int onlyDepth = 0,
        int minWidth = 0, bool attach = false, int doorWidth = 1, string variant = "", int capacity = 0,
        int pumpRadius = 0, int pumpPower = 0, ScienceBranch branch = ScienceBranch.None, bool planned = false,
        Color? color = null, string description = "")
    {
        return new BuildingDefinition
        {
            Id = id, Name = name, ShortName = shortName, Category = category, Role = role, Shape = shape,
            Size = new Vector2Int(width, depth), DoorX = width / 2, ControlWidth = 0, ControlDoorX = -1, DoorWidth = doorWidth,
            Cost = cost, MonthlyUpkeep = upkeep, Color = color ?? CategoryColor(category),
            Containment = containment, RequiredDirectorLevel = level, Buildable = buildable, OnlyDepth = onlyDepth,
            MinCorridorWidth = minWidth, AttachToEntrance = attach, Variant = variant, Capacity = capacity,
            PumpRadius = pumpRadius, PumpPower = pumpPower, Branch = branch,
            Planned = planned || role == BuildingRole.Stub, Description = description,
        };
    }

    private static BuildingDefinition Cell(string id, string name, string shortName, int width, int depth, int controlWidth,
        int doorX, int controlDoorX, int cost, int upkeep, ContainmentType type, Color color, string description,
        int level = 1, int minWidth = 0)
    {
        var def = B(id, name, shortName, BuildingCategory.Containment, width, depth, cost, upkeep, BuildingRole.Containment,
            BuildingShape.Cell, type, level, minWidth: minWidth, color: color, description: description);
        def.ControlWidth = controlWidth;
        def.DoorX = doorX;
        def.ControlDoorX = controlDoorX;
        return def;
    }

    private static BuildingDefinition Lab(string id, string name, string shortName, ScienceBranch branch, int level) =>
        B(id, name, shortName, BuildingCategory.Research, 7, 6, 16000 + 6000 * (level - 1), 1200 + 300 * (level - 1),
            level: level, branch: branch,
            description: "Дисциплина «" + BranchName(branch) + "». Её ускоряют объекты этой дисциплины.");

    private static BuildingDefinition Module(string id, string name, string shortName, BuildingCategory category, int width, int depth,
        int cost, int upkeep, BuildingShape shape, int level, int minWidth, Color color, string description, bool planned = false)
    {
        var def = B(id, name, shortName, category, width, depth, cost, upkeep, BuildingRole.Module, shape, level: level,
            minWidth: minWidth, planned: planned, color: color, description: description);
        def.DoorX = -1;
        return def;
    }

    /// <summary>Пост в трёх размерах на 2 / 6 / 12 мест. costs: цена и содержание малого, среднего, большого.</summary>
    private static IEnumerable<BuildingDefinition> PostSizes(string id, string name, string shortName, BuildingCategory category,
        BuildingRole role, string description, int[] costs, bool planned)
    {
        var sizes = new[] { new Vector2Int(4, 4), new Vector2Int(6, 5), new Vector2Int(8, 7) };
        var capacity = new[] { 2, 6, 12 };
        var variants = new[] { "малый", "средний", "большой" };
        var suffix = new[] { "_s", "_m", "_l" };

        for (int k = 0; k < 3; k++)
            yield return B(id + suffix[k], name, shortName, category, sizes[k].x, sizes[k].y, costs[k * 2], costs[k * 2 + 1],
                role, BuildingShape.Post, level: k + 1, variant: variants[k], capacity: capacity[k], planned: planned,
                description: description + " Мест: " + capacity[k] + ".");
    }

    /// <summary>Бытовая комната в трёх размерах на 10 / 25 / 40 человек.</summary>
    private static IEnumerable<BuildingDefinition> Sized(string id, string name, string shortName, string description, float price, bool small)
    {
        var sizes = small
            ? new[] { new Vector2Int(5, 4), new Vector2Int(7, 6), new Vector2Int(10, 8) }
            : new[] { new Vector2Int(6, 5), new Vector2Int(9, 7), new Vector2Int(12, 9) };
        var capacity = new[] { 10, 25, 40 };
        var variants = new[] { "малый", "средний", "большой" };
        var suffix = new[] { "_s", "_m", "_l" };
        var costs = new[] { 6000, 13000, 22000 };
        var upkeep = new[] { 400, 900, 1500 };

        for (int k = 0; k < 3; k++)
            yield return B(id + suffix[k], name, shortName, BuildingCategory.Personnel, sizes[k].x, sizes[k].y,
                (int)(costs[k] * price), (int)(upkeep[k] * price), level: k == 2 ? 2 : 1, variant: variants[k],
                capacity: capacity[k], description: description + " Мест: " + capacity[k] + ".");
    }

    private static Color CategoryColor(BuildingCategory category)
    {
        switch (category)
        {
            case BuildingCategory.Containment: return new Color(0.40f, 0.55f, 0.48f);
            case BuildingCategory.Research: return new Color(0.36f, 0.58f, 0.66f);
            case BuildingCategory.Security: return new Color(0.62f, 0.52f, 0.34f);
            case BuildingCategory.Personnel: return new Color(0.58f, 0.48f, 0.62f);
            case BuildingCategory.Infrastructure: return new Color(0.50f, 0.50f, 0.42f);
            case BuildingCategory.Administration: return new Color(0.46f, 0.48f, 0.66f);
            default: return new Color(0.42f, 0.52f, 0.70f);
        }
    }
}

[Serializable]
public class CorridorDefinition
{
    public string Name;
    [Min(1)] public int Width = 3;
    public int CostPerCell = 50;
    public float UpkeepPerCell = 2f;          // в месяц
    public Color Color = Color.gray;
    public int RequiredDirectorLevel = 1;
    public bool ServiceOnly;                  // техтоннель: комнаты к нему не подключаются
    [TextArea(1, 2)] public string Description;
}

[Serializable]
public class BuildingDefinition
{
    public string Id;                        // уникальный ключ записи
    public string Name;
    public string ShortName;                 // табличка на полу
    public string Variant;                   // «малый» / «средний» / «большой» — пусто, если вариант один
    public BuildingCategory Category;
    public BuildingRole Role;
    public BuildingShape Shape;
    public Vector2Int Size = new Vector2Int(4, 4);
    public int DoorX = 1;                    // основной вход на фасаде, считая слева; −1 — без входа (модуль)
    [Min(1)] public int DoorWidth = 1;       // ширина проёма — только вид
    public int ControlWidth;                 // камера: столько клеток слева занимает пультовая за стеклом
    public int ControlDoorX = -1;            // вход в пультовую на фасаде; −1 — нет
    public int Cost;
    public int MonthlyUpkeep;
    public Color Color = Color.gray;
    public ContainmentType Containment;      // для камер: какой тип объектов принимает
    public int Capacity;                     // мест: учёных, охранников, жильцов
    public int RequiredDirectorLevel = 1;
    public bool Buildable = true;            // главный въезд ставится сам
    public int OnlyDepth;                    // 0 — любой подземный этаж
    public int MinCorridorWidth;             // у основного входа (у модуля — под ним) нужен коридор не уже
    public bool AttachToEntrance;            // ворота должны выходить прямо в зал главного въезда
    public int PumpRadius;
    public int PumpPower;                    // 1 — грунтовые воды, 2 — ещё и реки
    public ScienceBranch Branch;             // лаборатория какой дисциплины
    public bool Planned;                     // заготовка: строится, механика — позже
    [TextArea(1, 3)] public string Description;

    public bool CorridorModule => Role == BuildingRole.Module;
    public bool HasDoor => DoorX >= 0 && !CorridorModule;
    public bool HasControlRoom => ControlWidth > 0 && ControlDoorX >= 0;
    public string FullName => string.IsNullOrEmpty(Variant) ? Name : Name + " (" + Variant + ")";

    public Vector2Int Footprint(int rotation) => (rotation & 1) == 0 ? Size : new Vector2Int(Size.y, Size.x);

    /// <summary>Локальная клетка (lx — вдоль фасада, lz — вглубь) после поворота, относительно Origin.</summary>
    public Vector2Int Local(int lx, int lz, int rotation)
    {
        int w = Size.x, d = Size.y;
        switch (rotation & 3)
        {
            case 0: return new Vector2Int(lx, lz);
            case 1: return new Vector2Int(lz, w - 1 - lx);
            case 2: return new Vector2Int(w - 1 - lx, d - 1 - lz);
            default: return new Vector2Int(d - 1 - lz, lx);
        }
    }

    public void Cells(Vector2Int origin, int rotation, List<Vector2Int> into)
    {
        into.Clear();
        for (int lx = 0; lx < Size.x; lx++)
            for (int lz = 0; lz < Size.y; lz++)
                into.Add(origin + Local(lx, lz, rotation));
    }

    public Vector2Int DoorInside(Vector2Int origin, int rotation) => origin + Local(DoorX, 0, rotation);
    public Vector2Int DoorOutside(Vector2Int origin, int rotation) => origin + Local(DoorX, -1, rotation);
    public Vector2Int ControlInside(Vector2Int origin, int rotation) => origin + Local(ControlDoorX, 0, rotation);
    public Vector2Int ControlOutside(Vector2Int origin, int rotation) => origin + Local(ControlDoorX, -1, rotation);

    /// <summary>Клетки перед всеми входами: основной первым, потом пультовая.</summary>
    public void DoorsOutside(Vector2Int origin, int rotation, List<Vector2Int> into)
    {
        into.Clear();
        if (!HasDoor) return;
        into.Add(DoorOutside(origin, rotation));
        if (HasControlRoom) into.Add(ControlOutside(origin, rotation));
    }

    /// <summary>Клетки проёма основного входа по всей ширине (DoorWidth) — у фасада, изнутри. Добавляет, не очищая.</summary>
    public void DoorSpan(Vector2Int origin, int rotation, List<Vector2Int> into)
    {
        if (!HasDoor) return;
        int width = Mathf.Max(1, DoorWidth);
        for (int k = -(width - 1) / 2; k <= width / 2; k++)
        {
            int lx = DoorX + k;
            if (lx >= 0 && lx < Size.x) into.Add(origin + Local(lx, 0, rotation));
        }
    }
}
