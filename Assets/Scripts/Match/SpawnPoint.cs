using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Place these in your gameplay scene to define where players spawn (and respawn).
/// GameNetworkRoomManager will use them instead of the fallback circle.
///
/// Setup:
/// - Create empty GameObjects in SampleScene, add this component
/// - Rotate them to face the direction the car should be facing on spawn
/// - Order in the hierarchy determines which player gets which point
/// </summary>
public class SpawnPoint : MonoBehaviour
{
    private static readonly List<SpawnPoint> _all = new();

    private void OnEnable()  => _all.Add(this);
    private void OnDisable() => _all.Remove(this);

    /// <summary>All active SpawnPoints in the scene (ordered by OnEnable, i.e. hierarchy order).</summary>
    public static IReadOnlyList<SpawnPoint> All => _all;

    /// <summary>
    /// Fisher-Yates shuffle of the spawn point list.
    /// Call once on the server before players spawn so each match uses a different order.
    /// </summary>
    public static void ShuffleForMatch()
    {
        for (int i = _all.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_all[i], _all[j]) = (_all[j], _all[i]);
        }
    }

    /// <summary>
    /// Get position + rotation for a player at the given index.
    /// Wraps around if there are fewer points than players.
    /// Falls back to a circle of radius 20 if no SpawnPoints exist in the scene.
    /// </summary>
    public static (Vector3 position, Quaternion rotation) Get(int index, int totalPlayers = 1)
    {
        if (_all.Count > 0)
        {
            var pt = _all[index % _all.Count];
            return (pt.transform.position, pt.transform.rotation);
        }

        // Fallback: spread in a circle
        float angle = index * (360f / Mathf.Max(totalPlayers, 1)) * Mathf.Deg2Rad;
        var pos = new Vector3(Mathf.Cos(angle) * 20f, 1f, Mathf.Sin(angle) * 20f);
        var rot = Quaternion.LookRotation(-pos.normalized); // face toward center
        return (pos, rot);
    }

    // ── Editor Gizmos ─────────────────────────────────────────────

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 0.9f, 0.1f, 0.5f);
        Gizmos.DrawSphere(transform.position, 0.6f);

        // Forward arrow
        Gizmos.color = Color.green;
        Gizmos.DrawRay(transform.position, transform.forward * 3f);

        // Car footprint hint
        Gizmos.color = new Color(0.1f, 0.9f, 0.1f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(Vector3.zero, new Vector3(2f, 0.1f, 4f));
        Gizmos.matrix = Matrix4x4.identity;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.6f);
        Gizmos.DrawRay(transform.position, transform.forward * 4f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f,
            $"SpawnPoint [{_all.IndexOf(this)}]");
#endif
    }
}
