/// <summary>
/// Состояние, которое живёт дольше партии: настройки и всё, что переживёт перезапуск.
/// Кладётся через .Inject(new RealtimeData()), поэтому не статика.
/// </summary>
public class RealtimeData
{
    public float MasterVolume = 1f;
    public float MusicVolume = 1f;
    public float SfxVolume = 1f;
}
