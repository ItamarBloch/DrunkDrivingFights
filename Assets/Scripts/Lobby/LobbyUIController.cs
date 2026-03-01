using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Lobby UI Controller — handles all lobby screen logic.
/// 
/// BUILD THE UI IN THE EDITOR, then drag references into the Inspector.
/// This script does NOT create any UI objects — it only controls them.
/// 
/// Attach to the LobbyCanvas (or any persistent object in the Lobby scene).
/// </summary>
public class LobbyUIController : MonoBehaviour
{
    // ═══════════════════════════════════════════════════════════════
    // INSPECTOR REFERENCES — drag your UI elements here
    // ═══════════════════════════════════════════════════════════════

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
    [SerializeField] private Transform roomListContent;       // The "Content" inside the ScrollView
    [SerializeField] private TextMeshProUGUI browserStatusText;
    [SerializeField] private GameObject roomEntryPrefab;      // Prefab or disabled template for room entries

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
    [SerializeField] private Transform playerListContent;     // The "Content" inside the PlayerList ScrollView
    [SerializeField] private Button leaveButton;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;

    [Header("=== PLAYER ENTRY (for lobby player list) ===")]
    [Tooltip("A small prefab/template with: ColorImage, NameText, StatusText")]
    [SerializeField] private GameObject playerEntryPrefab;    // Prefab or disabled template for player entries

    // ─── State ────────────────────────────────────────────────────
    private enum Screen { MainMenu, RoomBrowser, CreateRoom, InLobby }
    private GameNetworkRoomManager manager;

    // ═══════════════════════════════════════════════════════════════
    // LIFECYCLE
    // ═══════════════════════════════════════════════════════════════

    private void Start()
    {
        manager = GameNetworkRoomManager.singleton;

        if (manager == null)
        {
            Debug.LogError("[LobbyUI] GameNetworkRoomManager not found!");
            return;
        }

        // Load saved player name
        string savedName = PlayerPrefs.GetString("PlayerName", $"Player_{Random.Range(100, 999)}");
        if (playerNameInput != null)
            playerNameInput.text = savedName;

        // Populate dropdowns
        SetupDropdowns();

        // Wire up all buttons
        WireButtons();

        // Subscribe to network events
        SubscribeEvents();

        // Start on main menu
        ShowScreen(Screen.MainMenu);
    }

    private void OnDestroy()
    {
        UnsubscribeEvents();
    }

    private void Update()
    {
        // Auto-switch to lobby when connected as client
        if (roomBrowserPanel.activeSelf && NetworkClient.isConnected)
            ShowScreen(Screen.InLobby);

        // Auto-switch back to main menu if disconnected while in lobby
        if (inLobbyPanel.activeSelf && !NetworkClient.isConnected && !NetworkServer.active)
            ShowScreen(Screen.MainMenu);

        // Update ready button text dynamically
        if (inLobbyPanel.activeSelf)
            UpdateReadyButtonVisual();
    }

    // ═══════════════════════════════════════════════════════════════
    // SETUP
    // ═══════════════════════════════════════════════════════════════

    private void SetupDropdowns()
    {
        // Max Players: 2, 4, 6, 8
        if (maxPlayersDropdown != null)
        {
            maxPlayersDropdown.ClearOptions();
            maxPlayersDropdown.AddOptions(new List<string> { "2 Players", "4 Players", "6 Players", "8 Players" });
            maxPlayersDropdown.value = 2; // Default: 6 players
        }

        // Maps from the manager
        if (mapDropdown != null && manager != null)
        {
            mapDropdown.ClearOptions();
            mapDropdown.AddOptions(new List<string>(manager.AvailableMaps));
        }
    }

