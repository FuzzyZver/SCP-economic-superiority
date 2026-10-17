using UnityEngine;

/// <summary>
/// Точка доступа к UI из систем. Если у тебя тут уже есть свои поля — не заменяй файл,
/// а допиши недостающие.
/// </summary>
public class UI : MonoBehaviour
{
    [Header("Полоса")]
    public HudView Hud;

    [Header("Документ SCP")]
    public DocumentView Document;
    public ScpListView ScpList;

    [Header("Стройка")]
    public BuildView Build;

    [Header("Конец игры")]
    public GameOverView GameOver;
}
