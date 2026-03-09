using UnityEngine;

/// <summary>
/// Per-weapon tuning data. Create one asset per weapon type.
/// Create via: Right-click in Project → Create → Combat → Weapon Settings
///
/// Damage model: distance-based falloff from explosion center.
///   actualDamage = Lerp(maxDamage, minDamage, distance / explosionRadius)
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponSettings", menuName = "Combat/Weapon Settings")]
public class WeaponSettings : ScriptableObject
{
    // ── Ammo & Reload ───────────────────────────────────────

    [Header("Ammo & Reload")]
    [Tooltip("Max rockets before needing to reload.")]
    [Min(1)]
    public int maxAmmo = 3;

    [Tooltip("Time in seconds to reload all ammo.")]
    [Min(0.1f)]
    public float reloadTime = 2.5f;

    [Tooltip("Minimum time between consecutive shots.")]
    [Min(0.05f)]
    public float fireCooldown = 0.4f;

    [Tooltip("If true, reload refills one rocket at a time (reloadTime per rocket). " +
             "If false, all ammo refills at once after reloadTime.")]
    public bool reloadPerRocket = false;

    // ── Projectile Speed ────────────────────────────────────

    [Header("Projectile Speed")]
    [Tooltip("Base speed of the rocket in m/s ADDED ON TOP of the car's velocity. " +
             "So if car goes 55m/s and this is 80, rocket travels at 135m/s initially.")]
    [Min(1f)]
    public float rocketSpeed = 80f;

    [Tooltip("If true, the rocket inherits the car's current velocity on spawn. " +
             "This prevents the car from outrunning or colliding with its own rocket.")]
    public bool inheritShooterVelocity = true;

    [Tooltip("Max speed the rocket can ever reach (caps velocity inheritance + own speed). " +
             "Prevents absurd speeds when firing from a boosting car.")]
    [Min(1f)]
    public float rocketMaxSpeed = 200f;

    [Tooltip("After launch, the rocket lerps toward this cruise speed over time. " +
             "0 = no cruise (keeps inherited speed forever). " +
             "Use this to normalize rocket speed after the initial burst.")]
    public float rocketCruiseSpeed = 80f;

    [Tooltip("How fast the rocket transitions to cruise speed (units/sec). " +
             "Higher = snappier transition. 0 = instant.")]
    public float cruiseTransitionRate = 30f;

    [Tooltip("How long the rocket lives before self-destructing (seconds).")]
    [Min(0.5f)]
    public float rocketLifetime = 5f;

    [Tooltip("Gravity multiplier. 0 = straight line, 1 = full gravity arc.")]
    [Range(0f, 2f)]
    public float rocketGravityScale = 0.3f;

    // ── Explosion ───────────────────────────────────────────

    [Header("Explosion")]
    [Tooltip("Radius of the explosion area of effect (meters).")]
    [Min(0.5f)]
    public float explosionRadius = 8f;

    [Tooltip("Damage at the explosion center (direct hit).")]
    [Min(0f)]
    public float maxDamage = 100f;

    [Tooltip("Damage at the very edge of the explosion radius.")]
    [Min(0f)]
    public float minDamage = 15f;

    [Tooltip("Custom falloff curve. X = normalized distance (0=center, 1=edge), Y = damage multiplier. " +
             "Leave empty for linear falloff.")]
    public AnimationCurve damageFalloff;

    // ── Knockback ───────────────────────────────────────────

    [Header("Knockback")]
    [Tooltip("Force applied at explosion center. Falls off with distance.")]
    [Min(0f)]
    public float knockbackForce = 2000f;

    [Tooltip("Vertical bias for knockback. Higher = launches more upward.")]
    [Range(0f, 2f)]
    public float knockbackUpwardBias = 0.4f;

    // ── VFX Keys ────────────────────────────────────────────

    [Header("VFX Keys (match entries in VFXReferences asset)")]
    public string muzzleFlashVFXKey = "rocket_muzzle_flash";
    public string rocketTrailVFXKey = "rocket_trail";
    public string explosionVFXKey = "rocket_explosion";

    // ── Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Calculate damage based on distance from explosion center.
    /// </summary>
    public float CalculateDamage(float distance)
    {
        if (distance >= explosionRadius) return 0f;

        float t = Mathf.Clamp01(distance / explosionRadius);
        float falloff;

        if (damageFalloff != null && damageFalloff.keys.Length >= 2)
            falloff = damageFalloff.Evaluate(t);
        else
            falloff = 1f - t; // Linear

        return Mathf.Lerp(minDamage, maxDamage, falloff);
    }

    /// <summary>
    /// Calculate knockback magnitude based on distance.
    /// </summary>
    public float CalculateKnockback(float distance)
    {
        if (distance >= explosionRadius) return 0f;

        float t = Mathf.Clamp01(distance / explosionRadius);
        float falloff;

        if (damageFalloff != null && damageFalloff.keys.Length >= 2)
            falloff = damageFalloff.Evaluate(t);
        else
            falloff = 1f - t;

        return knockbackForce * falloff;
    }

    private void OnValidate()
    {
        if (minDamage > maxDamage) minDamage = maxDamage;
        if (rocketCruiseSpeed > rocketMaxSpeed) rocketCruiseSpeed = rocketMaxSpeed;
    }
}
