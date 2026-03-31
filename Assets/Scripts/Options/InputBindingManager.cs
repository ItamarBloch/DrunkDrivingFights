using UnityEngine;
using UnityEngine.InputSystem;
using System;
using System.Collections.Generic;

/// <summary>
/// Persistent singleton — survives scene loads.
/// Stores per-action key bindings, saves/loads from PlayerPrefs.
/// Supports multiple keys per action and mouse buttons.
///
/// Attach to any persistent GameObject (e.g. the same one as GameNetworkRoomManager).
/// </summary>
public class InputBindingManager : MonoBehaviour
{
    public static InputBindingManager singleton { get; private set; }

    // ── Encoding ──────────────────────────────────────────────
    // Value < 0 → mouse button: -1=left, -2=right, -3=middle
    // Value > 0 → Key enum (cast to int)

    public const int MouseLeft   = -1;
    public const int MouseRight  = -2;
    public const int MouseMiddle = -3;

    // ── Actions ───────────────────────────────────────────────

    public enum GameAction
    {
        CarThrottleForward,
        CarThrottleBack,
        CarSteerLeft,
        CarSteerRight,
        CarBrake,
        AerialPitchUp,
        AerialPitchDown,
        AerialRollLeft,
        AerialRollRight,
        WeaponFire,
        WeaponReload
    }

    public static readonly string[] ActionLabels =
    {
        "Drive Forward",
        "Drive Back",
        "Steer Left",
        "Steer Right",
        "Brake",
        "Aerial Pitch Up",
        "Aerial Pitch Down",
        "Aerial Roll Left",
        "Aerial Roll Right",
        "Fire",
        "Reload"
    };

    // ── Storage ───────────────────────────────────────────────

    private readonly Dictionary<GameAction, List<int>> _bindings = new();

    // ── Defaults ──────────────────────────────────────────────

    private static readonly Dictionary<GameAction, List<int>> Defaults = new()
    {
        [GameAction.CarThrottleForward] = new() { K(Key.W), K(Key.UpArrow) },
        [GameAction.CarThrottleBack]    = new() { K(Key.S), K(Key.DownArrow) },
        [GameAction.CarSteerLeft]       = new() { K(Key.A), K(Key.LeftArrow) },
        [GameAction.CarSteerRight]      = new() { K(Key.D), K(Key.RightArrow) },
        [GameAction.CarBrake]           = new() { K(Key.Space) },
        [GameAction.AerialPitchUp]      = new() { K(Key.UpArrow) },
        [GameAction.AerialPitchDown]    = new() { K(Key.DownArrow) },
        [GameAction.AerialRollLeft]     = new() { K(Key.LeftArrow) },
        [GameAction.AerialRollRight]    = new() { K(Key.RightArrow) },
        [GameAction.WeaponFire]         = new() { MouseLeft },
        [GameAction.WeaponReload]       = new() { K(Key.R) },
    };

    private static int K(Key k) => (int)k;

    // ── Lifecycle ─────────────────────────────────────────────

    private void Awake()
    {
        if (singleton != null && singleton != this) { Destroy(gameObject); return; }
        singleton = this;
        DontDestroyOnLoad(gameObject);
        LoadAll();
    }

    // ── Public API ────────────────────────────────────────────

    public bool IsHeld(GameAction action)
    {
        foreach (int code in GetBindings(action))
            if (IsCodeHeld(code)) return true;
        return false;
    }

    public bool WasPressedThisFrame(GameAction action)
    {
        foreach (int code in GetBindings(action))
            if (WasCodePressedThisFrame(code)) return true;
        return false;
    }

    public IReadOnlyList<int> GetBindings(GameAction action)
    {
        if (_bindings.TryGetValue(action, out var list)) return list;
        if (Defaults.TryGetValue(action, out var def)) return def;
        return Array.Empty<int>();
    }

    /// <summary>Replaces all bindings for an action and saves.</summary>
    public void SetBindings(GameAction action, List<int> codes)
    {
        _bindings[action] = new List<int>(codes);
        SaveAction(action);
    }

    /// <summary>Adds a single binding if not already present and saves.</summary>
    public void AddBinding(GameAction action, int code)
    {
        if (!_bindings.TryGetValue(action, out var list))
        {
            list = new List<int>(Defaults.TryGetValue(action, out var def) ? def : new List<int>());
            _bindings[action] = list;
        }
        if (!list.Contains(code)) { list.Add(code); SaveAction(action); }
    }

    /// <summary>Removes a single binding and saves.</summary>
    public void RemoveBinding(GameAction action, int code)
    {
        if (_bindings.TryGetValue(action, out var list))
        {
            list.Remove(code);
            SaveAction(action);
        }
    }

