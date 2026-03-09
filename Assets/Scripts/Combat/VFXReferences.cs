using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Central registry for all combat VFX prefabs.
/// Create via: Right-click in Project → Create → Combat → VFX References
///
/// Usage:
///   1. Download VFX from Unity Asset Store (or make placeholder particles)
///   2. Save them as prefabs
///   3. Add entries here — the key must match the VFX keys in WeaponSettings
///   4. All combat scripts look up effects by key through this registry
///
/// Everything works without VFX assigned — scripts just skip the visual.
/// </summary>
[CreateAssetMenu(fileName = "VFXReferences", menuName = "Combat/VFX References")]
public class VFXReferences : ScriptableObject
{
    [System.Serializable]
    public struct VFXEntry
    {
        [Tooltip("Key matching the VFX key fields in WeaponSettings.")]
        public string key;

        [Tooltip("Prefab with ParticleSystem (and optionally AudioSource, Light).")]
        public GameObject prefab;

        [Tooltip("Lifetime before auto-destroy. Match your particle system duration.")]
        public float lifetime;
    }

    [SerializeField] private VFXEntry[] entries;

    private Dictionary<string, VFXEntry> _lookup;

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, VFXEntry>();
        if (entries == null) return;
        foreach (var e in entries)
        {
            if (!string.IsNullOrEmpty(e.key))
                _lookup[e.key] = e;
        }
    }

    public VFXEntry? GetEffect(string key)
    {
        if (_lookup == null) BuildLookup();
        if (string.IsNullOrEmpty(key)) return null;
        if (_lookup.TryGetValue(key, out var entry) && entry.prefab != null)
            return entry;
        return null;
    }

    /// <summary>
    /// Spawn a VFX at position. Returns the GameObject or null if key not found.
    /// </summary>
    public GameObject SpawnEffect(string key, Vector3 position, Quaternion rotation)
    {
        var entry = GetEffect(key);
        if (entry == null) return null;

        var go = Instantiate(entry.Value.prefab, position, rotation);
        if (entry.Value.lifetime > 0f)
            Destroy(go, entry.Value.lifetime);
        return go;
    }

    private void OnValidate() { _lookup = null; }
}
