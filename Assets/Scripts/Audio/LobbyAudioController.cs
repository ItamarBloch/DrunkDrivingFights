using System.Collections;
using UnityEngine;

/// <summary>
/// Plays looping background music in the lobby scene.
/// Place one instance as a scene object in LobbyScene.
/// Automatically destroyed when the scene unloads, stopping the music.
///
/// Uses key "lobby_music" from the SoundRegistry.
/// Set spatialBlend = 0 (2D) and volume to taste in the registry entry.
///
/// Supports optional fade-in on start.
/// </summary>
public class LobbyAudioController : MonoBehaviour
{
    [SerializeField] private SoundRegistry _soundRegistry;

    [Header("Fade In")]
    [SerializeField] private bool _fadeIn = true;
    [SerializeField] private float _fadeInDuration = 2f;

    private AudioSource _musicSource;

    private void Start()
    {
        _musicSource = CreateMusicSource();
        if (_musicSource == null) return;

        if (_fadeIn)
        {
            _musicSource.volume = 0f;
            StartCoroutine(FadeIn(_fadeInDuration));
        }
    }

    private AudioSource CreateMusicSource()
    {
        if (_soundRegistry == null) return null;
        var entry = _soundRegistry.Get("lobby_music");
        if (entry == null || entry.Value.clip == null) return null;

        var src = gameObject.AddComponent<AudioSource>();
        src.clip = entry.Value.clip;
        src.loop = true;
        src.volume = entry.Value.volume;
        src.pitch = entry.Value.minPitch; // music: use minPitch directly, no randomization
        src.spatialBlend = 0f;            // always 2D for music
        src.playOnAwake = false;
        src.Play();
        return src;
    }

    private IEnumerator FadeIn(float duration)
    {
        if (_soundRegistry == null) yield break;
        var entry = _soundRegistry.Get("lobby_music");
        float targetVolume = entry?.volume ?? 0.5f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _musicSource.volume = Mathf.Lerp(0f, targetVolume, elapsed / duration);
            yield return null;
        }
        _musicSource.volume = targetVolume;
    }
}
