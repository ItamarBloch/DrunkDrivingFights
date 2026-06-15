using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class LobbyUIController : MonoBehaviour
{
    // ── Panels ───────────────────────────────────────────────────────────────
    [Header("=== PANELS ===")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject playSubMenu;
    [SerializeField] private GameObject quickMatchSearchPanel;
    [SerializeField] private GameObject matchBrowserPanel;
    [SerializeField] private GameObject createMatchPanel;
    [SerializeField] private GameObject matchLobbyPanel;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject passwordPromptPanel;

    // ── Main Menu ────────────────────────────────────────────────────────────
    [Header("=== MAIN MENU ===")]
    [SerializeField] private Button playButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button exitButton;

    // ── Play Sub-Menu ────────────────────────────────────────────────────────
    [Header("=== PLAY SUBMENU ===")]
    [SerializeField] private Button quickMatchButton;
    [SerializeField] private Button joinMatchButton;
    [SerializeField] private Button createMatchButton;

    // ── QuickMatch Search State ───────────────────────────────────────────────
    [Header("=== QUICKMATCH SEARCH ===")]
    [SerializeField] private TextMeshProUGUI quickMatchStatusText;
    [SerializeField] private TextMeshProUGUI quickMatchTimerText;
    [SerializeField] private Button quickMatchCancelButton;

    // ── Match Browser ─────────────────────────────────────────────────────────
    [Header("=== MATCH BROWSER ===")]
    [SerializeField] private Transform roomListContent;
    [SerializeField] private TextMeshProUGUI browserStatusText;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button backButton_Browser;
    [SerializeField] private Button joinByCodeButton;
    [SerializeField] private GameObject joinByCodePanel;
    [SerializeField] private TMP_InputField joinByCodeInput;
    [SerializeField] private Button joinByCodeConfirmButton;
    [SerializeField] private Button joinByCodeCloseButton;
    [SerializeField] private GameObject roomEntryPrefab;

    // ── Password Prompt ───────────────────────────────────────────────────────
    [Header("=== PASSWORD PROMPT ===")]
    [SerializeField] private TMP_InputField passwordPromptInput;
    [SerializeField] private Button passwordConfirmButton;
    [SerializeField] private Button passwordCancelButton;
    [SerializeField] private TextMeshProUGUI passwordPromptStatus;

    // ── Create Match ──────────────────────────────────────────────────────────
    [Header("=== CREATE MATCH ===")]
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;
    [SerializeField] private TMP_Dropdown mapDropdown;
    [SerializeField] private TMP_Dropdown gameModeDropdown;
    [SerializeField] private TMP_InputField passwordCreateInput;
    [SerializeField] private Button createButton;
    [SerializeField] private Button backButton_Create;

    // ── Match Lobby ───────────────────────────────────────────────────────────
    [Header("=== MATCH LOBBY ===")]
    [SerializeField] private TextMeshProUGUI lobbyRoomNameText;
    [SerializeField] private TextMeshProUGUI lobbyRoomCodeText;
    [SerializeField] private TextMeshProUGUI lobbyMapText;
    [SerializeField] private TextMeshProUGUI lobbyPlayerCountText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject playerEntryPrefab;

    // ── Options ───────────────────────────────────────────────────────────────
    [Header("=== OPTIONS ===")]
    [SerializeField] private Button backButton_Options;

    // ── Direct Connect (LAN direct / internet) ────────────────────────────────
    [Header("=== DIRECT CONNECT ===")]
    [SerializeField] private GameObject directConnectPanel;
    [SerializeField] private TMP_InputField directIPInput;
    [SerializeField] private Button directConnectButton;
    [SerializeField] private Button directConnectToggleButton;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum Screen { MainMenu, QuickMatchSearch, MatchBrowser, CreateMatch, MatchLobby, Options }
    private GameNetworkRoomManager manager;
    private bool playSubMenuOpen = false;
    private Coroutine quickMatchCoroutine;
    private Coroutine quickMatchTimerCoroutine;
    private bool quickMatchActive = false;

    // Set by OnJoinFailed so QuickMatchCoroutine knows the last join attempt failed
    // without halting the search entirely.
    private bool _quickMatchJoinFailed = false;

    // Timer state — updated every frame by QuickMatchTimerCoroutine
    private float _quickMatchElapsed = 0f;
    private string _quickMatchBaseStatus = "";

    // 99 minutes 59 seconds — display limit before auto-cancel
    private const float QuickMatchMaxSeconds = 99f * 60f + 59f;

    private DiscoveredRoom pendingRoom;
    private bool _passwordWasAttempted = false;

    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        manager = GameNetworkRoomManager.singleton;
        if (manager == null) { Debug.LogError("[LobbyUI] GameNetworkRoomManager not found!"); return; }

        SetupDropdowns();
        WireButtons();
        SubscribeEvents();
        ShowScreen(Screen.MainMenu);
        SetPlaySubMenu(false);

        if (PlayerPrefs.GetInt("PendingQuickMatch", 0) == 1)
        {
            PlayerPrefs.DeleteKey("PendingQuickMatch");
            StartQuickMatch();
        }
    }

    private void OnDestroy() => UnsubscribeEvents();

    private void Update()
    {
        bool inLobby = matchLobbyPanel != null && matchLobbyPanel.activeSelf;
        bool connected = NetworkClient.isConnected;

        // Don't switch screens while QuickMatch is actively probing connections.
        if (!quickMatchActive)
        {
            if (!inLobby && connected)
                ShowScreen(Screen.MatchLobby);

            if (inLobby && !connected && !NetworkServer.active)
            {
                StopQuickMatchSearch();
                ShowScreen(Screen.MainMenu);
            }
        }

        if (inLobby) UpdateReadyButton();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    private void SetupDropdowns()
    {
        if (maxPlayersDropdown != null)
        {
            maxPlayersDropdown.ClearOptions();
            maxPlayersDropdown.AddOptions(new List<string> { "2 Players", "4 Players", "6 Players", "8 Players" });
            maxPlayersDropdown.value = 2;
        }

        if (mapDropdown != null && manager?.MapRegistry != null)
        {
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(manager.MapRegistry.GetDisplayNames());
        }

        if (gameModeDropdown != null)
        {
            gameModeDropdown.ClearOptions();
            gameModeDropdown.AddOptions(new List<string> { "Last Man Standing" });
        }

        if (passwordCreateInput != null)
            passwordCreateInput.characterLimit = 10;
    }

    private void WireButtons()
    {
        // Main menu
        playButton?.onClick.AddListener(TogglePlaySubMenu);
        optionsButton?.onClick.AddListener(() => ShowScreen(Screen.Options));
        exitButton?.onClick.AddListener(Application.Quit);

        // Play sub-menu
        quickMatchButton?.onClick.AddListener(StartQuickMatch);
        joinMatchButton?.onClick.AddListener(() => ShowScreen(Screen.MatchBrowser));
        createMatchButton?.onClick.AddListener(() => ShowScreen(Screen.CreateMatch));

        // QuickMatch
        quickMatchCancelButton?.onClick.AddListener(StopQuickMatchSearch);

        // Match browser
        refreshButton?.onClick.AddListener(RefreshRoomList);
        backButton_Browser?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
        joinByCodeButton?.onClick.AddListener(ToggleJoinByCodePanel);
        joinByCodeConfirmButton?.onClick.AddListener(OnJoinByCode);
        joinByCodeCloseButton?.onClick.AddListener(ToggleJoinByCodePanel);
        directConnectToggleButton?.onClick.AddListener(ToggleDirectConnectPanel);
        directConnectButton?.onClick.AddListener(OnDirectConnect);

        // Password prompt
        passwordConfirmButton?.onClick.AddListener(OnPasswordConfirm);
        passwordCancelButton?.onClick.AddListener(ClosePasswordPrompt);

        // Create match
        createButton?.onClick.AddListener(OnCreateMatch);
        backButton_Create?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));

        // Match lobby
        readyButton?.onClick.AddListener(OnToggleReady);
        startButton?.onClick.AddListener(OnStartGame);
        leaveButton?.onClick.AddListener(OnLeaveRoom);

        // Options
        backButton_Options?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
    }

    private void SubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated += RefreshLobbyPlayers;
            manager.OnRoomCreated += HandleRoomCreated;
            manager.OnJoinFailed += OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged += RefreshLobbyPlayers;
    }

    private void UnsubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated -= RefreshLobbyPlayers;
            manager.OnRoomCreated -= HandleRoomCreated;
            manager.OnJoinFailed -= OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged -= RefreshLobbyPlayers;
    }

    // Named handler (not a lambda) so it can be unsubscribed in UnsubscribeEvents.
    // The manager is a DontDestroyOnLoad singleton, so a leaked subscription would
    // keep a destroyed LobbyUIController alive and fire ShowScreen on dead panels.
    private void HandleRoomCreated(string _) => ShowScreen(Screen.MatchLobby);

    // ── Screen Management ─────────────────────────────────────────────────────

    private void ShowScreen(Screen screen)
    {
        mainMenuPanel?.SetActive(screen == Screen.MainMenu);
        quickMatchSearchPanel?.SetActive(screen == Screen.QuickMatchSearch);
        matchBrowserPanel?.SetActive(screen == Screen.MatchBrowser);
        createMatchPanel?.SetActive(screen == Screen.CreateMatch);
        matchLobbyPanel?.SetActive(screen == Screen.MatchLobby);
        optionsPanel?.SetActive(screen == Screen.Options);

        if (screen != Screen.MainMenu && screen != Screen.QuickMatchSearch)
            SetPlaySubMenu(false);

        if (joinByCodePanel != null && screen != Screen.MatchBrowser)
            joinByCodePanel.SetActive(false);

        if (screen == Screen.MatchBrowser) RefreshRoomList();
        if (screen == Screen.MatchLobby) RefreshLobbyPlayers();
    }

    private void SetPlaySubMenu(bool open)
    {
        playSubMenuOpen = open;
        if (playSubMenu != null) playSubMenu.SetActive(open);
    }

    private void TogglePlaySubMenu() => SetPlaySubMenu(!playSubMenuOpen);

    // ── QuickMatch ────────────────────────────────────────────────────────────

    private void StartQuickMatch()
    {
        SetPlaySubMenu(false);
        ShowScreen(Screen.QuickMatchSearch);
        if (quickMatchStatusText != null) quickMatchStatusText.text = "Searching for a match...";
        if (quickMatchTimerText != null) quickMatchTimerText.text = "00:00";
        if (quickMatchCoroutine != null) StopCoroutine(quickMatchCoroutine);
        if (quickMatchTimerCoroutine != null) StopCoroutine(quickMatchTimerCoroutine);

        quickMatchActive = true;
        _quickMatchJoinFailed = false;
        _quickMatchElapsed = 0f;
        _quickMatchBaseStatus = "Searching for a match...";

        quickMatchTimerCoroutine = StartCoroutine(QuickMatchTimerCoroutine());
        quickMatchCoroutine      = StartCoroutine(QuickMatchCoroutine());
    }

    /// <summary>
    /// Runs every frame while QuickMatch is active.
    /// Tracks elapsed time, updates the display, and auto-cancels at 99:59.
    /// </summary>
    private IEnumerator QuickMatchTimerCoroutine()
    {
        while (quickMatchActive && !NetworkClient.isConnected)
        {
            _quickMatchElapsed += Time.deltaTime;
            //RefreshQuickMatchDisplay();

            if (_quickMatchElapsed >= QuickMatchMaxSeconds)
            {
                Debug.Log("[QuickMatch] Time limit reached (99:59) — cancelling search");
                StopQuickMatchSearch();
                yield break;
            }

            yield return null;
        }
    }

    /// <summary>
    /// Rebuilds the status text as:  "{base message}\n{MM:SS}"
    /// Call after changing _quickMatchBaseStatus or from the timer loop.
    /// </summary>
    //private void RefreshQuickMatchDisplay()
    //{
    //    if (quickMatchStatusText == null) return;
    //    int total = Mathf.FloorToInt(_quickMatchElapsed);
    //    int mins  = Mathf.Min(total / 60, 99);
    //    int secs  = total % 60;
    //    quickMatchStatusText.text = $"{_quickMatchBaseStatus}\n{mins:00}:{secs:00}";
    //}

    /// <summary>
    /// Continuously searches LAN + global servers for an available match.
    ///
    /// Flow per iteration:
    ///   1. Refresh room list (LAN broadcast + HTTP fetch, ~3-4s)
    ///   2. Filter: not full, not password protected
    ///   3. For each candidate, attempt to join and wait for success or failure
    ///   4. If all attempts fail or no rooms found → wait 5s → repeat from step 1
    ///
    /// Stops when: player cancels, connection succeeds, or timer hits 99:59.
    /// </summary>
    private IEnumerator QuickMatchCoroutine()
    {
        const float maxTime = 99 * 60f;
        float elapsed = 0f;
        float nextScanAt = 5f;
        bool localhostProbed = false;

        // Kick off an immediate LAN scan.
        manager.RefreshRoomList(TryJoinFromQuickMatch);

        while (quickMatchActive && elapsed < maxTime)
        {
            yield return new WaitForSeconds(1f);
            if (!quickMatchActive) yield break;

            elapsed += 1f;

            // Update MM:SS timer display.
            int mins = (int)(elapsed / 60f);
            int secs = (int)(elapsed % 60f);
            if (quickMatchTimerText != null) quickMatchTimerText.text = $"{mins:00}:{secs:00}";

            // Successfully connected — done.
            if (NetworkClient.isConnected) { quickMatchActive = false; yield break; }

            // Localhost probe once at ~4s (LAN broadcast doesn't loop back on Windows).
            if (!localhostProbed && elapsed >= 4f && !NetworkClient.active)
            {
                localhostProbed = true;
                if (quickMatchStatusText != null) quickMatchStatusText.text = "Trying local connection...";
                manager.JoinRoom("localhost", 7777);
            }
            else if (localhostProbed && elapsed >= 7f && quickMatchStatusText != null
                     && quickMatchStatusText.text != "Searching for a match...")
            {
                quickMatchStatusText.text = "Searching for a match...";
            }

            // Periodic LAN scan every 5s.
            if (elapsed >= nextScanAt)
            {
                nextScanAt += 5f;
                manager.RefreshRoomList(TryJoinFromQuickMatch);
            }
        }

        // Timed out at 99:00 — give up.
        quickMatchActive = false;
        if (!NetworkClient.isConnected)
        {
            if (quickMatchStatusText != null) quickMatchStatusText.text = "No matches found. Try creating one!";
            if (quickMatchTimerText != null) quickMatchTimerText.text = "99:00";
            yield return new WaitForSeconds(2f);
            ShowScreen(Screen.MainMenu);
        }
    }

    /// <summary>Callback for QuickMatch LAN scans — joins the first open room found.</summary>
    private void TryJoinFromQuickMatch(List<DiscoveredRoom> rooms)
    {
        if (!quickMatchActive || NetworkClient.active) return;
        var available = rooms.Where(r => r.currentPlayers < r.maxPlayers && !r.hasPassword).ToList();
        if (available.Count > 0)
        {
            var room = available[0];
            if (!string.IsNullOrEmpty(room.relayJoinCode))
                manager.JoinRoomViaRelay(room.relayJoinCode);
            else
                manager.JoinRoom(room.address, room.port);
        }
    }

    private void StopQuickMatchSearch()
    {
        quickMatchActive = false;

        if (quickMatchCoroutine != null)
        {
            StopCoroutine(quickMatchCoroutine);
            quickMatchCoroutine = null;
        }
        if (quickMatchTimerCoroutine != null)
        {
            StopCoroutine(quickMatchTimerCoroutine);
            quickMatchTimerCoroutine = null;
        }

        // Stop any in-progress connect attempt
        if (NetworkClient.active && !NetworkClient.isConnected)
            NetworkManager.singleton.StopClient();

        ShowScreen(Screen.MainMenu);
    }

    // ── Match Browser ─────────────────────────────────────────────────────────

    private void RefreshRoomList()
    {
        if (browserStatusText != null) browserStatusText.text = "Searching for matches...";
        ClearChildren(roomListContent);

        manager.RefreshRoomList(rooms =>
        {
            ClearChildren(roomListContent);

            if (rooms.Count == 0)
            {
                if (browserStatusText != null)
                    browserStatusText.text = "No matches found.";
                return;
            }

            if (browserStatusText != null)
                browserStatusText.text = $"Found {rooms.Count} match(es)";

            // Show local rooms first, then remote
            var local  = rooms.Where(r => r.address == "127.0.0.1" || r.address == "::1").ToList();
            var remote = rooms.Where(r => r.address != "127.0.0.1" && r.address != "::1").ToList();

            foreach (var room in local)  CreateRoomEntry(room);
            foreach (var room in remote) CreateRoomEntry(room);
        });
    }

    private void CreateRoomEntry(DiscoveredRoom room)
    {
        if (roomEntryPrefab == null || roomListContent == null) return;

        GameObject entry = Instantiate(roomEntryPrefab, roomListContent);
        entry.SetActive(true);

        var roomNameText = FindChildTMP(entry, "RoomNameText");
        var mapText      = FindChildTMP(entry, "MapText");
        var playersText  = FindChildTMP(entry, "PlayersText");
        var lockIcon     = FindChildGameObject(entry, "LockIcon");
        var joinButton   = FindChildButton(entry, "JoinButton");

        if (roomNameText != null) roomNameText.text = room.roomName;
        if (mapText      != null) mapText.text      = string.IsNullOrEmpty(room.mapName) ? "—" : room.mapName;
        if (playersText  != null)
            playersText.text = room.maxPlayers > 0 ? $"{room.currentPlayers}/{room.maxPlayers}" : "—";
        if (lockIcon     != null) lockIcon.SetActive(room.hasPassword);

        if (joinButton != null)
        {
            bool isFull = room.maxPlayers > 0 && room.currentPlayers >= room.maxPlayers;
            joinButton.interactable = !isFull;
            var joinText = joinButton.GetComponentInChildren<TextMeshProUGUI>();
            if (joinText != null) joinText.text = isFull ? "FULL" : "JOIN";

            DiscoveredRoom captured = room;
            joinButton.onClick.AddListener(() => TryJoinRoom(captured));
        }
    }

    private void TryJoinRoom(DiscoveredRoom room)
    {
        pendingRoom = room;
        _passwordWasAttempted = false;

        if (room.hasPassword)
            OpenPasswordPrompt();
        else if (!string.IsNullOrEmpty(room.relayJoinCode))
            manager.JoinRoomViaRelay(room.relayJoinCode);
        else
            manager.JoinRoom(room.address, room.port);
    }

    // ── Direct Connect (IP-based) ──────────────────────────────────────────────

    private void ToggleDirectConnectPanel()
    {
        if (directConnectPanel != null)
            directConnectPanel.SetActive(!directConnectPanel.activeSelf);
    }

    private void OnDirectConnect()
    {
        string input = directIPInput != null ? directIPInput.text.Trim() : "";
        if (string.IsNullOrEmpty(input)) return;

        string ip = input;
        int port = 7777;

        int colonIdx = input.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(input.Substring(colonIdx + 1), out int parsedPort))
        {
            ip = input.Substring(0, colonIdx);
            port = parsedPort;
        }

        Debug.Log($"[LobbyUI] Direct connect → {ip}:{port}");
        manager.JoinRoom(ip, port);
    }

    // ── Join By Code ──────────────────────────────────────────────────────────

    private void ToggleJoinByCodePanel()
    {
        if (joinByCodePanel != null)
            joinByCodePanel.SetActive(!joinByCodePanel.activeSelf);
    }

    private void OnJoinByCode()
    {
        if (joinByCodeInput == null) return;
        string code = joinByCodeInput.text.Trim().ToUpper();
        if (string.IsNullOrEmpty(code)) return;

        if (browserStatusText != null) browserStatusText.text = $"Searching for code {code}...";

        manager.RefreshRoomList(rooms =>
        {
            var match = rooms.FirstOrDefault(r => r.roomCode == code);
            if (match != null)
            {
                TryJoinRoom(match);
            }
            else
            {
                if (browserStatusText != null)
                    browserStatusText.text = $"Room with code '{code}' not found.";
            }
        });
    }

    // ── Password Prompt ───────────────────────────────────────────────────────

    private void OpenPasswordPrompt(bool isRetry = false)
    {
        if (passwordPromptPanel != null) passwordPromptPanel.SetActive(true);
        if (passwordPromptInput != null) passwordPromptInput.text = "";
        if (passwordPromptStatus != null)
            passwordPromptStatus.text = isRetry ? "Wrong password. Try again." : "";
    }

    private void ClosePasswordPrompt()
    {
        if (passwordPromptPanel != null) passwordPromptPanel.SetActive(false);
    }

    private void OnPasswordConfirm()
    {
        if (pendingRoom == null) { ClosePasswordPrompt(); return; }
        string pwd = passwordPromptInput != null ? passwordPromptInput.text : "";
        _passwordWasAttempted = true;
        ClosePasswordPrompt();

        if (!string.IsNullOrEmpty(pendingRoom.relayJoinCode))
            manager.JoinRoomViaRelay(pendingRoom.relayJoinCode, pwd);
        else
            manager.JoinRoom(pendingRoom.address, pendingRoom.port, pwd);
    }

    private void OnJoinFailed(string msg)
    {
        Debug.Log($"[LobbyUI] Join failed: {msg}");

        // QuickMatch: signal the coroutine that this attempt failed, then let it decide what to do.
        // We never stop the search from here — only the user can cancel.
        if (quickMatchActive)
        {
            _quickMatchJoinFailed = true;
            return;
        }

        // Manual join from browser
        if (msg.Contains("password") || msg.Contains("Password"))
        {
            // Return to browser so the password prompt has a backdrop
            ShowScreen(Screen.MatchBrowser);
            bool isRetry = _passwordWasAttempted;
            _passwordWasAttempted = false;
            OpenPasswordPrompt(isRetry);
        }
        else
        {
            ShowScreen(Screen.MainMenu);
        }
    }

    // ── Create Match ──────────────────────────────────────────────────────────

    private void OnCreateMatch()
    {
        int maxPlayers = (maxPlayersDropdown.value + 1) * 2;
        int mapIndex = mapDropdown != null ? mapDropdown.value : 0;
        string password = passwordCreateInput != null ? passwordCreateInput.text.Trim() : "";
        manager.CreateRoom($"Room_{Random.Range(1000, 9999)}", maxPlayers, mapIndex, password);
    }

    // ── Match Lobby ───────────────────────────────────────────────────────────

    private void RefreshLobbyPlayers()
    {
        if (matchLobbyPanel == null || !matchLobbyPanel.activeSelf) return;

        string displayRoomName = manager.roomName;
        string displayMap      = manager.selectedMap;
        string displayCode     = manager.roomCode;
        int    displayMax      = manager.maxConnections;

        if (!manager.IsOwner)
        {
            var players = manager.GetLobbyPlayers();
            if (players.Count > 0)
            {
                displayRoomName = players[0].syncedRoomName;
                displayMap      = players[0].syncedMapName;
                displayMax      = players[0].syncedMaxPlayers;
                displayCode     = players[0].syncedRoomCode;
            }
        }

        if (lobbyRoomNameText  != null) lobbyRoomNameText.text  = displayRoomName.ToUpper();
        if (lobbyRoomCodeText  != null) lobbyRoomCodeText.text  = $"Room Code: {displayCode}";
        if (lobbyMapText       != null) lobbyMapText.text       = $"Map: {displayMap}";
        if (lobbyPlayerCountText != null) lobbyPlayerCountText.text = $"Players: {manager.CurrentPlayerCount}/{displayMax}";

        ClearChildren(playerListContent);

        bool localIsOwner = LobbyPlayer.LocalPlayer != null && LobbyPlayer.LocalPlayer.isRoomOwner;
        foreach (var player in manager.GetLobbyPlayers())
            CreatePlayerEntry(player, localIsOwner);

        if (startButton != null) startButton.gameObject.SetActive(localIsOwner);
        if (readyButton != null) readyButton.gameObject.SetActive(!localIsOwner);

        if (localIsOwner && startButton != null)
            startButton.interactable = manager.AllPlayersReady && manager.CurrentPlayerCount >= 1;
    }

    private void CreatePlayerEntry(LobbyPlayer player, bool localIsOwner)
    {
        if (playerEntryPrefab == null || playerListContent == null) return;

        GameObject entry = Instantiate(playerEntryPrefab, playerListContent);
        entry.SetActive(true);

        var nameText   = FindChildTMP(entry, "NameText");
        var statusText = FindChildTMP(entry, "StatusText");
        var kickButton = FindChildButton(entry, "KickButton");

        if (nameText != null)
        {
            string display = player.playerName;
            if (player.isRoomOwner) display += " [HOST]";
            if (player.isOwned)     display += " (You)";
            nameText.text = display;
        }

        if (statusText != null)
        {
            if (player.isRoomOwner)
            { statusText.text = "HOST";      statusText.color = new Color(1f, 0.85f, 0.3f); }
            else if (player.readyToBegin)
            { statusText.text = "READY";     statusText.color = new Color(0.2f, 0.8f, 0.4f); }
            else
            { statusText.text = "NOT READY"; statusText.color = new Color(0.9f, 0.3f, 0.3f); }
        }

        if (kickButton != null)
        {
            bool showKick = localIsOwner && !player.isRoomOwner;
            kickButton.gameObject.SetActive(showKick);
            if (showKick)
            {
                uint targetNetId = player.netId;
                kickButton.onClick.AddListener(() => manager.KickPlayer(targetNetId));
            }
        }
    }

    private void UpdateReadyButton()
    {
        var local = LobbyPlayer.LocalPlayer;
        if (local == null || local.isRoomOwner || readyButton == null) return;
        var text = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = local.readyToBegin ? "UNREADY" : "READY";
    }

    // ── Lobby Actions ─────────────────────────────────────────────────────────

    private void OnToggleReady() => LobbyPlayer.LocalPlayer?.ToggleReady();
    private void OnStartGame()   => manager.StartGame();

    private void OnLeaveRoom()
    {
        manager.LeaveRoom();
        ShowScreen(Screen.MainMenu);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private TextMeshProUGUI FindChildTMP(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    private Button FindChildButton(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    private GameObject FindChildGameObject(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.gameObject : null;
    }
}
