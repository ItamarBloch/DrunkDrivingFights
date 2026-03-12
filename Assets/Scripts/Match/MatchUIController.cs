using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Countdown overlay — entirely self-contained, no Inspector wiring needed.
/// Creates its own Canvas + text at runtime so there is nothing to set up wrong.
/// Lives on the Car prefab (any component in the hierarchy works).
/// End-game screen is handled by EndGameScreen.cs (scene object).
/// </summary>
public class MatchUIController : NetworkBehaviour
{
    // ── Runtime (created in code) ─────────────────────────────

    private GameObject         _countdownGO;
    private TextMeshProUGUI    _countdownTMP;
    private float              _countdownRemaining;
    private bool               _countingDown;

    // ════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ════════════════════════════════════════════════════════

    public override void OnStartLocalPlayer()
    {
        MatchManager.OnCountdownBegan += HandleCountdownBegan;
        MatchManager.OnMatchBegan     += HandleMatchBegan;
        MatchManager.OnMatchEnded     += HandleMatchEnded;
        SyncToCurrentState();
    }

    private void OnDestroy()
    {
        MatchManager.OnCountdownBegan -= HandleCountdownBegan;
        MatchManager.OnMatchBegan     -= HandleMatchBegan;
        MatchManager.OnMatchEnded     -= HandleMatchEnded;
        DestroyOverlay();
    }

    // ════════════════════════════════════════════════════════
    //  HANDLERS
    // ════════════════════════════════════════════════════════

    private void HandleCountdownBegan(float duration)
    {
        if (!isLocalPlayer) return;
        _countdownRemaining = duration;
        _countingDown       = true;
        CreateOverlay();
        RefreshText();
    }

    private void HandleMatchBegan()
    {
        if (!isLocalPlayer) return;
        _countingDown = false;
        DestroyOverlay();
    }

    private void HandleMatchEnded(uint _)
    {
        if (!isLocalPlayer) return;
        _countingDown = false;
        DestroyOverlay();
    }

    // ════════════════════════════════════════════════════════
    //  COUNTDOWN TICK
    // ════════════════════════════════════════════════════════

    private void Update()
    {
        if (!isLocalPlayer || !_countingDown) return;
        _countdownRemaining = Mathf.Max(0f, _countdownRemaining - Time.deltaTime);
        RefreshText();
    }

    private void RefreshText()
    {
        if (_countdownTMP == null) return;
        int secs = Mathf.CeilToInt(_countdownRemaining);
        _countdownTMP.text = secs > 0 ? secs.ToString() : "GO!";
    }

    // ════════════════════════════════════════════════════════
    //  OVERLAY — built entirely in code
    // ════════════════════════════════════════════════════════

    private void CreateOverlay()
    {
        DestroyOverlay();

        _countdownGO = new GameObject("CountdownOverlay");

        // Canvas — sort order 150 so it sits above the car canvas
        var canvas = _countdownGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        _countdownGO.AddComponent<CanvasScaler>();
        _countdownGO.AddComponent<GraphicRaycaster>();

        // Dark background
        var bgGO  = new GameObject("Background");
        bgGO.transform.SetParent(_countdownGO.transform, false);
        var img   = bgGO.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.65f);
        img.raycastTarget = false;
        var bgRect        = bgGO.GetComponent<RectTransform>();
        bgRect.anchorMin  = Vector2.zero;
        bgRect.anchorMax  = Vector2.one;
        bgRect.offsetMin  = bgRect.offsetMax = Vector2.zero;

        // Countdown number
        var textGO = new GameObject("CountdownText");
        textGO.transform.SetParent(_countdownGO.transform, false);
        _countdownTMP               = textGO.AddComponent<TextMeshProUGUI>();
        _countdownTMP.alignment     = TextAlignmentOptions.Center;
        _countdownTMP.fontSize      = 120f;
        _countdownTMP.fontStyle     = FontStyles.Bold;
        _countdownTMP.color         = Color.white;
        _countdownTMP.raycastTarget = false;

        var textRect        = textGO.GetComponent<RectTransform>();
        textRect.anchorMin  = new Vector2(0f, 0.35f);
        textRect.anchorMax  = new Vector2(1f, 0.65f);
        textRect.offsetMin  = textRect.offsetMax = Vector2.zero;
    }

    private void DestroyOverlay()
    {
        if (_countdownGO != null) Destroy(_countdownGO);
        _countdownGO  = null;
        _countdownTMP = null;
    }

    // ════════════════════════════════════════════════════════
    //  LATE-JOIN CATCH-UP
    // ════════════════════════════════════════════════════════

    private void SyncToCurrentState()
    {
        if (MatchManager.singleton == null) return;
        if (MatchManager.singleton.State == MatchState.Countdown)
        {
            float remaining = (float)(MatchManager.singleton.CountdownEndTime - NetworkTime.time);
            HandleCountdownBegan(Mathf.Max(0f, remaining));
        }
    }
}
