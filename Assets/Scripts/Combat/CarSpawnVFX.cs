using UnityEngine;

/// <summary>
/// Plays a VFX at the car's position when it first appears and on every respawn.
/// Purely visual — runs on ALL clients so every player sees the effect.
///
/// Add to the Car prefab root.
/// Assign a spawn/respawn prefab from the Unity Particle Pack to SpawnVFXPrefab.
/// </summary>
[RequireComponent(typeof(HealthController))]
public class CarSpawnVFX : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("VFX prefab to play when the car spawns or respawns. " +
             "A flash, burst, or teleport-style effect from the Particle Pack works well.")]
    [SerializeField] private GameObject spawnVFXPrefab;

    [Tooltip("Vertical offset so the effect appears at car centre, not the floor.")]
    [SerializeField] private float heightOffset = 0.5f;

    // ── Lifecycle ────────────────────────────────────────────

    private void Start()
    {
        // Play on first appearance (covers initial spawn + scene load)
        PlaySpawnVFX();

        var health = GetComponent<HealthController>();
        if (health != null)
            health.OnRespawn += PlaySpawnVFX;
    }

    private void OnDestroy()
    {
        var health = GetComponent<HealthController>();
        if (health != null)
            health.OnRespawn -= PlaySpawnVFX;
    }

    // ── VFX ──────────────────────────────────────────────────

    private void PlaySpawnVFX()
    {
        if (spawnVFXPrefab == null) return;

        Vector3 pos = transform.position + Vector3.up * heightOffset;
        var instance = Instantiate(spawnVFXPrefab, pos, Quaternion.identity);

        // Remove all colliders so the car doesn't physically rest on the VFX object
        foreach (var col in instance.GetComponentsInChildren<Collider>())
            Destroy(col);

        // Remove any Rigidbody too
        foreach (var rb in instance.GetComponentsInChildren<Rigidbody>())
            Destroy(rb);

        var ps = instance.GetComponent<ParticleSystem>()
              ?? instance.GetComponentInChildren<ParticleSystem>();

        float lifetime = 3f;
        if (ps != null)
        {
            var main  = ps.main;
            main.loop = false;
            lifetime  = main.duration + main.startLifetime.constantMax;
            ps.Play();  // force play in case Play On Awake is off on the prefab
        }

        Destroy(instance, lifetime);
    }
}
