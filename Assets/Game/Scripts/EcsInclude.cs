using UnityEngine;
using Leopotam.Ecs;

public class EcsInclude : MonoBehaviour
{
    [SerializeField] private UI _ui;
    [SerializeField] private GameConfig _gameConfig;
    [SerializeField] private SceneData _sceneData;
    private EcsWorld _world;
    private EcsSystems _systems;

    public void Awake()
    {
        _world = new EcsWorld();
        _systems = new EcsSystems(_world);

        _systems
            //Add (new ...
            // Порядок здесь — не вкусовщина. Одно-кадровые события живут до конца кадра,
            // поэтому система, которая событие СОЗДАЁТ, стоит раньше той, что его читает.
            // Исключение — события из колбэков кнопок: они рождаются между кадрами.
            .Add(new InitSystem())           // сетка, объекты, этажи; стартовый въезд и коридор — событиями

            // ввод и камера
            .Add(new PlayerInputSystem())
#if UNITY_EDITOR
            .Add(new DevCheatSystem())       // рано: его события должны дойти до всех
#endif
            .Add(new FloorSystem())          // Z / C → этаж на экране и высота камеры
            .Add(new CameraSystem())
            .Add(new GridPointerSystem())    // курсор → клетка, после движения камеры

            // стройка
            .Add(new BuildSystem())          // постройки, объект в руках, снос, поворот → GridChanged, ContainScp, BuildingRemoved
            .Add(new CorridorSystem())       // коридоры-отрезки: прокладка и снос → GridChangedEvent
            .Add(new ConnectivitySystem())   // GridChanged → связность, этажи, выходы → ConnectivityChangedEvent
            .Add(new ResearchPostSystem())   // клик по посту, привязка камер, пути до постов
            .Add(new SelectionSystem())      // клик без инструмента → ScpClickedEvent

            // время и объекты
            .Add(new TimeSystem())           // DayStarted / MonthStarted
            .Add(new WaterSystem())          // вода и насосные; день → износ построек в воде
            .Add(new ContainmentSystem())    // ContainScp и снос → объекты в камерах; день → утечка
            .Add(new ScpObservationSystem()) // день → ResearchGainEvent
            .Add(new ScpResearchSystem())    // изученность → XpEvent, TrustChangeEvent, LogEvent
            .Add(new ScpDocumentSystem())    // документ, кличка, класс → XpEvent

            // Зона
            .Add(new EconomySystem())        // месяц → MoneyChangeEvent
            .Add(new DeclassificationSystem()) // раньше доверия: суточный штраф к доверию
            .Add(new TrustSystem())
            .Add(new DirectorSystem())       // после всех, кто даёт опыт
            .Add(new TreasurySystem())       // после всех, кто тратит и платит
            .Add(new GameOverSystem())       // после рассекречивания

            // отображение
            .Add(new LogSystem())            // последним из тех, кто пишет в журнал
            .Add(new HudSystem())
            .Add(new BuildMenuSystem())
            .Add(new ScpListSystem())

            //OneFrame<..
            .OneFrame<CameraPanInputEvent>()
            .OneFrame<CameraRotateInputEvent>()
            .OneFrame<CameraZoomInputEvent>()
            .OneFrame<ClickInputEvent>()
            .OneFrame<ReleaseInputEvent>()
            .OneFrame<CancelInputEvent>()
            .OneFrame<RotateInputEvent>()
            .OneFrame<FloorInputEvent>()
            .OneFrame<ToggleBuildMenuEvent>()

            .OneFrame<SetSpeedEvent>()
            .OneFrame<TogglePauseEvent>()
            .OneFrame<DayStartedEvent>()
            .OneFrame<MonthStartedEvent>()

            .OneFrame<MoneyChangeEvent>()
            .OneFrame<TrustChangeEvent>()
            .OneFrame<DeclassChangeEvent>()
            .OneFrame<XpEvent>()
            .OneFrame<LevelUpEvent>()
            .OneFrame<TrustLevelUpEvent>()
            .OneFrame<LogEvent>()
            .OneFrame<GameOverEvent>()
            .OneFrame<RestartEvent>()

            .OneFrame<ScpClickedEvent>()
            .OneFrame<CloseDocumentEvent>()
            .OneFrame<RenameScpEvent>()
            .OneFrame<AssignClassEvent>()
            .OneFrame<ResearchGainEvent>()

            .OneFrame<SelectToolEvent>()
            .OneFrame<PlaceBuildingEvent>()
            .OneFrame<PlaceCorridorEvent>()
            .OneFrame<GridChangedEvent>()
            .OneFrame<ConnectivityChangedEvent>()
            .OneFrame<BuildingRemovedEvent>()
            .OneFrame<SelectTabEvent>()
            .OneFrame<ContainScpEvent>()
            // DocumentChangedFlag сюда НЕ кладём: его снимает ScpDocumentSystem сама

            .Inject(_world)
            .Inject(_gameConfig)
            .Inject(_ui)
            .Inject(_sceneData)
            .Inject(new RealtimeData())

            .Init();
    }

    public void Update()
    {
        _systems?.Run();
    }

    private void OnDestroy()
    {
        _systems?.Destroy();
        _systems = null;
        _world?.Destroy();
        _world = null;
    }
}
