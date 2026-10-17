using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Шкала рассекречивания. Спадает сама, но чем она выше, тем медленнее: побег SCP
/// так быстро не забывают. Высокое значение каждый день бьёт по доверию O5,
/// максимум — отстранение директора.
/// Стоит раньше TrustSystem: суточный штраф к доверию должен дойти в тот же кадр.
/// </summary>
public class DeclassificationSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<DeclassChangeEvent> _changes;
    private EcsFilter<DayStartedEvent> _days;
    private EcsFilter<SiteFlag, DeclassificationAttribute>.Exclude<GameOverFlag> _sites;

    public void Run()
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var s in _sites)
        {
            ref var declass = ref _sites.Get2(s);

            foreach (var _ in _days) Daily(ref declass);

            foreach (var c in _changes)
            {
                ref var change = ref _changes.Get1(c);
                declass.Value = Mathf.Clamp(declass.Value + change.Delta, 0f, balance.DeclassMax);

                if (!string.IsNullOrEmpty(change.Reason))
                    _world.NewEntity().Get<LogEvent>().Text = $"Рассекречивание {Signed(change.Delta)}: {change.Reason}";
            }

            if (declass.Value >= balance.DeclassMax)
                _world.NewEntity().Get<GameOverEvent>().Reason =
                    "Рассекречивание достигло предела. Совет O5 отстранил вас от должности директора.";
        }
    }

    private void Daily(ref DeclassificationAttribute declass)
    {
        var balance = GameConfig.BalanceConfig;

        if (declass.Value >= balance.HighDeclassThreshold)
        {
            ref var penalty = ref _world.NewEntity().Get<TrustChangeEvent>();
            penalty.Delta = -balance.HighDeclassTrustPerDay;
            penalty.Reason = null;   // каждодневный штраф не засоряет журнал, его видно по шкале
        }

        if (declass.Value <= 0f) return;

        float factor = Mathf.Max(balance.DeclassMinDecayFactor, 1f - declass.Value / balance.DeclassMax);
        declass.Value = Mathf.Max(0f, declass.Value - balance.DeclassDecayPerDay * factor);
    }

    private static string Signed(float value) => value >= 0f ? $"+{value:0.#}" : $"−{-value:0.#}";
}
