using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Единственное место, где двигается время. Календарь отвечает только за экономику:
/// дни и месяцы. Длительность действий в мире считается в секундах симуляции, не здесь.
/// Шлёт DayStarted и MonthStarted — на них висят все суточные и месячные расчёты.
/// </summary>
public class TimeSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<SetSpeedEvent> _speedRequests;
    private EcsFilter<TogglePauseEvent> _pauseRequests;
    private EcsFilter<SiteFlag, CalendarAttribute, TimeScaleAttribute>.Exclude<GameOverFlag> _sites;

    public void Run()
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var s in _sites)
        {
            ref var calendar = ref _sites.Get2(s);
            ref var time = ref _sites.Get3(s);

            foreach (var r in _speedRequests)
            {
                time.SpeedIndex = Mathf.Clamp(_speedRequests.Get1(r).Index, 0, balance.Speeds.Length - 1);
                time.Paused = false;   // выбрать скорость — значит захотеть, чтобы время шло
            }

            foreach (var _ in _pauseRequests) time.Paused = !time.Paused;

            if (!time.Paused)
                calendar.Hours += Time.deltaTime * balance.HoursPerSecond * balance.Speed(time.SpeedIndex);

            // Дни считаются от последнего разосланного, а не от прошлого кадра:
            // так ни один день не потеряется, даже если часы прыгнули (чит, загрузка).
            for (int day = calendar.LastDay + 1; day <= calendar.Day; day++)
            {
                _world.NewEntity().Get<DayStartedEvent>().Day = day;

                if (day % CalendarAttribute.DaysPerMonth == 0)
                    _world.NewEntity().Get<MonthStartedEvent>().Month = day / CalendarAttribute.DaysPerMonth;
            }

            calendar.LastDay = calendar.Day;
        }
    }
}
