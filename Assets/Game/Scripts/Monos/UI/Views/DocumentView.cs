using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Окно документа SCP. Текст собирает ScpDocumentSystem, вьюха только показывает.
/// _classButtons — по порядку ObjectClass: Безопасный, Евклид, Кетер.
/// </summary>
public class DocumentView : MonoBehaviour
{
    [SerializeField] private GameObject _root;

    /// <summary>Корень окна. Не назначен — прячется сам объект вьюхи.</summary>
    private GameObject Root => _root != null ? _root : gameObject;
    [SerializeField] private TMP_Text _title;
    [SerializeField] private TMP_InputField _nickname;
    [SerializeField] private TMP_Text _research;
    [SerializeField] private TMP_Text _classLabel;
    [SerializeField] private Button[] _classButtons;
    [SerializeField] private TMP_Text _body;           // Rich Text включён
    [SerializeField] private ScrollRect _scroll;
    [SerializeField] private Button _close;

    public TMP_InputField NicknameInput => _nickname;
    public Button[] ClassButtons => _classButtons;
    public Button CloseButton => _close;

    public bool IsVisible => Root.activeSelf;

    public void SetVisible(bool value)
    {
        Root.SetActive(value);
    }

    public void Show(string title, string nickname, string research, string classLabel, string body)
    {
        if (_title != null) _title.text = title;
        if (_nickname != null && !_nickname.isFocused) _nickname.SetTextWithoutNotify(nickname);
        if (_research != null) _research.text = research;
        if (_classLabel != null) _classLabel.text = classLabel;
        if (_body != null) _body.text = body;
    }

    public void SetClassButtons(bool interactable, int selected)
    {
        if (_classButtons == null) return;
        for (int i = 0; i < _classButtons.Length; i++)
            if (_classButtons[i] != null) _classButtons[i].interactable = interactable && i != selected;
    }

    public void ScrollToTop()
    {
        if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
    }
}
