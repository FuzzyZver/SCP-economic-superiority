using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Отстранение от должности. Причину пишет GameOverSystem.</summary>
public class GameOverView : MonoBehaviour
{
    [SerializeField] private GameObject _root;

    /// <summary>Корень окна. Не назначен — прячется сам объект вьюхи.</summary>
    private GameObject Root => _root != null ? _root : gameObject;
    [SerializeField] private TMP_Text _reason;
    [SerializeField] private Button _restart;

    public Button RestartButton => _restart;

    public void SetVisible(bool value)
    {
        Root.SetActive(value);
    }

    public void Show(string reason)
    {
        if (_reason != null) _reason.text = reason;
        SetVisible(true);
    }
}
