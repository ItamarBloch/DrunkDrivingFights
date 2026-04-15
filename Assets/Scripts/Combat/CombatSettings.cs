using UnityEngine;

/// <summary>
/// Global combat rules. One asset shared across all combat scripts.
/// Create via: Right-click in Project → Create → Combat → Combat Settings
/// </summary>
[CreateAssetMenu(fileName = "CombatSettings", menuName = "Combat/Combat Settings")]
public class CombatSettings : ScriptableObject
{
    [Header("Damage")]
    [Tooltip("Global damage multiplier. 2 = double damage mode, etc.")]
    [Range(0.1f, 5f)]
    public float globalDamageMultiplier = 1f;

    [Header("Self-Damage")]
    [Tooltip("Can players damage themselves with their own explosions?")]
    public bool selfDamage = true;

    [Tooltip("Multiplier on self-inflicted damage. 0.5 = half damage from own rockets.")]
    [Range(0f, 1f)]
    public float selfDamageMultiplier = 0.5f;
}
