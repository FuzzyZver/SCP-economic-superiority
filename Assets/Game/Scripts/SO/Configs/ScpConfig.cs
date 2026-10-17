using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Документы объектов. Один SCP — одна запись: номер, настоящий класс, архетип,
/// разделы документа с порогами изученности и допуска.
///
/// ТЕКСТЫ — ЧЕРНОВИК своими словами. Перед релизом сверить с вики и заполнить
/// Author и SourceUrl: это обязательная атрибуция по CC BY-SA 3.0.
/// </summary>
[CreateAssetMenu(fileName = "ScpConfig", menuName = "Configs/ScpConfig")]
public class ScpConfig : ScriptableObject
{
    public string[] ClassNames = { "Безопасный", "Евклид", "Кетер" };
    public ScpDefinition[] Objects = DefaultObjects();

    public ScpDefinition Get(ScpId id) => Array.Find(Objects, o => o.Id == id);

    public string ClassName(ObjectClass objectClass)
    {
        int i = (int)objectClass;
        return ClassNames != null && i < ClassNames.Length ? ClassNames[i] : objectClass.ToString();
    }

    public string SectionHeading(DocumentSectionKind kind)
    {
        switch (kind)
        {
            case DocumentSectionKind.Containment: return "Особые условия содержания";
            case DocumentSectionKind.Description: return "Описание";
            default: return "Приложение";
        }
    }

