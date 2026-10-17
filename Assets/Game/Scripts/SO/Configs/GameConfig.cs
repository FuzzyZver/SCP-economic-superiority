using UnityEngine;

[CreateAssetMenu(fileName = "GameConfig", menuName = "Configs/GameConfig")]
public class GameConfig : ScriptableObject
{
    public BalanceConfig BalanceConfig;
    public ScpConfig ScpConfig;
    public BuildingsConfig BuildingsConfig;
    public SettingsConfig SettingsConfig;
}
