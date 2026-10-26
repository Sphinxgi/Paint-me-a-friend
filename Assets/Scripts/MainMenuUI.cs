using UnityEngine;
using DG.Tweening;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [Header("Buttons References")]
    // 0 = New Game, 1 = Load Game, 2 = Extras, 3 = Quit Game
    [SerializeField] private GameObject[] buttons;

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

    [Header("Extras Menu")]
    [SerializeField] private RectTransform extrasPanel;
    [SerializeField] private Vector2 extrasOnPos;
    [SerializeField] private Vector2 extrasOffPos;
    [SerializeField] private float extrasTransitionTime = 0.1f;
    private bool isExtrasOn;

    private Vector2 backgroundStartPos;
    private Vector2 foregroundStartPos;
    private Vector2 buttonsStartPos;
    private Vector2 velocity = Vector2.zero;

    void Start()
    {
        // Confine Cursor
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = true;

        if (backgroundRect != null) backgroundStartPos = backgroundRect.anchoredPosition;
        if (foregroundRect != null) foregroundStartPos = foregroundRect.anchoredPosition;
        if (buttonsRect != null) buttonsStartPos = buttonsRect.anchoredPosition;

        buttonRects = new RectTransform[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
        {
            buttonRects[i] = buttons[i].GetComponent<RectTransform>();
        }

        buttonNormalScale = buttonRects[0].localScale.x;
        buttonHoverScale = buttonNormalScale * buttonHoverScaleMult;
        buttonClickScale = buttonNormalScale * buttonClickScaleMult;

        Button newGameButton = buttons[0].GetComponent<Button>();
        Button continueButton = buttons[1].GetComponent<Button>();
        Button extrasButton = buttons[2].GetComponent<Button>();
        Button quitGameButton = buttons[3].GetComponent<Button>();
        newGameButton.onClick.AddListener(() => OnButtonClick(newGameButton));
        continueButton.onClick.AddListener(() => OnButtonClick(continueButton));
        extrasButton.onClick.AddListener(() => OnButtonClick(extrasButton));
        quitGameButton.onClick.AddListener(() => OnButtonClick(quitGameButton));

        isExtrasOn = false;
        extrasPanel.localPosition = extrasOffPos;
    }

    void Update()
    {
        if (!isExtrasOn)
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
                    buttonRect.DOScale(buttonNormalScale, buttonAnimationDuration).SetEase(Ease.OutBack);
                }
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
        button.DOScale(buttonHoverScale, buttonAnimationDuration).SetEase(Ease.OutBack);
    }

    private void OnButtonClick(Button button)
    {
        switch (button.name)
        {
            case "NewGame_Button":
                Debug.Log("New Game button clicked");
                break;
            case "Continue_Button":
                Debug.Log("Continue button clicked");
                // Load saved game
                break;
            case "Extras_Button":
                Debug.Log("Extras button clicked");
                ToggleExtrasMenu();
                break;
            case "QuitGame_Button":
                Debug.Log("Quit Game button clicked");
                Application.Quit();
                break;
        }
    }

    private void ToggleExtrasMenu()
    {
        if (isExtrasOn)
        {
            isExtrasOn = false;
            extrasPanel.DOLocalMove(extrasOffPos, extrasTransitionTime);
        }
        else if (!isExtrasOn)
        {
            isExtrasOn = true;
            extrasPanel.DOLocalMove(extrasOnPos, extrasTransitionTime);
        }
    }
}
