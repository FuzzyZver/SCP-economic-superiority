using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Инструмент редактора, в игру не входит. Собирает тестовую сцену среза:
/// конфиги, пол, пять камер содержания из примитивов, изометрическую камеру,
/// интерфейс (полоса, журнал, документ, конец игры), SceneData и EcsInclude.
///
/// Запускается сам, когда в проекте нет Assets/Game/SO/GameConfig.asset или когда версия
/// сборщика выросла (новая итерация среза). Пересобрать вручную: меню SCP → Пересобрать тестовую сцену.
/// Конфиги при пересборке не пересоздаются — правки в них сохраняются.
/// Отчёт пишется в Logs/ScpTestSceneBuilder.log.
/// </summary>
[InitializeOnLoad]
public static class ScpTestSceneBuilder
{
    private const string ScenePath = "Assets/Game/Scenes/TestLevel.unity";
    private const string SoFolder = "Assets/Game/SO";
    private const string MaterialsFolder = "Assets/Game/Materials";
    private const string GameConfigPath = SoFolder + "/GameConfig.asset";
    private const string LogPath = "Logs/ScpTestSceneBuilder.log";
    private const string Prefix = "[SCP] ";
    private const string Version = "3.2";   // итерация 3: каталог построек, панель стройки, коридоры-отрезки, вода

    private static readonly Color Panel = new Color(0.08f, 0.09f, 0.10f, 0.92f);
    private static readonly Color Paper = new Color(0.93f, 0.92f, 0.88f, 1f);
    private static readonly Color Ink = new Color(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color Gold = new Color(0.95f, 0.75f, 0.25f, 1f);

    private static TMP_DefaultControls.Resources _resources;

    static ScpTestSceneBuilder()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += TryAutoBuild;
    }

    private static string VersionKey => "SCP.TestSceneBuilder.Version." + Application.dataPath;

    private static void TryAutoBuild()
    {
        bool fresh = AssetDatabase.LoadAssetAtPath<GameConfig>(GameConfigPath) == null;
        if (!fresh && EditorPrefs.GetString(VersionKey, "1") == Version) return;

        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TryAutoBuild;
            return;
        }

