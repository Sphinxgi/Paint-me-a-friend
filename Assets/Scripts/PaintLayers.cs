using UnityEngine;

public static class PaintLayers
{
    private const string BlueLayerName = "BluePaint";
    private const string GreenLayerName = "GreenPaint";

    public static readonly int Blue = LayerMask.NameToLayer(BlueLayerName);
    public static readonly int Green = LayerMask.NameToLayer(GreenLayerName);

    public static readonly LayerMask Mask = (1 << Blue) | (1 << Green);

    static PaintLayers()
    {
        if (Blue == -1 || Green == -1)
        {
            Debug.LogError($"PaintLayers: layer(s) not found. Create layers named " +
                $"'{BlueLayerName}' and '{GreenLayerName}' in Project Settings > Tags and Layers.");
        }
    }

    public static PaintColor ColorFromLayer(int layer)
    {
        if (layer == Blue) return PaintColor.Blue;
        if (layer == Green) return PaintColor.Green;
        return PaintColor.None;
    }
}