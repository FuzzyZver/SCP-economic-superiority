using Leopotam.Ecs;

/// <summary>
/// Месячный отчёт: финансирование O5 приходит, зарплаты, содержание объектов и комплекса уходят.
/// Сама касса меняется только в TreasurySystem — здесь лишь события с причинами,
/// чтобы каждая строчка бюджета была видна в журнале.
/// </summary>
public class EconomySystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<MonthStartedEvent> _months;
    private EcsFilter<SiteFlag, TrustAttribute>.Exclude<GameOverFlag> _sites;
    private EcsFilter<ScpFlag, ClassificationAttribute> _scps;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    public void Run()
    {
        foreach (var m in _months)
            foreach (var s in _sites)
                Report(s, _months.Get1(m).Month);
    }

    private void Report(int siteIndex, int month)
    {
        var balance = GameConfig.BalanceConfig;
        ref var trust = ref _sites.Get2(siteIndex);
        bool cut = _sites.GetEntity(siteIndex).Has<FundingCutFlag>();

        float funding = balance.BaseFunding + trust.Value * balance.FundingPerTrust;
        if (cut) funding *= 1f - balance.FundingCutPercent / 100f;
        int income = (int)funding;

        int upkeep = 0;
        foreach (var i in _scps)
        {
            ref var classification = ref _scps.Get2(i);
            upkeep += balance.UpkeepFor(classification.IsAssigned, classification.Assigned);
        }

        int salaries = balance.MonthlyStaffSalaries;
        int complex = ComplexUpkeep(_sites.GetEntity(siteIndex));

        Money(income, cut ? "Финансирование O5 (урезано)" : "Финансирование O5");
        Money(-salaries, "Зарплаты персонала");
        if (upkeep > 0) Money(-upkeep, $"Содержание объектов ({_scps.GetEntitiesCount()})");
        if (complex > 0) Money(-complex, "Содержание комплекса");

        _world.NewEntity().Get<LogEvent>().Text =
            $"Отчёт O5 за месяц {month}: доход +{income:N0}, расходы −{salaries + upkeep + complex:N0} " +
            $"(персонал {salaries:N0}, объекты {upkeep:N0}, комплекс {complex:N0}).";
    }

    /// <summary>Постройки по их MonthlyUpkeep плюс каждый квадратный метр коридора.</summary>
    private int ComplexUpkeep(EcsEntity site)
    {
        var config = GameConfig.BuildingsConfig;
        float total = 0f;

        foreach (var b in _buildings)
        {
            var def = config.Def(_buildings.Get2(b).Def);
            if (def != null) total += def.MonthlyUpkeep;
        }

        if (!site.Has<GridAttribute>()) return (int)total;

        foreach (var floor in site.Get<GridAttribute>().Floors)
        {
            foreach (byte tier in floor.Corridor)
            {
                if (tier == 0) continue;
                var corridor = config.Corridor(tier - 1);
                if (corridor != null) total += corridor.UpkeepPerCell;
            }
        }

        return (int)total;
    }

    private void Money(int delta, string reason)
    {
        ref var change = ref _world.NewEntity().Get<MoneyChangeEvent>();
        change.Delta = delta;
        change.Reason = reason;
    }
}
