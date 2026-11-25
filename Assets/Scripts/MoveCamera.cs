using UnityEngine;

public class MoveCamera : MonoBehaviour
{
    public Transform cameraPos;

    void LateUpdate()
    {
        transform.position = cameraPos.position;
    }
}
