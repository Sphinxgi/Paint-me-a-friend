using UnityEngine;
using System.Collections;

public class PickUpObject : MonoBehaviour
{
    private Rigidbody rb;
    private PaintBounce paintBounce;
    public bool isBeingHeld { get; private set; }
    private Collider[] playerColliders;
    private Collider[] objectColliders;
    private int[] originalLayers;
    private Coroutine collisionResetRoutine;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        paintBounce = GetComponent<PaintBounce>();
        playerColliders = PlayerController.Instance.GetComponentsInChildren<Collider>();
        objectColliders = GetComponentsInChildren<Collider>();
        originalLayers = new int[gameObject.GetComponentsInChildren<Transform>().Length];
        Transform[] childTransforms = GetComponentsInChildren<Transform>();

        for (int i = 0; i < childTransforms.Length; i++)
        {
            originalLayers[i] = childTransforms[i].gameObject.layer;
        }

        isBeingHeld = false;
        TurnOnPhysics();
    }

    private void TurnOffPhysics()
    {
        if (paintBounce != null)
        {
            paintBounce.enabled = false;
        }
        rb.isKinematic = true;
        foreach (Collider col in objectColliders)
        {
            col.enabled = false;
        }
    }

    private void TurnOnPhysics()
    {
        if (paintBounce != null)
        {
            paintBounce.enabled = true;
        }
        rb.isKinematic = false;
        foreach (Collider col in objectColliders)
        {
            col.enabled = true;
        }
    }

    public void PickUp()
    {
        isBeingHeld = true;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        TurnOffPhysics();

        IgnorePlayerCollision(true);

        SetObjectLayer(LayerMask.NameToLayer("HeldObject"));

        transform.SetParent(PlayerController.Instance.holdParent, false);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    public void PutDown()
    {
        isBeingHeld = false;

        transform.SetParent(null, true);

        RestoreOriginalLayers();

        TurnOnPhysics(); 

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (collisionResetRoutine != null)
            StopCoroutine(collisionResetRoutine);

        collisionResetRoutine = StartCoroutine(WaitForPlayerClearance());
    }

    private IEnumerator WaitForPlayerClearance()
    {
        while (true)
        {
            bool overlapping = false;

            foreach (Collider playerCol in playerColliders)
            {
                foreach (Collider objectCol in objectColliders)
                {
                    if (playerCol != null && objectCol != null &&
                        playerCol.bounds.Intersects(objectCol.bounds))
                    {
                        overlapping = true;
                        break;
                    }
                }

                if (overlapping) break;
            }

            if (!overlapping) break;

            yield return new WaitForFixedUpdate();
        }

        IgnorePlayerCollision(false);
        collisionResetRoutine = null;
    }

    private void SetObjectLayer(int layer)
    {
        foreach (Transform child in GetComponentsInChildren<Transform>())
        {
            child.gameObject.layer = layer;
        }
    }

    private void IgnorePlayerCollision(bool ignore)
    {
        foreach (Collider playerCol in playerColliders)
        {
            foreach (Collider objectCol in objectColliders)
            {
                Physics.IgnoreCollision(playerCol, objectCol, ignore);
            }
        }
    }

    private void RestoreOriginalLayers()
    {
        Transform[] childTransforms = GetComponentsInChildren<Transform>();

        for (int i = 0; i < childTransforms.Length; i++)
        {
            childTransforms[i].gameObject.layer = originalLayers[i];
        }
    }
}
