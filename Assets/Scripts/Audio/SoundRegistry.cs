using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject registry mapping string keys to AudioClips + playback settings.
/// Same pattern as VFXReferences — array in inspector, lazy dictionary at runtime.
///
/// Create via: Assets → Create → DrunkDriving → Sound Registry
///
/// Suggested keys:
///   "engine_loop"   — car engine (looping, pitch/volume driven by speed)
///   "rocket_whoosh" — rocket in-flight (looping, attached to rocket transform)
///   "explosion"     — rocket explosion (one-shot, at world position)
///   "hit_impact"    — metal hit when car takes damage (one-shot)
/// </summary>
[CreateAssetMenu(menuName = "DrunkDriving/Sound Registry", fileName = "SoundRegistry")]
public class SoundRegistry : ScriptableObject
{
    [Serializable]
    public struct SoundEntry
    {
        public string key;
        public AudioClip clip;

        [Range(0f, 1f)]
        public float volume;

        [Range(0.5f, 2f)]
        public float minPitch;

        [Range(0.5f, 2f)]
        public float maxPitch;

        [Tooltip("0 = fully 2D (same volume everywhere). 1 = fully 3D (distance attenuation). 0.5-0.8 = blend.")]
        [Range(0f, 1f)]
        public float spatialBlend;

        [Tooltip("Distance at which sound plays at full volume.")]
        public float minDistance;

        [Tooltip("Distance at which sound fades to silence.")]
        public float maxDistance;
    }

    [SerializeField] private SoundEntry[] _sounds;

    private Dictionary<string, SoundEntry> _lookup;

    /// <summary>Returns the entry for the given key, or null if not found / clip missing.</summary>
    public SoundEntry? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(key, out var entry) ? entry : (SoundEntry?)null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<string, SoundEntry>(_sounds?.Length ?? 0);
        if (_sounds == null) return;
        foreach (var s in _sounds)
            if (!string.IsNullOrEmpty(s.key))
                _lookup[s.key] = s;
    }

    private void OnValidate() => _lookup = null;
}
