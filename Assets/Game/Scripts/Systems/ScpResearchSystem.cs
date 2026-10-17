using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Единственное место, где растёт изученность. Неважно, откуда пришли данные —
/// наблюдения, испытание или инцидент: пороги разделов, опыт директора
/// и завершение документа считаются здесь одинаково.
/// </summary>
public class ScpResearchSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<ResearchGainEvent> _gains;
    private EcsFilter<ScpFlag, ScpAttribute, ResearchAttribute, NicknameAttribute> _scps;
    private EcsFilter<SiteFlag> _sites;

    public void Run()
    {
        foreach (var g in _gains)
        {
            ref var gain = ref _gains.Get1(g);
            if (gain.Amount <= 0f) continue;

            foreach (var i in _scps)
            {
                if (_scps.Get2(i).Id != gain.Id) continue;
                Apply(ref _scps.Get3(i), _scps.Get4(i).Value, gain);
                break;
            }
        }
    }

    private void Apply(ref ResearchAttribute research, string nickname, ResearchGainEvent gain)
    {
        var balance = GameConfig.BalanceConfig;
        var definition = GameConfig.ScpConfig.Get(gain.Id);
        string number = definition != null ? definition.Number : gain.Id.ToString();

        float before = research.Progress;
        float after = Mathf.Min(100f, before + gain.Amount);
        research.Progress = after;

        int percents = Mathf.FloorToInt(after) - Mathf.FloorToInt(before);
        if (percents > 0) Xp(percents * balance.XpPerResearchPercent, $"изучение {number}");

        if (definition != null && definition.Sections != null)
        {
            foreach (var section in definition.Sections)
            {
                if (section.RequiredResearch <= before || section.RequiredResearch > after) continue;
                Log($"{number} «{nickname}»: новые данные ({gain.Source}).");
                break;   // одна строка на прирост, даже если открылось два раздела
            }
        }

        if (before < 100f && after >= 100f)
        {
            ref var trust = ref _world.NewEntity().Get<TrustChangeEvent>();
            trust.Delta = balance.DocumentCompleteTrust;
            trust.Reason = $"документ {number} заполнен полностью";

            Xp(balance.DocumentCompleteXp, $"документ {number}");
        }

        foreach (var s in _sites) _sites.GetEntity(s).Get<DocumentChangedFlag>();
    }

    private void Xp(int amount, string reason)
    {
        ref var xp = ref _world.NewEntity().Get<XpEvent>();
        xp.Amount = amount;
        xp.Reason = reason;
    }

    private void Log(string text) => _world.NewEntity().Get<LogEvent>().Text = text;
}
