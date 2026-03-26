using UnityEngine;
using Mirror;

/// <summary>
/// Trigger volume that instantly kills any car that enters it.
/// Use for map boundaries, death pits, and out-of-bounds zones.
///
/// Setup:
/// 1. Create a GameObject, add any Collider (Box / Mesh / Sphere)
/// 2. Tick "Is Trigger" on the collider
/// 3. Add this component
///
/// Server-authoritative: only the server processes kills.
/// No NetworkIdentity needed — this is a pure server-side scene object.
/// </summary>
[RequireComponent(typeof(Collider))]
public class KillZone : MonoBehaviour
{
    [Header("Appearance")]
    [Tooltip("Color used for the gizmo in Scene view.")]
    [SerializeField] private Color gizmoColor = new Color(1f, 0.1f, 0.1f, 0.25f);

    // ── Setup ────────────────────────────────────────────────────

    private void Awake()
    {
        // Enforce trigger — can't accidentally leave it unchecked
        foreach (var col in GetComponents<Collider>())
            col.isTrigger = true;
    }

    // ── Kill Logic (Server Only) ─────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkServer.active) return;

        var health = other.GetComponentInParent<HealthController>();
        if (health == null || !health.IsAlive) return;

        // netId 0 = environment / map kill — shows as "fell out of bounds" in future kill feed
        health.TakeDamage(99999f, 0u, transform.position);

        Debug.Log($"[KillZone] '{other.transform.root.name}' entered kill zone '{name}' and died.");
    }

    // ── Editor Gizmos ────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        DrawGizmo(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawGizmo(true);
    }

    private void DrawGizmo(bool selected)
    {
        var solidColor  = gizmoColor;
        var wireColor   = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, selected ? 1f : 0.7f);

        foreach (var col in GetComponents<Collider>())
        {
            Gizmos.matrix = transform.localToWorldMatrix;

            if (col is BoxCollider box)
            {
                Gizmos.color = solidColor;
                Gizmos.DrawCube(box.center, box.size);
                Gizmos.color = wireColor;
                Gizmos.DrawWireCube(box.center, box.size);
            }
            else if (col is SphereCollider sphere)
            {
                Gizmos.color = solidColor;
                Gizmos.DrawSphere(sphere.center, sphere.radius);
                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(sphere.center, sphere.radius);
            }
            else
            {
                // Generic: draw a small cross at the collider position
                Gizmos.color = wireColor;
                Gizmos.DrawWireSphere(col.bounds.center, 1f);
            }

            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
