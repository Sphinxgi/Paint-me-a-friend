using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuUI : MonoBehaviour
{
    // Maybe put this in a Separate Singleton later?
    [Header("Scene Settings")]
    [SerializeField] private int newGameSceneNumber;
    [SerializeField] private int loadStageScene1Number;
    [SerializeField] private int loadStageScene2Number;
    [SerializeField] private int loadStageScene3Number;


    [Header("Dim Settings")]
    [SerializeField] private CanvasGroup dimImage;
    [SerializeField] private float dimAlpha = 60f;
    [SerializeField] private float dimTime = 0.2f;
    private bool isDimOn = false;

    [Header("Buttons References")]
    [Tooltip("0 = New Game, 1 = Load Game, 2 = Options, 3 = Extras, 4 = Quit Game")]
    [SerializeField] private GameObject[] buttons;

    [Tooltip("0 = Return, 1 = Continue, 2 = Load Saved, 3 = Load Stage 1 and so on...")]
    [SerializeField] private Button[] loadGameButtons;

    [Tooltip("0 = Return, 1 = Credits, 2 = Concept Art")]
    [SerializeField] private Button[] extrasButtons;


    [Header("Button Effects")]
    private RectTransform[] buttonRects;
    [SerializeField] private float buttonNormalScale = 1f;
    [SerializeField] private float buttonHoverScaleMult = 1.1f;
    [SerializeField] private float buttonClickScaleMult = 0.9f;
    private float buttonHoverScale = 1.1f;
    private float buttonClickScale = 0.9f;
    [SerializeField] private float buttonAnimationDuration = 0.2f;

    [Header("Parallax Settings")]
    [SerializeField] private float backgroundMoveModifier = 20f;
    [SerializeField] private float foregroundMoveModifier = 10f;
    [SerializeField] private float buttonsMoveModifier = 5f;
    [SerializeField] private RectTransform backgroundRect;
    [SerializeField] private RectTransform foregroundRect;
    [SerializeField] private RectTransform buttonsRect;
    [SerializeField] private float smoothTime = 0.1f;
    private Vector2 backgroundStartPos;
    private Vector2 foregroundStartPos;
    private Vector2 buttonsStartPos;
    private Vector2 velocity = Vector2.zero;

    [Header("Load Game Menu")]
    [SerializeField] private RectTransform loadGamePanel;
    [SerializeField] private Vector2 loadGameOnPos;
    [SerializeField] private Vector2 loadGameOffPos;
    [SerializeField] private float loadGameTransitionTime = 0.1f;
    private bool isLoadGameOn = false;

    [Header("Extras Menu")]
    [SerializeField] private RectTransform extrasPanel;
    [SerializeField] private Vector2 extrasOnPos;
    [SerializeField] private Vector2 extrasOffPos;
    [SerializeField] private float extrasTransitionTime = 0.1f;
    private bool isExtrasOn = false;
    private bool isMainButtonsDisabled = false;

    private void OnEnable()
    {
        DOTween.Kill(transform);
    }

    void Start()
    {
        Time.timeScale = 1f;
        //DOTween.KillAll();
        // Confine Cursor
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;

        if (backgroundRect != null) backgroundStartPos = backgroundRect.anchoredPosition;
        if (foregroundRect != null) foregroundStartPos = foregroundRect.anchoredPosition;
        if (buttonsRect != null) buttonsStartPos = buttonsRect.anchoredPosition;

        buttonRects = new RectTransform[buttons.Length];

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null) continue;
            buttonRects[i] = buttons[i].GetComponent<RectTransform>();
        }

        buttonNormalScale = 0.18f;
        buttonHoverScale = buttonNormalScale * buttonHoverScaleMult;
        buttonClickScale = buttonNormalScale * buttonClickScaleMult;

        // Main Menu Buttons
        Button newGameButton = buttons[0].GetComponent<Button>();
        Button loadGameButton = buttons[1].GetComponent<Button>();
        Button optionsButton = buttons[2].GetComponent<Button>();
        Button extrasButton = buttons[3].GetComponent<Button>();
        Button quitGameButton = buttons[4].GetComponent<Button>();
        newGameButton.onClick.AddListener(() => OnButtonClick(newGameButton));
        loadGameButton.onClick.AddListener(() => OnButtonClick(loadGameButton));
        optionsButton.onClick.AddListener(() => OnButtonClick(optionsButton));
        extrasButton.onClick.AddListener(() => OnButtonClick(extrasButton));
        quitGameButton.onClick.AddListener(() => OnButtonClick(quitGameButton));

        // Load Game Menu Buttons
        for (int i = 0; i < loadGameButtons.Length; i++)
        {
            if (loadGameButtons[i] == null) continue;
            Button button = loadGameButtons[i];
            button.onClick.AddListener(() => OnButtonClick(button));
        }

        // Extra Menu Buttons
        for (int i = 0; i < extrasButtons.Length; i++)
        {
            if (extrasButtons[i] == null) continue;
            Button button = extrasButtons[i];
            button.onClick.AddListener(() => OnButtonClick(button));
        }

        isExtrasOn = false;
        isLoadGameOn = false;
        isMainButtonsDisabled = false;
        extrasPanel.localPosition = extrasOffPos;
        loadGamePanel.localPosition = loadGameOffPos;
        isDimOn = false;
        dimImage.alpha = 0f;
    }

    void Update()
    {
        if (!isMainButtonsDisabled)
        {
            // Check for button hover and click effects
            foreach (var button in buttons)
            {
                if (button == null) continue;
                RectTransform buttonRect = button.GetComponent<RectTransform>();
                if (buttonRect == null) continue;
                Vector2 localMousePosition = buttonRect.InverseTransformPoint(Input.mousePosition);
                bool isHovering = buttonRect.rect.Contains(localMousePosition);
                if (isHovering)
                {
                    HoverButtonEffect(buttonRect);
                    if (Input.GetMouseButtonDown(0))
                    {
                        buttonRect.DOScale(buttonClickScale, buttonAnimationDuration).SetEase(Ease.OutBack);
                    }
                    else if (Input.GetMouseButtonUp(0))
                    {
                        buttonRect.DOScale(buttonHoverScale, buttonAnimationDuration).SetEase(Ease.OutBack);
                    }
                }
                else
                {
                    if (buttonRect != null &&
                        buttonRect.localScale != Vector3.one * buttonNormalScale)
                    {
                        buttonRect.DOKill();
                        buttonRect.DOScale(buttonNormalScale, buttonAnimationDuration)
                                  .SetEase(Ease.OutBack);
                    }
                }
            }
        }

        if (isMainButtonsDisabled && Input.GetKeyDown(KeyCode.Escape))
        {
            if (isExtrasOn)
            {
                ToggleExtrasMenu();
            }
            else if (isLoadGameOn)
            {
                ToggleLoadGameMenu();
            }
        }
    }

    void LateUpdate()
    {
        ParallaxUI(backgroundRect, backgroundMoveModifier, backgroundStartPos);
        ParallaxUI(foregroundRect, foregroundMoveModifier, foregroundStartPos);
        ParallaxUI(buttonsRect, buttonsMoveModifier, buttonsStartPos);
    }

    private void ParallaxUI(RectTransform rect, float moveModifier, Vector2 startPos)
    {
        if (rect == null) return;
        Vector2 mouseNormalized = new Vector2(Input.mousePosition.x / Screen.width - 0.5f, Input.mousePosition.y / Screen.height - 0.5f);
        Vector2 targetPos = startPos + mouseNormalized * moveModifier;
        rect.anchoredPosition = Vector2.SmoothDamp(rect.anchoredPosition, targetPos, ref velocity, smoothTime);
    }

    private void HoverButtonEffect(RectTransform button)
    {
        if (button == null) return;

        button.DOKill();
        button.DOScale(buttonHoverScale, buttonAnimationDuration)
              .SetEase(Ease.OutBack);
    }

    private void OnButtonClick(Button button)
    {
        switch (button.name)
        {
            case "NewGame_Button" when !isMainButtonsDisabled:
                Debug.Log("New Game button clicked");
                DOTween.KillAll();
                DOTween.Clear(true);
                Debug.Log("Cleared DOTween");
                SceneManager.LoadScene(newGameSceneNumber);
                break;
            case "LoadGame_Button" when !isMainButtonsDisabled:
            case "LoadGame_Return_Button" when isLoadGameOn:
                Debug.Log("Load Game button clicked");
                ToggleLoadGameMenu();
                break;
            case "Options_Button" when !isMainButtonsDisabled:
                Debug.Log("Options button clicked");
                break;
            case "Extras_Button" when !isMainButtonsDisabled:
            case "Extras_Return_Button" when isExtrasOn:
                Debug.Log("Extras button clicked");
                ToggleExtrasMenu();
                break;
            case "QuitGame_Button" when !isMainButtonsDisabled:
                Debug.Log("Quit Game button clicked");
                Application.Quit();
                UnityEditor.EditorApplication.ExitPlaymode();
                break;
            case "LoadStage1_Button" when isLoadGameOn:
                SceneManager.LoadScene(loadStageScene1Number);
                break;
            case "LoadStage2_Button" when isLoadGameOn:
                SceneManager.LoadScene(loadStageScene2Number);
                break;
            case "LoadStage3_Button" when isLoadGameOn:
                SceneManager.LoadScene(loadStageScene3Number);
                break;
        }
    }

    private void ToggleExtrasMenu()
    {
        if (isExtrasOn)
        {
            isExtrasOn = false;
            isMainButtonsDisabled = false;
            extrasPanel.DOLocalMove(extrasOffPos, extrasTransitionTime).SetEase(Ease.OutSine);
            ToggleDim();
        }
        else if (!isExtrasOn)
        {
            isExtrasOn = true;
            isMainButtonsDisabled = true;
            extrasPanel.DOLocalMove(extrasOnPos, extrasTransitionTime).SetEase(Ease.OutSine);
            ToggleDim();
        }
    }

    private void ToggleDim()
    {
        if (isDimOn)
        {
            isDimOn = false;
            dimImage.DOFade(0f, dimTime);
        }
        else if (!isDimOn)
        {
            isDimOn = true;
            dimImage.DOFade(dimAlpha, dimTime);
        }
    }

    private void ToggleLoadGameMenu()
    {
        if (isLoadGameOn)
        {
            isLoadGameOn = false;
            isMainButtonsDisabled = false;
            loadGamePanel.DOLocalMove(loadGameOffPos, loadGameTransitionTime).SetEase(Ease.OutSine);
            ToggleDim();
        }
        else if (!isLoadGameOn)
        {
            isLoadGameOn = true;
            isMainButtonsDisabled = true;
            loadGamePanel.DOLocalMove(loadGameOnPos, loadGameTransitionTime).SetEase(Ease.OutSine);
            ToggleDim();
        }
    }
}
