using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections.Generic;

public class LobbyUIController : MonoBehaviour
{
    [Header("=== PANELS ===")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject roomBrowserPanel;
    [SerializeField] private GameObject createRoomPanel;
    [SerializeField] private GameObject inLobbyPanel;

    [Header("=== MAIN MENU ===")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button quickMatchButton;
    [SerializeField] private Button browseGamesButton;
    [SerializeField] private Button createGameButton;

    [Header("=== ROOM BROWSER ===")]
    [SerializeField] private Button backButton_Browser;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Transform roomListContent;
    [SerializeField] private TextMeshProUGUI browserStatusText;
    [SerializeField] private GameObject roomEntryPrefab;

    [Header("=== JOIN BY IP (for same-machine testing) ===")]
    [SerializeField] private TMP_InputField joinIPInput;
    [SerializeField] private Button joinIPButton;

    [Header("=== CREATE ROOM ===")]
    [SerializeField] private Button backButton_Create;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private TMP_Dropdown maxPlayersDropdown;
    [SerializeField] private TMP_Dropdown mapDropdown;
    [SerializeField] private Button createRoomButton;

    [Header("=== IN LOBBY ===")]
    [SerializeField] private TextMeshProUGUI roomNameText;
    [SerializeField] private TextMeshProUGUI mapInfoText;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Transform playerListContent;
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;

    [Header("=== PREFABS ===")]
    [SerializeField] private GameObject playerEntryPrefab;

    private enum Screen { MainMenu, RoomBrowser, CreateRoom, InLobby }
    private GameNetworkRoomManager manager;

    private void Start()
    {
        manager = GameNetworkRoomManager.singleton;
        if (manager == null)
        {
            Debug.LogError("[LobbyUI] GameNetworkRoomManager not found!");
            return;
        }

        string savedName = PlayerPrefs.GetString("PlayerName", $"Player_{Random.Range(100, 999)}");
        if (playerNameInput != null) playerNameInput.text = savedName;

        SetupDropdowns();
        WireButtons();
        SubscribeEvents();
        ShowScreen(Screen.MainMenu);
    }

    private void OnDestroy() { UnsubscribeEvents(); }

    private void Update()
    {
        // Auto-switch to lobby when we become connected (from any screen)
        bool isInLobbyScreen = inLobbyPanel != null && inLobbyPanel.activeSelf;
        bool isConnected = NetworkClient.isConnected;

        if (!isInLobbyScreen && isConnected)
        {
            Debug.Log("[LobbyUI] Connected detected — switching to InLobby screen");
            ShowScreen(Screen.InLobby);
        }

        // Auto-switch back to main menu if disconnected while in lobby
        if (isInLobbyScreen && !isConnected && !NetworkServer.active)
            ShowScreen(Screen.MainMenu);

        // Update ready button text dynamically
        if (isInLobbyScreen)
            UpdateReadyButtonVisual();
    }

    private void SetupDropdowns()
    {
        if (maxPlayersDropdown != null)
        {
            maxPlayersDropdown.ClearOptions();
            maxPlayersDropdown.AddOptions(new List<string> { "2 Players", "4 Players", "6 Players", "8 Players" });
            maxPlayersDropdown.value = 2;
        }

        // Populate map dropdown from the MapRegistry (single source of truth)
        if (mapDropdown != null && manager != null && manager.MapRegistry != null)
        {
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(manager.MapRegistry.GetDisplayNames());
        }
    }

    private void WireButtons()
    {
        quickMatchButton?.onClick.AddListener(OnQuickMatch);
        browseGamesButton?.onClick.AddListener(() => ShowScreen(Screen.RoomBrowser));
        createGameButton?.onClick.AddListener(() => ShowScreen(Screen.CreateRoom));
        playerNameInput?.onEndEdit.AddListener((val) => PlayerPrefs.SetString("PlayerName", val));

        backButton_Browser?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
        refreshButton?.onClick.AddListener(RefreshRoomList);
        joinIPButton?.onClick.AddListener(OnJoinByIP);

        backButton_Create?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
        createRoomButton?.onClick.AddListener(OnCreateRoom);

        leaveButton?.onClick.AddListener(OnLeaveRoom);
        readyButton?.onClick.AddListener(OnToggleReady);
        startGameButton?.onClick.AddListener(OnStartGame);
    }

    private void SubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated += RefreshLobbyPlayers;
            manager.OnRoomCreated += (name) => ShowScreen(Screen.InLobby);
            manager.OnJoinFailed += (msg) => { Debug.LogWarning(msg); ShowScreen(Screen.MainMenu); };
        }
        LobbyPlayer.OnAnyPlayerDataChanged += RefreshLobbyPlayers;
    }