    [ContextMenu("Добавить недостающие записи")]
    private void FillMissing()
    {
        var list = new List<ScpDefinition>(Objects ?? new ScpDefinition[0]);
        int added = 0;

        foreach (var def in DefaultObjects())
        {
            if (list.Exists(o => o.Id == def.Id)) continue;
            list.Add(def);
            added++;
        }

        Objects = list.ToArray();
        Debug.Log(added > 0 ? $"{name}: дописано объектов — {added}" : $"{name}: всё на месте", this);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Подтягивает из кода поля, которых не было в старом ассете (камера, ветки, вид объекта),
    /// не трогая тексты документов. Зовёт сборщик сцены после обновления.</summary>
    [ContextMenu("Обновить камеры и вид объектов из кода")]
    public void SyncFromDefaults()
    {
        if (Objects == null) return;

        var defaults = DefaultObjects();
        foreach (var current in Objects)
        {
            var source = Array.Find(defaults, d => d.Id == current.Id);
            if (source == null) continue;

            current.Containment = source.Containment;
            current.Stable = source.Stable;
            current.SpecialRoom = source.SpecialRoom;
            current.BranchA = source.BranchA;
            current.BranchB = source.BranchB;
            current.Shape = source.Shape;
            current.ShapeScale = source.ShapeScale;
            current.ShapeColor = source.ShapeColor;
        }
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Подходит ли камера такого типа объекту.</summary>
    public static bool Fits(ScpDefinition definition, BuildingDefinition room)
    {
        if (definition == null || room == null || room.Role != BuildingRole.Containment) return false;
        if (definition.Containment == ContainmentType.Special) return room.Id == definition.SpecialRoom;
        if (room.Containment == definition.Containment) return true;
        return definition.Stable && room.Containment == ContainmentType.StableHumanoid;
    }

    public string ContainmentName(ContainmentType type)
    {
        switch (type)
        {
            case ContainmentType.Item: return "предметы";
            case ContainmentType.Humanoid: return "гуманоиды";
            case ContainmentType.Creature: return "существа";
            case ContainmentType.Large: return "крупные";
            case ContainmentType.Special: return "особая камера";
            case ContainmentType.StableHumanoid: return "стабильные гуманоиды";
            default: return "—";
        }
    }

    [ContextMenu("Пересобрать с нуля")]
    private void ResetToDefaults()
    {
        Objects = DefaultObjects();
        Debug.LogWarning($"{name}: документы перезаписаны значениями из кода.", this);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    // ─────────────────────────  ОБЪЕКТЫ СРЕЗА  ─────────────────────────
    // Пороги: research — % изученности, clearance — минимальный допуск директора.
    // Директор стартует с допуском 3, поэтому разделы с 4 видны как ██████ и манят.

    private static ScpDefinition[] DefaultObjects() => new[]
    {
        new ScpDefinition {
            Id = ScpId.Scp134, Number = "SCP-134", Title = "Ребёнок со звёздными глазами",
            TrueClass = ObjectClass.Safe, Archetype = BehaviorArchetype.Cooperative,
            Containment = ContainmentType.Humanoid, Stable = true,
            BranchA = ScienceBranch.SpaceTime, BranchB = ScienceBranch.Biomedicine, Shape = PrimitiveType.Capsule,
            ShapeScale = new Vector3(0.45f, 0.6f, 0.45f), ShapeColor = new Color(0.55f, 0.70f, 0.95f),
            Sections = new[] {
                S(DocumentSectionKind.Containment, 0, 2,
                    "Объект содержится в жилой камере для гуманоидов. Игрушки и книги выдаются по согласованию с куратором. [ЧЕРНОВИК: сверить с вики]"),
                S(DocumentSectionKind.Description, 0, 2,
                    "Ребёнок, в глазах которого вместо радужки и зрачка видно звёздное небо. Спокоен, послушен и охотно идёт на контакт. [ЧЕРНОВИК]"),
                S(DocumentSectionKind.Description, 20, 3,
                    "За всё время содержания объект ни разу не проявил враждебности и не причинил вреда персоналу. [ЧЕРНОВИК]"),
                S(DocumentSectionKind.Addendum, 60, 4,
                    "Протоколы наблюдений за картиной звёзд в глазах объекта. [ЧЕРНОВИК: заполнить по вики]"),
            },
        },

        new ScpDefinition {
            Id = ScpId.Scp207, Number = "SCP-207", Title = "Бутылки с «Кока-Колой»",
            TrueClass = ObjectClass.Safe, Archetype = BehaviorArchetype.Resource,
            Containment = ContainmentType.Item, BranchA = ScienceBranch.Biomedicine, BranchB = ScienceBranch.Physics,
            Shape = PrimitiveType.Cylinder,
            ShapeScale = new Vector3(0.18f, 0.22f, 0.18f), ShapeColor = new Color(0.35f, 0.08f, 0.06f),
            Sections = new[] {
                S(DocumentSectionKind.Containment, 0, 2,
                    "Бутылки хранятся в запираемом шкафу хранилища предметов. Употреблять содержимое — только с разрешения научного руководителя."),
                S(DocumentSectionKind.Description, 0, 2,
                    "Набор стеклянных бутылок «Кока-Колы» обычного вида. На вкус напиток не отличается от оригинала."),
                S(DocumentSectionKind.Description, 15, 2,
                    "Напиток вызывает сильный прилив сил: у испытуемого растут скорость и выносливость. [ЧЕРНОВИК]"),
                S(DocumentSectionKind.Addendum, 40, 3,
                    "Повторное употребление усиливает эффект, но вредит организму. Испытания проводятся только под медицинским контролем. [ЧЕРНОВИК: сверить с вики]"),
            },
        },

        new ScpDefinition {
            Id = ScpId.Scp546, Number = "SCP-546", Title = "Блокнот",
            TrueClass = ObjectClass.Safe, Archetype = BehaviorArchetype.PassiveItem,
            Containment = ContainmentType.Item, BranchA = ScienceBranch.Memetics, BranchB = ScienceBranch.Biomedicine,
            Shape = PrimitiveType.Cube, ShapeScale = new Vector3(0.35f, 0.06f, 0.5f), ShapeColor = new Color(0.85f, 0.82f, 0.70f),
            Sections = new[] {
                S(DocumentSectionKind.Containment, 0, 2, "Блокнот хранится в сейфе хранилища предметов. [ЧЕРНОВИК: ОУС по вики]"),
                S(DocumentSectionKind.Description, 0, 2, "Блокнот. [ЧЕРНОВИК: описание по вики]"),
                S(DocumentSectionKind.Description, 20, 3, "[ЧЕРНОВИК: свойства, которые открывает изучение]"),
                S(DocumentSectionKind.Addendum, 50, 3, "[ЧЕРНОВИК: протоколы испытаний]"),
            },
        },

        new ScpDefinition {
            Id = ScpId.Scp173, Number = "SCP-173", Title = "Скульптура",
            TrueClass = ObjectClass.Euclid, Archetype = BehaviorArchetype.Observed,
            Author = "Moto42",
            Containment = ContainmentType.Creature, BranchA = ScienceBranch.Physics, BranchB = ScienceBranch.Chemistry,
            Shape = PrimitiveType.Cube,
            ShapeScale = new Vector3(0.55f, 1.8f, 0.45f), ShapeColor = new Color(0.55f, 0.48f, 0.40f),
            Sections = new[] {
                S(DocumentSectionKind.Containment, 0, 2,
                    "Объект содержится в запертом контейнере. Входить в камеру — не меньше чем втроём. Объект всё время держат в поле зрения, а перед морганием предупреждают коллег."),
                S(DocumentSectionKind.Description, 0, 2,
                    "Статуя из бетона и арматуры со следами аэрозольной краски. Объект живой и крайне враждебен."),
                S(DocumentSectionKind.Description, 10, 2,
                    "Пока на объект смотрят, он неподвижен. Стоит потерять его из виду даже на миг — он перемещается с огромной скоростью."),
                S(DocumentSectionKind.Containment, 25, 3,
                    "Раз в две недели камеру очищают от бурой субстанции неизвестного происхождения. Уборка — только бригадой с постоянным визуальным контактом."),
                S(DocumentSectionKind.Addendum, 50, 3,
                    "Жертвы погибают от перелома шеи или удушения. [ЧЕРНОВИК: отчёты об инцидентах]"),
            },
        },

        new ScpDefinition {
            Id = ScpId.Scp049, Number = "SCP-049", Title = "Чумной доктор",
            TrueClass = ObjectClass.Euclid, Archetype = BehaviorArchetype.Cooperative,
            ObservationRate = 0.8f,
            Containment = ContainmentType.Humanoid, BranchA = ScienceBranch.Chemistry, BranchB = ScienceBranch.Biomedicine,
            Shape = PrimitiveType.Capsule, ShapeScale = new Vector3(0.7f, 1f, 0.7f), ShapeColor = new Color(0.10f, 0.10f, 0.11f),
            Sections = new[] {
                S(DocumentSectionKind.Containment, 0, 2,
                    "Объект содержится в стандартной камере для гуманоидов. Прямой контакт с кожей объекта запрещён."),
                S(DocumentSectionKind.Description, 0, 2,
                    "Гуманоид ростом около 1,9 м, внешне похожий на средневекового чумного доктора. Разумен, вежлив, говорит с персоналом."),
                S(DocumentSectionKind.Description, 15, 3,
                    "Прикосновение объекта смертельно. Объект убеждён, что борется с «Поветрием», и проводит над телами жертв операции."),
                S(DocumentSectionKind.Addendum, 35, 3,
                    "После операций тела оживают и становятся враждебными. Такие экземпляры обозначаются SCP-049-2."),
                S(DocumentSectionKind.Addendum, 60, 4,
                    "Интервью: объект охотнее сотрудничает, когда ему позволяют «лечить». Предложение использовать это в исследованиях передано в Совет O5. [ЧЕРНОВИК]"),
            },
        },
    };

    private static DocumentSection S(DocumentSectionKind kind, float research, int clearance, string text) =>
        new DocumentSection { Kind = kind, RequiredResearch = research, RequiredClearance = clearance, Text = text };
}

[Serializable]
public class ScpDefinition
{
    public ScpId Id;
    public string Number;                 // «SCP-173»
    public string Title;                  // каноническое имя; кличку даёт игрок
    public ObjectClass TrueClass;         // игроку не показывается
    public BehaviorArchetype Archetype;
    public float ObservationRate = 1f;    // множитель к наблюдениям: кто-то раскрывается медленнее
    public ContainmentType Containment = ContainmentType.Item;   // в какую камеру его можно поместить
    public bool Stable;                   // гуманоид, которого можно держать в камере стабильных гуманоидов
    public string SpecialRoom;            // Id особой камеры из BuildingsConfig — только для Containment = Special

    [Header("Какие дисциплины ускоряет")]
    public ScienceBranch BranchA;
    public ScienceBranch BranchB;

    [Header("Вид, пока нет модели")]
    public PrimitiveType Shape = PrimitiveType.Capsule;
    public Vector3 ShapeScale = new Vector3(0.6f, 0.9f, 0.6f);
    public Color ShapeColor = Color.gray;

    [Header("Атрибуция CC BY-SA 3.0")]
    public string Author;
    public string SourceUrl;

    public DocumentSection[] Sections;
}

[Serializable]
public class DocumentSection
{
    public DocumentSectionKind Kind;
    public string Heading;                       // пусто — заголовок по виду раздела
    [Range(0f, 100f)] public float RequiredResearch;
    [Range(0, 5)] public int RequiredClearance;
    [TextArea(2, 8)] public string Text;
}
