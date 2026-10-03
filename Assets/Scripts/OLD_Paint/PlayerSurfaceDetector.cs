using UnityEngine;

public class PlayerSurfaceDetector : MonoBehaviour
{
    [SerializeField] private PlayerEffects effects;

    [Tooltip("How far down to cast, measured from Ray Origin Offset. Should just clear the paint collider's height plus a little slack.")]
    [SerializeField] private float rayDistance = 0.3f;

    [Tooltip("Offset from transform.position to start the ray. Assumes transform.position is at the player's feet - raise this if your pivot is at the character's center instead.")]
    [SerializeField] private Vector3 rayOriginOffset = new Vector3(0f, 0.1f, 0f);

    private void FixedUpdate()
    {
        CheckSurface();
    }

    private void CheckSurface()
    {
        Vector3 origin = transform.position + rayOriginOffset;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayOriginOffset.y + rayDistance, PaintLayers.Mask))
        {
            PaintColor color = PaintLayers.ColorFromLayer(hit.collider.gameObject.layer);
            effects.ApplyEffect(color);
            Debug.Log("Player is on: " + color);
        }
        else
        {
            effects.ClearEffect();
            Debug.Log("Player is on: None");
        }
    }
}