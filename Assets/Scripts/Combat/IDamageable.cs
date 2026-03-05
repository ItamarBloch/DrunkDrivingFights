using UnityEngine;

/// <summary>
/// Interface for any object that can receive damage.
/// Implemented by HealthController on cars. In the future, barrels,
/// turrets, destructible walls can implement this too — ExplosionHelper
/// finds them automatically via OverlapSphere + GetComponent.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Apply damage. Server only.
    /// </summary>
    /// <param name="damage">Damage amount (pre-multiplied by global settings).</param>
    /// <param name="instigatorNetId">NetId of the player who caused the damage (for kill feed).</param>
    /// <param name="damageSource">World position of the explosion center.</param>
    void TakeDamage(float damage, uint instigatorNetId, Vector3 damageSource);

    /// <summary>Is this object still alive?</summary>
    bool IsAlive { get; }

    /// <summary>Current health as 0–1 ratio (for UI).</summary>
    float HealthRatio { get; }
}
