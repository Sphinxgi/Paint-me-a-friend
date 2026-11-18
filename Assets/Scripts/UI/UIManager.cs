using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using DG.Tweening;
using System.ComponentModel;
using DG.Tweening.Core;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Other References")]
    [SerializeField] private PlayerController pc;
    [SerializeField] private PlayerPaintEffects playerPaintEffects;
    [SerializeField] private PaintBrush paintBrush;
    [Header("Indicator Texts")]
    [SerializeField] private TextMeshProUGUI speedText;
    //[SerializeField] private TextMeshProUGUI jumpText;
    [Header("Color Selection")]
    [SerializeField] private RectTransform blueColorImage;
    [SerializeField] private RectTransform greenColorImage;
    [SerializeField] private RectTransform selectionIndicator;
    [SerializeField] private Vector3 selectedRectScale = new Vector3(1f, 1f, 1f);
    [SerializeField] private Vector3 unselectedRectScale = new Vector3(0.8f, 0.8f, 0.8f);
    [SerializeField] private Vector3 indicatorRectScale = new Vector3(1.2f, 1.2f, 1.2f);
    [SerializeField] private float colorSelectTweenDuration = 3f;
    [SerializeField] private GameObject brushHand;
    [SerializeField] private UnityEngine.UI.Image brushHandImage;
    [SerializeField] private Sprite blueBrushImage;
    [SerializeField] private Sprite greenBrushImage;
    [Header("Color Vignette")]
    [SerializeField] private GameObject blueVignette;
    [SerializeField] private GameObject greenVignette;
    [SerializeField] private float vignetteTweenDuration = 0.4f;
    [SerializeField] private Tween[] VignetteAnims;
    [Header("Heads Up Text")]
    [SerializeField] private TextMeshProUGUI headsUpText;

    private void OnEnable()
    {
        DOTween.Kill(transform);
    }

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
        //DOTween.KillAll();
        pc = FindAnyObjectByType<PlayerController>();
        playerPaintEffects = FindAnyObjectByType<PlayerPaintEffects>();
        paintBrush = FindAnyObjectByType<PaintBrush>();
        selectionIndicator.localScale = indicatorRectScale;
        blueVignette.SetActive(false);
        greenVignette.SetActive(false);
        if (paintBrush != null)
        {
            paintBrush.OnPaintColorChanged += UpdateColorSelection;
        }
        if (pc != null)
        {
            pc.OnGetOnBluePaint += OnBluePaint;
            pc.OnGetOnGreenPaint += OnGreenPaint;
            pc.OnGetOffPaint += OnOffPaint;
            pc.OnPickUpObject += OnPickUpObject;
            pc.OnDropObject += OnDropObject;
        }
        HideHeadsUpText();
        ChangeBrushHandColor(PaintType.Blue);
    }

    void Update()
    {
        //UpdateIndicatorTexts();
    }

    private void UpdateIndicatorTexts()
    {
        speedText.text = $"Speed: {pc.Rb.linearVelocity.magnitude:F2}";
        //jumpText.text = $"Jump: {pc.jumpForce * playerPaintEffects.JumpMultiplier:F0}";
    }

    private void UpdateColorSelection()
    {
        //DOTween.KillAll(false);
        if (paintBrush.CurrentPaint == PaintType.Blue)
        {
            TriggerScaleChange(blueColorImage, selectedRectScale);
            TriggerScaleChange(greenColorImage, unselectedRectScale);
            TriggerPositionChange(selectionIndicator, blueColorImage.localPosition);
            brushHandImage.sprite = blueBrushImage;
        }
        else if (paintBrush.CurrentPaint == PaintType.Green)
        {
            TriggerScaleChange(blueColorImage, unselectedRectScale);
            TriggerScaleChange(greenColorImage, selectedRectScale);
            TriggerPositionChange(selectionIndicator, greenColorImage.localPosition);
            brushHandImage.sprite = greenBrushImage;
        }
    }

    private void TriggerScaleChange(RectTransform target, Vector3 targetScale)
    {
        target.DOScale(targetScale, colorSelectTweenDuration);
    }

    private void TriggerPositionChange(RectTransform target, Vector3 targetPosition)
    {
        target.DOLocalMove(targetPosition, colorSelectTweenDuration);
    }

    private void TurnOnVignette(PaintType paintType)
    {
        if (paintType == PaintType.Blue)
        {
            blueVignette.SetActive(true);
            TriggerAlphaChange(blueVignette, 1f);
            greenVignette.SetActive(false);
            TriggerAlphaChange(greenVignette, 0f);
        }
        else if (paintType == PaintType.Green)
        {
            blueVignette.SetActive(false);
            TriggerAlphaChange(blueVignette, 0f);
            greenVignette.SetActive(true);
            TriggerAlphaChange(greenVignette, 1f);
        }
        Debug.Log("Vignette turned on for " + paintType.ToString());
    }

    private void HideVignette()
    {
        TriggerAlphaChange(blueVignette, 0f);
        TriggerAlphaChange(greenVignette, 0f);
        Debug.Log("Vignette hidden");
    }

    private void TriggerAlphaChange(GameObject target, float targetAlpha)
    {
        CanvasGroup canvasGroup = target.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = target.AddComponent<CanvasGroup>();
        }
        canvasGroup.DOFade(targetAlpha, vignetteTweenDuration);
    }

    void OnBluePaint()
    {
        TurnOnVignette(PaintType.Blue);
    }

    void OnGreenPaint()
    {
        TurnOnVignette(PaintType.Green);
    }

    void OnOffPaint()
    {
        HideVignette();
    }

    void OnPickUpObject()
    {
        brushHand.SetActive(false);
    }

    void OnDropObject()
    {
        brushHand.SetActive(true);
    }

    public void ShowHeadsUpText(string message)
    {
        headsUpText.text = message;
        headsUpText.gameObject.SetActive(true);
    }

    public void HideHeadsUpText()
    {
        headsUpText.gameObject.SetActive(false);
    }

    private void ChangeBrushHandColor(PaintType color)
    {
        if (color == PaintType.Blue)
        {
            brushHandImage.sprite = blueBrushImage;
        }
        else if (color == PaintType.Green)
        {
            brushHandImage.sprite = greenBrushImage;
        }
    }
}
