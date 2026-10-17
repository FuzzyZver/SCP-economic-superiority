using System.Collections.Generic;

// ═══ ATTRIBUTE ═══ свойства сущности, живут всю партию

// ─────────── Зона ───────────

/// <summary>Игровое время в часах с начала кампании. Дни и месяцы считаются из часов,
/// а не хранятся отдельно — так они никогда не разойдутся.</summary>
public struct CalendarAttribute
{
    public const int HoursPerDay = 24;
    public const int DaysPerMonth = 30;

    public float Hours;
    public int LastDay;   // последний день, о котором уже разослан DayStartedEvent

    public int Day => (int)(Hours / HoursPerDay);          // с нуля
    public int Month => Day / DaysPerMonth;                  // с нуля
    public int DayOfMonth => Day % DaysPerMonth;             // с нуля
    public int HourOfDay => (int)Hours % HoursPerDay;
}

/// <summary>Скорость времени. Индекс в BalanceConfig.Speeds, пауза — отдельно,
/// чтобы снятие паузы возвращало ту скорость, что была.</summary>
public struct TimeScaleAttribute
{
    public int SpeedIndex;
    public bool Paused;
}

public struct TreasuryAttribute { public int Money; }

/// <summary>Доверие O5. Потолок растёт с уровнем директора, пол — исследованиями и комнатами.
/// BestLevel нужен, чтобы награда за уровень доверия выдавалась один раз, а не при каждом колебании.</summary>
public struct TrustAttribute
{
    public float Value;
    public float Floor;
    public float Ceiling;
    public int Level;
    public int BestLevel;
    public int LastIncidentDay;
}

/// <summary>Есть, пока идёт испытательный срок. Не успел вернуть доверие к DeadlineDay — срезают финансирование.</summary>
public struct ProbationAttribute { public int DeadlineDay; }

public struct DeclassificationAttribute { public float Value; }

public struct DirectorAttribute
{
    public int Level;
    public int Xp;          // всего за кампанию, не за уровень
    public int Clearance;   // 0..5
}

/// <summary>Чей документ открыт. Живёт в компоненте, а не в поле системы:
/// на выделение смотрят и документ, и читы, и кнопки UI.</summary>
public struct SelectedScpAttribute
{
    public bool HasTarget;
    public ScpId Id;
}

/// <summary>Журнал событий Зоны, свежие строки сверху.</summary>
public struct LogAttribute { public List<string> Lines; }

// ─────────── SCP ───────────

public struct ScpAttribute { public ScpId Id; }

/// <summary>Как объект зовут в твоей Зоне. Номер остаётся в конфиге, кличка — у игрока.</summary>
public struct NicknameAttribute { public string Value; }

/// <summary>Изученность объекта, 0..100. От неё зависит, какие разделы документа видны.</summary>
public struct ResearchAttribute { public float Progress; }

/// <summary>Класс, который назначил игрок. Настоящий лежит в ScpConfig и игроку не показывается.</summary>
public struct ClassificationAttribute
{
    public bool IsAssigned;
    public ObjectClass Assigned;
}

/// <summary>В какой камере сидит объект. Храним id постройки, а не сущность:
/// снесённая камера просто не найдётся. NoRoom — ждёт размещения.</summary>
public struct ContainedAttribute
{
    public const int NoRoom = -1;
    public int RoomId;

    public bool IsContained => RoomId != NoRoom;
}

// ─────────── стройка ───────────

/// <summary>Сетка всех этажей. Floors[0] — поверхность, Floors[1] — этаж −1 и так далее.</summary>
public struct GridAttribute
{
    public int Size;
    public FloorGrid[] Floors;
    public int NextId;
    public int NextCorridorId;
    public int Exits;   // сколько выходов на поверхность связано с коридорами
}

/// <summary>Постройка на сетке. Лифт занимает свои клетки на Depth и на Depth + 1.
/// Def — индекс записи в BuildingsConfig.Buildings. PostId — к какому исследовательскому посту
/// привязана камера (NoPost — ни к какому), PostDistance — путь от поста по коридорам, м.</summary>
public struct BuildingAttribute
{
    public const int NoPost = -1;

    public int Id;
    public int Def;
    public int Depth;
    public UnityEngine.Vector2Int Origin;   // левый нижний угол занятой области
    public int Rotation;                    // 0..3, шаг 90° по часовой
    public int Paid;                        // сколько заплатили — от этого считается возврат при сносе
    public float Condition;                 // прочность 0..1: в воде без насосной падает
    public ConnectionIssue Issue;           // почему не работает (считает ConnectivitySystem)
    public int PostId;
    public int PostDistance;                // −1 — до поста не дойти
}

/// <summary>Что игрок сейчас строит. Status — подсказка для меню стройки, собирает BuildSystem.</summary>
public struct BuildToolAttribute
{
    public BuildTool Tool;
    public int Building;             // индекс записи в BuildingsConfig.Buildings
    public int Corridor;
    public ScpId Scp;                // PlaceScp: кого размещаем
    public int BindPost;             // Bind: id выбранного исследовательского поста
    public int Rotation;
    public bool Dragging;
    public UnityEngine.Vector2Int DragStart;
    public bool DragAxisChosen;      // коридор: куда сначала повёл мышь — туда и первая часть «Г»
    public bool DragAlongZFirst;
    public string Status;
    public bool StatusIsError;
}

/// <summary>Курсор: экран от ввода, клетка — от GridPointerSystem на текущем этаже.</summary>
public struct PointerAttribute
{
    public UnityEngine.Vector2 Screen;
    public bool OverUI;
    public bool HasCell;
    public UnityEngine.Vector2Int Cell;
}

/// <summary>Какой этаж сейчас на экране.</summary>
public struct ViewFloorAttribute { public int Depth; }

/// <summary>Нижняя панель стройки: открыта ли и какая закладка.</summary>
public struct BuildMenuAttribute
{
    public bool Open;
    public BuildingCategory Tab;
}

// ─────────── камера ───────────

public struct CameraAttribute
{
    public float Yaw;
    public float TargetYaw;
    public float Zoom;
    public float TargetHeight;   // высота этажа, на который смотрим
}
