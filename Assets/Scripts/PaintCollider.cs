using System.Collections.Generic;
using UnityEngine;

public class PaintCollider : MonoBehaviour
{
    [Tooltip("Prefab with a trigger Collider on it. No renderer needed - purely logic.")]
    [SerializeField] private GameObject colliderPrefab;

    [Tooltip("World-space size the spawned collider is scaled to. Should roughly match PaintGridManager's cell size.")]
    [SerializeField] private float colliderSize = 0.5f;

    private readonly Dictionary<Vector2Int, GameObject> activeColliders = new Dictionary<Vector2Int, GameObject>();

    private void OnEnable()
    {
        PaintGridManager.OnCellPainted += HandleCellPainted;
    }

    private void OnDisable()
    {
        PaintGridManager.OnCellPainted -= HandleCellPainted;
    }

    private void HandleCellPainted(Vector2Int key, PaintColor color, Vector3 worldPos)
    {
        if (activeColliders.TryGetValue(key, out GameObject existing) && existing != null)
        {
            Destroy(existing);
            activeColliders.Remove(key);
        }

        if (color == PaintColor.None)
        {
            return;
        }

        GameObject instance = Instantiate(colliderPrefab, worldPos, Quaternion.identity, transform);
        instance.layer = color == PaintColor.Blue ? PaintLayers.Blue : PaintLayers.Green;
        instance.transform.localScale = Vector3.one * colliderSize;
        activeColliders[key] = instance;
    }
}