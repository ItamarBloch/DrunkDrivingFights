using UnityEngine;
using UnityEngine.UI;
using Mirror;

/// <summary>
/// Place on any scene object in SampleScene.
/// Wire up the panel and buttons in the Inspector.
///
/// ESC opens the panel and unlocks the cursor.
/// ESC again (or Resume) closes it and re-locks.
/// ThirdPersonCameraController already stops orbiting when the cursor is unlocked.
/// </summary>
public class InGameOptionsPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button goToLobbyButton;
    [SerializeField] private Button resumeButton;

    public static bool IsOpen { get; private set; }

    private void Start()
    {
        panelRoot.SetActive(false);
        goToLobbyButton.onClick.AddListener(OnGoToLobby);
        resumeButton.onClick.AddListener(Close);
    }

    private void Update()
    {
        // Only act when a local player exists (i.e. we're in a match)
        if (NetworkClient.localPlayer == null) return;

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            Toggle();
    }

    private void Toggle() { if (IsOpen) Close(); else Open(); }

    public void Open()
    {
        IsOpen = true;
        panelRoot.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        IsOpen = false;
        panelRoot.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnGoToLobby()
    {
        Close();
        GameNetworkRoomManager.singleton?.ReturnToLobby();
    }
}
