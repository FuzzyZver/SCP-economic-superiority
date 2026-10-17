using System.Collections.Generic;
using UnityEngine.Events;
using Leopotam.Ecs;

/// <summary>
/// Панель стройки: кнопка «Строительство» (и B) открывает нижнюю панель с закладками категорий.
/// В закладке — карточки построек из BuildingsConfig (в «Коммуникациях» ещё и коридоры):
/// размер, цена, содержание, вместимость; закрытые уровнем — серые с причиной, заготовки помечены.
/// Ещё здесь полоса этажа: этаж, предупреждение о выходах и строка состояния от инструментов.
/// Всё переписывается только когда что-то поменялось.
/// </summary>
public class BuildMenuSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private struct Entry
    {
        public BuildTool Tool;
        public int Building;
        public int Corridor;
        public BuildingCategory Category;
        public string Label;
        public int Level;
    }

    private static readonly BuildingCategory[] Tabs =
    {
        BuildingCategory.Containment, BuildingCategory.Research, BuildingCategory.Security, BuildingCategory.Personnel,
        BuildingCategory.Infrastructure, BuildingCategory.Administration, BuildingCategory.Communications,
    };

    private EcsWorld _world;

    private EcsFilter<ToggleBuildMenuEvent> _toggles;
    private EcsFilter<SelectTabEvent> _tabSelects;
    private EcsFilter<SiteFlag, BuildToolAttribute, ViewFloorAttribute, GridAttribute, DirectorAttribute, BuildMenuAttribute> _sites;

    private readonly List<Entry> _entries = new List<Entry>();
    private UnityEngine.UI.Button[] _cards = new UnityEngine.UI.Button[0];
    private UnityEngine.UI.Button[] _tabs = new UnityEngine.UI.Button[0];
    private readonly List<UnityEngine.UI.Button> _subscribed = new List<UnityEngine.UI.Button>();
    private readonly List<UnityAction> _handlers = new List<UnityAction>();
    private int[] _state = new int[0];

    private int _floorKey = int.MinValue;
    private int _exits = int.MinValue;
    private int _menuKey = int.MinValue;
    private int _demolishKey = int.MinValue;
    private string _status;
    private bool _statusError;

    public void Init()
    {
        var view = UI.Build;
        if (view == null) return;

        var config = GameConfig.BuildingsConfig;

        for (int i = 0; i < config.Corridors.Length; i++)
        {
            var corridor = config.Corridors[i];
            _entries.Add(new Entry
            {
                Tool = BuildTool.Corridor, Corridor = i, Category = BuildingCategory.Communications, Level = corridor.RequiredDirectorLevel,
                Label = $"<b>{corridor.Name}</b>\n<size=78%>ширина {corridor.Width} м · {corridor.CostPerCell}/м²</size>" +
                        (corridor.ServiceOnly ? "\n<size=70%>только инфраструктура</size>" : string.Empty),
            });
        }

        for (int i = 0; i < config.Buildings.Length; i++)
        {
            var def = config.Buildings[i];
            if (!def.Buildable) continue;

            string variant = string.IsNullOrEmpty(def.Variant) ? string.Empty : $" <size=80%>({def.Variant})</size>";
            string extra = def.Capacity > 0 ? $"мест {def.Capacity}" : string.Empty;
            if (def.CorridorModule) extra = Join(extra, "модуль в коридор");
            if (def.Planned) extra = Join(extra, "<color=#9a8a60>функция позже</color>");

            _entries.Add(new Entry
            {
                Tool = BuildTool.Building, Building = i, Category = def.Category, Level = def.RequiredDirectorLevel,
                Label = $"<b>{def.Name}</b>{variant}\n<size=74%>{def.Size.x}×{def.Size.y} м · {def.Cost:N0} · {def.MonthlyUpkeep:N0}/мес</size>" +
                        (extra.Length > 0 ? $"\n<size=70%>{extra}</size>" : string.Empty),
            });
        }

        var names = new string[Tabs.Length];
        for (int t = 0; t < Tabs.Length; t++) names[t] = BuildingsConfig.CategoryName(Tabs[t]);
        _tabs = view.CreateTabs(names);
        for (int t = 0; t < _tabs.Length; t++)
        {
            var category = Tabs[t];
            Subscribe(_tabs[t], () => _world.NewEntity().Get<SelectTabEvent>().Tab = category);
        }

        _cards = view.CreateCards(_entries.Count);
        _state = new int[_cards.Length];
        for (int i = 0; i < _cards.Length; i++)
        {
            var entry = _entries[i];
            _state[i] = -1;
            Subscribe(_cards[i], () => Choose(entry));
        }

        Subscribe(view.ToggleButton, Toggle);
        Subscribe(view.DemolishButton, () => _world.NewEntity().Get<SelectToolEvent>().Tool = BuildTool.Demolish);
    }

    public void Destroy()
    {
        for (int i = 0; i < _subscribed.Count; i++)
            if (_subscribed[i] != null) _subscribed[i].onClick.RemoveListener(_handlers[i]);
    }

    public void Run()
    {
        var view = UI.Build;
        if (view == null) return;

        var config = GameConfig.BuildingsConfig;

        foreach (var s in _sites)
        {
            ref var tool = ref _sites.Get2(s);
            int depth = _sites.Get3(s).Depth;
            ref var grid = ref _sites.Get4(s);
            int level = _sites.Get5(s).Level;
            ref var menu = ref _sites.Get6(s);

            foreach (var _ in _toggles) menu.Open = !menu.Open;
            foreach (var t in _tabSelects)
            {
                menu.Tab = _tabSelects.Get1(t).Tab;
                menu.Open = true;
            }

            int menuKey = (menu.Open ? 1 : 0) + (int)menu.Tab * 2;
            if (_menuKey != menuKey)
            {
                bool tabChanged = _menuKey == int.MinValue || (_menuKey >> 1) != (int)menu.Tab;
                _menuKey = menuKey;
                view.SetOpen(menu.Open);
                for (int t = 0; t < Tabs.Length; t++) view.SetTab(t, Tabs[t] == menu.Tab);
                for (int i = 0; i < _cards.Length; i++) view.SetCardVisible(i, _entries[i].Category == menu.Tab);
                if (tabChanged) view.ScrollToTop();
            }

            for (int i = 0; i < _cards.Length; i++)
            {
                var entry = _entries[i];
                if (entry.Category != menu.Tab) continue;

                bool available = level >= entry.Level;
                bool selected = tool.Tool == entry.Tool
                    && (entry.Tool != BuildTool.Building || tool.Building == entry.Building)
                    && (entry.Tool != BuildTool.Corridor || tool.Corridor == entry.Corridor);

                int state = (available ? 1 : 0) + (selected ? 2 : 0);
                if (_state[i] == state) continue;
                _state[i] = state;

                string label = available ? entry.Label : entry.Label + $"\n<size=70%><color=#ff9a80>нужен уровень {entry.Level}</color></size>";
                view.SetCard(i, label, available, selected);
            }

            int demolishKey = tool.Tool == BuildTool.Demolish ? 1 : 0;
            if (_demolishKey != demolishKey)
            {
                _demolishKey = demolishKey;
                view.SetDemolishSelected(demolishKey == 1);
            }

            bool open = depth > 0 && grid.Floors[depth].Open;
            int floorKey = depth * 2 + (open ? 1 : 0);
            if (_floorKey != floorKey)
            {
                _floorKey = floorKey;
                string text = config.FloorName(depth);
                if (depth == 0) text += " · строить пока нельзя";
                else if (!open) text += $" · закрыт: нужны пассажирский и грузовой лифты на −{depth - 1}";
                text += "   <size=75%>Z — ниже · C — выше</size>";
                view.SetFloor(text, depth == 0 || open);
            }

            if (_exits != grid.Exits)
            {
                _exits = grid.Exits;
                view.SetWarning(grid.Exits < 2
                    ? "Выход на поверхность один — постройте аварийный выход на −1 для эвакуации."
                    : string.Empty);
            }

            if (_status != tool.Status || _statusError != tool.StatusIsError)
            {
                _status = tool.Status;
                _statusError = tool.StatusIsError;
                view.SetStatus(_status ?? string.Empty, _statusError);
            }
        }
    }

    private void Toggle() => _world.NewEntity().Get<ToggleBuildMenuEvent>();

    private void Choose(Entry entry)
    {
        ref var select = ref _world.NewEntity().Get<SelectToolEvent>();
        select.Tool = entry.Tool;
        select.Building = entry.Building;
        select.Corridor = entry.Corridor;
    }

    private void Subscribe(UnityEngine.UI.Button button, UnityAction handler)
    {
        if (button == null) return;
        button.onClick.AddListener(handler);
        _subscribed.Add(button);
        _handlers.Add(handler);
    }

    private static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : a + " · " + b;
}
