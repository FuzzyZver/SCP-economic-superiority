using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Leopotam.Ecs;

/// <summary>
/// Переводит клавиатуру и мышь в события. Больше никто не читает Input напрямую.
/// Пока игрок печатает в поле ввода (кличка объекта), клавиатура молчит,
/// а клики и колесо над UI не уходят в мир.
///
/// WASD / стрелки — сдвиг, Q / E — поворот камеры, колесо — зум, Z / C — этаж вниз / вверх,
/// R — повернуть постройку, B — панель стройки, пробел — пауза, 1…6 — скорость, ЛКМ — выбор и стройка,
/// ПКМ / Esc — отмена.
/// </summary>
public class PlayerInputSystem : Injects, IEcsRunSystem
{
    private EcsWorld _world;

    private EcsFilter<SiteFlag, PointerAttribute> _sites;

    private readonly List<RaycastResult> _hits = new List<RaycastResult>();
    private PointerEventData _pointerData;
    private EventSystem _pointerSystem;

    public void Run()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        bool overUI = mouse != null && IsPointerOverUI(mouse.position.ReadValue());

        if (mouse != null)
        {
            foreach (var s in _sites)
            {
                ref var pointer = ref _sites.Get2(s);
                pointer.Screen = mouse.position.ReadValue();
                pointer.OverUI = overUI;
            }
        }

        if (keyboard != null && !IsTyping()) ReadKeyboard(keyboard);
        if (mouse != null) ReadMouse(mouse, overUI);
    }

    private void ReadKeyboard(Keyboard keyboard)
    {
        var pan = Vector2.zero;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan.y += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan.y -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan.x += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan.x -= 1f;
        if (pan != Vector2.zero) _world.NewEntity().Get<CameraPanInputEvent>().Value = pan.normalized;

        if (keyboard.qKey.wasPressedThisFrame) _world.NewEntity().Get<CameraRotateInputEvent>().Direction = -1;
        if (keyboard.eKey.wasPressedThisFrame) _world.NewEntity().Get<CameraRotateInputEvent>().Direction = 1;

        if (keyboard.zKey.wasPressedThisFrame) _world.NewEntity().Get<FloorInputEvent>().Delta = 1;
        if (keyboard.cKey.wasPressedThisFrame) _world.NewEntity().Get<FloorInputEvent>().Delta = -1;

        if (keyboard.rKey.wasPressedThisFrame) _world.NewEntity().Get<RotateInputEvent>();
        if (keyboard.bKey.wasPressedThisFrame) _world.NewEntity().Get<ToggleBuildMenuEvent>();
        if (keyboard.escapeKey.wasPressedThisFrame) _world.NewEntity().Get<CancelInputEvent>();

        if (keyboard.spaceKey.wasPressedThisFrame) _world.NewEntity().Get<TogglePauseEvent>();

        int speeds = GameConfig.BalanceConfig.Speeds.Length;
        for (int i = 0; i < speeds && i < 9; i++)
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                _world.NewEntity().Get<SetSpeedEvent>().Index = i;
    }

    private void ReadMouse(Mouse mouse, bool overUI)
    {
        // Отпускание ловим всегда: иначе протянутый коридор, отпущенный над меню, повиснет.
        if (mouse.leftButton.wasReleasedThisFrame) _world.NewEntity().Get<ReleaseInputEvent>();

        if (overUI) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) _world.NewEntity().Get<CameraZoomInputEvent>().Value = Mathf.Sign(scroll);

        if (mouse.leftButton.wasPressedThisFrame)
            _world.NewEntity().Get<ClickInputEvent>().ScreenPosition = mouse.position.ReadValue();

        if (mouse.rightButton.wasPressedThisFrame) _world.NewEntity().Get<CancelInputEvent>();
    }

    private static bool IsTyping()
    {
        var system = EventSystem.current;
        if (system == null || system.currentSelectedGameObject == null) return false;

        var field = system.currentSelectedGameObject.GetComponent<TMP_InputField>();
        return field != null && field.isFocused;
    }

    /// <summary>
    /// Над UI ли курсор прямо сейчас. Не IsPointerOverGameObject: тот помнит прошлый кадр,
    /// и быстрый клик по кнопке меню успевал пролететь в мир и поставить постройку под панелью.
    /// </summary>
    private bool IsPointerOverUI(Vector2 position)
    {
        var system = EventSystem.current;
        if (system == null) return false;

        if (_pointerData == null || _pointerSystem != system)
        {
            _pointerData = new PointerEventData(system);
            _pointerSystem = system;
        }

        _pointerData.position = position;

        _hits.Clear();
        system.RaycastAll(_pointerData, _hits);
        return _hits.Count > 0;
    }
}
