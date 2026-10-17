using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Стройка в интерфейсе. Полоса этажа (всегда видна): этаж, стрелки, кнопка «Строительство»,
/// предупреждение и строка состояния. Нижняя панель (открывается кнопкой или B):
/// закладки категорий сверху, карточки построек с прокруткой, кнопка «Снос».
/// Закладки и карточки собираются из образцов при старте — список берётся из BuildingsConfig.
/// </summary>
public class BuildView : MonoBehaviour
{
    [SerializeField] private GameObject _root;

    [Header("Полоса этажа")]
    [SerializeField] private RectTransform _floorBar;
    [SerializeField] private float _barClosedY = 20f;
    [SerializeField] private float _barOpenY = 340f;
    [SerializeField] private TMP_Text _floor;
    [SerializeField] private Button _floorUp;
    [SerializeField] private Button _floorDown;
    [SerializeField] private Button _toggle;
    [SerializeField] private TMP_Text _toggleLabel;
    [SerializeField] private TMP_Text _warning;
    [SerializeField] private TMP_Text _status;

    [Header("Панель")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Button _demolish;
    [SerializeField] private Button _tabTemplate;        // неактивный образец закладки
    [SerializeField] private RectTransform _tabParent;   // с HorizontalLayoutGroup
    [SerializeField] private Button _cardTemplate;       // неактивный образец карточки
    [SerializeField] private RectTransform _cardParent;  // с GridLayoutGroup внутри ScrollRect
    [SerializeField] private ScrollRect _scroll;

    [Header("Цвета")]
    [SerializeField] private Color _normal = new Color(0.92f, 0.92f, 0.92f);
    [SerializeField] private Color _selected = new Color(0.95f, 0.75f, 0.25f);
    [SerializeField] private Color _locked = new Color(0.45f, 0.45f, 0.47f);
    [SerializeField] private Color _tab = new Color(0.30f, 0.32f, 0.36f);
    [SerializeField] private Color _tabSelected = new Color(0.95f, 0.75f, 0.25f);
    [SerializeField] private Color _okText = new Color(0.85f, 0.95f, 0.85f);
    [SerializeField] private Color _errorText = new Color(1f, 0.45f, 0.4f);

    private readonly List<Button> _tabs = new List<Button>();
    private readonly List<TMP_Text> _tabLabels = new List<TMP_Text>();
    private readonly List<Button> _cards = new List<Button>();
    private readonly List<TMP_Text> _cardLabels = new List<TMP_Text>();

    /// <summary>Корень стройки. Не назначен — прячется сам объект вьюхи.</summary>
    private GameObject Root => _root != null ? _root : gameObject;

    public Button ToggleButton => _toggle;
    public Button FloorUpButton => _floorUp;
    public Button FloorDownButton => _floorDown;
    public Button DemolishButton => _demolish;

    public void SetVisible(bool value) => Root.SetActive(value);

    public void SetOpen(bool open)
    {
        if (_panel != null) _panel.SetActive(open);
        if (_floorBar != null) _floorBar.anchoredPosition = new Vector2(_floorBar.anchoredPosition.x, open ? _barOpenY : _barClosedY);
        if (_toggleLabel != null) _toggleLabel.text = open ? "Закрыть  [B]" : "Строительство  [B]";
        if (_toggle != null && _toggle.image != null) _toggle.image.color = open ? _selected : _normal;
    }

    public Button[] CreateTabs(string[] names)
    {
        Rebuild(_tabs, _tabLabels, _tabTemplate, _tabParent, names.Length, "Закладка ");
        for (int i = 0; i < _tabLabels.Count && i < names.Length; i++)
            if (_tabLabels[i] != null) _tabLabels[i].text = names[i];
        return _tabs.ToArray();
    }

    public void SetTab(int index, bool selected)
    {
        if (index < 0 || index >= _tabs.Count) return;
        if (_tabs[index].image != null) _tabs[index].image.color = selected ? _tabSelected : _tab;
        if (_tabLabels[index] != null) _tabLabels[index].color = selected ? Color.black : Color.white;
    }

    public Button[] CreateCards(int count)
    {
        Rebuild(_cards, _cardLabels, _cardTemplate, _cardParent, count, "Карточка ");
        return _cards.ToArray();
    }

    public void SetCard(int index, string label, bool available, bool selected)
    {
        if (index < 0 || index >= _cards.Count) return;

        var card = _cards[index];
        card.interactable = available;
        if (card.image != null) card.image.color = selected ? _selected : available ? _normal : _locked;
        if (_cardLabels[index] != null) _cardLabels[index].text = label;
    }

    public void SetCardVisible(int index, bool visible)
    {
        if (index < 0 || index >= _cards.Count) return;
        if (_cards[index].gameObject.activeSelf != visible) _cards[index].gameObject.SetActive(visible);
    }

    public void ScrollToTop()
    {
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }

    public void SetDemolishSelected(bool selected)
    {
        if (_demolish != null && _demolish.image != null) _demolish.image.color = selected ? _selected : _normal;
    }

    public void SetFloor(string text, bool open)
    {
        if (_floor == null) return;
        _floor.text = text;
        _floor.color = open ? Color.white : _errorText;
    }

    public void SetWarning(string text)
    {
        if (_warning != null) _warning.text = text;
    }

    public void SetStatus(string text, bool error)
    {
        if (_status == null) return;
        _status.text = text;
        _status.color = error ? _errorText : _okText;
    }

    private static void Rebuild(List<Button> buttons, List<TMP_Text> labels, Button template, RectTransform parent, int count, string prefix)
    {
        foreach (var button in buttons)
            if (button != null) Destroy(button.gameObject);
        buttons.Clear();
        labels.Clear();

        if (template == null || parent == null) return;

        for (int i = 0; i < count; i++)
        {
            var button = Instantiate(template, parent);
            button.gameObject.name = prefix + i;
            button.gameObject.SetActive(true);
            buttons.Add(button);
            labels.Add(button.GetComponentInChildren<TMP_Text>());
        }
    }
}
