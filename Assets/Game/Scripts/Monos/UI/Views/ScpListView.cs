using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Список объектов Зоны: где сидит, чего ждёт. Клик по строке открывает документ.</summary>
public class ScpListView : MonoBehaviour
{
    [SerializeField] private GameObject _root;
    [SerializeField] private Button _rowTemplate;        // неактивный образец строки
    [SerializeField] private RectTransform _rowParent;   // с VerticalLayoutGroup

    private readonly List<Button> _rows = new List<Button>();
    private readonly List<TMP_Text> _labels = new List<TMP_Text>();

    /// <summary>Корень окна. Не назначен — прячется сам объект вьюхи.</summary>
    private GameObject Root => _root != null ? _root : gameObject;

    public void SetVisible(bool value) => Root.SetActive(value);

    public Button[] CreateRows(int count)
    {
        foreach (var row in _rows)
            if (row != null) Destroy(row.gameObject);
        _rows.Clear();
        _labels.Clear();

        if (_rowTemplate == null || _rowParent == null) return new Button[0];

        for (int i = 0; i < count; i++)
        {
            var row = Instantiate(_rowTemplate, _rowParent);
            row.gameObject.name = "Объект " + i;
            row.gameObject.SetActive(true);
            _rows.Add(row);
            _labels.Add(row.GetComponentInChildren<TMP_Text>());
        }

        return _rows.ToArray();
    }

    public void SetRow(int index, string text, Color color)
    {
        if (index < 0 || index >= _labels.Count || _labels[index] == null) return;
        _labels[index].text = text;
        _labels[index].color = color;
    }
}
