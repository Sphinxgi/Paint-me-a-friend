using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent (typeof(MeshRenderer))]
public class MeshCombinder : MonoBehaviour
{
    private void Start()
    {
        CombineInstance[] combine = new CombineInstance[transform.childCount];

        int index = 0;
        foreach (Transform child in gameObject.transform)
        {
            MeshFilter filter = child.GetComponentInChildren<MeshFilter>();
            //if (filter = null)
            //{
            //    filter = child.GetComponentInChildren<MeshFilter>();
            //}
            combine[index].mesh = filter.sharedMesh;
            combine[index].transform = child.localToWorldMatrix;
            child.gameObject.SetActive(false);
            Debug.Log($"JJK MID {index}");
            index++;
        }

        Mesh mesh = new Mesh();
        mesh.CombineMeshes(combine);
        transform.GetComponent<MeshFilter>().sharedMesh = mesh;
        transform.gameObject.SetActive(true);
    }
}
