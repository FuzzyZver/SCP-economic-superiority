using UnityEngine;

/// <summary>
/// Все числа экономики среза. Время, деньги, доверие, рассекречивание, директор, исследования.
/// Массивы по уровням читаются через методы снизу: выход за край берёт последнее значение,
/// так что уровни можно добавлять, не трогая код.
/// </summary>
[CreateAssetMenu(fileName = "BalanceConfig", menuName = "Configs/BalanceConfig")]
public class BalanceConfig : ScriptableObject
{
    [Header("Время")]
    public float HoursPerSecond = 1f;                          // 1 игровой час = 1 с при 1x
    public float[] Speeds = { 0.5f, 1f, 2f, 3f, 4f, 5f };
    public int DefaultSpeedIndex = 1;
    [Range(0, 23)] public int StartHour = 8;

    [Header("Деньги")]
    public int StartMoney = 60000;
    public int BaseFunding = 20000;                            // ежемесячно от O5
    public int FundingPerTrust = 300;                          // за каждое очко доверия
    [Range(0, 100)] public int FundingCutPercent = 30;         // после проваленного испытательного срока
    public int MonthlyStaffSalaries = 12000;                   // заглушка, пока нет персонала
    public int[] UpkeepByClass = { 1500, 4000, 10000 };        // Безопасный / Евклид / Кетер в месяц
    public int UnassignedUpkeep = 4000;                        // без класса содержим «на всякий случай»

    [Header("Доверие O5")]
    public float StartTrust = 40f;
    public float TrustFloor = 0f;
    public float[] TrustCeilingByLevel = { 60f, 70f, 80f, 90f, 100f };   // по уровню директора
    public float TrustPerLevel = 20f;                          // ширина одного уровня доверия
    public int QuietDaysForGrowth = 3;                         // столько дней без инцидентов — и доверие растёт само
    public float PassiveTrustPerDay = 0.3f;
    public float LowDeclassThreshold = 10f;
    public float LowDeclassTrustPerDay = 0.2f;
    public float ProbationBelow = 25f;
    public int ProbationDays = 45;                             // полтора месяца на исправление

    [Header("Рассекречивание")]
    public float DeclassMax = 100f;
    public float DeclassDecayPerDay = 1f;                      // при нуле; чем выше шкала, тем медленнее
    [Range(0f, 1f)] public float DeclassMinDecayFactor = 0.1f; // у самого края спад не останавливается совсем
    public float HighDeclassThreshold = 60f;
    public float HighDeclassTrustPerDay = 0.5f;

    [Header("Директор")]
    public int StartDirectorLevel = 1;
    /// <summary>XpForLevel[i] — сколько опыта всего нужно, чтобы стоять на уровне i + 1.</summary>
    public int[] XpForLevel = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };
    public int[] ClearanceByLevel = { 3, 3, 3, 4, 4, 4, 4, 5, 5, 5 };

    [Header("Исследования")]
    public float ObservationPerDay = 1.5f;                     // % изученности в день от наблюдений
    public float ObservationCap = 25f;                         // дальше наблюдения ничего не дают
    public int XpPerResearchPercent = 2;
    public float ClassifyMinResearch = 15f;                    // раньше классифицировать нечем
    public int ClassifyXp = 30;
    public float DocumentCompleteTrust = 5f;
    public int DocumentCompleteXp = 100;

    [Header("Исследовательские посты")]
    public int ResearchFreeDistance = 15;                      // до стольких метров от поста — полная скорость
    public int ResearchFalloffDistance = 60;                   // за столько метров сверху скорость падает до минимума
    [Range(0f, 1f)] public float ResearchMinFactor = 0.25f;
    public int ElevatorTravelCost = 10;                        // переезд на лифте считается как столько метров

    [Header("Содержание")]
    public float UncontainedDeclassPerDay = 0.3f;              // за каждый объект без камеры, в день
    public float RoomLostDeclass = 5f;                         // снесли камеру с объектом внутри

    [Header("Стройка")]
    [Range(0, 5)] public int StartViewDepth = 1;               // на каком этаже открывается игра

    // ─────────────────────── чтение массивов ───────────────────────

    public float Speed(int index) => Pick(Speeds, index, 1f);

    public int XpToReach(int level) => Pick(XpForLevel, level - 1, int.MaxValue);

    public int ClearanceFor(int level) => Pick(ClearanceByLevel, level - 1, 3);

    public float CeilingFor(int level) => Pick(TrustCeilingByLevel, level - 1, 100f);

    public int MaxDirectorLevel => XpForLevel != null ? XpForLevel.Length : 1;

    /// <summary>Множитель скорости исследований от пути поста до камеры по коридорам.</summary>
    public float ResearchFactor(int distance)
    {
        if (distance < 0) return 0f;
        if (distance <= ResearchFreeDistance) return 1f;
        float t = (distance - ResearchFreeDistance) / (float)Mathf.Max(1, ResearchFalloffDistance);
        return Mathf.Lerp(1f, ResearchMinFactor, Mathf.Clamp01(t));
    }

    public int TrustLevelFor(float trust) => TrustPerLevel > 0f ? Mathf.FloorToInt(trust / TrustPerLevel) : 0;

    public int UpkeepFor(bool assigned, ObjectClass objectClass) =>
        assigned ? Pick(UpkeepByClass, (int)objectClass, UnassignedUpkeep) : UnassignedUpkeep;

    private static T Pick<T>(T[] values, int index, T fallback)
    {
        if (values == null || values.Length == 0) return fallback;
        return values[Mathf.Clamp(index, 0, values.Length - 1)];
    }
}
