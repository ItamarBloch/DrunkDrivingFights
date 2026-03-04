using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Data for a single playable map.
/// Add fields here as needed — preview image, environment type, etc.
/// </summary>
[System.Serializable]
public class MapData
{
    [Tooltip("Exact scene name as it appears in Build Settings")]
    public string sceneName;

    [Tooltip("Display name shown to players in the UI")]
    public string displayName;

    [Tooltip("Short description of the map")]
    [TextArea(1, 3)]
    public string description;

    [Tooltip("Minimum players recommended for this map")]
    public int minPlayers = 2;

    [Tooltip("Maximum players this map supports")]
    public int maxPlayers = 8;

    // Future fields you might want:
    // public Sprite previewImage;
    // public string environmentType; // "Desert", "City", etc.
    // public Vector3[] spawnPoints;
}

/// <summary>
/// Central registry of all available maps in the game.
/// 
/// ONE place to manage maps — everything else reads from this.
/// 
/// Setup:
/// 1. Right-click in Project → Create → Game → Map Registry
/// 2. Add your maps in the Inspector
/// 3. Assign this asset to GameNetworkRoomManager's "Map Registry" field
/// 
/// To add a new map:
/// 1. Create the scene in Unity
/// 2. Add it to Build Settings (File → Build Settings → Add Open Scenes)
/// 3. Add an entry here in the Inspector — done!
/// </summary>
[CreateAssetMenu(fileName = "MapRegistry", menuName = "Game/Map Registry")]
public class MapRegistry : ScriptableObject
{
    [SerializeField] private List<MapData> maps = new List<MapData>();

    /// <summary> All available maps. </summary>
    public IReadOnlyList<MapData> Maps => maps;

    /// <summary> Number of maps. </summary>
    public int Count => maps.Count;

    /// <summary> Get map by index. </summary>
    public MapData GetMap(int index)
    {
        if (index < 0 || index >= maps.Count) return null;
        return maps[index];
    }

    /// <summary> Get map by scene name. </summary>
    public MapData GetMapByScene(string sceneName)
    {
        return maps.Find(m => m.sceneName == sceneName);
    }

    /// <summary> Get display names for UI dropdowns. </summary>
    public List<string> GetDisplayNames()
    {
        var names = new List<string>();
        foreach (var map in maps)
            names.Add(map.displayName);
        return names;
    }

    /// <summary> Get all scene names (for Build Settings validation). </summary>
    public List<string> GetSceneNames()
    {
        var names = new List<string>();
        foreach (var map in maps)
            names.Add(map.sceneName);
        return names;
    }

    /// <summary> Validate that all maps exist in Build Settings. </summary>
    public List<string> ValidateMaps()
    {
        var missing = new List<string>();

#if UNITY_EDITOR
        var buildScenes = new HashSet<string>();
        foreach (var scene in UnityEditor.EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                buildScenes.Add(System.IO.Path.GetFileNameWithoutExtension(scene.path));
        }

        foreach (var map in maps)
        {
            if (!buildScenes.Contains(map.sceneName))
                missing.Add(map.sceneName);
        }

        if (missing.Count > 0)
            Debug.LogWarning($"[MapRegistry] Maps not in Build Settings: {string.Join(", ", missing)}");
#endif

        return missing;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Auto-validate when editing in Inspector
        if (maps.Count > 0)
            ValidateMaps();
    }
#endif
}
