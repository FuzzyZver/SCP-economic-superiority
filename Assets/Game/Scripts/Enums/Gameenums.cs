/// <summary>Объекты. Новые дописывать только в конец: на номер завязаны ассеты.</summary>
public enum ScpId
{
    Scp134,   // Ребёнок со звёздными глазами
    Scp207,   // Бутылки с «Кока-Колой»
    Scp546,   // Блокнот
    Scp173,   // Скульптура
    Scp049    // Чумной доктор
}

/// <summary>Класс объекта. Порядок совпадает с кнопками в документе и с UpkeepByClass.</summary>
public enum ObjectClass
{
    Safe,     // Безопасный
    Euclid,   // Евклид
    Keter     // Кетер
}

/// <summary>Архетипы поведения из диздока. Дописывать только в конец.</summary>
public enum BehaviorArchetype
{
    Hunter,          // Охотник
    Observed,        // Наблюдаемый
    Triggered,       // Триггерный
    Aura,            // Аура
    Infection,       // Заражение
    Cooperative,     // Сотрудничающий
    Possession,      // Одержимость
    PassiveItem,     // Пассивный предмет
    Resource,        // Ресурсный
    Escapee,         // Беглец
    Cognitive,       // Когнитивная угроза
    Indestructible,  // Неуязвимый разрушитель
    Digital,         // Цифровой
    External,        // Внешний
    Cyclic           // Циклический
}

/// <summary>Из чего состоит документ объекта.</summary>
public enum DocumentSectionKind
{
    Containment,   // Особые условия содержания
    Description,   // Описание
    Addendum       // Приложение
}

// ─────────────────────────── стройка ───────────────────────────

/// <summary>Под какой тип камеры подходит объект. Особые камеры — под конкретный SCP.
/// Дописывать только в конец: номера лежат в ScpConfig.</summary>
public enum ContainmentType
{
    None,
    Item,            // предмет
    Humanoid,        // гуманоид
    Creature,        // существо
    Large,           // крупный
    Special,         // особый, своя камера под объект
    StableHumanoid   // стабильный гуманоид: комфорт, без постоянной слежки
}

/// <summary>Закладки панели стройки.</summary>
public enum BuildingCategory
{
    Containment,      // Содержание
    Research,         // Исследования
    Security,         // Безопасность
    Personnel,        // Персонал
    Infrastructure,   // Инфраструктура
    Administration,   // Администрация
    Communications    // Коммуникации: коридоры, лифты, выходы, связь
}

/// <summary>Что постройка делает в коде. Stub — заготовка: строится, но своей механики пока нет.</summary>
public enum BuildingRole
{
    Stub,
    MainEntrance,        // главный въезд, ставится сам
    SurfaceExit,         // аварийный выход, транспортный въезд, эвакуационный туннель
    PersonnelElevator,
    FreightElevator,
    HeavyElevator,
    Containment,         // камера содержания
    ResearchPost,        // учёные ведут привязанные камеры
    GuardPost,
    Pump,                // откачивает воду в радиусе
    Module               // ставится внутрь коридора
}

/// <summary>Как постройка выглядит, пока нет своих моделей.</summary>
public enum BuildingShape
{
    Room,
    Elevator,
    Exit,
    Checkpoint,
    TeslaGate,
    Cell,        // камера + пультовая за стеклом
    Entrance,    // зал главного въезда с дорогой и пандусом
    Post,        // пост: столы с пультами
    Pump,        // насосы и трубы
    Pad,         // площадка: автопарк, вертолётная
    Device       // малый модуль: турель, камера наблюдения, газ
}

/// <summary>Научные дисциплины: по лаборатории на каждую, каждый SCP ускоряет две.</summary>
public enum ScienceBranch
{
    None,
    Physics,       // аномальная физика
    Biomedicine,   // биомедицина
    Chemistry,     // химия и материаловедение
    Memetics,      // меметика и психология
    Engineering,   // инженерия
    Computing,     // вычисления
    SpaceTime,     // пространство-время
    Thaumaturgy    // тауматургия
}

/// <summary>Почему постройка не работает. Считает ConnectivitySystem.</summary>
public enum ConnectionIssue
{
    None,
    NoCorridor,        // перед входом нет связанного коридора
    NoControlAccess,   // к пультовой нет связанного коридора
    NarrowCorridor,    // коридор у входа уже, чем нужно
    ServiceTunnel,     // у входа только техтоннель
    NotAtEntrance      // должна выходить прямо в зал главного въезда
}

public enum BuildTool
{
    None,
    Corridor,
    Building,
    Demolish,
    PlaceScp,   // объект «в руках»: навести на свободную камеру или поставить новую
    Bind        // выбран исследовательский пост: клик по камере — привязать / отвязать
}
