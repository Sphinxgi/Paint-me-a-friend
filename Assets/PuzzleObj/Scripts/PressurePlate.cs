using UnityEngine;

public class PressurePlate : MonoBehaviour
{
    [SerializeField] private Transform door;
    private Door doorScript;
    private readonly Collider[] hits = new Collider[5];
    private BoxCollider box;
    private bool wasEmpty = true;
    private int excludedMask;

    private void Start()
    {
        if (door != null) doorScript = door.GetComponentInChildren<Door>();
        excludedMask = LayerMask.GetMask("BluePaint");
    }
    

    private void Awake() => box = GetComponent<BoxCollider>();

    private void FixedUpdate()
    {
        int count = Physics.OverlapBoxNonAlloc(
            box.transform.TransformPoint(box.center),
            Vector3.Scale(box.size, box.transform.lossyScale) * 0.5f,
            hits,
            box.transform.rotation,
            ~excludedMask,
            QueryTriggerInteraction.Collide);

        int others = 1;
        for (int i = 0; i < count; i++)
        {
            if (hits[i] == box) continue;
            if (hits[i].transform.IsChildOf(transform)) continue;
            others++;
            WhileInside(hits[i]);              
        }

        bool isEmpty = others == 1;

        if (isEmpty && !wasEmpty)
            OnZoneEmpty();

        wasEmpty = isEmpty;
    }

    private void WhileInside(Collider target)
    {
        Debug.Log($"{target.name} is inside");
        SignalOn();
    }

    private void OnZoneEmpty()
    {
        Debug.Log("Nothing inside the trigger");
        SignalOff();
    }
    //private void OnTriggerEnter(Collider other)
    //{
    //    SignalOn();
    //}

    //private void OnTriggerExit(Collider other)
    //{
    //    SignalOff();
    //}
    

    public void SignalOn()
    {
        doorScript.Activate();
    }

    public void SignalOff()
    {
        doorScript.DeActivate();
    }
}
