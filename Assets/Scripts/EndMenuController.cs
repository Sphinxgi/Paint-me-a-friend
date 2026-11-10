using DG.Tweening;
using DG.Tweening.Core.Easing;
using UnityEngine;
using UnityEngine.SceneManagement;

public class EndMenuController : MonoBehaviour
{
    public static EndMenuController Instance { get; private set; }

    [SerializeField] private GameObject endMenuPanel;

    public bool isPaused { get; private set; } = false;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        ResumeGame();
    }
  
    public void EndTheGame()
    {
        PauseGame();
    }

    public void PauseGame()
    {
        endMenuPanel.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResumeGame()
    {
        endMenuPanel.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void GotoMainMenu()
    {
        DOTween.KillAll(false);
        ResumeGame();
        SceneManager.LoadScene(0);
    }

    public void QuitGame()
    {
        Application.Quit();
        //UnityEditor.EditorApplication.ExitPlaymode();
    }
}
