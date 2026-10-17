using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Этаж Зоны: пол с сеткой, вода, коридоры одним мешем на каждый тип, стены коридоров
/// и родитель для построек. Сам ничего не решает — что рисовать, говорят системы.
/// </summary>
public class FloorActor : MonoBehaviour
{
    private const int TexturePixels = 32;
    private const float WallThickness = 0.12f;

    [SerializeField] private int _depth;   // 0 — поверхность, 1 — этаж −1…

    private readonly List<CellMeshActor> _corridorMeshes = new List<CellMeshActor>();
    private readonly List<List<Vector2Int>> _groups = new List<List<Vector2Int>>();
    private readonly List<Vector3> _wallCenters = new List<Vector3>();
    private readonly List<Vector3> _wallSizes = new List<Vector3>();
    private readonly List<Vector2Int>[] _water = { new List<Vector2Int>(), new List<Vector2Int>(), new List<Vector2Int>() };
    private CellMeshActor[] _waterMeshes;
    private CellMeshActor _walls;
    private BuildingsConfig _config;
    private Transform _rooms;
    private Material _groundMaterial;

    public int Depth => _depth;

    public void Init(BuildingsConfig config)
    {
        _config = config;

        _rooms = new GameObject("Постройки").transform;
        _rooms.SetParent(transform, false);

        CreateGround();
        SetOpen(_depth <= 1);

        _walls = Mesh("Стены коридоров");
        _walls.SetColor(config.CorridorWallColor);
        _walls.SetShadows(true);

        _waterMeshes = new[] { Mesh("Грунтовые воды"), Mesh("Подземные реки"), Mesh("Откачанная вода") };
        _waterMeshes[0].SetColor(config.GroundwaterColor);
        _waterMeshes[1].SetColor(config.RiverColor);
        _waterMeshes[2].SetColor(config.DrainedColor);
    }

    public void SetVisible(bool value) => gameObject.SetActive(value);

    /// <summary>Закрытый этаж темнеет и краснеет: строить нельзя, пока нет лифтов этажом выше.</summary>
    public void SetOpen(bool open)
    {
        if (_groundMaterial == null) return;

        var color = _depth == 0 ? _config.SurfaceColor : open ? _config.UndergroundColor : _config.LockedColor;
        if (_depth > 0) color *= 1.25f;   // сетка в текстуре темнит заливку, возвращаем яркость
        color.a = 1f;
        _groundMaterial.color = color;
    }

    public RoomActor CreateRoom(string title)
    {
        var go = new GameObject(title);
        go.transform.SetParent(_rooms, false);
        return go.AddComponent<RoomActor>();
    }

    /// <summary>
    /// Коридоры группами «тип × связан/не связан»: несвязанные красноватые, сразу видно, где обрыв.
    /// Стены — по краю коридора там, где рядом пусто; у комнат свои стены, у перекрёстков проёмы.
    /// </summary>
    public void RebuildCorridors(FloorGrid grid)
    {
        int types = _config.Corridors != null ? _config.Corridors.Length : 0;
        int groups = types * 2;

        while (_corridorMeshes.Count < groups)
        {
            _corridorMeshes.Add(Mesh("Коридоры " + _corridorMeshes.Count));
            _groups.Add(new List<Vector2Int>());
        }

        foreach (var list in _groups) list.Clear();
        _wallCenters.Clear();
        _wallSizes.Clear();

        float height = _config.CorridorWallHeight;
        float y = _config.FloorY(_depth) + height * 0.5f;

        for (int z = 0; z < grid.Size; z++)
        {
            for (int x = 0; x < grid.Size; x++)
            {
                int i = z * grid.Size + x;
                int tier = grid.Corridor[i];
                if (tier == 0 || tier > types) continue;

                var cell = new Vector2Int(x, z);
                _groups[(tier - 1) * 2 + (grid.Reached[i] ? 0 : 1)].Add(cell);

                var corner = _config.CellCorner(cell, _depth);
                Wall(grid, cell + Vector2Int.left, new Vector3(corner.x + WallThickness * 0.5f, y, corner.z + 0.5f), new Vector3(WallThickness, height, 1f));
                Wall(grid, cell + Vector2Int.right, new Vector3(corner.x + 1f - WallThickness * 0.5f, y, corner.z + 0.5f), new Vector3(WallThickness, height, 1f));
                Wall(grid, cell + Vector2Int.down, new Vector3(corner.x + 0.5f, y, corner.z + WallThickness * 0.5f), new Vector3(1f, height, WallThickness));
                Wall(grid, cell + Vector2Int.up, new Vector3(corner.x + 0.5f, y, corner.z + 1f - WallThickness * 0.5f), new Vector3(1f, height, WallThickness));
            }
        }

        for (int g = 0; g < groups; g++)
        {
            var definition = _config.Corridors[g / 2];
            bool reached = g % 2 == 0;
            _corridorMeshes[g].SetCells(_groups[g], _config, _depth, 0.02f, 0f);
            _corridorMeshes[g].SetColor(reached ? definition.Color : Color.Lerp(definition.Color, _config.UnreachedCorridorColor, 0.6f));
        }

        _walls.SetBoxes(_wallCenters, _wallSizes);
    }

