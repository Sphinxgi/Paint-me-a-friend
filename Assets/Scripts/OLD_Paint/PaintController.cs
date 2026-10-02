using UnityEngine;

public class PaintController : MonoBehaviour
{
    [SerializeField] private PaintGridManager gridManager;
    [SerializeField] private Camera paintCamera;

    [Tooltip("Layer(s) the paint raycast is allowed to hit. Should be your paintable ground/wall meshes only - not the player, not Blue/Green paint colliders.")]
    [SerializeField] private LayerMask paintableMask;

    [SerializeField] private float paintRadius = 1.5f;
    [SerializeField] private float maxPaintDistance = 100f;

    [Tooltip("Draws the paint ray in the Scene view every frame - green if it hit a paintable surface, red if it didn't. Debug-only, safe to leave on.")]
    [SerializeField] private bool showDebugRay = true;

    private void Reset()
    {
        paintCamera = Camera.main;
    }

    private void Update()
    {
        if (showDebugRay)
        {
            DrawDebugRay();
        }

        if (Input.GetMouseButton(0))
        {
            TryPaint(PaintColor.Blue);
        }
        else if (Input.GetMouseButton(1))
        {
            TryPaint(PaintColor.Green);
        }
    }

    private void DrawDebugRay()
    {
        bool hitSomething = Physics.Raycast(paintCamera.transform.position, paintCamera.transform.forward,
            out RaycastHit hit, maxPaintDistance, paintableMask);

        Color rayColor = hitSomething ? Color.green : Color.red;
        float length = hitSomething ? hit.distance : maxPaintDistance;
        Debug.DrawRay(paintCamera.transform.position, paintCamera.transform.forward * length, rayColor);
    }

    private void TryPaint(PaintColor color)
    {
        Ray ray = new Ray(paintCamera.transform.position, paintCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, maxPaintDistance, paintableMask))
        {
            Debug.Log($"PaintController: painted {color} at {hit.point} on '{hit.collider.name}'.");
            PaintCircle(hit.point, color);
        }
        else
        {
            Debug.Log($"PaintController: {color} paint attempt hit nothing within {maxPaintDistance}m on mask {paintableMask.value}.");
        }
    }

    private void PaintCircle(Vector3 center, PaintColor color)
    {
        float cellSize = gridManager.CellSize;
        Vector2Int centerKey = gridManager.GetCellKey(center);
        int radiusInCells = Mathf.CeilToInt(paintRadius / cellSize);

        for (int dx = -radiusInCells; dx <= radiusInCells; dx++)
        {
            for (int dz = -radiusInCells; dz <= radiusInCells; dz++)
            {
                Vector2Int key = new Vector2Int(centerKey.x + dx, centerKey.y + dz);
                Vector3 cellWorldPos = gridManager.CellToWorld(key, center.y);

                // Flatten to XZ distance so height doesn't skew the circle check.
                float dx2 = cellWorldPos.x - center.x;
                float dz2 = cellWorldPos.z - center.z;
                float distance = Mathf.Sqrt(dx2 * dx2 + dz2 * dz2);

                if (distance <= paintRadius)
                {
                    gridManager.SetCell(key, color, cellWorldPos);
                }
            }
        }
    }
}