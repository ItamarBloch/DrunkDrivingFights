using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Windowed ⇄ Fullscreen toggle for an options-menu button.
///
/// SETUP (all in the Editor — this script creates nothing):
///   1. Make your own Button in the options panel.
///   2. Put this component anywhere in the panel (e.g. on the panel root).
///   3. Drag your Button into 'toggleButton'.
///   4. (Optional) Drag the Button's text into 'buttonLabel' so it shows
///      "Windowed" / "Fullscreen" and flips on click.
///
/// Fullscreen uses the monitor's native resolution. Windowed uses a fraction
/// of that native resolution (windowedScale), so the window is comfortably
/// smaller than the screen while keeping the SAME aspect ratio — UI layout
/// stays consistent between the two modes.
/// </summary>
public class FullscreenToggle : MonoBehaviour
{
    [Tooltip("The button the player clicks. Its onClick is hooked up in code.")]
    [SerializeField] private Button toggleButton;

    [Tooltip("Optional. Text that shows the CURRENT mode and updates on toggle.")]
    [SerializeField] private TMP_Text buttonLabel;

    [Tooltip("Windowed size as a fraction of the monitor's native resolution. " +
             "Aspect ratio is preserved because width and height scale together.")]
    [Range(0.3f, 0.95f)]
    [SerializeField] private float windowedScale = 0.7f;

    private void Start()
    {
        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);

        RefreshLabel(Screen.fullScreen);
    }

    /// <summary>Flip between fullscreen and windowed. Public so a Button's
    /// onClick can also call it directly from the Inspector if preferred.</summary>
    public void Toggle() => SetFullscreen(!Screen.fullScreen);

    public void SetFullscreen(bool fullscreen)
    {
        // Native monitor resolution — the source aspect ratio for both modes.
        Resolution native = Screen.currentResolution;

        if (fullscreen)
        {
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            // Scale both dimensions equally so the aspect ratio is unchanged,
            // just smaller than the screen.
            int w = Mathf.RoundToInt(native.width * windowedScale);
            int h = Mathf.RoundToInt(native.height * windowedScale);
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
        }

        RefreshLabel(fullscreen);
    }

    private void RefreshLabel(bool fullscreen)
    {
        if (buttonLabel != null)
            buttonLabel.text = fullscreen ? "Windowed" : "Fullscreen";
    }
}
