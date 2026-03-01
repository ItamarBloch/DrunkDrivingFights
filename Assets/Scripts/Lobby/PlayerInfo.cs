using UnityEngine;
using Mirror;

/// <summary>
/// Carries player identity data from lobby into the game scene.
/// Attach this to the Car prefab alongside your CarController, NetworkIdentity, etc.
/// 
/// The GameNetworkRoomManager sets playerName and playerColor when spawning the car.
/// Other scripts can read these to display nameplates, color the car, etc.
/// </summary>
public class PlayerInfo : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    // ─── Events ───────────────────────────────────────────────────
    public event System.Action<string> OnPlayerNameChanged;
    public event System.Action<Color> OnPlayerColorChanged;

    // ─── Lifecycle ────────────────────────────────────────────────

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Apply visuals on spawn
        ApplyColor(playerColor);
        ApplyNameplate(playerName);
    }

    // ─── SyncVar Hooks ────────────────────────────────────────────

    private void OnNameChanged(string oldName, string newName)
    {
        ApplyNameplate(newName);
        OnPlayerNameChanged?.Invoke(newName);
    }

    private void OnColorChanged(Color oldColor, Color newColor)
    {
        ApplyColor(newColor);
        OnPlayerColorChanged?.Invoke(newColor);
    }

    // ─── Visual Application ───────────────────────────────────────

    /// <summary>
    /// Apply the player's color to the car body.
    /// Searches for a child named "Body" (matching your car hierarchy).
    /// </summary>
    private void ApplyColor(Color color)
    {
        Transform body = transform.Find("Body");
        if (body != null)
        {
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                // Use MaterialPropertyBlock to avoid creating material instances
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color); // URP/HDRP
                mpb.SetColor("_Color", color);     // Built-in fallback
                renderer.SetPropertyBlock(mpb);
            }
        }
    }

    /// <summary>
    /// Apply nameplate text. Override this if you have a world-space canvas
    /// nameplate system — for now it just updates the GameObject name.
    /// </summary>
    private void ApplyNameplate(string name)
    {
        gameObject.name = $"Car_{name}";
        // TODO: If you add a world-space nameplate canvas above the car,
        // find it here and set the TextMeshPro text.
    }
}
