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
    private bool quickMatchActive = false;
    private DiscoveredRoom pendingRoom;
    private bool _passwordWasAttempted = false; // true only after the user clicked Confirm

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

        if (!inLobby && connected)
            ShowScreen(Screen.MatchLobby);

        if (inLobby && !connected && !NetworkServer.active)
        {
            StopQuickMatchSearch();
            ShowScreen(Screen.MainMenu);
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
            manager.OnRoomCreated += (_) => ShowScreen(Screen.MatchLobby);
            manager.OnJoinFailed += OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged += RefreshLobbyPlayers;
    }

    private void UnsubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated -= RefreshLobbyPlayers;
            manager.OnJoinFailed -= OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged -= RefreshLobbyPlayers;
    }

    // ── Screen Management ─────────────────────────────────────────────────────

    private void ShowScreen(Screen screen)
    {
        mainMenuPanel?.SetActive(screen == Screen.MainMenu || screen == Screen.QuickMatchSearch);
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
        if (quickMatchCoroutine != null) StopCoroutine(quickMatchCoroutine);
        quickMatchActive = true;
        quickMatchCoroutine = StartCoroutine(QuickMatchCoroutine());
    }

    private IEnumerator QuickMatchCoroutine()
    {
        const float totalTimeout = 60f;
        float elapsed = 0f;

        // ── Step 1: LAN search (3s) ──────────────────────────────────────────
        if (quickMatchStatusText != null)
            quickMatchStatusText.text = "Searching for a match...";

        bool lanFound = false;
        manager.RefreshRoomList((rooms) =>
        {
            var available = rooms.Where(r => r.currentPlayers < r.maxPlayers && !r.hasPassword).ToList();
            if (available.Count > 0 && !NetworkClient.isConnected)
            {
                lanFound = true;
                manager.JoinRoom(available[0].address, available[0].port);
            }
        });

        // Wait for LAN discovery to finish (3s)
        yield return new WaitForSeconds(4f);
        elapsed += 4f;

        if (NetworkClient.isConnected) { quickMatchActive = false; yield break; }

        // ── Step 2: localhost probe (same-machine testing) ───────────────────
        // LAN broadcast doesn't reliably loop back on Windows, so same-machine games
        // won't show up in step 1. We probe localhost directly.
        // If the game there is password-protected the server rejects us — quickMatchActive
        // suppresses all UI so the user never sees a prompt. The _pendingAuth guard in
        // PasswordAuthenticator also prevents any stale rejection messages from firing later.
        if (!lanFound)
        {
            if (quickMatchStatusText != null)
                quickMatchStatusText.text = "Trying local connection...";

            manager.JoinRoom("localhost", 7777);
            yield return new WaitForSeconds(3f);
            elapsed += 3f;

            if (NetworkClient.isConnected) { quickMatchActive = false; yield break; }
        }

        // ── Step 3: keep scanning LAN until timeout ──────────────────────────
        while (elapsed < totalTimeout && !NetworkClient.isConnected)
        {
            int remaining = Mathf.CeilToInt(totalTimeout - elapsed);
            if (quickMatchStatusText != null)
                quickMatchStatusText.text = $"Searching for a match... ({remaining}s)";

            manager.RefreshRoomList((rooms) =>
            {
                var available = rooms.Where(r => r.currentPlayers < r.maxPlayers && !r.hasPassword).ToList();
                if (available.Count > 0 && !NetworkClient.isConnected)
                    manager.JoinRoom(available[0].address, available[0].port);
            });

            yield return new WaitForSeconds(5f);
            elapsed += 5f;
        }

        // ── Step 4: give up ──────────────────────────────────────────────────
        quickMatchActive = false;
        if (!NetworkClient.isConnected)
        {
            if (quickMatchStatusText != null)
                quickMatchStatusText.text = "No matches found. Try creating one!";
            yield return new WaitForSeconds(3f);
            ShowScreen(Screen.MainMenu);
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
        ShowScreen(Screen.MainMenu);
    }

    // ── Match Browser ─────────────────────────────────────────────────────────

    private void RefreshRoomList()
    {
        if (browserStatusText != null) browserStatusText.text = "Searching for matches...";
        ClearChildren(roomListContent);

        manager.RefreshRoomList((rooms) =>
        {
            ClearChildren(roomListContent);

            // Prefer a discovered room at localhost over a hardcoded entry so we get
            // real data (hasPassword, player count, room name, map).
            var localDiscovered = rooms.FirstOrDefault(r =>
                r.address == "127.0.0.1" || r.address == "localhost" || r.address == "::1");

            var remoteRooms = localDiscovered != null
                ? rooms.Where(r => r.serverId != localDiscovered.serverId).ToList()
                : rooms;

            int total = remoteRooms.Count + (localDiscovered != null ? 1 : 0);

            if (total == 0)
            {
                if (browserStatusText != null)
                    browserStatusText.text = "No matches found.";
            }
            else
            {
                if (browserStatusText != null) browserStatusText.text = $"Found {total} match(es)";
                if (localDiscovered != null) CreateRoomEntry(localDiscovered);
                foreach (var room in remoteRooms) CreateRoomEntry(room);
            }
        });
    }

    private void CreateRoomEntry(DiscoveredRoom room)
    {
        if (roomEntryPrefab == null || roomListContent == null) return;

        GameObject entry = Instantiate(roomEntryPrefab, roomListContent);
        entry.SetActive(true);

        var roomNameText = FindChildTMP(entry, "RoomNameText");
        var mapText = FindChildTMP(entry, "MapText");
        var playersText = FindChildTMP(entry, "PlayersText");
        var lockIcon = FindChildGameObject(entry, "LockIcon");
        var joinButton = FindChildButton(entry, "JoinButton");

        if (roomNameText != null) roomNameText.text = room.roomName;
        if (mapText != null) mapText.text = room.mapName;
        if (playersText != null)
            playersText.text = room.maxPlayers > 0 ? $"{room.currentPlayers}/{room.maxPlayers}" : "—";
        if (lockIcon != null) lockIcon.SetActive(room.hasPassword);

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
        // Always set pendingRoom so OnJoinFailed("Wrong password.") can retry with the prompt
        // even if the room wasn't marked as password-protected in the browser.
        pendingRoom = room;
        _passwordWasAttempted = false;

        if (room.hasPassword)
        {
            OpenPasswordPrompt();
        }
        else
        {
            manager.JoinRoom(room.address, room.port);
        }
    }

    // ── Direct Connect (IP-based — LAN direct or internet) ───────────────────

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

        // Support "ip:port" format
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

        if (browserStatusText != null) browserStatusText.text = $"Searching for room with code {code}...";

        manager.RefreshRoomList((rooms) =>
        {
            var match = rooms.FirstOrDefault(r => r.roomCode == code);
            if (match != null)
            {
                TryJoinRoom(match);
            }
            else
            {
                // Not found on LAN — try localhost (same-machine testing)
                // Code is trusted since both instances are on the same PC
                if (browserStatusText != null)
                    browserStatusText.text = $"Not found on LAN. Trying local connection...";
                manager.JoinRoom("localhost", 7777);
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
        manager.JoinRoom(pendingRoom.address, pendingRoom.port, pwd);
    }

    private void OnJoinFailed(string msg)
    {
        Debug.Log($"[LobbyUI] Join failed: {msg}");

        // During QuickMatch, failed attempts are expected — let the coroutine handle flow.
        if (quickMatchActive) return;

        if (msg.Contains("password") || msg.Contains("Password"))
        {
            // Ensure we're on the browser screen first so the prompt has a panel to appear over.
            // Something can navigate away during the 1-second server-side delay before rejection.
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
        string displayMap = manager.selectedMap;
        string displayCode = manager.roomCode;
        int displayMax = manager.maxConnections;

        if (!manager.IsOwner)
        {
            var players = manager.GetLobbyPlayers();
            if (players.Count > 0)
            {
                displayRoomName = players[0].syncedRoomName;
                displayMap = players[0].syncedMapName;
                displayMax = players[0].syncedMaxPlayers;
                displayCode = players[0].syncedRoomCode;
            }
        }

        if (lobbyRoomNameText != null) lobbyRoomNameText.text = displayRoomName.ToUpper();
        if (lobbyRoomCodeText != null) lobbyRoomCodeText.text = $"CODE: {displayCode}";
        if (lobbyMapText != null) lobbyMapText.text = $"Map: {displayMap}";
        if (lobbyPlayerCountText != null) lobbyPlayerCountText.text = $"{manager.CurrentPlayerCount}/{displayMax}";

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

        var nameText = FindChildTMP(entry, "NameText");
        var statusText = FindChildTMP(entry, "StatusText");
        var kickButton = FindChildButton(entry, "KickButton");

        if (nameText != null)
        {
            string display = player.playerName;
            if (player.isRoomOwner) display += " [HOST]";
            if (player.isOwned) display += " (You)";
            nameText.text = display;
        }

        if (statusText != null)
        {
            if (player.isRoomOwner)
            { statusText.text = "HOST"; statusText.color = new Color(1f, 0.85f, 0.3f); }
            else if (player.readyToBegin)
            { statusText.text = "READY"; statusText.color = new Color(0.2f, 0.8f, 0.4f); }
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
    private void OnStartGame() => manager.StartGame();

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
