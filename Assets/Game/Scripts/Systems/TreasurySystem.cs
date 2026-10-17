using Leopotam.Ecs;

/// <summary>Единственное место, где меняются деньги. Предупреждает, когда бюджет уходит в минус.</summary>
public class TreasurySystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<MoneyChangeEvent> _changes;
    private EcsFilter<SiteFlag, TreasuryAttribute> _sites;

    public void Run()
    {
        foreach (var c in _changes)
        {
            ref var change = ref _changes.Get1(c);

            foreach (var s in _sites)
            {
                ref var treasury = ref _sites.Get2(s);
                int before = treasury.Money;
                treasury.Money += change.Delta;

                if (before >= 0 && treasury.Money < 0)
                    _world.NewEntity().Get<LogEvent>().Text = "Бюджет Зоны ушёл в минус. O5 это заметит.";
            }
        }
    }
}
