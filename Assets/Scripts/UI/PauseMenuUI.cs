using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject pauseMenuPanel;

    public bool isPaused { get; private set; } = false;

    void Start()
    {
        ResumeGame();
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (isPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }

    public void PauseGame()
    {
        pauseMenuPanel.SetActive(true);
        Time.timeScale = 0f;
        isPaused = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ResumeGame()
    {
        pauseMenuPanel.SetActive(false);
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
        UnityEditor.EditorApplication.ExitPlaymode();
    }
}