    public void ResetToDefaults()
    {
        _bindings.Clear();
        foreach (GameAction a in Enum.GetValues(typeof(GameAction)))
        {
            if (Defaults.TryGetValue(a, out var def))
                _bindings[a] = new List<int>(def);
            PlayerPrefs.DeleteKey(PrefKey(a));
        }
        PlayerPrefs.Save();
    }

    // ── Display Names ──────────────────────────────────────────

    public static string CodeDisplayName(int code)
    {
        if (code == MouseLeft)   return "Mouse L";
        if (code == MouseRight)  return "Mouse R";
        if (code == MouseMiddle) return "Mouse M";
        if (code <= 0)           return "?";

        return (Key)code switch
        {
            Key.Space        => "Space",
            Key.LeftShift    => "L.Shift",
            Key.RightShift   => "R.Shift",
            Key.LeftCtrl     => "L.Ctrl",
            Key.RightCtrl    => "R.Ctrl",
            Key.LeftAlt      => "L.Alt",
            Key.RightAlt     => "R.Alt",
            Key.UpArrow      => "↑",
            Key.DownArrow    => "↓",
            Key.LeftArrow    => "←",
            Key.RightArrow   => "→",
            Key.Enter        => "Enter",
            Key.Escape       => "Esc",
            Key.Backspace    => "Bksp",
            Key.Tab          => "Tab",
            Key.Delete       => "Del",
            Key.Insert       => "Ins",
            Key.Home         => "Home",
            Key.End          => "End",
            Key.PageUp       => "PgUp",
            Key.PageDown     => "PgDn",
            Key.Numpad0      => "Num0",
            Key.Numpad1      => "Num1",
            Key.Numpad2      => "Num2",
            Key.Numpad3      => "Num3",
            Key.Numpad4      => "Num4",
            Key.Numpad5      => "Num5",
            Key.Numpad6      => "Num6",
            Key.Numpad7      => "Num7",
            Key.Numpad8      => "Num8",
            Key.Numpad9      => "Num9",
            Key key          => key.ToString().ToUpper()
        };
    }

    /// <summary>
    /// Scan all currently pressed keys/mouse buttons and return the first one found.
    /// Returns 0 if nothing is pressed.
    /// </summary>
    public static int ScanAnyPress()
    {
        var mouse = Mouse.current;
        if (mouse != null)
        {
            if (mouse.leftButton.wasPressedThisFrame)   return MouseLeft;
            if (mouse.rightButton.wasPressedThisFrame)  return MouseRight;
            if (mouse.middleButton.wasPressedThisFrame) return MouseMiddle;
        }

        var kb = Keyboard.current;
        if (kb != null)
        {
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k == Key.None || k == Key.Escape) continue;
                try { if (kb[k].wasPressedThisFrame) return K(k); }
                catch { }
            }
        }
        return 0;
    }

    // ── Internal ───────────────────────────────────────────────

    private static bool IsCodeHeld(int code)
    {
        if (code == MouseLeft)   return Mouse.current?.leftButton.isPressed   ?? false;
        if (code == MouseRight)  return Mouse.current?.rightButton.isPressed  ?? false;
        if (code == MouseMiddle) return Mouse.current?.middleButton.isPressed ?? false;
        if (code <= 0)           return false;
        try { return Keyboard.current?[(Key)code].isPressed ?? false; }
        catch { return false; }
    }

    private static bool WasCodePressedThisFrame(int code)
    {
        if (code == MouseLeft)   return Mouse.current?.leftButton.wasPressedThisFrame   ?? false;
        if (code == MouseRight)  return Mouse.current?.rightButton.wasPressedThisFrame  ?? false;
        if (code == MouseMiddle) return Mouse.current?.middleButton.wasPressedThisFrame ?? false;
        if (code <= 0)           return false;
        try { return Keyboard.current?[(Key)code].wasPressedThisFrame ?? false; }
        catch { return false; }
    }

    // ── Persistence ────────────────────────────────────────────

    private static string PrefKey(GameAction a) => $"Binding_{a}";

    private void SaveAction(GameAction action)
    {
        if (!_bindings.TryGetValue(action, out var list)) return;
        PlayerPrefs.SetString(PrefKey(action), string.Join(",", list));
        PlayerPrefs.Save();
    }

    private void LoadAll()
    {
        foreach (GameAction action in Enum.GetValues(typeof(GameAction)))
        {
            string key = PrefKey(action);
            if (!PlayerPrefs.HasKey(key))
            {
                if (Defaults.TryGetValue(action, out var def))
                    _bindings[action] = new List<int>(def);
                continue;
            }
            string raw = PlayerPrefs.GetString(key, "");
            var list = new List<int>();
            foreach (var part in raw.Split(','))
            {
                if (int.TryParse(part.Trim(), out int v)) list.Add(v);
            }
            _bindings[action] = list;
        }
    }
}
