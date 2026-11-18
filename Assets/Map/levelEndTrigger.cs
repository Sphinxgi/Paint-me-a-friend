using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class levelEndTrigger : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            LoadNextScene();
        }
    }
    public void LoadNextScene()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        int nextSceneIndex = currentSceneIndex + 1;

        if (nextSceneIndex < SceneManager.sceneCountInBuildSettings)
        {
            // Load the next scene
            AudioListener.pause = true;
            DOTween.Clear();
            SceneManager.LoadScene(nextSceneIndex);
        }
        else
        {
            AudioListener.pause = true;
            DOTween.Clear();
            EndMenuController.Instance.EndTheGame();
        }
    }
    
}
