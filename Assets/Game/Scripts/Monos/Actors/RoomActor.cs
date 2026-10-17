using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Постройка на сетке. Пока нет моделей, собирается из примитивов: пол, стены с проёмами
/// на месте входов, оформление по форме (пультовая камеры, зал въезда, столы поста…),
/// табличка, предупреждения и место для объекта.
/// Создаёт его BuildSystem через FloorActor.CreateRoom; нижняя станция лифта —
/// тот же актор без своей сущности (Init не вызывается).
///
/// Оформление ставится в локальных координатах постройки (lx — вдоль фасада, lz — вглубь),
/// поэтому поворот на него не влияет.
/// </summary>
public class RoomActor : Actor
{
    private static readonly Color WallColor = new Color(0.62f, 0.63f, 0.66f);
    private static readonly Color GlassColor = new Color(0.55f, 0.80f, 0.92f);
    private static readonly Color Dark = new Color(0.07f, 0.07f, 0.08f);
    private static readonly Color Asphalt = new Color(0.17f, 0.17f, 0.18f);
    private static readonly Color Stripe = new Color(0.90f, 0.82f, 0.30f);
    private static readonly Color Concrete = new Color(0.55f, 0.55f, 0.53f);
    private static readonly Color Red = new Color(1f, 0.3f, 0.25f);

    private MaterialPropertyBlock _block;   // не в инициализаторе поля: Unity запрещает создавать его до Awake

    private BuildingsConfig _config;
    private TextMeshPro _warning;
    private TextMeshPro _flood;
    private readonly List<GameObject> _doorMarks = new List<GameObject>();
    private GameObject _occupant;
    private Vector3 _occupantSpot;
    private float _floorTop;

    // чем строили в последний раз — чтобы перестроить стены, когда к залу пристроят автопарк
    private BuildingDefinition _def;
    private Vector2Int _origin;
    private int _rotation, _depth;
    private bool _lowerStop;
    private readonly HashSet<Vector2Int> _openings = new HashSet<Vector2Int>();

    // локальная система постройки: центр клетки (0, 0) и шаги по lx и lz в мире
    private Vector3 _c00, _ex, _ez;

    public override void ExpandEntity(EcsEntity entity)
    {
        entity.Get<BuildingFlag>();
        entity.Get<BuildingRef>().Value = this;
    }

