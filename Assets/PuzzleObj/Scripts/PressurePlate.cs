using UnityEngine;

public class PressurePlate : MonoBehaviour
{
    [SerializeField] private Transform door;
    private Door doorScript;

    private void Start()
    {
        if (door != null) doorScript = door.GetComponentInChildren<Door>();
    }
    private void OnTriggerEnter(Collider other)
    {
        SignalOn();
    }

    private void OnTriggerExit(Collider other)
    {
        SignalOff();
    }

    public void SignalOn()
    {
        doorScript.Activate();
    }

    public void SignalOff()
    {
        doorScript.DeActivate();
    }
}
