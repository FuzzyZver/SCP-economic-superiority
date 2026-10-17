using Leopotam.Ecs;

/// <summary>Журнал Зоны: строка с отметкой времени, свежие сверху, хвост обрезается.</summary>
public class LogSystem : Injects, IEcsRunSystem
{
    private EcsFilter<LogEvent> _events;
    private EcsFilter<SiteFlag, LogAttribute, CalendarAttribute> _sites;

    public void Run()
    {
        if (_events.IsEmpty()) return;

        int max = GameConfig.SettingsConfig.LogLines;

        foreach (var s in _sites)
        {
            ref var log = ref _sites.Get2(s);
            ref var calendar = ref _sites.Get3(s);
            string stamp = $"<color=#8a8a8a>[Д{calendar.Day + 1} {calendar.HourOfDay:00}:00]</color> ";

            foreach (var e in _events)
            {
                string text = _events.Get1(e).Text;
                if (string.IsNullOrEmpty(text)) continue;
                log.Lines.Insert(0, stamp + text);
            }

            if (max > 0 && log.Lines.Count > max) log.Lines.RemoveRange(max, log.Lines.Count - max);

            UI.Hud.SetLog(log.Lines);
        }
    }
}
