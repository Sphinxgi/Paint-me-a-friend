
using UnityEngine;
using System.Collections.Generic;

public class PressurePlate : MonoBehaviour
{
    [SerializeField] private Transform door;

    private Door doorScript;

    private HashSet<GameObject> objectsOnPlate = new HashSet<GameObject>();

    private bool isSignalOn;

    private void Start()
    {
        if (door != null)
            doorScript = door.GetComponentInChildren<Door>();

        isSignalOn = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        GameObject occupant = GetOccupant(other);

        if (occupant != null)
            AddObject(occupant);
    }

    private void OnTriggerExit(Collider other)
    {
        GameObject occupant = GetOccupant(other);

        if (occupant != null)
            RemoveObject(occupant);
    }

    private GameObject GetOccupant(Collider other)
    {
        // Check whether the collider belongs to the player.
        PlayerController player = other.GetComponentInParent<PlayerController>();

        if (player != null)
            return player.gameObject;

        // Check whether the collider belongs to a pickup object.
        PickUpObject pickup = other.GetComponentInParent<PickUpObject>();

        if (pickup != null && !pickup.isBeingHeld)
            return pickup.gameObject;

        return null;
    }

    public void AddObject(GameObject obj)
    {
        if (obj == null) return;

        if (objectsOnPlate.Add(obj))
            UpdatePlateState();
    }

    public void RemoveObject(GameObject obj)
    {
        if (obj == null) return;

        if (objectsOnPlate.Remove(obj))
            UpdatePlateState();
    }

    private void UpdatePlateState()
    {
        bool shouldBeActive = objectsOnPlate.Count > 0;

        if (shouldBeActive == isSignalOn) return;

        isSignalOn = shouldBeActive;

        if (doorScript == null) return;

        if (isSignalOn)
            doorScript.Activate();
        else
            doorScript.DeActivate();
    }
}
