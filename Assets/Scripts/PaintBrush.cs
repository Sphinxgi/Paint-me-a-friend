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

    [Header("Paint Preview")]
    [SerializeField] private Transform paintPreview;
    [SerializeField] private float previewSmoothSpeed = 10f;
    [SerializeField] private float previewSurfaceOffset = 0.02f;
    [SerializeField] private Renderer paintPreviewRenderer;
    [SerializeField] private Texture2D bluePreviewTexture;
    [SerializeField] private Texture2D greenPreviewTexture;
    [SerializeField] private Texture2D erasePreviewTexture;
    private Material paintPreviewMaterial;

    private void Awake()
    {
        paintPreviewMaterial = paintPreviewRenderer.material;
    }

    private void Start()
    {
        ChangeCurrentPaint(PaintType.Blue);
    }

    private void Update()
    {
        UpdatePaintPreview();
        UpdatePreviewTexture();

        if (Input.GetMouseButton(0))
        {
            Paint();
        }
        else if (Input.GetMouseButton(1))
        {
            Erase();
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

        if (!TryGetPaintableHit(ray, out RaycastHit hit))
        {
            hasPainted = false;
            lastPaintCollider = null;
            return;
        }

        int layer = hit.collider.gameObject.layer;

        if ((paintableLayer.value & (1 << layer)) == 0)
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

    private void Erase()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        if (!TryGetPaintableHit(ray, out RaycastHit hit))
        {
            hasPainted = false;
            lastPaintCollider = null;
            return;
        }

        int layer = hit.collider.gameObject.layer;

        if ((paintableLayer.value & (1 << layer)) == 0)
        {
            hasPainted = false;
            lastPaintCollider = null;
            return;
        }

        if (!hasPainted || hit.collider != lastPaintCollider)
        {
            ErasePaint(hit.point, hit.collider);

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

                ErasePaint(position, hit.collider);
            }

            lastPaintPosition = hit.point;
        }
    }

    private void ErasePaint(Vector3 position, Collider surface)
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

            Destroy(patch.gameObject);
        }
    }

    private bool TryGetPaintableHit(
    Ray ray,
    out RaycastHit paintableHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            paintDistance
        );

        System.Array.Sort(hits, (a, b) =>
            a.distance.CompareTo(b.distance)
        );

        foreach (RaycastHit hit in hits)
        {
            PaintPatch patch = hit.collider.GetComponent<PaintPatch>();

            if (patch != null)
                continue;

            int layer = hit.collider.gameObject.layer;

            if ((paintableLayer.value & (1 << layer)) == 0)
            {
                paintableHit = default;
                return false;
            }

            paintableHit = hit;
            return true;
        }

        paintableHit = default;
        return false;
    }

    private void UpdatePaintPreview()
    {
        Ray ray = playerCamera.ScreenPointToRay(Input.mousePosition);

        Vector3 targetScale = Vector3.zero;

        if (TryGetPaintableHit(ray, out RaycastHit hit))
        {
            paintPreview.position =
                hit.point + hit.normal * previewSurfaceOffset;

            paintPreview.rotation =
                Quaternion.LookRotation(-hit.normal);

            targetScale = Vector3.one * brushSize;
        }

        paintPreview.localScale = Vector3.Lerp(
            paintPreview.localScale,
            targetScale,
            previewSmoothSpeed * Time.deltaTime
        );
    }

    private void UpdatePreviewTexture()
    {
        Texture2D targetTexture;

        if (Input.GetMouseButton(1))
        {
            targetTexture = erasePreviewTexture;
        }
        else
        {
            switch (CurrentPaint)
            {
                case PaintType.Blue:
                    targetTexture = bluePreviewTexture;
                    break;

                case PaintType.Green:
                    targetTexture = greenPreviewTexture;
                    break;

                default:
                    targetTexture = erasePreviewTexture;
                    break;
            }
        }

        paintPreviewMaterial.SetTexture("Base_Map", targetTexture);
    }
}