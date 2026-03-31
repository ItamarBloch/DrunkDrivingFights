using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// End-game screen. Lives as a SCENE OBJECT in SampleScene — NOT on the Car prefab.
///
/// Why not on the Car prefab?
///   When the host leaves, Mirror destroys all networked car objects on every client.
///   By living in the scene, this canvas survives that destruction and the
///   buttons remain clickable.
///
/// Canvas must be Screen Space - Overlay so it requires no camera.
///
/// Scene setup:
///   SampleScene → EndGameScreen (GameObject)
///     └── Canvas (Screen Space - Overlay)
///           └── Panel → wire all references below
/// </summary>
public class EndGameScreen : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panel;

    [Header("Texts")]
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text winnerNameText;
    [SerializeField] private TMP_Text kdaText;

    [Header("Sounds")]
    [Tooltip("Key in SoundRegistry for the win jingle.")]
    [SerializeField] private string winSoundKey  = "match_win";
    [Tooltip("Key in SoundRegistry for the lose jingle.")]
    [SerializeField] private string loseSoundKey = "match_lose";

    [Header("Buttons")]
    [SerializeField] private Button returnToLobbyButton;
    [SerializeField] private Button quickMatchButton;

    // ════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════

    private void Start()
    {
        panel?.SetActive(false);

        returnToLobbyButton?.onClick.AddListener(OnReturnToLobby);
        quickMatchButton?.onClick.AddListener(OnQuickMatch);

        MatchManager.OnMatchEnded     += HandleMatchEnded;
        MatchManager.OnLocalStatsReady += HandleStatsReady;

        // The game scene often has no EventSystem (it lived in the lobby scene).
        // Without one, button clicks are never processed — create one if missing.
        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();
            Debug.Log("[EndGameScreen] Created missing EventSystem.");
        }

        // Render above everything else (car canvas + death panel sit below this).
        var canvas = GetComponent<Canvas>();
        if (canvas != null) canvas.sortingOrder = 200;
    }

    private void OnDestroy()
    {
        MatchManager.OnMatchEnded      -= HandleMatchEnded;
        MatchManager.OnLocalStatsReady -= HandleStatsReady;
    }

    // ════════════════════════════════════════════════════════
    //  MATCH ENDED
    // ════════════════════════════════════════════════════════

    private void HandleMatchEnded(uint winnerNetId)
    {
        // Capture the local player's netId NOW while the car object still exists.
        // (The host may destroy car objects shortly after this fires.)
        uint localNetId = NetworkClient.localPlayer != null
            ? NetworkClient.localPlayer.netId
            : 0;

        bool localWon = winnerNetId != 0 && winnerNetId == localNetId;

        ShowScreen(localWon, winnerNetId);
    }

    private void ShowScreen(bool won, uint winnerNetId)
    {
        panel?.SetActive(true);

        // Unlock cursor so the player can click the buttons.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;

        if (resultText != null)
            resultText.text = won ? "YOU WON!" : "YOU LOST";

        if (winnerNameText != null)
            winnerNameText.text = winnerNetId == 0
                ? "It's a draw!"
                : $"Winner: {PlayerInfo.GetName(winnerNetId)}";

        // Stats will be filled in by HandleStatsReady; show placeholder until it arrives.
        if (kdaText != null)
            kdaText.text = "K: -   D: -   DMG: -";

        // Play win or lose sound
        string soundKey = won ? winSoundKey : loseSoundKey;
        SoundManager.Instance?.PlayAtPoint(soundKey, Vector3.zero);
    }

    private void HandleStatsReady(int kills, int deaths, int damageDealt)
    {
        if (kdaText != null)
            kdaText.text = $"K: {kills}   D: {deaths}   DMG: {damageDealt}";
    }

    // ════════════════════════════════════════════════════════
    //  BUTTONS
    // ════════════════════════════════════════════════════════

    private void OnReturnToLobby()
    {
        var mgr = GameNetworkRoomManager.singleton;
        if (mgr != null)
            mgr.ReturnToLobby();
        else
            SceneManager.LoadScene("LobbyScene");
    }

    private void OnQuickMatch()
    {
        // Tell LobbyUIController to auto-trigger QuickMatch when lobby loads.
        PlayerPrefs.SetInt("PendingQuickMatch", 1);

        var mgr = GameNetworkRoomManager.singleton;
        if (mgr != null)
            mgr.LeaveRoom();   // always fully disconnect for quick match

        SceneManager.LoadScene("LobbyScene");
    }
}