        Build();
    }

    [MenuItem("SCP/Пересобрать тестовую сцену")]
    public static void Build()
    {
        Log("──────── сборка " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

        try
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Log("Отменено: есть несохранённые сцены.");
                return;
            }

            if (TMP_Settings.instance == null)
            {
                Log("ОШИБКА: нет TMP Settings. Импортируй Window → TextMeshPro → Import TMP Essential Resources и запусти сборку из меню SCP.");
                return;
            }

            bool upgrade = EditorPrefs.GetString(VersionKey, "1") != Version;
            var config = BuildConfigs(upgrade);
            Step(upgrade ? "конфиги (каталог построек пересобран под итерацию " + Version + ")" : "конфиги");

            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            ClearScene(scene);
            Step("очистка сцены");

            var materials = BuildMaterials();
            var floors = BuildFloors(config.BuildingsConfig, materials);
            Step("этажи: " + floors.Length + ", поверхность с КПП и порталом тоннеля");

            var ghost = new GameObject(Prefix + "Призрак стройки").AddComponent<GhostActor>();

            var camera = BuildCamera(out var rig);
            Step("изометрическая камера");

            BuildEventSystem();
            var ui = BuildUI(config);
            Step("интерфейс");

            var sceneData = new GameObject(Prefix + "SceneData").AddComponent<SceneData>();
            sceneData.CameraRig = rig;
            sceneData.Camera = camera;
            sceneData.Floors = floors;
            sceneData.Ghost = ghost;

            var ecs = new GameObject(Prefix + "ECS").AddComponent<EcsInclude>();
            Set(ecs, "_ui", ui);
            Set(ecs, "_gameConfig", config);
            Set(ecs, "_sceneData", sceneData);
            Step("SceneData и EcsInclude");

            if (string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene, ScenePath);
            else
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EnsureInBuild(ScenePath);
            AssetDatabase.SaveAssets();
            EditorPrefs.SetString(VersionKey, Version);
            Step("сцена сохранена: " + ScenePath);
            Log("ГОТОВО.");
            Debug.Log("[SCP] Тестовая сцена собрана. Жми Play.");
        }
        catch (Exception e)
        {
            Log("ОШИБКА: " + e);
            Debug.LogException(e);
        }
    }

    // ─────────────────────────── конфиги ───────────────────────────

    private static GameConfig BuildConfigs(bool upgrade)
    {
        EnsureFolder(SoFolder);

        var balance = LoadOrCreate<BalanceConfig>(SoFolder + "/BalanceConfig.asset");
        var scp = LoadOrCreate<ScpConfig>(SoFolder + "/ScpConfig.asset");
        var settings = LoadOrCreate<SettingsConfig>(SoFolder + "/SettingsConfig.asset");
        var buildings = LoadOrCreate<BuildingsConfig>(SoFolder + "/BuildingsConfig.asset");
        var game = LoadOrCreate<GameConfig>(GameConfigPath);

        EnsureFolder(MaterialsFolder);
        if (buildings.BaseMaterial == null) buildings.BaseMaterial = Mat("Base", Color.white);
        EditorUtility.SetDirty(buildings);

        scp.SyncFromDefaults();   // камеры, ветки и вид объектов, которых не было в старых версиях ассета

        // каталог построек сменил формат (Id и роли вместо номеров) — старый ассет пересобираем
        bool outdated = buildings.Buildings == null || buildings.Buildings.Length == 0
                        || string.IsNullOrEmpty(buildings.Buildings[0].Id) || buildings.IndexOfRole(BuildingRole.MainEntrance) < 0;
        if (upgrade || outdated) buildings.RebuildDefaults();

        game.BalanceConfig = balance;
        game.ScpConfig = scp;
        game.SettingsConfig = settings;
        game.BuildingsConfig = buildings;
        EditorUtility.SetDirty(game);
        return game;
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    // ─────────────────────────── мир ───────────────────────────

    private struct Materials
    {
        public Material Fence, Concrete, Road, Dark, Booth, Barrier, Stripe, Post;
    }

    private static Materials BuildMaterials()
    {
        EnsureFolder(MaterialsFolder);
        return new Materials
        {
            Fence = Mat("Fence", new Color(0.42f, 0.45f, 0.40f)),
            Concrete = Mat("Concrete", new Color(0.58f, 0.58f, 0.56f)),
            Road = Mat("Road", new Color(0.18f, 0.18f, 0.19f)),
            Dark = Mat("Dark", new Color(0.04f, 0.04f, 0.05f)),
            Booth = Mat("Booth", new Color(0.70f, 0.66f, 0.40f)),
            Barrier = Mat("Barrier", new Color(0.85f, 0.20f, 0.18f)),
            Stripe = Mat("Stripe", new Color(0.90f, 0.85f, 0.30f)),
            Post = Mat("GuardPost", new Color(0.30f, 0.38f, 0.55f)),
        };
    }

    private static Material Mat(string name, Color color)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ─────────────────────────── этажи ───────────────────────────

    /// <summary>Пустые этажи с FloorActor: пол, сетку и постройки они создают сами в игре.
    /// Поверхность (глубина 0) получает декорации: забор, КПП, посты охраны, дорогу и бункер входа.</summary>
    private static FloorActor[] BuildFloors(BuildingsConfig config, Materials m)
    {
        var root = new GameObject(Prefix + "Этажи").transform;
        var floors = new FloorActor[config.MaxDepth + 1];

        for (int depth = 0; depth <= config.MaxDepth; depth++)
        {
            var go = new GameObject(config.FloorName(depth));
            go.transform.SetParent(root, false);
            var floor = go.AddComponent<FloorActor>();

            var serialized = new SerializedObject(floor);
            serialized.FindProperty("_depth").intValue = depth;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            floors[depth] = floor;
        }

        BuildSurface(floors[0].transform, config, m);
        return floors;
    }

    private static void BuildSurface(Transform surface, BuildingsConfig config, Materials m)
    {
        var decor = new GameObject("Декорации поверхности").transform;
        decor.SetParent(surface, false);

        float half = config.GridSize * 0.5f;
        const float fenceHeight = 2.4f, gate = 5f;

        // забор по периметру, проём под КПП на юге
        Box(decor, "Забор север", new Vector3(0f, fenceHeight * 0.5f, half - 0.1f), new Vector3(config.GridSize, fenceHeight, 0.2f), m.Fence);
        Box(decor, "Забор запад", new Vector3(-half + 0.1f, fenceHeight * 0.5f, 0f), new Vector3(0.2f, fenceHeight, config.GridSize), m.Fence);
        Box(decor, "Забор восток", new Vector3(half - 0.1f, fenceHeight * 0.5f, 0f), new Vector3(0.2f, fenceHeight, config.GridSize), m.Fence);
        float side = half - gate;
        Box(decor, "Забор юг (запад)", new Vector3(-gate - side * 0.5f, fenceHeight * 0.5f, -half + 0.1f), new Vector3(side, fenceHeight, 0.2f), m.Fence);
        Box(decor, "Забор юг (восток)", new Vector3(gate + side * 0.5f, fenceHeight * 0.5f, -half + 0.1f), new Vector3(side, fenceHeight, 0.2f), m.Fence);

        // КПП: две будки и шлагбаум
        Box(decor, "Будка КПП (запад)", new Vector3(-gate - 1.6f, 1.4f, -half + 1.8f), new Vector3(3f, 2.8f, 3f), m.Booth);
        Box(decor, "Будка КПП (восток)", new Vector3(gate + 1.6f, 1.4f, -half + 1.8f), new Vector3(3f, 2.8f, 3f), m.Booth);
        Box(decor, "Шлагбаум", new Vector3(0f, 1.1f, -half + 0.6f), new Vector3(gate * 2f, 0.15f, 0.15f), m.Barrier);
        WorldLabel(decor, "КПП", new Vector3(0f, 0.05f, -half + 4f), new Vector2(8f, 2f), 14f, Color.white);

        // посты охраны: охрану ещё предстоит нанять
        Box(decor, "Пост охраны (запад)", new Vector3(-gate - 6f, 0.05f, -half + 6f), new Vector3(3f, 0.1f, 3f), m.Post);
        Box(decor, "Пост охраны (восток)", new Vector3(gate + 6f, 0.05f, -half + 6f), new Vector3(3f, 0.1f, 3f), m.Post);
        WorldLabel(decor, "Пост охраны\nнет персонала", new Vector3(-gate - 6f, 0.12f, -half + 6f), new Vector2(3f, 2f), 6f, new Color(1f, 0.6f, 0.5f));
        WorldLabel(decor, "Пост охраны\nнет персонала", new Vector3(gate + 6f, 0.12f, -half + 6f), new Vector2(3f, 2f), 6f, new Color(1f, 0.6f, 0.5f));

        // дорога от ворот к порталу тоннеля: дальше она уходит под землю, в зал главного въезда на −1
        var entranceDef = config.Def(config.IndexOfRole(BuildingRole.MainEntrance));
        var size = entranceDef != null ? entranceDef.Footprint(config.MainEntranceRotation) : new Vector2Int(18, 16);
        var hall = config.CellCorner(config.MainEntranceOrigin, 0);
        float roadX = hall.x + size.x * 0.5f;
        float portalZ = hall.z + 3f;                 // устье тоннеля над дальней стеной зала
        float rampStart = portalZ - 10f;
        float roadStart = -half - 6f;

        Box(decor, "Дорога", new Vector3(roadX, 0.02f, (roadStart + rampStart) * 0.5f), new Vector3(8f, 0.04f, rampStart - roadStart), m.Road);
        for (float z = roadStart + 3f; z < rampStart - 2f; z += 6f)
            Box(decor, "Разметка", new Vector3(roadX, 0.05f, z), new Vector3(0.25f, 0.02f, 2.5f), m.Stripe);

        // пандус вниз и подпорные стены
        Box(decor, "Пандус", new Vector3(roadX, 0.01f, (rampStart + portalZ) * 0.5f), new Vector3(8f, 0.03f, portalZ - rampStart), m.Dark);
        for (int k = 0; k < 5; k++)
            Box(decor, "Ступень пандуса", new Vector3(roadX, 0.03f, rampStart + 1f + k * 2f), new Vector3(7.6f, 0.02f, 0.3f), m.Road);
        Box(decor, "Подпорная стена (запад)", new Vector3(roadX - 4.4f, 0.6f, (rampStart + portalZ) * 0.5f), new Vector3(0.6f, 1.2f, portalZ - rampStart), m.Concrete);
        Box(decor, "Подпорная стена (восток)", new Vector3(roadX + 4.4f, 0.6f, (rampStart + portalZ) * 0.5f), new Vector3(0.6f, 1.2f, portalZ - rampStart), m.Concrete);

        // портал тоннеля
        Box(decor, "Портал (запад)", new Vector3(roadX - 4.6f, 1.8f, portalZ), new Vector3(1.2f, 3.6f, 1.6f), m.Concrete);
        Box(decor, "Портал (восток)", new Vector3(roadX + 4.6f, 1.8f, portalZ), new Vector3(1.2f, 3.6f, 1.6f), m.Concrete);
        Box(decor, "Портал (перемычка)", new Vector3(roadX, 3.3f, portalZ), new Vector3(10.4f, 0.8f, 1.6f), m.Concrete);
        Box(decor, "Устье тоннеля", new Vector3(roadX, 1.45f, portalZ + 0.5f), new Vector3(8f, 2.9f, 0.4f), m.Dark);
        Box(decor, "Насыпь над тоннелем", new Vector3(roadX, 0.9f, portalZ + 4f), new Vector3(12f, 1.8f, 7f), m.Fence);
        WorldLabel(decor, "ГЛАВНЫЙ ВЪЕЗД\nтоннель на −1", new Vector3(roadX, 1.85f, portalZ + 4f), new Vector2(11f, 6f), 14f, Color.white);
    }

    private static void WorldLabel(Transform parent, string text, Vector3 position, Vector2 size, float fontSize, Color color)
    {
        var label = new GameObject("Надпись").AddComponent<TextMeshPro>();
        label.transform.SetParent(parent, false);
        label.transform.localPosition = position;
        label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        label.rectTransform.sizeDelta = size;
        label.enableAutoSizing = true;
        label.fontSizeMin = 1f;
        label.fontSizeMax = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.text = text;
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material) =>
        Shape(parent, PrimitiveType.Cube, name, position, scale, material);

    private static GameObject Shape(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // клик ловит коллайдер всей камеры
        return go;
    }

    // ─────────────────────────── камера ───────────────────────────

    private static Camera BuildCamera(out Transform rig)
    {
        rig = new GameObject(Prefix + "Камера").transform;
        rig.position = Vector3.zero;
        rig.rotation = Quaternion.Euler(0f, 45f, 0f);

        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        go.transform.SetParent(rig, false);
        go.transform.localPosition = new Vector3(0f, 14f, -22f);
        go.transform.LookAt(rig.position);

        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 10f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
        go.AddComponent<AudioListener>();

        if (Object.FindFirstObjectByType<Light>() == null)
        {
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        return camera;
    }

    private static void BuildEventSystem()
    {
        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    // ─────────────────────────── интерфейс ───────────────────────────

    private static UI BuildUI(GameConfig config)
    {
        _resources = new TMP_DefaultControls.Resources
        {
            standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
            background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
            inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
            knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
            checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
            dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
            mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
        };

        var canvasGo = new GameObject(Prefix + "UI", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var ui = canvasGo.AddComponent<UI>();
        ui.Hud = BuildHud(canvasGo.transform, config);
        ui.Document = BuildDocument(canvasGo.transform, config);
        ui.ScpList = BuildScpList(canvasGo.transform);
        ui.Build = BuildBuildMenu(canvasGo.transform);
        ui.GameOver = BuildGameOver(canvasGo.transform);
        EditorUtility.SetDirty(ui);
        return ui;
    }

    private static HudView BuildHud(Transform canvas, GameConfig config)
    {
        var root = Rect("HUD", canvas);
        Stretch(root);
        var view = root.gameObject.AddComponent<HudView>();

        // верхняя полоса
        var bar = Rect("Полоса", root);
        Anchor(bar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        bar.sizeDelta = new Vector2(0f, 110f);
        bar.anchoredPosition = Vector2.zero;
        Img(bar, Panel);

        var date = Text("Дата", bar, "День 1 · Месяц 1 · 08:00", 26, TopLeft(20f, -12f, 460f, 40f));
        var speed = Text("Скорость", bar, "x1", 24, TopLeft(20f, -60f, 110f, 40f));
        speed.alignment = TextAlignmentOptions.MidlineLeft;

        var pause = Button("Пауза", bar, "II", TopLeft(130f, -60f, 60f, 40f));
        var speeds = config.BalanceConfig.Speeds;
        var speedButtons = new Button[speeds.Length];
        for (int i = 0; i < speeds.Length; i++)
            speedButtons[i] = Button("x" + speeds[i], bar, "x" + speeds[i].ToString("0.#"), TopLeft(198f + 68f * i, -60f, 62f, 40f));

        var money = Text("Бюджет", bar, "0", 30, TopLeft(640f, -12f, 300f, 44f));
        money.fontStyle = FontStyles.Bold;
        Text("Подпись бюджета", bar, "Бюджет Зоны", 18, TopLeft(640f, -62f, 300f, 30f)).color = new Color(1f, 1f, 1f, 0.6f);

        var trust = Text("Доверие", bar, "Доверие O5", 20, TopLeft(980f, -8f, 440f, 30f));
        var trustFill = Bar("Шкала доверия", bar, TopLeft(980f, -42f, 440f, 18f));
        var probation = Text("Испытательный срок", bar, "", 18, TopLeft(980f, -66f, 440f, 30f));

        var declass = Text("Рассекречивание", bar, "Рассекречивание: 0%", 20, TopLeft(1450f, -8f, 300f, 30f));
        var declassFill = Bar("Шкала рассекречивания", bar, TopLeft(1450f, -42f, 300f, 18f));

        var circle = Rect("Директор", bar);
        Anchor(circle, Vector2.one, Vector2.one, Vector2.one);
        circle.sizeDelta = new Vector2(90f, 90f);
        circle.anchoredPosition = new Vector2(-20f, -10f);
        var circleBack = Img(circle, new Color(1f, 1f, 1f, 0.12f));
        circleBack.sprite = _resources.knob;
        var xpFillRect = Rect("Опыт", circle);
        Stretch(xpFillRect);
        var xpFill = Img(xpFillRect, Gold);
        xpFill.sprite = _resources.knob;
        xpFill.type = Image.Type.Filled;
        xpFill.fillMethod = Image.FillMethod.Radial360;
        xpFill.fillOrigin = (int)Image.Origin360.Top;
        xpFill.fillAmount = 0f;
        var inner = Rect("Центр", circle);
        Anchor(inner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        inner.sizeDelta = new Vector2(70f, 70f);
        Img(inner, Panel).sprite = _resources.knob;
        var level = Text("Уровень", circle, "1", 34, Vector4.zero);
        Stretch(level.rectTransform);
        level.alignment = TextAlignmentOptions.Center;
        level.fontStyle = FontStyles.Bold;

        var clearance = Text("Допуск", bar, "Допуск 3", 22, Vector4.zero);
        Anchor(clearance.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        clearance.rectTransform.sizeDelta = new Vector2(150f, 40f);
        clearance.rectTransform.anchoredPosition = new Vector2(-120f, -34f);
        clearance.alignment = TextAlignmentOptions.MidlineRight;

        // журнал
        var logPanel = Rect("Журнал", root);
        Anchor(logPanel, Vector2.zero, Vector2.zero, Vector2.zero);
        logPanel.sizeDelta = new Vector2(760f, 270f);
        logPanel.anchoredPosition = new Vector2(20f, 20f);
        Img(logPanel, new Color(0.08f, 0.09f, 0.10f, 0.80f));
        var log = Text("Текст журнала", logPanel, "", 18, Vector4.zero);
        Stretch(log.rectTransform, 14f);
        log.alignment = TextAlignmentOptions.TopLeft;
        log.overflowMode = TextOverflowModes.Truncate;

        var hint = Text("Подсказка", root,
            "WASD — камера · Q/E — поворот · колесо — зум · Z/C — этаж ниже/выше · R — повернуть постройку · ПКМ/Esc — отмена · пробел — пауза · 1–6 — скорость\n" +
            "Читы в редакторе: F1 +10% изучения · F2 +10 рассекреч. · F3 −10 доверия · F4 +10 000 · F5 +100 опыта · F6 +1 день",
            15, Vector4.zero);
        Anchor(hint.rectTransform, Vector2.one, Vector2.one, Vector2.one);
        hint.rectTransform.sizeDelta = new Vector2(1500f, 50f);
        hint.rectTransform.anchoredPosition = new Vector2(-20f, -116f);
        hint.alignment = TextAlignmentOptions.TopRight;
        hint.color = new Color(1f, 1f, 1f, 0.55f);

        Set(view, "_root", root.gameObject);
        Set(view, "_date", date);
        Set(view, "_speed", speed);
        Set(view, "_pause", pause);
        SetArray(view, "_speedButtons", speedButtons);
        Set(view, "_money", money);
        Set(view, "_trustFill", trustFill);
        Set(view, "_trust", trust);
        Set(view, "_probation", probation);
        Set(view, "_declassFill", declassFill);
        Set(view, "_declass", declass);
        Set(view, "_xpFill", xpFill);
        Set(view, "_level", level);
        Set(view, "_clearance", clearance);
        Set(view, "_log", log);
        return view;
    }

    private static DocumentView BuildDocument(Transform canvas, GameConfig config)
    {
        var window = Rect("Документ SCP", canvas);
        Anchor(window, Vector2.one, Vector2.one, Vector2.one);
        window.sizeDelta = new Vector2(700f, 690f);
        window.anchoredPosition = new Vector2(-20f, -170f);
        Img(window, Paper);
        var view = window.gameObject.AddComponent<DocumentView>();

        var title = Text("Номер", window, "SCP-000", 38, TopLeft(28f, -20f, 560f, 52f));
        title.color = Ink;
        title.fontStyle = FontStyles.Bold;

        var close = Button("Закрыть", window, "X", Vector4.zero);
        var closeRect = (RectTransform)close.transform;
        Anchor(closeRect, Vector2.one, Vector2.one, Vector2.one);
        closeRect.sizeDelta = new Vector2(48f, 48f);
        closeRect.anchoredPosition = new Vector2(-18f, -18f);

        var nicknameGo = TMP_DefaultControls.CreateInputField(_resources);
        nicknameGo.name = "Кличка";
        nicknameGo.transform.SetParent(window, false);
        Place((RectTransform)nicknameGo.transform, TopLeft(28f, -84f, 644f, 48f));
        var nickname = nicknameGo.GetComponent<TMP_InputField>();
        nickname.pointSize = 24f;
        nickname.characterLimit = 40;
        var placeholder = nickname.placeholder as TMP_Text;
        if (placeholder != null) placeholder.text = "Кличка объекта в вашей Зоне";

        var research = Text("Изученность", window, "Изучено: 0%", 22, TopLeft(28f, -144f, 300f, 34f));
        research.color = Ink;
        var classLabel = Text("Класс", window, "Класс: не назначен", 22, TopLeft(340f, -144f, 332f, 34f));
        classLabel.color = Ink;
        classLabel.alignment = TextAlignmentOptions.MidlineRight;

        var classButtons = new Button[3];
        for (int i = 0; i < classButtons.Length; i++)
            classButtons[i] = Button("Класс " + i, window, config.ScpConfig.ClassName((ObjectClass)i),
                TopLeft(28f + 218f * i, -188f, 208f, 46f));

        // прокрутка текста документа
        var scroll = Rect("Текст", window);
        Anchor(scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        scroll.offsetMin = new Vector2(28f, 24f);
        scroll.offsetMax = new Vector2(-28f, -252f);
        Img(scroll, new Color(0f, 0f, 0f, 0.04f));
        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

        var viewport = Rect("Viewport", scroll);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = Rect("Content", viewport);
        Anchor(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 10, 10);
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var body = Text("Тело документа", content, "", 22, Vector4.zero);
        body.color = Ink;
        body.alignment = TextAlignmentOptions.TopLeft;
        body.richText = true;

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        Set(view, "_root", window.gameObject);
        Set(view, "_title", title);
        Set(view, "_nickname", nickname);
        Set(view, "_research", research);
        Set(view, "_classLabel", classLabel);
        SetArray(view, "_classButtons", classButtons);
        Set(view, "_body", body);
        Set(view, "_scroll", scrollRect);
        Set(view, "_close", close);
        window.gameObject.SetActive(false);   // откроется кликом по камере
        return view;
    }

    private static ScpListView BuildScpList(Transform canvas)
    {
        var panel = Rect("Объекты Зоны", canvas);
        Anchor(panel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        panel.sizeDelta = new Vector2(380f, 336f);
        panel.anchoredPosition = new Vector2(20f, -126f);
        Img(panel, new Color(0.08f, 0.09f, 0.10f, 0.80f));
        var view = panel.gameObject.AddComponent<ScpListView>();

        var title = Text("Заголовок", panel, "Объекты Зоны", 20, TopLeft(14f, -6f, 350f, 30f));
        title.fontStyle = FontStyles.Bold;

        var rows = Rect("Строки", panel);
        Anchor(rows, Vector2.zero, Vector2.one, new Vector2(0.5f, 1f));
        rows.offsetMin = new Vector2(8f, 8f);
        rows.offsetMax = new Vector2(-8f, -40f);
        var layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlHeight = false;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var template = Button("Образец строки", rows, "SCP-000", Vector4.zero);
        ((RectTransform)template.transform).sizeDelta = new Vector2(360f, 54f);
        var label = template.GetComponentInChildren<TMP_Text>();
        label.fontSize = 17f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 11f;
        label.fontSizeMax = 17f;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.margin = new Vector4(10f, 0f, 6f, 0f);
        label.richText = true;
        template.image.color = new Color(1f, 1f, 1f, 0.08f);
        template.gameObject.SetActive(false);

        Set(view, "_root", panel.gameObject);
        Set(view, "_rowTemplate", template);
        Set(view, "_rowParent", rows);
        return view;
    }

    /// <summary>
    /// Стройка: полоса этажа внизу справа (этаж, стрелки, «Строительство [B]», предупреждение, состояние)
    /// и нижняя панель под ней — закладки категорий, карточки построек с прокруткой, «Снос».
    /// Когда панель открыта, полоса этажа поднимается над ней.
    /// </summary>
    private static BuildView BuildBuildMenu(Transform canvas)
    {
        const float width = 1100f, barHeight = 100f, panelHeight = 330f, margin = 20f;

        var root = Rect("Стройка", canvas);
        Stretch(root);
        var view = root.gameObject.AddComponent<BuildView>();

        // ── полоса этажа ──
        var bar = Rect("Полоса этажа", root);
        Anchor(bar, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
        bar.sizeDelta = new Vector2(width, barHeight);
        bar.anchoredPosition = new Vector2(-margin, margin);
        Img(bar, new Color(0.08f, 0.09f, 0.10f, 0.88f));

        var floor = Text("Этаж", bar, "Этаж −1", 22, TopLeft(16f, -8f, 620f, 32f));
        floor.fontStyle = FontStyles.Bold;
        floor.richText = true;
        floor.enableAutoSizing = true;   // «закрыт: нужны лифты…» длиннее строки — ужимаем, а не переносим на предупреждение
        floor.fontSizeMin = 13f;
        floor.fontSizeMax = 22f;
        floor.textWrappingMode = TextWrappingModes.NoWrap;

        var toggle = Button("Строительство", bar, "Строительство  [B]", Vector4.zero);
        var toggleRect = (RectTransform)toggle.transform;
        Anchor(toggleRect, Vector2.one, Vector2.one, Vector2.one);
        toggleRect.sizeDelta = new Vector2(230f, 34f);
        toggleRect.anchoredPosition = new Vector2(-210f, -8f);
        var toggleLabel = toggle.GetComponentInChildren<TMP_Text>();
        toggleLabel.fontSize = 18f;
        toggleLabel.fontStyle = FontStyles.Bold;

        var up = Button("Выше", bar, "▲ C", Vector4.zero);
        var upRect = (RectTransform)up.transform;
        Anchor(upRect, Vector2.one, Vector2.one, Vector2.one);
        upRect.sizeDelta = new Vector2(90f, 34f);
        upRect.anchoredPosition = new Vector2(-108f, -8f);

        var down = Button("Ниже", bar, "▼ Z", Vector4.zero);
        var downRect = (RectTransform)down.transform;
        Anchor(downRect, Vector2.one, Vector2.one, Vector2.one);
        downRect.sizeDelta = new Vector2(90f, 34f);
        downRect.anchoredPosition = new Vector2(-12f, -8f);

        var warning = Text("Предупреждение", bar, "", 17, TopLeft(16f, -44f, width - 32f, 24f));
        warning.color = new Color(1f, 0.6f, 0.35f);
        var status = Text("Состояние", bar, "", 16, TopLeft(16f, -68f, width - 32f, 28f));
        status.enableAutoSizing = true;
        status.fontSizeMin = 11f;
        status.fontSizeMax = 16f;

        // ── панель ──
        var panel = Rect("Панель стройки", root);
        Anchor(panel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f));
        panel.sizeDelta = new Vector2(width, panelHeight);
        panel.anchoredPosition = new Vector2(-margin, margin);
        Img(panel, new Color(0.08f, 0.09f, 0.10f, 0.94f));

        var tabs = Rect("Закладки", panel);
        Anchor(tabs, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f));
        tabs.offsetMin = new Vector2(10f, -50f);
        tabs.offsetMax = new Vector2(-170f, -8f);
        var tabLayout = tabs.gameObject.AddComponent<HorizontalLayoutGroup>();
        tabLayout.spacing = 4f;
        tabLayout.childControlWidth = true;
        tabLayout.childControlHeight = true;
        tabLayout.childForceExpandWidth = true;
        tabLayout.childForceExpandHeight = true;

        var tabTemplate = Button("Образец закладки", tabs, "Категория", Vector4.zero);
        var tabLabel = tabTemplate.GetComponentInChildren<TMP_Text>();
        tabLabel.fontSize = 16f;
        tabLabel.enableAutoSizing = true;
        tabLabel.fontSizeMin = 10f;
        tabLabel.fontSizeMax = 15f;
        tabLabel.fontStyle = FontStyles.Bold;
        tabLabel.textWrappingMode = TextWrappingModes.NoWrap;   // одно слово — ужимаем шрифт, а не вылезаем за закладку
        tabLabel.overflowMode = TextOverflowModes.Ellipsis;
        tabLabel.margin = new Vector4(4f, 0f, 4f, 0f);
        tabTemplate.gameObject.SetActive(false);

        var demolish = Button("Снос", panel, "Снос", Vector4.zero);
        var demolishRect = (RectTransform)demolish.transform;
        Anchor(demolishRect, Vector2.one, Vector2.one, Vector2.one);
        demolishRect.sizeDelta = new Vector2(150f, 42f);
        demolishRect.anchoredPosition = new Vector2(-10f, -8f);

        var scroll = Rect("Карточки", panel);
        Anchor(scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        scroll.offsetMin = new Vector2(10f, 8f);
        scroll.offsetMax = new Vector2(-10f, -58f);
        Img(scroll, new Color(1f, 1f, 1f, 0.03f));
        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();

        var viewport = Rect("Viewport", scroll);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = Rect("Content", viewport);
        Anchor(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f));
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(172f, 100f);
        grid.spacing = new Vector2(6f, 6f);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 6;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content = content;
        scrollRect.viewport = viewport;
        scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 30f;

        var cardTemplate = Button("Образец карточки", content, "Постройка", Vector4.zero);
        var cardLabel = cardTemplate.GetComponentInChildren<TMP_Text>();
        cardLabel.fontSize = 15f;
        cardLabel.enableAutoSizing = true;
        cardLabel.fontSizeMin = 9f;
        cardLabel.fontSizeMax = 15f;
        cardLabel.richText = true;
        cardLabel.alignment = TextAlignmentOptions.MidlineLeft;
        cardLabel.margin = new Vector4(8f, 4f, 6f, 4f);
        cardLabel.overflowMode = TextOverflowModes.Truncate;   // автоподбор ужмёт, а если и так не влезло — обрежем внутри карточки
        cardTemplate.gameObject.SetActive(false);

        panel.gameObject.SetActive(false);   // откроется кнопкой «Строительство» или B

        Set(view, "_root", root.gameObject);
        Set(view, "_floorBar", bar);
        Set(view, "_floor", floor);
        Set(view, "_floorUp", up);
        Set(view, "_floorDown", down);
        Set(view, "_toggle", toggle);
        Set(view, "_toggleLabel", toggleLabel);
        Set(view, "_warning", warning);
        Set(view, "_status", status);
        Set(view, "_panel", panel.gameObject);
        Set(view, "_demolish", demolish);
        Set(view, "_tabTemplate", tabTemplate);
        Set(view, "_tabParent", tabs);
        Set(view, "_cardTemplate", cardTemplate);
        Set(view, "_cardParent", content);
        Set(view, "_scroll", scrollRect);

        var serialized = new SerializedObject(view);
        serialized.FindProperty("_barClosedY").floatValue = margin;
        serialized.FindProperty("_barOpenY").floatValue = margin + panelHeight + 8f;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return view;
    }

    private static GameOverView BuildGameOver(Transform canvas)
    {
        var root = Rect("Конец игры", canvas);
        Stretch(root);
        Img(root, new Color(0f, 0f, 0f, 0.88f));
        var view = root.gameObject.AddComponent<GameOverView>();

        var header = Text("Заголовок", root, "ВЫ ОТСТРАНЕНЫ", 56, Vector4.zero);
        Centered(header.rectTransform, new Vector2(0f, 160f), new Vector2(1200f, 80f));
        header.alignment = TextAlignmentOptions.Center;
        header.fontStyle = FontStyles.Bold;
        header.color = new Color(0.90f, 0.35f, 0.35f);

        var reason = Text("Причина", root, "", 30, Vector4.zero);
        Centered(reason.rectTransform, new Vector2(0f, 20f), new Vector2(1100f, 180f));
        reason.alignment = TextAlignmentOptions.Center;

        var restart = Button("Заново", root, "Начать заново", Vector4.zero);
        Centered((RectTransform)restart.transform, new Vector2(0f, -160f), new Vector2(340f, 70f));

        Set(view, "_root", root.gameObject);
        Set(view, "_reason", reason);
        Set(view, "_restart", restart);
        root.gameObject.SetActive(false);
        return view;
    }

    // ─────────────────────────── кирпичики UI ───────────────────────────

    /// <summary>x, y — отступ от левого верхнего угла родителя (y вниз отрицательный), z, w — размер.</summary>
    private static Vector4 TopLeft(float x, float y, float width, float height) => new Vector4(x, y, width, height);

    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 pivot)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = pivot;
    }

    private static void Stretch(RectTransform rect, float padding = 0f)
    {
        Anchor(rect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f));
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    private static void Centered(RectTransform rect, Vector2 position, Vector2 size)
    {
        Anchor(rect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Place(RectTransform rect, Vector4 topLeft)
    {
        Anchor(rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));
        rect.anchoredPosition = new Vector2(topLeft.x, topLeft.y);
        rect.sizeDelta = new Vector2(topLeft.z, topLeft.w);
    }

    private static Image Img(RectTransform rect, Color color)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Vector4 topLeft)
    {
        var rect = Rect(name, parent);
        if (topLeft != Vector4.zero) Place(rect, topLeft);

        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(string name, Transform parent, string label, Vector4 topLeft)
    {
        var go = TMP_DefaultControls.CreateButton(_resources);
        go.name = name;
        go.transform.SetParent(parent, false);
        if (topLeft != Vector4.zero) Place((RectTransform)go.transform, topLeft);

        var text = go.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.text = label;
            text.fontSize = 22f;
            text.color = Ink;
        }

        return go.GetComponent<Button>();
    }

    private static Image Bar(string name, Transform parent, Vector4 topLeft)
    {
        var back = Rect(name, parent);
        Place(back, topLeft);
        Img(back, new Color(1f, 1f, 1f, 0.12f));

        var fillRect = Rect("Заливка", back);
        Stretch(fillRect);
        var fill = Img(fillRect, new Color(0.36f, 0.80f, 0.45f));
        fill.sprite = _resources.standard;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 0.5f;
        return fill;
    }

    // ─────────────────────────── служебное ───────────────────────────

    private static void ClearScene(Scene scene)
    {
        var doomed = new HashSet<string> { "Main Camera", "EventSystem" };

        foreach (var go in scene.GetRootGameObjects())
            if (go.name.StartsWith(Prefix) || doomed.Contains(go.name))
                Object.DestroyImmediate(go);
    }

    private static void Set(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field);
        if (property == null)
        {
            Log($"  нет поля {field} у {target.GetType().Name}");
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object target, string field, Object[] values)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(field);
        if (property == null)
        {
            Log($"  нет поля {field} у {target.GetType().Name}");
            return;
        }

        property.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static void EnsureInBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (scenes.Exists(s => s.path == path)) return;
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void Step(string text) => Log("  ✓ " + text);

    private static void Log(string text)
    {
        try { File.AppendAllText(LogPath, text + Environment.NewLine); }
        catch (Exception) { /* журнал не важнее сборки */ }
    }
}
