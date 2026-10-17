using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Доверие O5. Растёт само в спокойные дни и при низком рассекречивании,
/// меняется событиями, держится между полом и потолком (потолок — от уровня директора).
/// Ниже порога включается испытательный срок; не исправился вовремя — финансирование урезают.
/// </summary>
public class TrustSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<TrustChangeEvent> _changes;
    private EcsFilter<DayStartedEvent> _days;
    private EcsFilter<SiteFlag, TrustAttribute, CalendarAttribute, DeclassificationAttribute, DirectorAttribute>
        .Exclude<GameOverFlag> _sites;

    public void Run()
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var s in _sites)
        {
            ref var trust = ref _sites.Get2(s);
            ref var calendar = ref _sites.Get3(s);
            ref var declass = ref _sites.Get4(s);
            ref var director = ref _sites.Get5(s);
            var site = _sites.GetEntity(s);

            trust.Ceiling = balance.CeilingFor(director.Level);

            foreach (var d in _days)
            {
                int day = _days.Get1(d).Day;

                if (day - trust.LastIncidentDay >= balance.QuietDaysForGrowth)
                    trust.Value += balance.PassiveTrustPerDay;

                if (declass.Value < balance.LowDeclassThreshold)
                    trust.Value += balance.LowDeclassTrustPerDay;

                CheckDeadline(site, day);
            }

            foreach (var c in _changes)
            {
                ref var change = ref _changes.Get1(c);
                trust.Value += change.Delta;
                if (change.Incident) trust.LastIncidentDay = calendar.Day;

                if (!string.IsNullOrEmpty(change.Reason))
                    Log($"Доверие O5 {(change.Delta >= 0f ? "+" : "−")}{Mathf.Abs(change.Delta):0.#}: {change.Reason}");
            }

            trust.Value = Mathf.Clamp(trust.Value, trust.Floor, trust.Ceiling);

            UpdateLevel(ref trust);
            UpdateProbation(site, trust.Value, calendar.Day);
        }
    }

    private void UpdateLevel(ref TrustAttribute trust)
    {
        trust.Level = GameConfig.BalanceConfig.TrustLevelFor(trust.Value);
        if (trust.Level <= trust.BestLevel) return;

        trust.BestLevel = trust.Level;
        _world.NewEntity().Get<TrustLevelUpEvent>().Level = trust.Level;
        Log($"Совет O5 повысил уровень доверия к Зоне до {trust.Level}.");
    }

    private void UpdateProbation(EcsEntity site, float value, int day)
    {
        var balance = GameConfig.BalanceConfig;
        bool low = value < balance.ProbationBelow;

        if (low && !site.Has<ProbationAttribute>() && !site.Has<FundingCutFlag>())
        {
            site.Get<ProbationAttribute>().DeadlineDay = day + balance.ProbationDays;
            Log($"Совет O5 назначил испытательный срок: {balance.ProbationDays} дн., чтобы вернуть доверие выше {balance.ProbationBelow:0}.");
            return;
        }

        if (low) return;

        if (site.Has<ProbationAttribute>())
        {
            site.Del<ProbationAttribute>();
            Log("Испытательный срок снят. Совет O5 доволен.");
        }

        if (site.Has<FundingCutFlag>())
        {
            site.Del<FundingCutFlag>();
            Log("Совет O5 вернул полное финансирование.");
        }
    }

    private void CheckDeadline(EcsEntity site, int day)
    {
        if (!site.Has<ProbationAttribute>()) return;
        if (day < site.Get<ProbationAttribute>().DeadlineDay) return;

        site.Del<ProbationAttribute>();
        site.Get<FundingCutFlag>();
        Log($"Испытательный срок провален. Финансирование урезано на {GameConfig.BalanceConfig.FundingCutPercent}%.");
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
