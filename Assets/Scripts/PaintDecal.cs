using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class PaintDecal : MonoBehaviour
{
    [Tooltip("Prefab with a Decal Projector + blue decal material.")]
    [SerializeField] private GameObject blueDecalPrefab;

    [Tooltip("Prefab with a Decal Projector + green decal material.")]
    [SerializeField] private GameObject greenDecalPrefab;

    [Tooltip("Decal footprint (width/height) in world units. Depth is left as authored on the prefab.")]
    [SerializeField] private float decalSize = 0.5f;

    [Tooltip("How far above the paint point to spawn the projector before it projects downward. Needs to clear any surface bumps within its projection depth.")]
    [SerializeField] private float heightOffset = 0.5f;

    private readonly Dictionary<Vector2Int, GameObject> activeDecals = new Dictionary<Vector2Int, GameObject>();

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
        if (activeDecals.TryGetValue(key, out GameObject existing) && existing != null)
        {
            Destroy(existing);
            activeDecals.Remove(key);
        }

        if (color == PaintColor.None)
        {
            return;
        }

        GameObject prefab = color == PaintColor.Blue ? blueDecalPrefab : greenDecalPrefab;

        Vector3 spawnPos = worldPos + Vector3.up * heightOffset;
        GameObject instance = Instantiate(prefab, spawnPos, Quaternion.LookRotation(Vector3.up), transform);

        DecalProjector projector = instance.GetComponent<DecalProjector>();
        if (projector != null)
        {
            Vector3 size = projector.size;
            projector.size = new Vector3(decalSize, decalSize, size.z);
        }
        else
        {
            Debug.LogWarning($"PaintDecal: prefab for {color} has no DecalProjector component.");
        }

        activeDecals[key] = instance;
    }
}