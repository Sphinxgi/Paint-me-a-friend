using UnityEngine;

public class PlayerEffects : MonoBehaviour
{
    [SerializeField] private float speedBoostMultiplier = 1.5f;
    [SerializeField] private float jumpBoostMultiplier = 1.5f;

    public float SpeedMultiplier { get; private set; } = 1f;
    public float JumpMultiplier { get; private set; } = 1f;

    private PaintColor currentEffect = PaintColor.None;

    public void ApplyEffect(PaintColor color)
    {
        if (color == currentEffect)
        {
            return;
        }

        currentEffect = color;

        switch (color)
        {
            case PaintColor.Blue:
                Debug.Log("Apply Blue effect: Speed Boost");
                SpeedMultiplier = speedBoostMultiplier;
                JumpMultiplier = 1f;
                break;
            case PaintColor.Green:
                Debug.Log("Apply Green effect: Jump Boost");
                SpeedMultiplier = 1f;
                JumpMultiplier = jumpBoostMultiplier;
                break;
            default:
                Debug.Log("Clear effect");
                SpeedMultiplier = 1f;
                JumpMultiplier = 1f;
                break;
        }
    }

    public void ClearEffect()
    {
        ApplyEffect(PaintColor.None);
    }
}