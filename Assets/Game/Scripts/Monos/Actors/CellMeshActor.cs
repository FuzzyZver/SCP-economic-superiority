using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Один меш из квадратиков-клеток: коридоры этажа, призрак постройки.
/// Тысячи клеток рисуются одним объектом, а не тысячей кубов.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class CellMeshActor : MonoBehaviour
{
    private readonly List<Vector3> _vertices = new List<Vector3>();
    private readonly List<int> _triangles = new List<int>();
    private Mesh _mesh;
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _block;

    public void Init(Material material)
    {
        _mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        GetComponent<MeshFilter>().sharedMesh = _mesh;

        _renderer = GetComponent<MeshRenderer>();
        _renderer.sharedMaterial = material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _block = new MaterialPropertyBlock();
    }

    public void SetColor(Color color)
    {
        if (_renderer == null) return;
        _block.SetColor("_BaseColor", color);
        _block.SetColor("_Color", color);
        _renderer.SetPropertyBlock(_block);
    }

    /// <summary>lift — подъём над полом этажа, inset — зазор по краю клетки.</summary>
    public void SetCells(List<Vector2Int> cells, BuildingsConfig config, int depth, float lift, float inset)
    {
        if (_mesh == null) return;

        _vertices.Clear();
        _triangles.Clear();

        foreach (var cell in cells)
        {
            var corner = transform.InverseTransformPoint(config.CellCorner(cell, depth) + Vector3.up * lift);
            int start = _vertices.Count;

            _vertices.Add(corner + new Vector3(inset, 0f, inset));
            _vertices.Add(corner + new Vector3(inset, 0f, 1f - inset));
            _vertices.Add(corner + new Vector3(1f - inset, 0f, 1f - inset));
            _vertices.Add(corner + new Vector3(1f - inset, 0f, inset));

            _triangles.Add(start);
            _triangles.Add(start + 1);
            _triangles.Add(start + 2);
            _triangles.Add(start);
            _triangles.Add(start + 2);
            _triangles.Add(start + 3);
        }

        Commit();
    }

    /// <summary>Набор коробок одним мешем: стены коридоров. centers и sizes — в мире, попарно.</summary>
    public void SetBoxes(List<Vector3> centers, List<Vector3> sizes)
    {
        if (_mesh == null) return;

        _vertices.Clear();
        _triangles.Clear();

        for (int i = 0; i < centers.Count && i < sizes.Count; i++)
        {
            var c = transform.InverseTransformPoint(centers[i]);
            var h = sizes[i] * 0.5f;
            Face(c, new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z), new Vector3(0f, h.y, 0f));    // верх
            Face(c, new Vector3(0f, 0f, h.z), new Vector3(0f, h.y, 0f), new Vector3(h.x, 0f, 0f));    // восток
            Face(c, new Vector3(0f, h.y, 0f), new Vector3(0f, 0f, h.z), new Vector3(-h.x, 0f, 0f));   // запад
            Face(c, new Vector3(0f, h.y, 0f), new Vector3(h.x, 0f, 0f), new Vector3(0f, 0f, h.z));    // север
            Face(c, new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f), new Vector3(0f, 0f, -h.z));   // юг
        }

        Commit();
    }

    /// <summary>Грань коробки: центр грани = c + n, стороны u и v (половины), нормаль наружу по n.</summary>
    private void Face(Vector3 c, Vector3 u, Vector3 v, Vector3 n)
    {
        int start = _vertices.Count;
        var center = c + n;
        _vertices.Add(center - u - v);
        _vertices.Add(center - u + v);
        _vertices.Add(center + u + v);
        _vertices.Add(center + u - v);

        // порядок вершин подбираем по нормали, чтобы грань смотрела наружу
        var normal = Vector3.Cross(_vertices[start + 1] - _vertices[start], _vertices[start + 2] - _vertices[start]);
        bool flip = Vector3.Dot(normal, n) < 0f;

        _triangles.Add(start);
        _triangles.Add(flip ? start + 2 : start + 1);
        _triangles.Add(flip ? start + 1 : start + 2);
        _triangles.Add(start);
        _triangles.Add(flip ? start + 3 : start + 2);
        _triangles.Add(flip ? start + 2 : start + 3);
    }

    private void Commit()
    {
        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.SetTriangles(_triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    public void SetShadows(bool cast)
    {
        if (_renderer != null) _renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
    }
}
