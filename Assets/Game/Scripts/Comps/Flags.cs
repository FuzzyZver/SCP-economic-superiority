// ═══ FLAG ═══ пустые метки: кто это и в каком он состоянии

// кто это
public struct SiteFlag { }     // единственная сущность Зоны: время, деньги, доверие, директор
public struct ScpFlag { }
public struct CameraFlag { }
public struct BuildingFlag { }

// состояние постройки
public struct ConnectedFlag { }    // все входы постройки выходят в коридор, связанный с главным въездом
/// <summary>Постройку размыло водой. Ставит WaterSystem, сносит BuildSystem в следующем кадре.
/// Не одно-кадровый: живёт, пока постройку не уберут.</summary>
public struct CollapseFlag { }

// состояние Зоны
public struct GameOverFlag { }
public struct FundingCutFlag { }   // испытательный срок провален: финансирование урезано

/// <summary>Документ надо перерисовать. Намеренно НЕ одно-кадровый: его ставят и системы,
/// стоящие после ScpDocumentSystem (директор с новым допуском). Снимает сама ScpDocumentSystem.</summary>
public struct DocumentChangedFlag { }
