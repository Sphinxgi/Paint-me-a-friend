using UnityEngine;

public class ButtonObject : MonoBehaviour
{
    [SerializeField] private Transform door;
    private Door doorScript;

    bool playerIsClose = false;
    bool doorIsColse = true;
    bool isButtonReady = true;
    [SerializeField] private float buttonCooldown = 1f;

    private void Start()
    {
        doorIsColse = true;
        if (door != null) doorScript = door.GetComponentInChildren<Door>();
    }
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E) && playerIsClose && doorIsColse && isButtonReady)
        {
            Debug.Log($"Player try to signal On");
            isButtonReady = false;
            SignalOn();
            Invoke(nameof(RefreshButtonCooldown), buttonCooldown);
        }
        if (Input.GetKeyDown(KeyCode.E) && playerIsClose && !doorIsColse && isButtonReady)
        {
            Debug.Log($"Player try to signal Off");
            isButtonReady = false;
            SignalOff();
            Invoke(nameof(RefreshButtonCooldown), buttonCooldown);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject.CompareTag("Player"))
        {
            playerIsClose = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            playerIsClose = false;
        }
    }

    private void RefreshButtonCooldown()
    {
        isButtonReady = true;
    }

    public void SignalOn()
    {
        doorIsColse = false;
        doorScript.Activate();
    }

    public void SignalOff()
    {
        doorIsColse = true;
        doorScript.DeActivate();
    }
}
