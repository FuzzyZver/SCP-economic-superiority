using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Верхняя полоса Зоны: время и скорость, деньги, доверие O5, рассекречивание, директор и журнал.
/// Любое поле можно не назначать — вьюха просто его пропустит.
/// </summary>
public class HudView : MonoBehaviour
{
    [SerializeField] private GameObject _root;

    /// <summary>Корень окна. Не назначен — прячется сам объект вьюхи.</summary>
    private GameObject Root => _root != null ? _root : gameObject;

    [Header("Время")]
    [SerializeField] private TMP_Text _date;
    [SerializeField] private TMP_Text _speed;
    [SerializeField] private Button _pause;
    [SerializeField] private Button[] _speedButtons;   // по порядку BalanceConfig.Speeds

    [Header("Деньги")]
    [SerializeField] private TMP_Text _money;

    [Header("Доверие O5")]
    [SerializeField] private Image _trustFill;         // Image Type = Filled
    [SerializeField] private TMP_Text _trust;
    [SerializeField] private TMP_Text _probation;

    [Header("Рассекречивание")]
    [SerializeField] private Image _declassFill;
    [SerializeField] private TMP_Text _declass;

    [Header("Директор")]
    [SerializeField] private Image _xpFill;            // кружок в углу: Filled, Radial 360
    [SerializeField] private TMP_Text _level;
    [SerializeField] private TMP_Text _clearance;

    [Header("Журнал")]
    [SerializeField] private TMP_Text _log;

    [Header("Цвета")]
    [SerializeField] private Color _ok = new Color(0.36f, 0.80f, 0.45f);
    [SerializeField] private Color _warning = new Color(0.92f, 0.75f, 0.30f);
    [SerializeField] private Color _danger = new Color(0.90f, 0.35f, 0.35f);

    private readonly StringBuilder _builder = new StringBuilder();

    public Button PauseButton => _pause;
    public Button[] SpeedButtons => _speedButtons;

    public void SetVisible(bool value)
    {
        Root.SetActive(value);
    }

    public void SetDate(int dayOfMonth, int month, int hour)
    {
        if (_date != null) _date.text = $"День {dayOfMonth} · Месяц {month} · {hour:00}:00";
    }

    public void SetSpeed(int index, float multiplier, bool paused)
    {
        if (_speed != null) _speed.text = paused ? "Пауза" : $"x{multiplier:0.#}";

        if (_speedButtons == null) return;
        for (int i = 0; i < _speedButtons.Length; i++)
            if (_speedButtons[i] != null) _speedButtons[i].interactable = paused || i != index;
    }

    public void SetMoney(int money)
    {
        if (_money == null) return;
        _money.text = money.ToString("N0");
        _money.color = money < 0 ? _danger : Color.white;
    }

    /// <summary>probationDaysLeft меньше нуля — испытательного срока нет.</summary>
    public void SetTrust(float value, float ceiling, int level, bool fundingCut, int probationDaysLeft, float probationBelow)
    {
        if (_trustFill != null)
        {
            _trustFill.fillAmount = Mathf.Clamp01(value / 100f);
            _trustFill.color = value < probationBelow ? _danger : _ok;
        }

        if (_trust != null) _trust.text = $"Доверие O5: {value:0} / {ceiling:0} · ур. {level}";

        if (_probation == null) return;
        _probation.text =
            probationDaysLeft >= 0 ? $"Испытательный срок: {probationDaysLeft} дн." :
            fundingCut ? "Финансирование урезано" : string.Empty;
        _probation.color = _danger;
    }

    public void SetDeclass(float value, float max, float highThreshold)
    {
        if (_declassFill != null)
        {
            _declassFill.fillAmount = max > 0f ? Mathf.Clamp01(value / max) : 0f;
            _declassFill.color = value >= highThreshold ? _danger : value > 0.5f * highThreshold ? _warning : _ok;
        }

        if (_declass != null) _declass.text = $"Рассекречивание: {value:0}%";
    }

    public void SetDirector(int level, int clearance, float xpProgress)
    {
        if (_xpFill != null) _xpFill.fillAmount = Mathf.Clamp01(xpProgress);
        if (_level != null) _level.text = level.ToString();
        if (_clearance != null) _clearance.text = $"Допуск {clearance}";
    }

    public void SetLog(List<string> lines)
    {
        if (_log == null) return;

        _builder.Clear();
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) _builder.Append('\n');
            _builder.Append(lines[i]);
        }

        _log.text = _builder.ToString();
    }
}
