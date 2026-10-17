using Leopotam.Ecs;

/// <summary>
/// Опыт и уровень директора. Уровень никогда не падает; новый уровень может дать
/// новый допуск — тогда документы перерисовываются, а часть ██████ становится текстом.
/// </summary>
public class DirectorSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<XpEvent> _gains;
    private EcsFilter<SiteFlag, DirectorAttribute> _sites;

    public void Run()
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var g in _gains)
        {
            int amount = _gains.Get1(g).Amount;
            if (amount <= 0) continue;

            foreach (var s in _sites)
            {
                ref var director = ref _sites.Get2(s);
                director.Xp += amount;

                while (director.Level < balance.MaxDirectorLevel && director.Xp >= balance.XpToReach(director.Level + 1))
                {
                    director.Level++;
                    _world.NewEntity().Get<LevelUpEvent>().Level = director.Level;

                    int clearance = balance.ClearanceFor(director.Level);
                    if (clearance > director.Clearance)
                    {
                        director.Clearance = clearance;
                        _sites.GetEntity(s).Get<DocumentChangedFlag>();
                        Log($"Уровень директора {director.Level}. Совет O5 выдал допуск {clearance}.");
                    }
                    else
                    {
                        Log($"Уровень директора {director.Level}.");
                    }
                }
            }
        }
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
