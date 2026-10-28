using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PaintBounce : MonoBehaviour
{
    [SerializeField] private float bounceSpeed = 7f;
    [SerializeField] private float bounceCooldown = 0.1f;
    [SerializeField] private float minimumImpactSpeed = 3f;

    private Rigidbody rb;
    private float lastBounceTime = -Mathf.Infinity;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        Debug.Log("Green is awake");
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("somthing entered green");
        PaintPatch patch = other.GetComponent<PaintPatch>();

        if (patch == null)
            return;

        if (patch.PaintType != PaintType.Green)
            return;

        if (Time.time < lastBounceTime + bounceCooldown)
            return;
        Debug.Log("Green is deep in working");
        Vector3 normal = patch.transform.forward;

        float velocityIntoSurface = Vector3.Dot(rb.linearVelocity, normal);

        if (velocityIntoSurface > -minimumImpactSpeed)
            return;

        Vector3 tangentVelocity =
            rb.linearVelocity - normal * velocityIntoSurface;

        rb.linearVelocity =
            tangentVelocity + normal * bounceSpeed;

        lastBounceTime = Time.time;
    }
}