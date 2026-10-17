using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Призрак под курсором: клетки будущей постройки или коридора, маркеры входов
/// и зона (откачка насосной, привязанные к посту камеры).
/// Цвет выбирают системы: зелёный — можно, жёлтый — можно, но не заработает, красный — нельзя.
/// Меши перестраиваются, только если клетки или цвет поменялись.
/// </summary>
public class GhostActor : MonoBehaviour
{
    private MaterialPropertyBlock _block;   // не в инициализаторе поля: Unity запрещает создавать его до Awake

    private Material _material;
    private CellMeshActor _cells;
    private CellMeshActor _zone;
    private readonly List<Renderer> _doors = new List<Renderer>();
    private int _hash = int.MinValue;
    private int _zoneHash = int.MinValue;

    public void Init(Material material)
    {
        _material = material;
        _cells = Mesh("Клетки");
        _zone = Mesh("Зона");
        Hide();
    }

    public void Show(List<Vector2Int> cells, BuildingsConfig config, int depth, Color color)
    {
        if (_cells == null) return;

        int hash = Hash(cells, depth, color);
        _cells.gameObject.SetActive(true);
        if (hash == _hash) return;

        _hash = hash;
        _cells.SetCells(cells, config, depth, 0.12f, 0.05f);
        _cells.SetColor(color);
    }

    /// <summary>Второй слой под клетками: зона откачки насосной, камеры поста.</summary>
    public void ShowZone(List<Vector2Int> cells, BuildingsConfig config, int depth, Color color)
    {
        if (_zone == null) return;

        int hash = Hash(cells, depth, color);
        _zone.gameObject.SetActive(cells.Count > 0);
        if (hash == _zoneHash) return;

        _zoneHash = hash;
        _zone.SetCells(cells, config, depth, 0.08f, 0.2f);
        _zone.SetColor(color);
    }

    public void HideZone()
    {
        if (_zone != null) _zone.gameObject.SetActive(false);
        _zoneHash = int.MinValue;
    }

    /// <summary>Маркеры входов: по кубику на каждую клетку перед входом.</summary>
    public void ShowDoors(List<Vector3> positions, Color color)
    {
        while (_doors.Count < positions.Count)
        {
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Вход";
            Destroy(door.GetComponent<Collider>());
            door.transform.SetParent(transform, false);
            door.transform.localScale = new Vector3(0.6f, 0.3f, 0.6f);
            var renderer = door.GetComponent<Renderer>();
            renderer.sharedMaterial = _material;
            _doors.Add(renderer);
        }

        if (_block == null) _block = new MaterialPropertyBlock();
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);

        for (int i = 0; i < _doors.Count; i++)
        {
            bool show = i < positions.Count;
            _doors[i].gameObject.SetActive(show);
            if (!show) continue;

            _doors[i].transform.position = positions[i] + Vector3.up * 0.25f;
            _doors[i].SetPropertyBlock(_block);
        }
    }

    public void Hide()
    {
        if (_cells != null) _cells.gameObject.SetActive(false);
        foreach (var door in _doors) door.gameObject.SetActive(false);
        _hash = int.MinValue;
        HideZone();
    }

    private CellMeshActor Mesh(string title)
    {
        var go = new GameObject(title);
        go.transform.SetParent(transform, false);
        var mesh = go.AddComponent<CellMeshActor>();
        mesh.Init(_material);
        return mesh;
    }

    private static int Hash(List<Vector2Int> cells, int depth, Color color)
    {
        unchecked
        {
            int hash = 17 * 31 + depth;
            hash = hash * 31 + color.GetHashCode();
            hash = hash * 31 + cells.Count;
            foreach (var cell in cells) hash = hash * 31 + cell.x * 7919 + cell.y;
            return hash;
        }
    }
}