    private void UnsubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated -= RefreshLobbyPlayers;
        }
        LobbyPlayer.OnAnyPlayerDataChanged -= RefreshLobbyPlayers;
    }

    // ─── Screen Management ────────────────────────────────────

    private void ShowScreen(Screen screen)
    {
        if (mainMenuPanel != null) mainMenuPanel.SetActive(screen == Screen.MainMenu);
        if (roomBrowserPanel != null) roomBrowserPanel.SetActive(screen == Screen.RoomBrowser);
        if (createRoomPanel != null) createRoomPanel.SetActive(screen == Screen.CreateRoom);
        if (inLobbyPanel != null) inLobbyPanel.SetActive(screen == Screen.InLobby);

        if (screen == Screen.RoomBrowser) RefreshRoomList();
        if (screen == Screen.InLobby) RefreshLobbyPlayers();
    }

    // ─── Button Actions ───────────────────────────────────────

    private void OnQuickMatch()
    {
        SavePlayerName();
        manager.QuickMatch();
    }

    private void OnCreateRoom()
    {
        SavePlayerName();
        string name = roomNameInput != null ? roomNameInput.text : $"Room_{Random.Range(100, 999)}";
        int maxPlayers = (maxPlayersDropdown.value + 1) * 2;
        int mapIndex = mapDropdown != null ? mapDropdown.value : 0;
        manager.CreateRoom(name, maxPlayers, mapIndex);
    }

    private void OnLeaveRoom()
    {
        manager.LeaveRoom();
        ShowScreen(Screen.MainMenu);
    }

    private void OnJoinByIP()
    {
        SavePlayerName();
        string ip = joinIPInput != null ? joinIPInput.text.Trim() : "localhost";
        if (string.IsNullOrEmpty(ip)) ip = "localhost";
        Debug.Log($"[LobbyUI] Joining by IP: {ip}");
        manager.JoinRoom(ip, 7777);
    }

    private void OnToggleReady()
    {
        var local = LobbyPlayer.LocalPlayer;
        if (local != null) local.ToggleReady();
    }

    private void OnStartGame()
    {
        manager.StartGame();
    }

    // ─── Room Browser ─────────────────────────────────────────

    private void RefreshRoomList()
    {
        if (browserStatusText != null) browserStatusText.text = "Searching for rooms...";
        ClearChildren(roomListContent);

        manager.RefreshRoomList((rooms) =>
        {
            ClearChildren(roomListContent);

            // Always add a localhost option for same-machine testing
            var localhostRoom = new DiscoveredRoom
            {
                roomName = "Local Game (this PC)",
                address = "localhost",
                port = 7777,
                currentPlayers = 0,
                maxPlayers = 0,
                mapName = "Direct Connect - localhost:7777",
                serverId = -1
            };
            CreateRoomEntry(localhostRoom);

            if (rooms.Count == 0)
            {
                if (browserStatusText != null)
                    browserStatusText.text = "No LAN rooms found. Use localhost for same-PC testing.";
            }
            else
            {
                if (browserStatusText != null)
                    browserStatusText.text = $"Found {rooms.Count} room(s)";

                foreach (var room in rooms)
                    CreateRoomEntry(room);
            }
        });
    }

    private void CreateRoomEntry(DiscoveredRoom room)
    {
        if (roomEntryPrefab == null || roomListContent == null) return;

        GameObject entry = Instantiate(roomEntryPrefab, roomListContent);
        entry.SetActive(true);

        var nameText = FindChildTMP(entry, "RoomEntryName");
        var detailsText = FindChildTMP(entry, "RoomEntryDetails");
        var joinButton = FindChildButton(entry, "JoinButton");

        if (nameText != null) nameText.text = room.roomName;
        if (detailsText != null) detailsText.text = $"{room.mapName} | {room.currentPlayers}/{room.maxPlayers} players";

        if (joinButton != null)
        {
            bool isFull = room.maxPlayers > 0 && room.currentPlayers >= room.maxPlayers;
            joinButton.interactable = !isFull;
            var joinText = joinButton.GetComponentInChildren<TextMeshProUGUI>();
            if (joinText != null) joinText.text = isFull ? "FULL" : "JOIN";

            string address = room.address;
            int port = room.port;
            joinButton.onClick.AddListener(() => manager.JoinRoom(address, port));
        }
    }

    // ─── Lobby Player List ────────────────────────────────────

    private void RefreshLobbyPlayers()
    {
        if (inLobbyPanel == null || !inLobbyPanel.activeSelf) return;

        // Get room info — host reads from manager, clients read from synced player data
        string displayRoomName = manager.roomName;
        string displayMap = manager.selectedMap;
        int displayMax = manager.maxConnections;

        // If we're a client (not host), get room info from any LobbyPlayer's synced data
        if (!manager.IsOwner)
        {
            var players = manager.GetLobbyPlayers();
            if (players.Count > 0)
            {
                displayRoomName = players[0].syncedRoomName;
                displayMap = players[0].syncedMapName;
                displayMax = players[0].syncedMaxPlayers;
            }
        }

        if (roomNameText != null) roomNameText.text = displayRoomName.ToUpper();
        if (mapInfoText != null) mapInfoText.text = $"Map: {displayMap}";
        if (playerCountText != null) playerCountText.text = $"Players: {manager.CurrentPlayerCount}/{displayMax}";

        ClearChildren(playerListContent);

        var allPlayers = manager.GetLobbyPlayers();
        foreach (var player in allPlayers)
            CreatePlayerEntry(player);

        var localPlayer = LobbyPlayer.LocalPlayer;
        bool isOwner = localPlayer != null && localPlayer.isRoomOwner;

        if (startGameButton != null) startGameButton.gameObject.SetActive(isOwner);
        if (readyButton != null) readyButton.gameObject.SetActive(!isOwner);

        if (isOwner && startGameButton != null)
        {
            bool canStart = manager.AllPlayersReady && manager.CurrentPlayerCount >= 1;
            startGameButton.interactable = canStart;
        }
    }

    private void CreatePlayerEntry(LobbyPlayer player)
    {
        if (playerEntryPrefab == null || playerListContent == null) return;

        GameObject entry = Instantiate(playerEntryPrefab, playerListContent);
        entry.SetActive(true);

        var colorImage = FindChildImage(entry, "ColorImage");
        var nameText = FindChildTMP(entry, "NameText");
        var statusText = FindChildTMP(entry, "StatusText");

        if (colorImage != null) colorImage.color = player.playerColor;

        if (nameText != null)
        {
            string displayName = player.playerName;
            if (player.isRoomOwner) displayName += " [HOST]";
            if (player.isOwned) displayName += " (You)";
            nameText.text = displayName;
        }

        if (statusText != null)
        {
            if (player.isRoomOwner)
            {
                statusText.text = "OWNER";
                statusText.color = new Color(1f, 0.85f, 0.3f);
            }
            else if (player.readyToBegin)
            {
                statusText.text = "READY";
                statusText.color = new Color(0.2f, 0.8f, 0.4f);
            }
            else
            {
                statusText.text = "NOT READY";
                statusText.color = new Color(0.9f, 0.3f, 0.3f);
            }
        }
    }

    private void UpdateReadyButtonVisual()
    {
        var local = LobbyPlayer.LocalPlayer;
        if (local == null || local.isRoomOwner || readyButton == null) return;
        var text = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null) text.text = local.readyToBegin ? "UNREADY" : "READY";
    }

    // ─── Helpers ──────────────────────────────────────────────

    private void SavePlayerName()
    {
        if (playerNameInput != null)
            PlayerPrefs.SetString("PlayerName", playerNameInput.text);
    }

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

    private Image FindChildImage(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<Image>() : null;
    }
}