    public void Build(BuildingDefinition def, BuildingsConfig config, Vector2Int origin, int rotation, int depth, bool lowerStop)
    {
        _config = config;
        _def = def;
        _origin = origin;
        _rotation = rotation;
        _depth = depth;
        _lowerStop = lowerStop;
        Clear();

        var size = def.Footprint(rotation);
        var min = config.CellCorner(origin, depth);
        var center = min + new Vector3(size.x * 0.5f, 0f, size.y * 0.5f);
        _floorTop = min.y + 0.1f;

        _c00 = config.CellCenter(origin + def.Local(0, 0, rotation), depth);
        _ex = config.CellCenter(origin + def.Local(1, 0, rotation), depth) - _c00;
        _ez = config.CellCenter(origin + def.Local(0, 1, rotation), depth) - _c00;

        float w = def.Size.x, d = def.Size.y;
        float height = config.WallHeight;
        _occupantSpot = Local((w - 1f) * 0.5f, (d - 1f) * 0.5f);
        var titleSpot = _occupantSpot;
        float titleRoom = Mathf.Min(size.x, size.y);

        switch (def.Shape)
        {
            case BuildingShape.Checkpoint: BuildCheckpoint(def, size, center, height); break;
            case BuildingShape.TeslaGate: BuildTesla(def, size, center, height); break;
            case BuildingShape.Device: BuildDevice(def, center); break;
            default:
                Part(PrimitiveType.Cube, center + Vector3.up * 0.05f, new Vector3(size.x - 0.1f, 0.1f, size.y - 0.1f), def.Color);
                BuildWalls(def, origin, rotation, size, min, height, lowerStop);
                switch (def.Shape)
                {
                    case BuildingShape.Elevator: BuildShaft(size, center); break;
                    case BuildingShape.Exit: BuildExit(def, w, d); break;
                    case BuildingShape.Cell:
                        BuildControlRoom(def, d, height);
                        _occupantSpot = Local((def.ControlWidth + w - 1f) * 0.5f, (d - 1f) * 0.5f);
                        titleSpot = _occupantSpot;
                        titleRoom = Mathf.Min(w - def.ControlWidth, d);
                        break;
                    case BuildingShape.Entrance:
                        BuildEntrance(w, d);
                        titleSpot = Local((w - 1f) * 0.5f, 2.5f);
                        titleRoom = 6f;
                        break;
                    case BuildingShape.Room:
                        if (def.Containment == ContainmentType.StableHumanoid) BuildComfort(w, d);
                        break;
                    case BuildingShape.Post: BuildPost(def, w, d); break;
                    case BuildingShape.Pump: BuildPump(w, d); break;
                    case BuildingShape.Pad: BuildPad(def, w, d); break;
                }
                break;
        }

        string title = def.ShortName;
        if (!string.IsNullOrEmpty(def.Variant)) title += "\n<size=70%>" + def.Variant + "</size>";
        if (lowerStop) title += "\n<size=70%>(станция)</size>";
        Label(title, new Vector2(Mathf.Max(1f, titleRoom - 0.6f), Mathf.Clamp(titleRoom - 0.4f, 0.8f, 2.4f)),
            titleSpot + Vector3.up * 0.13f, Color.white, 10f);

        _warning = Label("", new Vector2(Mathf.Max(3f, size.x), 2.2f), center + Vector3.up * (height + 0.15f), Red, 12f);
        _warning.gameObject.SetActive(false);

        _flood = Label("", new Vector2(Mathf.Max(3f, size.x), 1.4f),
            center + Vector3.up * (height + 0.2f) + Vector3.back * 1.4f, new Color(0.45f, 0.75f, 1f), 9f);
        _flood.gameObject.SetActive(false);

        // клетки перед входами: сюда нужно подвести коридор, видны, пока доступа нет
        if (def.HasDoor)
        {
            DoorMark(config.CellCenter(def.DoorOutside(origin, rotation), depth), height);
            if (def.HasControlRoom && !lowerStop) DoorMark(config.CellCenter(def.ControlOutside(origin, rotation), depth), height);
        }
    }

    /// <summary>
    /// Проёмы в чужие постройки: клетки соседей вплотную к стене (например, ворота пристроенного автопарка).
    /// Стена напротив них не ставится. Перестраивает постройку, только если набор изменился.
    /// </summary>
    public void SetOpenings(List<Vector2Int> cells)
    {
        if (_def == null) return;
        if (_openings.Count == cells.Count && _openings.SetEquals(cells)) return;

        _openings.Clear();
        foreach (var cell in cells) _openings.Add(cell);
        Build(_def, _config, _origin, _rotation, _depth, _lowerStop);
    }

    /// <summary>Почему постройка не работает (пусто — работает) и какие входы без коридора.</summary>
    public void SetState(string issue, bool mainOk, bool controlOk)
    {
        if (_warning != null)
        {
            _warning.gameObject.SetActive(!string.IsNullOrEmpty(issue));
            _warning.text = issue ?? string.Empty;
        }

        if (_doorMarks.Count > 0) _doorMarks[0].SetActive(!mainOk);
        if (_doorMarks.Count > 1) _doorMarks[1].SetActive(!controlOk);
    }

    /// <summary>Подтопление: видно, пока постройка в воде или ещё не просохла.</summary>
    public void SetFlood(bool wet, float condition)
    {
        if (_flood == null) return;
        bool show = wet || condition < 0.999f;
        _flood.gameObject.SetActive(show);
        if (show) _flood.text = (wet ? "ПОДТОПЛЕНИЕ" : "сохнет") + " · прочность " + Mathf.RoundToInt(condition * 100f) + "%";
    }

