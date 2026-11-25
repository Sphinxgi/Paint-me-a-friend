using UnityEngine;
using System;

public class PlayerPaintEffects : MonoBehaviour
{
    [SerializeField] private float blueSpeedMultiplier = 2f;
    [SerializeField] private float greenJumpMultiplier = 1.5f;

    public float SpeedMultiplier { get; private set; } = 1f;
    public float JumpMultiplier { get; private set; } = 1f;

    public void SetPaint(PaintType paintType)
    {
        SpeedMultiplier = 1f;
        JumpMultiplier = 1f;

        switch (paintType)
        {
            case PaintType.Blue:
                SpeedMultiplier = blueSpeedMultiplier;
                break;

            case PaintType.Green:
                JumpMultiplier = greenJumpMultiplier;
                break;
        }
    }

    public void ClearPaint()
    {
        SpeedMultiplier = 1f;
        JumpMultiplier = 1f;
    }
}