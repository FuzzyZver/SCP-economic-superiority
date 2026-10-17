using UnityEngine;

// ═══ EVENT ═══ одно-кадровые сообщения. Система, которая событие СОЗДАЁТ,
// стоит в EcsInclude раньше той, что его читает. Исключение — события из колбэков UI:
// они рождаются между кадрами и доживают до любой системы.

// ─────────── ввод ───────────

public struct CameraPanInputEvent { public Vector2 Value; }     // x — вправо, y — вперёд
public struct CameraRotateInputEvent { public int Direction; } // -1 / +1, шаг 90°
public struct CameraZoomInputEvent { public float Value; }     // знак колеса
public struct ClickInputEvent { public Vector2 ScreenPosition; }   // ЛКМ нажата не над UI
public struct ReleaseInputEvent { }                                 // ЛКМ отпущена где угодно
public struct CancelInputEvent { }                                  // ПКМ или Esc
public struct RotateInputEvent { }                                  // R
public struct FloorInputEvent { public int Delta; }                 // +1 глубже (Z), −1 выше (C)
public struct ToggleBuildMenuEvent { }                              // B или кнопка «Строительство»

// ─────────── время ───────────

public struct SetSpeedEvent { public int Index; }
public struct TogglePauseEvent { }
public struct DayStartedEvent { public int Day; }
public struct MonthStartedEvent { public int Month; }

// ─────────── Зона ───────────

public struct MoneyChangeEvent
{
    public int Delta;
    public string Reason;
}

public struct TrustChangeEvent
{
    public float Delta;
    public string Reason;
    public bool Incident;   // сбрасывает отсчёт спокойных дней
}

public struct DeclassChangeEvent
{
    public float Delta;
    public string Reason;
}

public struct XpEvent
{
    public int Amount;
    public string Reason;
}

public struct LevelUpEvent { public int Level; }
public struct TrustLevelUpEvent { public int Level; }

public struct LogEvent { public string Text; }
public struct GameOverEvent { public string Reason; }
public struct RestartEvent { }

// ─────────── SCP ───────────

public struct ScpClickedEvent { public ScpId Id; }
public struct CloseDocumentEvent { }
public struct RenameScpEvent { public string Name; }               // для открытого документа
public struct AssignClassEvent { public ObjectClass Class; }       // для открытого документа

/// <summary>Прирост изученности. Шлют и наблюдения, и (позже) испытания с инцидентами —
/// пороги разделов, опыт и завершение документа считает одна ScpResearchSystem.</summary>
public struct ResearchGainEvent
{
    public ScpId Id;
    public float Amount;
    public string Source;
}

// ─────────── стройка ───────────

/// <summary>Выбор инструмента в меню или в списке объектов. Повторный выбор того же — снять инструмент.</summary>
public struct SelectToolEvent
{
    public BuildTool Tool;
    public int Building;    // индекс записи в BuildingsConfig.Buildings
    public int Corridor;
    public ScpId Scp;       // PlaceScp
}

public struct SelectTabEvent { public BuildingCategory Tab; }

/// <summary>Поставить постройку. Free — без денег и проверок уровня (стартовый въезд).</summary>
public struct PlaceBuildingEvent
{
    public int Def;
    public int Depth;
    public Vector2Int Origin;
    public int Rotation;
    public bool Free;
}

/// <summary>Проложить коридор от From до To буквой «Г».</summary>
public struct PlaceCorridorEvent
{
    public int Depth;
    public Vector2Int From;
    public Vector2Int To;
    public int Corridor;
    public bool Free;
}

public struct GridChangedEvent { }            // сетка изменилась — пересчитать связность
public struct ConnectivityChangedEvent { }    // связность пересчитана

public struct BuildingRemovedEvent
{
    public int Id;
    public int Def;
}

/// <summary>Поместить объект в камеру. Проверяет и записывает ContainmentSystem.</summary>
public struct ContainScpEvent
{
    public ScpId Id;
    public int RoomId;
}