    /// <summary>Вода на полу: грунтовые, реки и то, что уже откачивают насосные.</summary>
    public void RebuildWater(FloorGrid grid)
    {
        if (_waterMeshes == null) return;
        foreach (var list in _water) list.Clear();

        for (int z = 0; z < grid.Size; z++)
        {
            for (int x = 0; x < grid.Size; x++)
            {
                int i = z * grid.Size + x;
                byte water = grid.Water[i];
                if (water == 0) continue;

                var cell = new Vector2Int(x, z);
                if (grid.Drain[i] >= water) _water[2].Add(cell);
                else _water[water >= 2 ? 1 : 0].Add(cell);
            }
        }

        for (int k = 0; k < _waterMeshes.Length; k++)
            _waterMeshes[k].SetCells(_water[k], _config, _depth, 0.006f, 0f);
    }

    private void Wall(FloorGrid grid, Vector2Int neighbour, Vector3 center, Vector3 size)
    {
        if (grid.IsCorridor(neighbour)) return;                               // продолжение коридора или перекрёсток
        if (grid.BuildingAt(neighbour) != FloorGrid.Empty) return;           // у комнаты своя стена и проём
        _wallCenters.Add(center);
        _wallSizes.Add(size);
    }

    private CellMeshActor Mesh(string title)
    {
        var go = new GameObject(title);
        go.transform.SetParent(transform, false);
        var mesh = go.AddComponent<CellMeshActor>();
        mesh.Init(_config.BaseMaterial);
        return mesh;
    }

    private void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "Пол этажа";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(transform, false);

        float size = _config.GridSize;
        ground.transform.position = new Vector3(0f, _config.FloorY(_depth) - 0.02f, 0f);
        ground.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        ground.transform.localScale = new Vector3(size, size, 1f);

        _groundMaterial = new Material(_config.BaseMaterial);
        if (_depth > 0)
        {
            _groundMaterial.mainTexture = GridTexture();
            _groundMaterial.mainTextureScale = new Vector2(size, size);
        }

        ground.GetComponent<Renderer>().sharedMaterial = _groundMaterial;
    }

    private Texture2D GridTexture()
    {
        var texture = new Texture2D(TexturePixels, TexturePixels, TextureFormat.RGBA32, true)
        {
            name = "Сетка этажа",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4,
        };

        var fill = new Color(0.8f, 0.8f, 0.8f, 1f);
        var line = Color.Lerp(fill, Color.white, 0.6f);
        var pixels = new Color[TexturePixels * TexturePixels];
        for (int y = 0; y < TexturePixels; y++)
            for (int x = 0; x < TexturePixels; x++)
                pixels[y * TexturePixels + x] = x == 0 || y == 0 ? line : fill;

        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }
}
