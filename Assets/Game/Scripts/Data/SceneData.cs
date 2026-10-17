using UnityEngine;

/// <summary>
/// Точка доступа к объектам сцены из систем. Если у тебя тут уже есть свои поля —
/// не заменяй файл, а допиши недостающие.
/// </summary>
public class SceneData : MonoBehaviour
{
    [Header("Камера")]
    public Transform CameraRig;   // точка на полу, вокруг которой крутится взгляд; камера — её ребёнок
    public Camera Camera;         // ортографическая, наклонена вниз примерно на 30°

    [Header("Этажи")]
    public FloorActor[] Floors;   // индекс — глубина: 0 — поверхность, 1 — этаж −1…

    [Header("Стройка")]
    public GhostActor Ghost;      // призрак постройки под курсором
}
