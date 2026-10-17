using UnityEngine;

// ═══ REF ═══ ссылки на объекты сцены

public struct TransformRef { public Transform Value; }

public struct BuildingRef { public RoomActor Value; }

/// <summary>Нижняя станция лифта: та же постройка, но на этаже ниже.</summary>
public struct LowerStopRef { public RoomActor Value; }
