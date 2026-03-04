using UnityEngine;
using Mirror;

public class PlayerInfo : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnNameChanged))]
    public string playerName = "Player";

    [SyncVar(hook = nameof(OnColorChanged))]
    public Color playerColor = Color.white;

    public event System.Action<string> OnPlayerNameChanged;
    public event System.Action<Color> OnPlayerColorChanged;

    public override void OnStartClient()
    {
        base.OnStartClient();
        ApplyColor(playerColor);
        ApplyNameplate(playerName);
    }

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

    private void ApplyColor(Color color)
    {
        Transform body = transform.Find("Body");
        if (body != null)
        {
            var renderer = body.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mpb = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", color);
                mpb.SetColor("_Color", color);
                renderer.SetPropertyBlock(mpb);
            }
        }
    }

    private void ApplyNameplate(string name)
    {
        gameObject.name = $"Car_{name}";
    }
}