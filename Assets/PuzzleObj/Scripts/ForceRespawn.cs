using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ForceRespawn : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            ResetScene();
        }
    }
    public void ResetScene()
    {
        DOTween.Clear();
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }
}
