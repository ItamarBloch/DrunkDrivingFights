using UnityEngine;

public static class ExplosionHelper
{
    private static readonly Collider[] _overlapBuffer = new Collider[32];

    public static void ProcessExplosion(
        Vector3 explosionCenter, WeaponSettings weapon,
        CombatSettings combat, uint instigatorNetId)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            explosionCenter, weapon.explosionRadius, _overlapBuffer);

        var processed = new System.Collections.Generic.HashSet<GameObject>();

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;

            GameObject root = col.attachedRigidbody != null
                ? col.attachedRigidbody.gameObject : col.gameObject;

            if (!processed.Add(root)) continue;

            IDamageable damageable = root.GetComponent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) continue;

            var netId = root.GetComponent<Mirror.NetworkIdentity>();
            bool isSelf = netId != null && netId.netId == instigatorNetId;
            if (isSelf && !combat.selfDamage) continue;

            Vector3 closestPoint = col.ClosestPoint(explosionCenter);
            float distance = Vector3.Distance(explosionCenter, closestPoint);

            float damage = weapon.CalculateDamage(distance);
            damage *= combat.globalDamageMultiplier;
            if (isSelf) damage *= combat.selfDamageMultiplier;
            if (damage > 0f) damageable.TakeDamage(damage, instigatorNetId, explosionCenter);

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
