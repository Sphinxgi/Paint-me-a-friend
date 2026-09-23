using UnityEngine;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("Other References")]
    public PlayerController pc;
    [Header("Vignettes")]
    public GameObject greenVignette;
    public GameObject blueVignette;
    [Header("Indicator Texts")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI jumpText;

    void Start()
    {
        pc = FindAnyObjectByType<PlayerController>();
        // Vignettes
        greenVignette.SetActive(false);
        blueVignette.SetActive(false);
    }

    void Update()
    {
        //switch (pc.isOnBlue, pc.isOnGreen)
        //{
        //    case (true, false):
        //        ActivateVignette("Blue");
        //        break;
        //    case (false, true):
        //        ActivateVignette("Green");
        //        break;
        //    default:
        //        ActivateVignette("None");
        //        break;
        //}
        UpdateIndicatorTexts();
    }

    private void ActivateVignette(string vignetteName)
    {
        switch (vignetteName)
        {
            case "Blue":
                blueVignette.SetActive(true);
                greenVignette.SetActive(false);
                break;
            case "Green":
                blueVignette.SetActive(false);
                greenVignette.SetActive(true);
                break;
            default:
                blueVignette.SetActive(false);
                greenVignette.SetActive(false);
                break;
        }
    }

    private void UpdateIndicatorTexts()
    {
        speedText.text = $"Speed: {pc.rb.linearVelocity.magnitude:F2}";
        jumpText.text = $"Jump: {pc.currentJumpForce:F2}";
    }
}
