
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

    private PressurePlate currentPressurePlate;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        paintBounce = GetComponent<PaintBounce>();

        if (PlayerController.Instance != null)
        {
            playerColliders = PlayerController.Instance
                .GetComponentsInChildren<Collider>();
        }

        objectColliders = GetComponentsInChildren<Collider>();

        Transform[] childTransforms = GetComponentsInChildren<Transform>();

        originalLayers = new int[childTransforms.Length];

        for (int i = 0; i < childTransforms.Length; i++)
        {
            originalLayers[i] = childTransforms[i].gameObject.layer;
        }

        isBeingHeld = false;

        TurnOnPhysics();
    }

    private void OnTriggerEnter(Collider other)
    {
        PressurePlate plate = other.GetComponent<PressurePlate>();

        if (plate != null && !isBeingHeld)
        {
            currentPressurePlate = plate;
            plate.AddObject(gameObject);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        PressurePlate plate = other.GetComponent<PressurePlate>();

        if (plate != null && currentPressurePlate == plate)
        {
            plate.RemoveObject(gameObject);
            currentPressurePlate = null;
        }
    }

    private void TurnOffPhysics()
    {
        if (paintBounce != null)
            paintBounce.enabled = false;

        rb.isKinematic = true;

        foreach (Collider col in objectColliders)
            col.enabled = false;
    }

    private void TurnOnPhysics()
    {
        if (paintBounce != null)
            paintBounce.enabled = true;

        rb.isKinematic = false;

        foreach (Collider col in objectColliders)
            col.enabled = true;
    }

    public void PickUp()
    {
        if (isBeingHeld) return;

        isBeingHeld = true;

        if (currentPressurePlate != null)
        {
            currentPressurePlate.RemoveObject(gameObject);
            currentPressurePlate = null;
        }

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
        if (!isBeingHeld) return;

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

            if (playerColliders != null)
            {
                foreach (Collider playerCol in playerColliders)
                {
                    foreach (Collider objectCol in objectColliders)
                    {
                        if (playerCol != null &&
                            objectCol != null &&
                            playerCol.bounds.Intersects(objectCol.bounds))
                        {
                            overlapping = true;
                            break;
                        }
                    }

                    if (overlapping) break;
                }
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
            child.gameObject.layer = layer;
    }

    private void IgnorePlayerCollision(bool ignore)
    {
        if (playerColliders == null) return;

        foreach (Collider playerCol in playerColliders)
        {
            foreach (Collider objectCol in objectColliders)
            {
                if (playerCol != null && objectCol != null)
                    Physics.IgnoreCollision(playerCol, objectCol, ignore);
            }
        }
    }

    private void RestoreOriginalLayers()
    {
        Transform[] childTransforms = GetComponentsInChildren<Transform>();

        for (int i = 0; i < childTransforms.Length; i++)
            childTransforms[i].gameObject.layer = originalLayers[i];
    }
}
