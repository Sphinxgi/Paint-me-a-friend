using System;
using System.Collections.Generic;
using UnityEngine;

public enum PaintColor
{
    None,
    Blue,
    Green
}

public struct PaintCellData
{
    public PaintColor Color;
    public Vector3 WorldPosition;
}

public class PaintGridManager : MonoBehaviour
{
    [Tooltip("World-space size of one grid cell. Smaller = higher resolution.")]
    [SerializeField] private float cellSize = 0.5f;

    public float CellSize => cellSize;

    private readonly Dictionary<Vector2Int, PaintCellData> cells = new Dictionary<Vector2Int, PaintCellData>();

    public static event Action<Vector2Int, PaintColor, Vector3> OnCellPainted;

    public Vector2Int GetCellKey(Vector3 worldPos)
    {
        int x = Mathf.RoundToInt(worldPos.x / cellSize);
        int z = Mathf.RoundToInt(worldPos.z / cellSize);
        return new Vector2Int(x, z);
    }

    public void SetCell(Vector3 worldPos, PaintColor color)
    {
        SetCell(GetCellKey(worldPos), color, worldPos);
    }

    public void SetCell(Vector2Int key, PaintColor color)
    {
        SetCell(key, color, CellToWorld(key));
    }

    public void SetCell(Vector2Int key, PaintColor color, Vector3 worldPos)
    {
        if (cells.TryGetValue(key, out PaintCellData existing) && existing.Color == color)
        {
            return;
        }

        cells[key] = new PaintCellData { Color = color, WorldPosition = worldPos };
        OnCellPainted?.Invoke(key, color, worldPos);
    }

    public PaintColor GetCell(Vector2Int key)
    {
        return cells.TryGetValue(key, out PaintCellData data) ? data.Color : PaintColor.None;
    }

    public bool TryGetCell(Vector2Int key, out PaintColor color)
    {
        if (cells.TryGetValue(key, out PaintCellData data))
        {
            color = data.Color;
            return true;
        }

        color = PaintColor.None;
        return false;
    }

    public Vector3 CellToWorld(Vector2Int key, float y = 0f)
    {
        return new Vector3(key.x * cellSize, y, key.y * cellSize);
    }
}