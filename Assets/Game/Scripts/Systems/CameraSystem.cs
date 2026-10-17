using UnityEngine;
using Leopotam.Ecs;

/// <summary>
/// Изометрическая камера. Риг стоит на полу и крутится по Y шагами по 90°,
/// камера — его ребёнок, ортографическая, смотрит вниз под углом.
/// Работает на unscaledDeltaTime: камера живёт и на паузе.
/// Высоту рига задаёт этаж (TargetHeight от FloorSystem), границы — размер сетки.
/// </summary>
public class CameraSystem : Injects, IEcsRunSystem
{
    private EcsFilter<CameraPanInputEvent> _pans;
    private EcsFilter<CameraRotateInputEvent> _rotations;
    private EcsFilter<CameraZoomInputEvent> _zooms;
    private EcsFilter<CameraFlag, CameraAttribute> _cameras;

    public void Run()
    {
        var rig = SceneData.CameraRig;
        var camera = SceneData.Camera;
        if (rig == null || camera == null) return;

        var settings = GameConfig.SettingsConfig;
        float dt = Time.unscaledDeltaTime;

        foreach (var c in _cameras)
        {
            ref var state = ref _cameras.Get2(c);

            foreach (var r in _rotations) state.TargetYaw += 90f * _rotations.Get1(r).Direction;

            foreach (var z in _zooms)
                state.Zoom = Mathf.Clamp(state.Zoom - _zooms.Get1(z).Value * settings.ZoomStep, settings.MinZoom, settings.MaxZoom);

            state.Yaw = Mathf.LerpAngle(state.Yaw, state.TargetYaw, Sharp(settings.RotateSharpness, dt));
            rig.rotation = Quaternion.Euler(0f, state.Yaw, 0f);

            foreach (var p in _pans)
            {
                var input = _pans.Get1(p).Value;
                var flat = Quaternion.Euler(0f, state.Yaw, 0f) * new Vector3(input.x, 0f, input.y);
                float zoomFactor = state.Zoom / Mathf.Max(0.01f, settings.StartZoom);
                rig.position += flat * settings.PanSpeed * zoomFactor * dt;
            }

            float bounds = GameConfig.BuildingsConfig.GridSize * 0.5f + 5f;
            var position = rig.position;
            position.x = Mathf.Clamp(position.x, -bounds, bounds);
            position.z = Mathf.Clamp(position.z, -bounds, bounds);
            position.y = Mathf.Lerp(position.y, state.TargetHeight, Sharp(settings.ZoomSharpness, dt));
            rig.position = position;

            if (camera.orthographic)
                camera.orthographicSize = Mathf.Lerp(camera.orthographicSize, state.Zoom, Sharp(settings.ZoomSharpness, dt));
        }
    }

    /// <summary>Доля сближения за кадр, не зависящая от FPS.</summary>
    private static float Sharp(float sharpness, float dt) => 1f - Mathf.Exp(-sharpness * dt);
}
