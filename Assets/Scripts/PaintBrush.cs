using UnityEngine;
using System;

public class PaintBrush : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private LayerMask paintableLayer;
    [SerializeField] private PaintPatch paintPatchPrefab;

    [SerializeField] private float paintDistance = 20f;
    [SerializeField] private float paintDuration = 10f;
    [SerializeField] private float brushSpacing = 0.05f;
    [SerializeField] private float surfaceOffset = 0.01f; // Offset to prevent z-fighting

    public PaintType CurrentPaint {get; private set;}
    public event Action OnPaintColorChanged;
    [SerializeField] private float brushSize = 0.6f;

    private Vector3 lastPaintPosition;
    private Collider lastPaintCollider;
    private bool hasPainted;

    private void Start()
    {
        ChangeCurrentPaint(PaintType.Blue);
    }

    private void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Paint();
        }
        else
        {
            hasPainted = false;
            lastPaintCollider = null;
        }

        if (Input.mouseScrollDelta.y > 0f)
        {
            if (CurrentPaint == PaintType.Blue) return;
            ChangeCurrentPaint(PaintType.Blue);
        }
        else if (Input.mouseScrollDelta.y < 0f)
        {
            if (CurrentPaint == PaintType.Green) return;
            ChangeCurrentPaint(PaintType.Green);
        }
    }

    private void Paint()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, paintDistance, paintableLayer))
        {
            hasPainted = false;
            lastPaintCollider = null;
            return;
        }

        if (!hasPainted || hit.collider != lastPaintCollider)
        {
            CreatePaintPatch(hit.point, hit.normal, hit.collider);

            lastPaintPosition = hit.point;
            lastPaintCollider = hit.collider;
            hasPainted = true;

            return;
        }

        float distance = Vector3.Distance(lastPaintPosition, hit.point);

        if (distance >= brushSpacing)
        {
            int patchCount = Mathf.FloorToInt(distance / brushSpacing);

            for (int i = 1; i <= patchCount; i++)
            {
                float t = (float)i / patchCount;

                Vector3 position = Vector3.Lerp(
                    lastPaintPosition,
                    hit.point,
                    t
                );

                CreatePaintPatch(position, hit.normal, hit.collider);
            }

            lastPaintPosition = hit.point;
        }
    }

    private void CreatePaintPatch(
    Vector3 position,
    Vector3 normal,
    Collider surface)
    {
        ReplaceOverlappingPaint(position, surface);

        PaintPatch patch = Instantiate(
            paintPatchPrefab,
            position + normal * surfaceOffset,
            Quaternion.LookRotation(normal)
        );

        patch.Initialize(CurrentPaint, paintDuration, surface);
        //Debug.Log($"Painted {CurrentPaint} at {position} on {surface.name}");
    }

    private void ReplaceOverlappingPaint(Vector3 position, Collider surface)
    {
        Collider[] overlaps = Physics.OverlapSphere(
            position,
            brushSize / 2f
        );

        foreach (Collider overlap in overlaps)
        {
            PaintPatch patch = overlap.GetComponent<PaintPatch>();

            if (patch == null)
                continue;

            if (!patch.BelongsToSurface(surface))
                continue;

            if (patch.PaintType == CurrentPaint)
                continue;

            Destroy(patch.gameObject);
        }
    }

    private void ChangeCurrentPaint(PaintType paintType)
    {
        CurrentPaint = paintType;
        OnPaintColorChanged?.Invoke();
    }
}