    public void ShowOccupant(ScpDefinition definition)
    {
        ClearOccupant();
        if (definition == null) return;

        var scale = definition.ShapeScale;
        bool tall = definition.Shape == PrimitiveType.Capsule || definition.Shape == PrimitiveType.Cylinder;
        float halfHeight = tall ? scale.y : scale.y * 0.5f;

        _occupant = Part(definition.Shape, new Vector3(_occupantSpot.x, _floorTop + halfHeight, _occupantSpot.z), scale, definition.ShapeColor);
        _occupant.name = definition.Number;
    }

    public void ClearOccupant()
    {
        if (_occupant != null) Destroy(_occupant);
        _occupant = null;
    }

    private void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        _occupant = null;
        _warning = null;
        _flood = null;
        _doorMarks.Clear();
    }

    // ─────────────────────── локальные координаты ───────────────────────

    /// <summary>Точка на полу в локальных координатах постройки (центр клетки — целые lx, lz).</summary>
    private Vector3 Local(float lx, float lz) => _c00 + _ex * lx + _ez * lz;

    /// <summary>Коробка вдоль локальных осей: sx — вдоль фасада, sz — вглубь, от высоты y0 до y0 + h.</summary>
    private GameObject Box(float lx, float lz, float sx, float sz, float y0, float h, Color color)
    {
        var size = new Vector3(Mathf.Abs(_ex.x) * sx + Mathf.Abs(_ez.x) * sz, h, Mathf.Abs(_ex.z) * sx + Mathf.Abs(_ez.z) * sz);
        return Part(PrimitiveType.Cube, Local(lx, lz) + Vector3.up * (y0 + h * 0.5f), size, color);
    }

    // ─────────────────────── стены ───────────────────────

    private void BuildWalls(BuildingDefinition def, Vector2Int origin, int rotation, Vector2Int size, Vector3 min, float height, bool lowerStop)
    {
        var gaps = new HashSet<Vector2Int>();
        var facing = Vector2Int.zero;

        if (def.HasDoor)
        {
            facing = def.DoorOutside(origin, rotation) - def.DoorInside(origin, rotation);   // куда смотрит фасад
            int width = Mathf.Max(1, def.DoorWidth);
            for (int k = -(width - 1) / 2; k <= width / 2; k++)
            {
                int lx = def.DoorX + k;
                if (lx >= 0 && lx < def.Size.x) gaps.Add(origin + def.Local(lx, 0, rotation));
            }
            if (def.HasControlRoom && !lowerStop) gaps.Add(def.ControlInside(origin, rotation));
        }

        int x0 = origin.x, x1 = origin.x + size.x - 1;
        int z0 = origin.y, z1 = origin.y + size.y - 1;

        Run(x0, x1, z0, true, false, facing.y == -1, Vector2Int.down, gaps, min, height);   // юг
        Run(x0, x1, z1, true, true, facing.y == 1, Vector2Int.up, gaps, min, height);       // север
        Run(z0, z1, x0, false, false, facing.x == -1, Vector2Int.left, gaps, min, height);  // запад
        Run(z0, z1, x1, false, true, facing.x == 1, Vector2Int.right, gaps, min, height);   // восток
    }

    /// <summary>Одна сторона: стены отрезками между проёмами. alongX — сторона идёт вдоль X,
    /// farSide — это северная или восточная сторона (стена у дальнего края клетки).</summary>
    private void Run(int from, int to, int fixedCoord, bool alongX, bool farSide, bool front, Vector2Int outward, HashSet<Vector2Int> gaps, Vector3 min, float height)
    {
        int start = int.MinValue;
        for (int i = from; i <= to + 1; i++)
        {
            var cell = alongX ? new Vector2Int(i, fixedCoord) : new Vector2Int(fixedCoord, i);
            bool solid = i <= to && !(front && gaps.Contains(cell)) && !_openings.Contains(cell + outward);

            if (solid && start == int.MinValue) start = i;
            if (solid || start == int.MinValue) continue;

            Segment(start, i - 1, fixedCoord, alongX, farSide, min, height);
            start = int.MinValue;
        }
    }

    private void Segment(int a, int b, int fixedCoord, bool alongX, bool farSide, Vector3 min, float height)
    {
        float t = _config.WallThickness;
        float length = b - a + 1;
        var origin = _config.CellCorner(alongX ? new Vector2Int(a, fixedCoord) : new Vector2Int(fixedCoord, a), 0);
        float y = min.y + height * 0.5f;

        Vector3 center, scale;
        if (alongX)
        {
            float z = origin.z + (farSide ? 1f - t * 0.5f : t * 0.5f);
            center = new Vector3(origin.x + length * 0.5f, y, z);
            scale = new Vector3(length, height, t);
        }
        else
        {
            float x = origin.x + (farSide ? 1f - t * 0.5f : t * 0.5f);
            center = new Vector3(x, y, origin.z + length * 0.5f);
            scale = new Vector3(t, height, length);
        }

        Part(PrimitiveType.Cube, center, scale, WallColor);
    }

    // ─────────────────────── оформление ───────────────────────

    /// <summary>Камера: слева пультовая за стеклом, приборные панели вдоль стекла.</summary>
    private void BuildControlRoom(BuildingDefinition def, float d, float height)
    {
        float cw = def.ControlWidth;
        float midZ = (d - 1f) * 0.5f;

        Box((cw - 1f) * 0.5f, midZ, cw - 0.15f, d - 0.15f, 0.1f, 0.02f, new Color(0.22f, 0.24f, 0.28f));   // пол пультовой
        Box(cw - 0.5f, midZ, 0.15f, d, 0f, 0.7f, WallColor);                                                 // стенка под стеклом
        Box(cw - 0.5f, midZ, 0.06f, d, 0.7f, height - 0.7f, GlassColor);                                     // стекло

        for (float lz = 1f; lz < d - 0.5f; lz += 2f)
        {
            Box(cw - 1f, lz, 0.5f, 0.9f, 0.1f, 0.55f, new Color(0.28f, 0.30f, 0.34f));      // пульт
            Box(cw - 1.2f, lz, 0.05f, 0.7f, 0.65f, 0.35f, new Color(0.15f, 0.45f, 0.40f));  // экран
        }

        Label("пульт", new Vector2(Mathf.Max(0.8f, cw - 0.2f), 0.8f), Local((cw - 1f) * 0.5f, 0.4f) + Vector3.up * 0.13f,
            new Color(1f, 1f, 1f, 0.7f), 4f);
    }

    /// <summary>Зал главного въезда: дорога из тоннеля, пандус у дальней стены, разметка, стоянка.</summary>
    private void BuildEntrance(float w, float d)
    {
        float mid = (w - 1f) * 0.5f;
        Box(mid, (d - 1f) * 0.5f, w - 0.2f, d - 0.2f, 0.1f, 0.02f, Asphalt);
        Box(mid, (d - 1f) * 0.5f + 1f, 6f, d - 2.2f, 0.12f, 0.02f, new Color(0.12f, 0.12f, 0.13f));

        for (float lz = 2.5f; lz < d - 5f; lz += 2.5f)
            Box(mid, lz, 0.18f, 1.2f, 0.14f, 0.02f, Stripe);

        for (int k = 0; k < 4; k++)   // пандус к тоннелю: поднимается к дальней стене
            Box(mid, d - 1.5f - k, 6f, 1f, 0.1f, 0.3f * (4 - k), new Color(0.20f, 0.20f, 0.21f));

        Box(mid, d - 0.55f, 6.6f, 0.4f, 1.3f, 2.3f, Dark);              // устье тоннеля
        Box(mid - 3.6f, d - 0.6f, 0.6f, 0.8f, 0.1f, 3.8f, Concrete);
        Box(mid + 3.6f, d - 0.6f, 0.6f, 0.8f, 0.1f, 3.8f, Concrete);
        Box(mid, d - 0.6f, 7.8f, 0.8f, 3.6f, 0.4f, Concrete);
        Label("ТОННЕЛЬ НА ПОВЕРХНОСТЬ", new Vector2(6f, 1.2f), Local(mid, d - 6.5f) + Vector3.up * 0.15f, Stripe, 6f);

        for (float lz = 3f; lz < d - 5f; lz += 2f)   // стоянка по бокам
        {
            Box(2.5f, lz, 3.4f, 0.1f, 0.12f, 0.02f, Color.white);
            Box(w - 3.5f, lz, 3.4f, 0.1f, 0.12f, 0.02f, Color.white);
        }

        Box(2.5f, 4f, 2.2f, 1.2f, 0.12f, 1f, new Color(0.25f, 0.32f, 0.26f));        // машины охраны
        Box(w - 3.5f, 6f, 2.2f, 1.2f, 0.12f, 1f, new Color(0.30f, 0.30f, 0.34f));
    }

    /// <summary>Пост: столы с пультами по числу мест.</summary>
    /// <summary>Камера стабильного гуманоида — жилая комната: кровать, стол со стулом, ковёр, полка. Без пультовой и стекла.</summary>
    private void BuildComfort(float w, float d)
    {
        var wood = new Color(0.55f, 0.40f, 0.26f);
        var linen = new Color(0.86f, 0.86f, 0.80f);
        var rug = new Color(0.55f, 0.30f, 0.32f);

        Box(w - 1.6f, d - 1.9f, 1.6f, 2.6f, 0.1f, 0.35f, wood);    // кровать у дальней стены
        Box(w - 1.6f, d - 1.6f, 1.4f, 1.9f, 0.45f, 0.08f, linen);  // постель
        Box(w - 1.6f, d - 0.75f, 1.4f, 0.45f, 0.45f, 0.14f, linen); // подушка
        Box(0.4f, d - 1.2f, 1.2f, 0.8f, 0.1f, 0.6f, wood);         // стол
        Box(0.4f, d - 2.1f, 0.45f, 0.45f, 0.1f, 0.4f, wood);       // стул
        Box(0.1f, 1.2f, 0.4f, 1.6f, 0.1f, 1.1f, wood);             // книжная полка
        Box((w - 1f) * 0.5f, (d - 1f) * 0.5f, 2.2f, 1.6f, 0.1f, 0.02f, rug);
    }

    private void BuildPost(BuildingDefinition def, float w, float d)
    {
        int seats = Mathf.Max(1, def.Capacity);
        int columns = Mathf.Max(1, Mathf.FloorToInt(w - 1f));
        float startX = (w - columns) * 0.5f;
        for (int k = 0; k < seats; k++)
        {
            float lx = startX + (k % columns);
            float lz = 1.3f + (k / columns) * 1.4f;
            if (lz > d - 0.8f) break;
            Box(lx, lz, 0.75f, 0.5f, 0.1f, 0.45f, new Color(0.35f, 0.33f, 0.30f));
            Box(lx, lz + 0.12f, 0.5f, 0.06f, 0.55f, 0.3f, new Color(0.15f, 0.45f, 0.40f));
        }
    }

    private void BuildPump(float w, float d)
    {
        Part(PrimitiveType.Cylinder, Local(w * 0.3f, d * 0.55f) + Vector3.up * 0.6f, new Vector3(1.1f, 0.5f, 1.1f), new Color(0.35f, 0.45f, 0.55f));
        Part(PrimitiveType.Cylinder, Local(w * 0.65f, d * 0.55f) + Vector3.up * 0.6f, new Vector3(1.1f, 0.5f, 1.1f), new Color(0.35f, 0.45f, 0.55f));
        Box((w - 1f) * 0.5f, d - 1f, w - 0.8f, 0.3f, 0.6f, 0.3f, new Color(0.30f, 0.35f, 0.40f));   // труба
    }

    private void BuildPad(BuildingDefinition def, float w, float d)
    {
        float mid = (w - 1f) * 0.5f;
        float midZ = (d - 1f) * 0.5f;
        Box(mid, midZ, w - 0.2f, d - 0.2f, 0.1f, 0.02f, Asphalt);

        if (def.Id == "helipad")
        {
            Part(PrimitiveType.Cylinder, Local(mid, midZ) + Vector3.up * 0.13f, new Vector3(w * 0.7f, 0.01f, w * 0.7f), Stripe);
            Part(PrimitiveType.Cylinder, Local(mid, midZ) + Vector3.up * 0.14f, new Vector3(w * 0.62f, 0.01f, w * 0.62f), Asphalt);
            Box(mid - 1.2f, midZ, 0.4f, 3f, 0.15f, 0.02f, Color.white);
            Box(mid + 1.2f, midZ, 0.4f, 3f, 0.15f, 0.02f, Color.white);
            Box(mid, midZ, 2.4f, 0.4f, 0.15f, 0.02f, Color.white);
            return;
        }

        for (float lx = 1f; lx < w - 2f; lx += 2.5f)   // места под машины
        {
            Box(lx, d - 3f, 0.1f, 4f, 0.12f, 0.02f, Color.white);
            Box(lx + 1.25f, d - 3f, 1.6f, 3f, 0.12f, 1.1f, new Color(0.26f, 0.32f, 0.26f));
        }
    }

    private void BuildShaft(Vector2Int size, Vector3 center)
    {
        float side = Mathf.Max(1f, Mathf.Min(size.x, size.y) - 1.4f);
        Part(PrimitiveType.Cube, center + Vector3.up * 0.11f, new Vector3(side, 0.02f, side), Dark);
        Part(PrimitiveType.Cube, center + Vector3.up * 0.6f, new Vector3(side * 0.7f, 1f, side * 0.7f), new Color(0.75f, 0.76f, 0.78f));
    }

    private void BuildExit(BuildingDefinition def, float w, float d)
    {
        float mid = (w - 1f) * 0.5f;
        if (def.Id == "vehicle_entrance")
        {
            float road = Mathf.Min(4f, w - 1f);
            Box(mid, (d - 1f) * 0.5f, road, d - 0.4f, 0.12f, 0.02f, new Color(0.12f, 0.12f, 0.13f));
            for (int k = 0; k < 4; k++)
                Box(mid, d - 1.5f - k, road, 1f, 0.1f, 0.3f * (4 - k), new Color(0.20f, 0.20f, 0.21f));
            Box(mid, d - 0.55f, road + 0.4f, 0.4f, 1.3f, 2.3f, Dark);
            return;
        }

        for (int i = 0; i < 3; i++)   // лестница наверх у дальней стены
            Box(mid, d - 1f - i * 0.5f, Mathf.Max(1f, w - 1.5f), 0.45f, 0.1f, 0.12f * (3 - i) + 0.1f, new Color(0.45f, 0.45f, 0.48f));
    }

    private void BuildCheckpoint(BuildingDefinition def, Vector2Int size, Vector3 center, float height)
    {
        bool alongX = size.x >= size.y;
        float length = alongX ? size.x : size.y;
        var axis = alongX ? Vector3.right : Vector3.forward;
        var booth = alongX ? new Vector3(0.8f, height, size.y * 0.6f) : new Vector3(size.x * 0.6f, height, 0.8f);

        Part(PrimitiveType.Cube, center + Vector3.up * 0.04f, new Vector3(size.x - 0.1f, 0.06f, size.y - 0.1f), def.Color * 0.8f);
        Part(PrimitiveType.Cube, center - axis * (length * 0.5f - 0.4f) + Vector3.up * height * 0.5f, booth, def.Color);
        Part(PrimitiveType.Cube, center + axis * (length * 0.5f - 0.4f) + Vector3.up * height * 0.5f, booth, def.Color);
        Part(PrimitiveType.Cube, center + Vector3.up * 1f,
            alongX ? new Vector3(length - 1.6f, 0.1f, 0.1f) : new Vector3(0.1f, 0.1f, length - 1.6f), new Color(0.9f, 0.2f, 0.2f));
    }

    private void BuildTesla(BuildingDefinition def, Vector2Int size, Vector3 center, float height)
    {
        bool alongX = size.x >= size.y;
        float length = alongX ? size.x : size.y;
        var axis = alongX ? Vector3.right : Vector3.forward;
        var pillar = new Vector3(0.4f, height + 0.6f, 0.4f);

        Part(PrimitiveType.Cube, center - axis * (length * 0.5f - 0.25f) + Vector3.up * pillar.y * 0.5f, pillar, new Color(0.25f, 0.25f, 0.28f));
        Part(PrimitiveType.Cube, center + axis * (length * 0.5f - 0.25f) + Vector3.up * pillar.y * 0.5f, pillar, new Color(0.25f, 0.25f, 0.28f));
        Part(PrimitiveType.Cube, center + Vector3.up * (pillar.y - 0.15f),
            alongX ? new Vector3(length, 0.3f, 0.5f) : new Vector3(0.5f, 0.3f, length), new Color(0.25f, 0.25f, 0.28f));
        Part(PrimitiveType.Cube, center + Vector3.up * 0.05f,
            alongX ? new Vector3(length - 0.8f, 0.04f, 0.3f) : new Vector3(0.3f, 0.04f, length - 0.8f), def.Color);
    }

    private void BuildDevice(BuildingDefinition def, Vector3 center)
    {
        Part(PrimitiveType.Cube, center + Vector3.up * 0.04f, new Vector3(0.8f, 0.06f, 0.8f), def.Color * 0.7f);
        Part(PrimitiveType.Cube, center + Vector3.up * 0.9f, new Vector3(0.12f, 1.6f, 0.12f), new Color(0.3f, 0.3f, 0.32f));
        Part(PrimitiveType.Cube, center + Vector3.up * 1.75f, new Vector3(0.4f, 0.3f, 0.4f), def.Color);
    }

    private void DoorMark(Vector3 cell, float height)
    {
        var mark = new GameObject("Сюда коридор");
        mark.transform.SetParent(transform, false);

        var tile = Part(PrimitiveType.Cube, cell + Vector3.up * 0.03f, new Vector3(0.9f, 0.06f, 0.9f), Red);
        tile.transform.SetParent(mark.transform, true);

        // ромб над клеткой: плитку на полу может закрыть дальняя стена
        var beacon = Part(PrimitiveType.Cube, cell + Vector3.up * (height + 0.8f), Vector3.one * 0.45f, Red);
        beacon.transform.rotation = Quaternion.Euler(45f, 0f, 45f);
        beacon.transform.SetParent(mark.transform, true);

        mark.SetActive(false);
        _doorMarks.Add(mark);
    }

    // ─────────────────────── кирпичики ───────────────────────

    private GameObject Part(PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());   // клики идут через сетку, физика не нужна
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.transform.localScale = scale;

        var renderer = go.GetComponent<Renderer>();
        renderer.sharedMaterial = _config.BaseMaterial;
        if (_block == null) _block = new MaterialPropertyBlock();
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);
        renderer.SetPropertyBlock(_block);
        return go;
    }

    private TextMeshPro Label(string text, Vector2 size, Vector3 position, Color color, float maxSize)
    {
        var go = new GameObject("Табличка");
        var label = go.AddComponent<TextMeshPro>();   // сначала компонент: он превращает Transform в RectTransform
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        label.rectTransform.sizeDelta = size;
        label.text = text;
        label.color = color;
        label.richText = true;
        label.alignment = TextAlignmentOptions.Center;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = maxSize;
        return label;
    }
}
