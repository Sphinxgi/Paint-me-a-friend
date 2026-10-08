using UnityEngine;

public class Door : MonoBehaviour
{
    [SerializeField] private Vector3 startPos;
    [SerializeField] private Vector3 endPos;
    [SerializeField] private GameObject endPosObj;

    public float duration = 2f;
    public float startTime;
    private bool isOpening = false;
    private bool isClosing = false;

    private void Start()
    {
       startPos = transform.position;
       endPos = endPosObj.transform.position;
    }

    private void Update()
    {
       if (isOpening)
       {
            float completion = Mathf.Clamp01((Time.time - startTime) / duration);    // calculate % till open
            transform.position = Vector3.Lerp(transform.position, endPos, completion);
            if (completion >= 1f) isOpening = false;
       }
       if (isClosing)
       {
            float completion = Mathf.Clamp01((Time.time - startTime) / duration);    // calculate % till close
            transform.position = Vector3.Lerp(transform.position, startPos, completion);
            if (completion >= 1f) isClosing = false;
       }
    }
    public void Activate()
    {
        startTime = Time.time;
        isOpening = true;
        isClosing = false;
    }

    public void DeActivate()
    {
        startTime = Time.time;
        isClosing = true;
        isOpening = false;
    }
}
