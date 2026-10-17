using UnityEngine;
using UnityEngine.Events;
using Leopotam.Ecs;

/// <summary>
/// Верхняя полоса. Тексты переписываются только когда цифра изменилась —
/// иначе TMP перестраивает меш каждый кадр на пустом месте.
/// </summary>
public class HudSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private EcsWorld _world;

    private EcsFilter<SiteFlag, CalendarAttribute, TimeScaleAttribute, TreasuryAttribute,
        TrustAttribute, DeclassificationAttribute> _sites;
    private EcsFilter<SiteFlag, DirectorAttribute> _directors;

    private UnityAction[] _speedHandlers;

    private int _hour = int.MinValue;
    private int _speedIndex = int.MinValue;
    private bool _paused;
    private int _money = int.MinValue;
    private int _trust = int.MinValue;
    private int _trustState = int.MinValue;
    private int _declass = int.MinValue;
    private int _xp = int.MinValue;

    public void Init()
    {
        var hud = UI.Hud;
        if (hud.PauseButton != null) hud.PauseButton.onClick.AddListener(RequestPause);

        var buttons = hud.SpeedButtons;
        if (buttons == null) return;

        _speedHandlers = new UnityAction[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            int index = i;
            _speedHandlers[i] = () => _world.NewEntity().Get<SetSpeedEvent>().Index = index;
            buttons[i].onClick.AddListener(_speedHandlers[i]);
        }
    }

    public void Destroy()
    {
        var hud = UI.Hud;
        if (hud.PauseButton != null) hud.PauseButton.onClick.RemoveListener(RequestPause);

        var buttons = hud.SpeedButtons;
        if (buttons == null || _speedHandlers == null) return;

        for (int i = 0; i < buttons.Length && i < _speedHandlers.Length; i++)
            if (buttons[i] != null && _speedHandlers[i] != null) buttons[i].onClick.RemoveListener(_speedHandlers[i]);
    }

    public void Run()
    {
        var balance = GameConfig.BalanceConfig;

        foreach (var s in _sites)
        {
            var site = _sites.GetEntity(s);
            ref var calendar = ref _sites.Get2(s);
            ref var time = ref _sites.Get3(s);
            ref var treasury = ref _sites.Get4(s);
            ref var trust = ref _sites.Get5(s);
            ref var declass = ref _sites.Get6(s);

            int hour = (int)calendar.Hours;
            if (_hour != hour)
            {
                _hour = hour;
                UI.Hud.SetDate(calendar.DayOfMonth + 1, calendar.Month + 1, calendar.HourOfDay);
            }

            if (_speedIndex != time.SpeedIndex || _paused != time.Paused)
            {
                _speedIndex = time.SpeedIndex;
                _paused = time.Paused;
                UI.Hud.SetSpeed(time.SpeedIndex, balance.Speed(time.SpeedIndex), time.Paused);
            }

            if (_money != treasury.Money)
            {
                _money = treasury.Money;
                UI.Hud.SetMoney(treasury.Money);
            }

            int daysLeft = site.Has<ProbationAttribute>()
                ? Mathf.Max(0, site.Get<ProbationAttribute>().DeadlineDay - calendar.Day)
                : -1;
            bool cut = site.Has<FundingCutFlag>();
            int trustValue = Mathf.RoundToInt(trust.Value * 10f);
            int trustState = daysLeft * 4 + (cut ? 2 : 0) + Mathf.RoundToInt(trust.Ceiling) * 1000;

            if (_trust != trustValue || _trustState != trustState)
            {
                _trust = trustValue;
                _trustState = trustState;
                UI.Hud.SetTrust(trust.Value, trust.Ceiling, trust.Level, cut, daysLeft, balance.ProbationBelow);
            }

            int declassValue = Mathf.RoundToInt(declass.Value * 10f);
            if (_declass != declassValue)
            {
                _declass = declassValue;
                UI.Hud.SetDeclass(declass.Value, balance.DeclassMax, balance.HighDeclassThreshold);
            }
        }

        foreach (var d in _directors)
        {
            ref var director = ref _directors.Get2(d);
            if (_xp == director.Xp) continue;
            _xp = director.Xp;

            int from = balance.XpToReach(director.Level);
            int to = director.Level < balance.MaxDirectorLevel ? balance.XpToReach(director.Level + 1) : from;
            float progress = to > from ? (float)(director.Xp - from) / (to - from) : 1f;

            UI.Hud.SetDirector(director.Level, director.Clearance, progress);
        }
    }

    private void RequestPause() => _world.NewEntity().Get<TogglePauseEvent>();
}