    private void WireButtons()
    {
        // Main Menu
        quickMatchButton?.onClick.AddListener(OnQuickMatch);
        browseGamesButton?.onClick.AddListener(() => ShowScreen(Screen.RoomBrowser));
        createGameButton?.onClick.AddListener(() => ShowScreen(Screen.CreateRoom));

        // Save name when changed
        playerNameInput?.onEndEdit.AddListener((val) => PlayerPrefs.SetString("PlayerName", val));

        // Room Browser
        backButton_Browser?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
        refreshButton?.onClick.AddListener(RefreshRoomList);

        // Create Room
        backButton_Create?.onClick.AddListener(() => ShowScreen(Screen.MainMenu));
        createRoomButton?.onClick.AddListener(OnCreateRoom);

        // In Lobby
        leaveButton?.onClick.AddListener(OnLeaveRoom);
        readyButton?.onClick.AddListener(OnToggleReady);
        startGameButton?.onClick.AddListener(OnStartGame);
    }

    private void SubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated += RefreshLobbyPlayers;
            manager.OnRoomCreated += OnRoomCreated;
            manager.OnJoinFailed += OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged += RefreshLobbyPlayers;
    }

    private void UnsubscribeEvents()
    {
        if (manager != null)
        {
            manager.OnLobbyPlayersUpdated -= RefreshLobbyPlayers;
            manager.OnRoomCreated -= OnRoomCreated;
            manager.OnJoinFailed -= OnJoinFailed;
        }
        LobbyPlayer.OnAnyPlayerDataChanged -= RefreshLobbyPlayers;
    }

    // ═══════════════════════════════════════════════════════════════
    // SCREEN MANAGEMENT
    // ═══════════════════════════════════════════════════════════════

    private void ShowScreen(Screen screen)
    {
        mainMenuPanel.SetActive(screen == Screen.MainMenu);
        roomBrowserPanel.SetActive(screen == Screen.RoomBrowser);
        createRoomPanel.SetActive(screen == Screen.CreateRoom);
        inLobbyPanel.SetActive(screen == Screen.InLobby);

        if (screen == Screen.RoomBrowser)
            RefreshRoomList();

        if (screen == Screen.InLobby)
            RefreshLobbyPlayers();
    }

    // ═══════════════════════════════════════════════════════════════
    // BUTTON ACTIONS
    // ═══════════════════════════════════════════════════════════════

    private void OnQuickMatch()
    {
        SavePlayerName();
        manager.QuickMatch();
    }

    private void OnCreateRoom()
    {
        SavePlayerName();

        string roomName = roomNameInput != null ? roomNameInput.text : $"Room_{Random.Range(100, 999)}";
        int maxPlayers = (maxPlayersDropdown.value + 1) * 2; // index 0=2, 1=4, 2=6, 3=8
        string map = manager.AvailableMaps[mapDropdown.value];

        manager.CreateRoom(roomName, maxPlayers, map);
    }

    private void OnLeaveRoom()
    {
        manager.LeaveRoom();
        ShowScreen(Screen.MainMenu);
    }

    private void OnToggleReady()
    {
        var local = LobbyPlayer.LocalPlayer;
        if (local != null)
            local.CmdToggleReady();
    }

    private void OnStartGame()
    {
        manager.StartGame();
    }

    private void OnRoomCreated(string name)
    {
        ShowScreen(Screen.InLobby);
    }

    private void OnJoinFailed(string message)
    {
        Debug.LogWarning($"[LobbyUI] Join failed: {message}");
        ShowScreen(Screen.MainMenu);
    }

    // ═══════════════════════════════════════════════════════════════
    // ROOM BROWSER
    // ═══════════════════════════════════════════════════════════════

    private void RefreshRoomList()
    {
        if (browserStatusText != null)
            browserStatusText.text = "Searching for rooms...";

        ClearChildren(roomListContent);

        manager.RefreshRoomList((rooms) =>
        {
            ClearChildren(roomListContent);

            if (rooms.Count == 0)
            {
                if (browserStatusText != null)
                    browserStatusText.text = "No rooms found. Create one or try Quick Match!";
                return;
            }

            if (browserStatusText != null)
                browserStatusText.text = $"Found {rooms.Count} room(s)";

            foreach (var room in rooms)
            {
                CreateRoomEntry(room);
            }
        });
    }

    private void CreateRoomEntry(DiscoveredRoom room)
    {
        if (roomEntryPrefab == null || roomListContent == null) return;

        GameObject entry = Instantiate(roomEntryPrefab, roomListContent);
        entry.SetActive(true);

        // Find child elements by name
        // Expected children: RoomEntryName (TMP), RoomEntryDetails (TMP), JoinButton (Button)
        var nameText = FindChildTMP(entry, "RoomEntryName");
        var detailsText = FindChildTMP(entry, "RoomEntryDetails");
        var joinButton = FindChildButton(entry, "JoinButton");

        if (nameText != null)
            nameText.text = room.roomName;

        if (detailsText != null)
            detailsText.text = $"{room.mapName} | {room.currentPlayers}/{room.maxPlayers} players";

        if (joinButton != null)
        {
            bool isFull = room.currentPlayers >= room.maxPlayers;
            joinButton.interactable = !isFull;

            // Update join button text
            var joinText = joinButton.GetComponentInChildren<TextMeshProUGUI>();
            if (joinText != null)
                joinText.text = isFull ? "FULL" : "JOIN";

            // Capture for closure
            string address = room.address;
            int port = room.port;
            joinButton.onClick.AddListener(() => manager.JoinRoom(address, port));
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // IN-LOBBY PLAYER LIST
    // ═══════════════════════════════════════════════════════════════

    private void RefreshLobbyPlayers()
    {
        if (!inLobbyPanel.activeSelf) return;

        // Update header info
        if (roomNameText != null)
            roomNameText.text = manager.roomName.ToUpper();

        if (mapInfoText != null)
            mapInfoText.text = $"Map: {manager.selectedMap}";

        if (playerCountText != null)
            playerCountText.text = $"Players: {manager.CurrentPlayerCount}/{manager.maxConnections}";

        // Rebuild player list
        ClearChildren(playerListContent);

        var players = manager.GetLobbyPlayers();
        foreach (var player in players)
        {
            CreatePlayerEntry(player);
        }

        // Show/hide owner-only buttons
        var localPlayer = LobbyPlayer.LocalPlayer;
        bool isOwner = localPlayer != null && localPlayer.isRoomOwner;

        if (startGameButton != null)
            startGameButton.gameObject.SetActive(isOwner);

        if (readyButton != null)
            readyButton.gameObject.SetActive(!isOwner);

        // Owner can only start when all are ready
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

        // Expected children: ColorImage (Image), NameText (TMP), StatusText (TMP)
        var colorImage = FindChildImage(entry, "ColorImage");
        var nameText = FindChildTMP(entry, "NameText");
        var statusText = FindChildTMP(entry, "StatusText");

        if (colorImage != null)
            colorImage.color = player.playerColor;

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
                statusText.color = new Color(1f, 0.85f, 0.3f); // Yellow
            }
            else if (player.readyToBegin)
            {
                statusText.text = "READY";
                statusText.color = new Color(0.2f, 0.8f, 0.4f); // Green
            }
            else
            {
                statusText.text = "NOT READY";
                statusText.color = new Color(0.9f, 0.3f, 0.3f); // Red
            }
        }
    }

    private void UpdateReadyButtonVisual()
    {
        var local = LobbyPlayer.LocalPlayer;
        if (local == null || local.isRoomOwner || readyButton == null) return;

        var text = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
            text.text = local.readyToBegin ? "UNREADY" : "READY";
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════

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

    /// <summary> Find a TextMeshProUGUI in children by GameObject name. </summary>
    private TextMeshProUGUI FindChildTMP(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    /// <summary> Find a Button in children by GameObject name. </summary>
    private Button FindChildButton(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<Button>() : null;
    }

    /// <summary> Find an Image in children by GameObject name. </summary>
    private Image FindChildImage(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<Image>() : null;
    }
}
