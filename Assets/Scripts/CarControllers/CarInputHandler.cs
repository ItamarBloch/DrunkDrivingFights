using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads player input using Unity's new Input System package.
/// Produces a CarInputData snapshot each frame.
/// Only active on the owning client — remote clients never poll input.
/// </summary>
public class CarInputHandler : MonoBehaviour
{
    // ── State ───────────────────────────────────────────────

    /// <summary>Latest polled input snapshot.</summary>
    public CarInputData CurrentInput { get; private set; }

    // ── Per-Frame Polling ───────────────────────────────────

    /// <summary>
    /// Call from Update() to capture input at display refresh rate.
    /// </summary>
    public void Poll()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null)
        {
            CurrentInput = CarInputData.Empty;
            return;
        }

        CurrentInput = new CarInputData
        {
            Throttle       = ReadThrottle(kb),
            Steer          = ReadSteer(kb),
            Brake          = ReadBrake(kb),
            AerialThrottle = ReadAerialThrottle(kb),
            AerialSteer    = ReadAerialSteer(kb)
        };
    }

    // ── Axis Reading ────────────────────────────────────────

    /// <summary>
    /// Reads W/S or Up/Down arrows into a -1 to +1 throttle value.
    /// </summary>
    private float ReadThrottle(Keyboard kb)
    {
        float value = 0f;

        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)
            value += 1f;

        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
            value -= 1f;

        return value;
    }

    /// <summary>
    /// Reads A/D or Left/Right arrows into a -1 to +1 steer value.
    /// </summary>
    private float ReadSteer(Keyboard kb)
    {
        float value = 0f;

        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
            value += 1f;

        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)
            value -= 1f;

        return value;
    }

    /// <summary>
    /// Reads Space bar as brake.
    /// </summary>
    private bool ReadBrake(Keyboard kb)
    {
        return kb.spaceKey.isPressed;
    }

    /// <summary>
    /// Aerial pitch — Up/Down arrows only (not W/S).
    /// </summary>
    private float ReadAerialThrottle(Keyboard kb)
    {
        float value = 0f;
        if (kb.upArrowKey.isPressed)   value += 1f;
        if (kb.downArrowKey.isPressed) value -= 1f;
        return value;
    }

    /// <summary>
    /// Aerial roll — Left/Right arrows only (not A/D).
    /// </summary>
    private float ReadAerialSteer(Keyboard kb)
    {
        float value = 0f;
        if (kb.rightArrowKey.isPressed) value += 1f;
        if (kb.leftArrowKey.isPressed)  value -= 1f;
        return value;
    }
}
