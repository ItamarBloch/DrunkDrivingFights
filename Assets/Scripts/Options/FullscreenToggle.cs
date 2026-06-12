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
/// It only flips Screen.fullScreen — it never forces a resolution, so the
/// aspect ratio (and therefore your UI layout) is left untouched. Unity
/// remembers the fullscreen choice across sessions on its own.
/// </summary>
public class FullscreenToggle : MonoBehaviour
{
    [Tooltip("The button the player clicks. Its onClick is hooked up in code.")]
    [SerializeField] private Button toggleButton;

    [Tooltip("Optional. Text that shows the CURRENT mode and updates on toggle.")]
    [SerializeField] private TMP_Text buttonLabel;

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
        // Keeps the current resolution; only the windowing mode changes.
        Screen.fullScreen = fullscreen;
        RefreshLabel(fullscreen);
    }

    private void RefreshLabel(bool fullscreen)
    {
        if (buttonLabel != null)
            buttonLabel.text = fullscreen ? "Windowed" : "Fullscreen";
    }
}
