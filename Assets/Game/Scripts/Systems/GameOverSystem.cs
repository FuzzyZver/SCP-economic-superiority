using UnityEngine.SceneManagement;
using Leopotam.Ecs;

/// <summary>
/// Отстранение директора: время встаёт, показывается причина.
/// Рестарт в срезе — простая перезагрузка сцены.
/// </summary>
public class GameOverSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private EcsWorld _world;

    private EcsFilter<GameOverEvent> _gameOvers;
    private EcsFilter<RestartEvent> _restarts;
    private EcsFilter<SiteFlag, TimeScaleAttribute>.Exclude<GameOverFlag> _sites;

    public void Init() => Subscribe(true);
    public void Destroy() => Subscribe(false);

    public void Run()
    {
        foreach (var g in _gameOvers)
        {
            string reason = _gameOvers.Get1(g).Reason;

            foreach (var s in _sites)
            {
                _sites.Get2(s).Paused = true;
                _sites.GetEntity(s).Get<GameOverFlag>();
                UI.Document.SetVisible(false);
                UI.GameOver.Show(reason);
            }
        }

        if (!_restarts.IsEmpty())
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);   // сцена должна быть в Build Profiles
    }

    private void Subscribe(bool on)
    {
        var button = UI.GameOver.RestartButton;
        if (button == null) return;

        if (on) button.onClick.AddListener(RequestRestart);
        else button.onClick.RemoveListener(RequestRestart);
    }

    private void RequestRestart() => _world.NewEntity().Get<RestartEvent>();
}
