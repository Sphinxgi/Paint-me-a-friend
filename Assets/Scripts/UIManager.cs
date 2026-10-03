using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UIElements;
using DG.Tweening;
using System.ComponentModel;
using DG.Tweening.Core;

public class UIManager : MonoBehaviour
{
    [Header("Other References")]
    [SerializeField] private PlayerController pc;
    [SerializeField] private PlayerPaintEffects playerPaintEffects;
    [SerializeField] private PaintBrush paintBrush;
    [Header("Indicator Texts")]
    [SerializeField] private TextMeshProUGUI speedText;
    [SerializeField] private TextMeshProUGUI jumpText;
    [Header("Color Selection")]
    [SerializeField] private RectTransform blueColorImage;
    [SerializeField] private RectTransform greenColorImage;
    [SerializeField] private RectTransform selectionIndicator;
    [SerializeField] private Vector3 selectedRectScale = new Vector3(1f, 1f, 1f);
    [SerializeField] private Vector3 unselectedRectScale = new Vector3(0.8f, 0.8f, 0.8f);
    [SerializeField] private Vector3 indicatorRectScale = new Vector3(1.2f, 1.2f, 1.2f);
    [SerializeField] private float colorSelectTweenDuration = 3f;
    [Header("Color Vignette")]
    [SerializeField] private GameObject blueVignette;
    [SerializeField] private GameObject greenVignette;
    [SerializeField] private float vignetteTweenDuration = 0.4f;
    [SerializeField] private Tween[] VignetteAnims;

    void Start()
    {
        pc = FindAnyObjectByType<PlayerController>();
        playerPaintEffects = FindAnyObjectByType<PlayerPaintEffects>();
        paintBrush = FindAnyObjectByType<PaintBrush>();
        selectionIndicator.localScale = indicatorRectScale;
        UpdateColorSelection();
        HideVignette();
    }

    void Update()
    {
        UpdateIndicatorTexts();
    }

    private void OnEnable()
    {
        if (paintBrush != null)
        {
            paintBrush.OnPaintColorChanged += UpdateColorSelection;
        }
        if (pc != null)
        {
            pc.OnGetOnBluePaint += OnBluePaint;
            pc.OnGetOnGreenPaint += OnGreenPaint;
            pc.OnGetOffPaint += OnOffPaint;
        }
    }

    private void UpdateIndicatorTexts()
    {
        speedText.text = $"Speed: {pc.Rb.linearVelocity.magnitude:F2}";
        jumpText.text = $"Jump: {pc.jumpForce * playerPaintEffects.JumpMultiplier:F0}";
    }

    private void UpdateColorSelection()
    {
        DOTween.KillAll(false);
        if (paintBrush.CurrentPaint == PaintType.Blue)
        {
            TriggerScaleChange(blueColorImage, selectedRectScale);
            TriggerScaleChange(greenColorImage, unselectedRectScale);
            TriggerPositionChange(selectionIndicator, blueColorImage.localPosition);
        }
        else if (paintBrush.CurrentPaint == PaintType.Green)
        {
            TriggerScaleChange(blueColorImage, unselectedRectScale);
            TriggerScaleChange(greenColorImage, selectedRectScale);
            TriggerPositionChange(selectionIndicator, greenColorImage.localPosition);
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
            TriggerAlphaChange(blueVignette, 1f);
            TriggerAlphaChange(greenVignette, 0f);
        }
        else if (paintType == PaintType.Green)
        {
            TriggerAlphaChange(blueVignette, 0f);
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
}
