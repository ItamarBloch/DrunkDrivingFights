using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Singleton audio manager. Place one instance in the first scene that loads.
/// Survives scene transitions via DontDestroyOnLoad.
///
/// Two playback modes:
///   PlayAtPoint(key, pos)          — pooled one-shot at world position (explosions, impacts)
///   PlayAttached(key, transform)   — new AudioSource parented to a transform, caller owns it
///
/// All calls are null-safe — missing keys or clips silently do nothing.
/// </summary>
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [SerializeField] private SoundRegistry _registry;

    [Header("Pool")]
    [SerializeField] private int _poolSize = 20;

    private readonly Queue<AudioSource> _pool = new();

    // ── Lifecycle ────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPool();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Public API ───────────────────────────────────────────

    /// <summary>Play a one-shot sound at a world position using the pool.</summary>
    public void PlayAtPoint(string key, Vector3 position)
    {
        if (_registry == null) { Debug.LogWarning("[SoundManager] No SoundRegistry assigned."); return; }
        var entry = _registry.Get(key);
        if (entry == null) { Debug.LogWarning($"[SoundManager] Key not found in registry: '{key}'"); return; }
        if (entry.Value.clip == null) { Debug.LogWarning($"[SoundManager] Key '{key}' has no AudioClip assigned."); return; }

        var src = RentFromPool();
        ApplySettings(src, entry.Value);
        src.transform.position = position;
        src.Play();
        StartCoroutine(ReturnToPool(src, entry.Value.clip.length + 0.1f));
    }

    /// <summary>
    /// Create a persistent AudioSource on a transform (e.g. engine loop).
    /// The caller is responsible for stopping/destroying it.
    /// Returns null if the key doesn't exist or has no clip.
    /// </summary>
    public AudioSource PlayAttached(string key, Transform parent, bool loop = false)
    {
        if (_registry == null) { Debug.LogWarning("[SoundManager] No SoundRegistry assigned."); return null; }
        var entry = _registry.Get(key);
        if (entry == null) { Debug.LogWarning($"[SoundManager] Key not found in registry: '{key}'"); return null; }
        if (entry.Value.clip == null) { Debug.LogWarning($"[SoundManager] Key '{key}' has no AudioClip assigned."); return null; }

        var go = new GameObject($"Sound_{key}");
        go.transform.SetParent(parent, false);

        var src = go.AddComponent<AudioSource>();
        ApplySettings(src, entry.Value);
        src.loop = loop;
        src.Play();
        return src;
    }

    // ── Internal ─────────────────────────────────────────────

    private void BuildPool()
    {
        for (int i = 0; i < _poolSize; i++)
            _pool.Enqueue(MakeSource($"Pool_{i}"));
    }

    private AudioSource MakeSource(string goName)
    {
        var go = new GameObject(goName);
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        go.SetActive(false);
        return src;
    }

    private AudioSource RentFromPool()
    {
        AudioSource src;
        if (_pool.Count > 0)
        {
            src = _pool.Dequeue();
        }
        else
        {
            // Pool exhausted — grow it silently
            src = MakeSource("Pool_Overflow");
        }
        src.gameObject.SetActive(true);
        return src;
    }

    private IEnumerator ReturnToPool(AudioSource src, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (src == null) yield break;
        src.Stop();
        src.clip = null;
        src.transform.SetParent(transform);
        src.gameObject.SetActive(false);
        _pool.Enqueue(src);
    }

    private static void ApplySettings(AudioSource src, SoundRegistry.SoundEntry e)
    {
        src.clip = e.clip;
        src.volume = e.volume;
        src.pitch = Random.Range(e.minPitch, e.maxPitch);
        src.spatialBlend = e.spatialBlend;
        src.minDistance = e.minDistance;
        src.maxDistance = e.maxDistance;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.loop = false; // default; PlayAttached overrides
    }
}
