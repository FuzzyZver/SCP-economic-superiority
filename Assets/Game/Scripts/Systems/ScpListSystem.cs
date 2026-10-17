using UnityEngine;
using UnityEngine.Events;
using Leopotam.Ecs;

/// <summary>
/// Список объектов слева: номер, кличка и где объект сейчас.
/// Клик по объекту без камеры берёт его «в руки» — дальше игрок указывает камеру (BuildSystem);
/// клик по размещённому — документ. Строки переписываются раз в полсекунды — чаще незачем.
/// </summary>
public class ScpListSystem : Injects, IEcsInitSystem, IEcsRunSystem, IEcsDestroySystem
{
    private const float RefreshInterval = 0.5f;

    private static readonly Color Contained = new Color(0.75f, 0.95f, 0.75f);
    private static readonly Color Cut = new Color(0.95f, 0.85f, 0.45f);
    private static readonly Color Loose = new Color(1f, 0.5f, 0.45f);

    private EcsWorld _world;

    private EcsFilter<ScpFlag, ScpAttribute, ContainedAttribute, NicknameAttribute> _scps;
    private EcsFilter<BuildingFlag, BuildingAttribute> _buildings;

    private ScpId[] _ids = new ScpId[0];
    private UnityEngine.UI.Button[] _rows = new UnityEngine.UI.Button[0];
    private UnityAction[] _handlers = new UnityAction[0];
    private string[] _texts = new string[0];
    private float _timer;

    public void Init()
    {
        var view = UI.ScpList;
        if (view == null) return;

        int count = _scps.GetEntitiesCount();
        _ids = new ScpId[count];
        _texts = new string[count];
        int n = 0;
        foreach (var i in _scps) _ids[n++] = _scps.Get2(i).Id;

        _rows = view.CreateRows(count);
        _handlers = new UnityAction[_rows.Length];
        for (int i = 0; i < _rows.Length; i++)
        {
            var id = _ids[i];
            _handlers[i] = () => Click(id);
            _rows[i].onClick.AddListener(_handlers[i]);
        }
    }

    private void Click(ScpId id)
    {
        foreach (var i in _scps)
        {
            if (_scps.Get2(i).Id != id) continue;
            if (_scps.Get3(i).IsContained) break;

            ref var select = ref _world.NewEntity().Get<SelectToolEvent>();
            select.Tool = BuildTool.PlaceScp;
            select.Scp = id;
            return;
        }

        _world.NewEntity().Get<ScpClickedEvent>().Id = id;
    }

    public void Destroy()
    {
        for (int i = 0; i < _rows.Length && i < _handlers.Length; i++)
            if (_rows[i] != null) _rows[i].onClick.RemoveListener(_handlers[i]);
    }

    public void Run()
    {
        var view = UI.ScpList;
        if (view == null) return;

        _timer -= Time.unscaledDeltaTime;
        if (_timer > 0f) return;
        _timer = RefreshInterval;

        var scpConfig = GameConfig.ScpConfig;
        var buildings = GameConfig.BuildingsConfig;

        int row = 0;
        foreach (var i in _scps)
        {
            if (row >= _ids.Length) break;

            var definition = scpConfig.Get(_scps.Get2(i).Id);
            string number = definition != null ? definition.Number : _scps.Get2(i).Id.ToString();
            int roomId = _scps.Get3(i).RoomId;

            string status;
            Color color;
            if (roomId == ContainedAttribute.NoRoom)
            {
                status = "ждёт камеру: " + scpConfig.ContainmentName(definition != null ? definition.Containment : ContainmentType.None) + " · клик — разместить";
                color = Loose;
            }
            else if (TryRoom(roomId, out int depth, out bool connected) && connected)
            {
                status = "в камере · " + buildings.FloorName(depth);
                color = Contained;
            }
            else
            {
                status = "камера без доступа";
                color = Cut;
            }

            string text = $"<b>{number}</b> «{_scps.Get4(i).Value}»\n<size=80%>{status}</size>";
            if (_texts[row] != text)
            {
                _texts[row] = text;
                view.SetRow(row, text, color);
            }

            row++;
        }
    }

    private bool TryRoom(int roomId, out int depth, out bool connected)
    {
        foreach (var b in _buildings)
        {
            ref var building = ref _buildings.Get2(b);
            if (building.Id != roomId) continue;

            depth = building.Depth;
            connected = _buildings.GetEntity(b).Has<ConnectedFlag>();
            return true;
        }

        depth = 0;
        connected = false;
        return false;
    }
}
