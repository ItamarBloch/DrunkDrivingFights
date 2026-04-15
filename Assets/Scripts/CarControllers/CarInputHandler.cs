using UnityEngine;
using static InputBindingManager;

/// <summary>
/// Reads player input via InputBindingManager (supports custom key bindings).
/// Produces a CarInputData snapshot each frame.
/// Only active on the owning client — remote clients never poll input.
/// Falls back to hardcoded defaults when InputBindingManager is not present.
/// </summary>
public class CarInputHandler : MonoBehaviour
{
    /// <summary>Latest polled input snapshot.</summary>
    public CarInputData CurrentInput { get; private set; }

    /// <summary>
    /// Call from Update() to capture input at display refresh rate.
    /// </summary>
    public void Poll()
    {
        var mgr = InputBindingManager.singleton;
        if (mgr == null)
        {
            PollFallback();
            return;
        }

        CurrentInput = new CarInputData
        {
            Throttle       = ReadAxis(mgr, GameAction.CarThrottleForward, GameAction.CarThrottleBack),
            Steer          = ReadAxis(mgr, GameAction.CarSteerRight, GameAction.CarSteerLeft),
            Brake          = mgr.IsHeld(GameAction.CarBrake),
            AerialThrottle = ReadAxis(mgr, GameAction.AerialPitchUp, GameAction.AerialPitchDown),
            AerialSteer    = ReadAxis(mgr, GameAction.AerialRollRight, GameAction.AerialRollLeft)
        };
    }

    private static float ReadAxis(InputBindingManager mgr, GameAction positive, GameAction negative)
    {
        float v = 0f;
        if (mgr.IsHeld(positive))  v += 1f;
        if (mgr.IsHeld(negative))  v -= 1f;
        return v;
    }

    // Fallback to hardcoded keys when InputBindingManager hasn't loaded yet
    private void PollFallback()
    {
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) { CurrentInput = CarInputData.Empty; return; }

        float throttle = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    throttle += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  throttle -= 1f;

        float steer = 0f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  steer -= 1f;

        float aerialThrottle = 0f;
        if (kb.upArrowKey.isPressed)   aerialThrottle += 1f;
        if (kb.downArrowKey.isPressed) aerialThrottle -= 1f;

        float aerialSteer = 0f;
        if (kb.rightArrowKey.isPressed) aerialSteer += 1f;
        if (kb.leftArrowKey.isPressed)  aerialSteer -= 1f;

        CurrentInput = new CarInputData
        {
            Throttle       = throttle,
            Steer          = steer,
            Brake          = kb.spaceKey.isPressed,
            AerialThrottle = aerialThrottle,
            AerialSteer    = aerialSteer
        };
    }
}
