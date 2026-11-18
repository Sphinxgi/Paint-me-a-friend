using UnityEngine;

public class PaintPatch : MonoBehaviour
{
    public PaintType PaintType { get; private set; }

    private float lifetime;
    private Collider surface;
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Material bluePaintMaterial;
    [SerializeField] private Material greenPaintMaterial;
    [SerializeField] private ParticleSystem paintParticle;
    private ParticleSystemRenderer particleSystemRenderer;

    public void Initialize(
        PaintType paintType,
        float duration,
        Collider surfaceCollider)
    {
        PaintType = paintType;
        lifetime = duration;
        surface = surfaceCollider;
        meshRenderer.material = paintType == PaintType.Blue ? bluePaintMaterial : greenPaintMaterial;
        particleSystemRenderer = paintParticle.GetComponent<ParticleSystemRenderer>();
        particleSystemRenderer.material = paintType == PaintType.Blue ? bluePaintMaterial : greenPaintMaterial;
        PlayPaintParticle();
    }

    public bool BelongsToSurface(Collider collider)
    {
        return surface == collider;
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;

        if (lifetime <= 0)
        {
            Destroy(gameObject);
        }
    }

    public void PlayPaintParticle()
    {
        if (paintParticle != null)
        {
            paintParticle.Play();
        }
    }
}