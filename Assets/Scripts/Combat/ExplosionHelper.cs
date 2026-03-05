using UnityEngine;

/// <summary>
/// Static utility — processes explosion damage + knockback. SERVER ONLY.
/// Called by Rocket.cs when a rocket detonates.
///
/// Flow:
///   1. OverlapSphere to find colliders in radius
///   2. Deduplicate by root Rigidbody (car has multiple colliders)
///   3. Check IDamageable on each root
///   4. Self-damage check (skip if CombatSettings.selfDamage is false)
///   5. Distance-based damage via WeaponSettings.CalculateDamage()
///   6. Knockback force if CombatSettings.enableKnockback is true
/// </summary>
public static class ExplosionHelper
{
    private static readonly Collider[] _overlapBuffer = new Collider[32];

    /// <summary>
    /// Process an explosion. Call on the server only.
    /// </summary>
    public static void ProcessExplosion(
        Vector3 explosionCenter,
        WeaponSettings weapon,
        CombatSettings combat,
        uint instigatorNetId)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            explosionCenter,
            weapon.explosionRadius,
            _overlapBuffer
        );

        // Track processed roots so we don't damage a car twice
        var processed = new System.Collections.Generic.HashSet<GameObject>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;

            // Get root — IDamageable lives on the car root (where Rigidbody is)
            GameObject root = col.attachedRigidbody != null
                ? col.attachedRigidbody.gameObject
                : col.gameObject;

            if (!processed.Add(root)) continue;

            IDamageable damageable = root.GetComponent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) continue;

            // ── Self-damage check ──
            var netId = root.GetComponent<Mirror.NetworkIdentity>();
            bool isSelf = netId != null && netId.netId == instigatorNetId;

            if (isSelf && !combat.selfDamage)
                continue;

            // ── Distance ──
            Vector3 closestPoint = col.ClosestPoint(explosionCenter);
            float distance = Vector3.Distance(explosionCenter, closestPoint);

            // ── Damage ──
            float damage = weapon.CalculateDamage(distance);
            damage *= combat.globalDamageMultiplier;
            if (isSelf) damage *= combat.selfDamageMultiplier;

            if (damage > 0f)
                damageable.TakeDamage(damage, instigatorNetId, explosionCenter);

            // ── Knockback ──
            if (combat.enableKnockback)
            {
                Rigidbody rb = root.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    float force = weapon.CalculateKnockback(distance);
                    force *= combat.knockbackMultiplier;
                    if (isSelf) force *= combat.selfDamageMultiplier;

                    if (force > 0f)
                    {
                        Vector3 dir = (root.transform.position - explosionCenter).normalized;
                        dir += Vector3.up * weapon.knockbackUpwardBias;
                        dir.Normalize();
                        rb.AddForce(dir * force, ForceMode.Impulse);
                    }
                }
            }
        }
    }
}
