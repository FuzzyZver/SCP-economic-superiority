using UnityEngine;

[CreateAssetMenu(fileName = "SettingsConfig", menuName = "Configs/SettingsConfig")]
public class SettingsConfig : ScriptableObject
{
    [Header("Камера")]
    public float PanSpeed = 14f;              // единиц в секунду при зуме 10
    public float RotateSharpness = 10f;       // чем больше, тем резче доворот на 90°
    public float ZoomStep = 1.5f;             // за один щелчок колеса
    public float ZoomSharpness = 12f;
    public float MinZoom = 4f;                // orthographicSize
    public float MaxZoom = 35f;
    public float StartZoom = 10f;

    [Header("Журнал")]
    public int LogLines = 8;
}
