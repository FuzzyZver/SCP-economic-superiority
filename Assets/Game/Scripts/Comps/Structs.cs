using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Один этаж Зоны на сетке 1 клетка = 1 м = 1 юнит.
/// Building — id постройки в клетке (Empty — пусто). Corridor — 0 или номер типа коридора + 1:
/// растр, который CorridorSystem пересобирает из отрезков Segments. Reached — клетка коридора связана
/// с главным въездом (считает ConnectivitySystem). Модули коридора (КПП, ворота Теслы) стоят прямо
/// на коридоре: у таких клеток есть и Building, и Corridor.
/// Water — 0 сухо, 1 грунтовые воды, 2 подземная река; Drain — какую воду здесь откачивают насосные.
/// </summary>
public class FloorGrid
{
    public const int Empty = -1;

    public readonly int Size;
    public readonly int Depth;     // 0 — поверхность, 1 — этаж −1, 2 — этаж −2…
    public readonly int[] Building;
    public readonly byte[] Corridor;
    public readonly bool[] Reached;
    public readonly byte[] Water;
    public readonly byte[] Drain;
    public readonly List<CorridorSegment> Segments = new List<CorridorSegment>();
    public bool Open;              // можно ли здесь строить

    public FloorGrid(int size, int depth)
    {
        Size = size;
        Depth = depth;
        Building = new int[size * size];
        Corridor = new byte[size * size];
        Reached = new bool[size * size];
        Water = new byte[size * size];
        Drain = new byte[size * size];
        for (int i = 0; i < Building.Length; i++) Building[i] = Empty;
    }

    public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Size && cell.y < Size;
    public int Index(Vector2Int cell) => cell.y * Size + cell.x;

    public int BuildingAt(Vector2Int cell) => InBounds(cell) ? Building[Index(cell)] : Empty;
    public bool IsCorridor(Vector2Int cell) => InBounds(cell) && Corridor[Index(cell)] > 0;
    public byte TierAt(Vector2Int cell) => InBounds(cell) ? Corridor[Index(cell)] : (byte)0;
    public bool IsReached(Vector2Int cell) => InBounds(cell) && Reached[Index(cell)];

    /// <summary>Пусто под комнату: ни постройки, ни коридора.</summary>
    public bool IsFree(Vector2Int cell) => InBounds(cell) && Building[Index(cell)] == Empty && Corridor[Index(cell)] == 0;

    /// <summary>Вода стоит и её никто не откачивает.</summary>
    public bool IsWet(Vector2Int cell) => InBounds(cell) && Water[Index(cell)] > Drain[Index(cell)];
}

/// <summary>
/// Отрезок коридора: прямой кусок вдоль X или Z с осью Center, от From до To включительно.
/// Ширина берётся из типа: клетки поперёк оси — Center + Low … Center + High.
/// Коридоры рисуются отрезками, как дороги в градостроителе: так они не слипаются в залы,
/// а сеть отрезков потом станет путями для персонала.
/// </summary>
public class CorridorSegment
{
    public int Id;
    public bool AlongX;
    public int Center;
    public int From;
    public int To;
    public int Type;     // индекс в BuildingsConfig.Corridors

    public static int Low(int width) => -(width - 1) / 2;
    public static int High(int width) => width / 2;

    public int Length => To - From + 1;

    public bool Contains(Vector2Int cell, int width)
    {
        int along = AlongX ? cell.x : cell.y;
        int across = AlongX ? cell.y : cell.x;
        return along >= From && along <= To && across >= Center + Low(width) && across <= Center + High(width);
    }

    public Vector2Int Cell(int along, int across) => AlongX ? new Vector2Int(along, across) : new Vector2Int(across, along);

    public CorridorSegment Copy() =>
        new CorridorSegment { Id = Id, AlongX = AlongX, Center = Center, From = From, To = To, Type = Type };
}
