using System.Text;
using UnityEngine;
using UnityEngine.Events;
using Leopotam.Ecs;

/// <summary>
/// Документ SCP: открыть, закрыть, дать кличку, назначить класс и собрать текст.
/// Раздел виден, только если хватает и изученности, и допуска директора:
/// не хватает изученности — «данные отсутствуют», не хватает допуска — ██████.
///
/// Класс назначает игрок. Настоящий класс в конфиге, расплата за ошибку — в будущей
/// системе нарушений: заниженный класс дешевле в содержании, но опаснее.
/// </summary>
public class ScpDocumentSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private EcsWorld _world;

    private EcsFilter<ScpClickedEvent> _clicks;
    private EcsFilter<CloseDocumentEvent> _closes;
    private EcsFilter<RenameScpEvent> _renames;
    private EcsFilter<AssignClassEvent> _assigns;

    private EcsFilter<SiteFlag, SelectedScpAttribute, DirectorAttribute> _sites;
    private EcsFilter<ScpFlag, ScpAttribute, ResearchAttribute, ClassificationAttribute, NicknameAttribute> _scps;

    private readonly StringBuilder _builder = new StringBuilder();
    private UnityAction[] _classHandlers;

    // ─────────────────────── подписки UI ───────────────────────

    public void Init()
    {
        var view = UI.Document;

        if (view.CloseButton != null) view.CloseButton.onClick.AddListener(RequestClose);
        if (view.NicknameInput != null) view.NicknameInput.onEndEdit.AddListener(RequestRename);

        var buttons = view.ClassButtons;
        if (buttons == null) return;

        _classHandlers = new UnityAction[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            var objectClass = (ObjectClass)i;
            _classHandlers[i] = () => _world.NewEntity().Get<AssignClassEvent>().Class = objectClass;
            buttons[i].onClick.AddListener(_classHandlers[i]);
        }
    }

    public void Destroy()
    {
        var view = UI.Document;

        if (view.CloseButton != null) view.CloseButton.onClick.RemoveListener(RequestClose);
        if (view.NicknameInput != null) view.NicknameInput.onEndEdit.RemoveListener(RequestRename);

        var buttons = view.ClassButtons;
        if (buttons == null || _classHandlers == null) return;

        for (int i = 0; i < buttons.Length && i < _classHandlers.Length; i++)
            if (buttons[i] != null && _classHandlers[i] != null) buttons[i].onClick.RemoveListener(_classHandlers[i]);
    }

    private void RequestClose() => _world.NewEntity().Get<CloseDocumentEvent>();
    private void RequestRename(string text) => _world.NewEntity().Get<RenameScpEvent>().Name = text;

    // ─────────────────────── кадр ───────────────────────

    public void Run()
    {
        foreach (var s in _sites)
        {
            ref var selected = ref _sites.Get2(s);
            var site = _sites.GetEntity(s);

            foreach (var c in _clicks)
            {
                selected.HasTarget = true;
                selected.Id = _clicks.Get1(c).Id;
                site.Get<DocumentChangedFlag>();
                UI.Document.SetVisible(true);
                UI.Document.ScrollToTop();
            }

            foreach (var _ in _closes)
            {
                selected.HasTarget = false;
                UI.Document.SetVisible(false);
            }

            if (selected.HasTarget && TryFind(selected.Id, out int index))
            {
                foreach (var r in _renames) Rename(index, _renames.Get1(r).Name, site);
                foreach (var a in _assigns) Assign(index, _assigns.Get1(a).Class, site);

                if (site.Has<DocumentChangedFlag>() && UI.Document.IsVisible)
                    Redraw(index, _sites.Get3(s).Clearance);
            }

            if (site.Has<DocumentChangedFlag>()) site.Del<DocumentChangedFlag>();
        }
    }

    // ─────────────────────── действия ───────────────────────

    private void Rename(int index, string name, EcsEntity site)
    {
        var definition = GameConfig.ScpConfig.Get(_scps.Get2(index).Id);
        string fallback = definition != null ? definition.Title : _scps.Get2(index).Id.ToString();
        string value = string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();

        ref var nickname = ref _scps.Get5(index);
        if (nickname.Value == value) return;

        nickname.Value = value;
        site.Get<DocumentChangedFlag>();

        if (definition != null)
            _world.NewEntity().Get<LogEvent>().Text = $"{definition.Number} теперь зовут «{value}».";
    }

    private void Assign(int index, ObjectClass objectClass, EcsEntity site)
    {
        var balance = GameConfig.BalanceConfig;
        var config = GameConfig.ScpConfig;
        var definition = config.Get(_scps.Get2(index).Id);
        string number = definition != null ? definition.Number : _scps.Get2(index).Id.ToString();

        if (_scps.Get3(index).Progress < balance.ClassifyMinResearch)
        {
            _world.NewEntity().Get<LogEvent>().Text =
                $"{number}: недостаточно данных для классификации. Нужно изучить хотя бы {balance.ClassifyMinResearch:0}%.";
            return;
        }

        ref var classification = ref _scps.Get4(index);
        if (classification.IsAssigned && classification.Assigned == objectClass) return;

        bool first = !classification.IsAssigned;
        classification.IsAssigned = true;
        classification.Assigned = objectClass;
        site.Get<DocumentChangedFlag>();

        _world.NewEntity().Get<LogEvent>().Text = $"{number} классифицирован: {config.ClassName(objectClass)}.";

        if (!first) return;

        ref var xp = ref _world.NewEntity().Get<XpEvent>();
        xp.Amount = balance.ClassifyXp;
        xp.Reason = $"классификация {number}";
    }

    // ─────────────────────── текст ───────────────────────

    private void Redraw(int index, int clearance)
    {
        var config = GameConfig.ScpConfig;
        var balance = GameConfig.BalanceConfig;
        var id = _scps.Get2(index).Id;
        var definition = config.Get(id);
        float progress = _scps.Get3(index).Progress;
        ref var classification = ref _scps.Get4(index);
        string nickname = _scps.Get5(index).Value;

        string number = definition != null ? definition.Number : id.ToString();
        string classLabel = classification.IsAssigned
            ? $"Класс: {config.ClassName(classification.Assigned)}"
            : "Класс: не назначен";

        UI.Document.Show(
            number,
            nickname,
            $"Изучено: {progress:0}%",
            classLabel,
            BuildBody(definition, progress, clearance));

        UI.Document.SetClassButtons(
            progress >= balance.ClassifyMinResearch,
            classification.IsAssigned ? (int)classification.Assigned : -1);
    }

    private string BuildBody(ScpDefinition definition, float progress, int clearance)
    {
        _builder.Clear();
        if (definition == null || definition.Sections == null) return "[ДОКУМЕНТ НЕ НАЙДЕН]";

        var config = GameConfig.ScpConfig;
        bool first = true;
        DocumentSectionKind previous = DocumentSectionKind.Containment;

        if (definition.BranchA != ScienceBranch.None || definition.BranchB != ScienceBranch.None)
            _builder.Append("<color=#3d6b6b><i>Ускоряет дисциплины: ")
                    .Append(BuildingsConfig.BranchName(definition.BranchA)).Append(", ")
                    .Append(BuildingsConfig.BranchName(definition.BranchB)).Append("</i></color>\n\n");

        foreach (var section in definition.Sections)
        {
            bool newHeading = first || section.Kind != previous || !string.IsNullOrEmpty(section.Heading);
            if (newHeading)
            {
                if (!first) _builder.Append('\n');
                string heading = string.IsNullOrEmpty(section.Heading) ? config.SectionHeading(section.Kind) : section.Heading;
                _builder.Append("<b>").Append(heading).Append(":</b>\n");
            }

            if (progress < section.RequiredResearch)
                _builder.Append("<color=#7a7a7a>[ДАННЫЕ ОТСУТСТВУЮТ — нужна изученность ")
                        .Append(section.RequiredResearch.ToString("0")).Append("%]</color>");
            else if (clearance < section.RequiredClearance)
                _builder.Append("<mark=#000000ff>").Append(Redacted(section.Text)).Append("</mark> ")
                        .Append("<color=#b04040>[ТРЕБУЕТСЯ ДОПУСК ").Append(section.RequiredClearance).Append("]</color>");
            else
                _builder.Append(section.Text);

            _builder.Append('\n');
            previous = section.Kind;
            first = false;
        }

        return _builder.ToString();
    }

    private static string Redacted(string text)
    {
        int length = Mathf.Clamp(string.IsNullOrEmpty(text) ? 8 : text.Length / 3, 8, 60);
        return new string('█', length);
    }

    private bool TryFind(ScpId id, out int index)
    {
        foreach (var i in _scps)
        {
            if (_scps.Get2(i).Id != id) continue;
            index = i;
            return true;
        }

        index = -1;
        return false;
    }
}
