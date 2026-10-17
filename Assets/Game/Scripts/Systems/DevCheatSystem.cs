#if UNITY_EDITOR
using UnityEngine.InputSystem;
using Leopotam.Ecs;

/// <summary>
/// Читы для проверки среза, только в редакторе. Шлют те же события, что и игра,
/// поэтому проверяют настоящие системы, а не обходной путь.
/// F1 — +10% изученности открытому объекту, F2 — +10 рассекречивания,
/// F3 — −10 доверия, F4 — +10 000 денег, F5 — +100 опыта, F6 — промотать день.
/// </summary>
public class DevCheatSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<SiteFlag, SelectedScpAttribute, CalendarAttribute> _sites;

    public void Run()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        foreach (var s in _sites)
        {
            ref var selected = ref _sites.Get2(s);

            if (keyboard.f1Key.wasPressedThisFrame && selected.HasTarget)
            {
                ref var gain = ref _world.NewEntity().Get<ResearchGainEvent>();
                gain.Id = selected.Id;
                gain.Amount = 10f;
                gain.Source = "чит";
            }

            if (keyboard.f2Key.wasPressedThisFrame)
            {
                ref var change = ref _world.NewEntity().Get<DeclassChangeEvent>();
                change.Delta = 10f;
                change.Reason = "чит";
            }

            if (keyboard.f3Key.wasPressedThisFrame)
            {
                ref var change = ref _world.NewEntity().Get<TrustChangeEvent>();
                change.Delta = -10f;
                change.Reason = "чит";
                change.Incident = true;
            }

            if (keyboard.f4Key.wasPressedThisFrame)
            {
                ref var change = ref _world.NewEntity().Get<MoneyChangeEvent>();
                change.Delta = 10000;
                change.Reason = "чит";
            }

            if (keyboard.f5Key.wasPressedThisFrame)
            {
                ref var xp = ref _world.NewEntity().Get<XpEvent>();
                xp.Amount = 100;
                xp.Reason = "чит";
            }

            if (keyboard.f6Key.wasPressedThisFrame)
                _sites.Get3(s).Hours += CalendarAttribute.HoursPerDay;   // TimeSystem заметит смену дня в следующем кадре
        }
    }
}
#endif
