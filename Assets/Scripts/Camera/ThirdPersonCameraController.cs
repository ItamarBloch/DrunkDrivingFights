using UnityEngine;
using Mirror;
using Unity.Cinemachine;

/// <summary>
/// Multiplayer-ready 3rd person orbit camera using Cinemachine 3.
/// Attach to your Car prefab — creates Camera + Cinemachine rig at runtime.
/// Only the local player's camera activates.
///
/// Components created programmatically:
///   - Camera + CinemachineBrain (the real camera)
///   - CinemachineCamera (virtual camera)
///   - CinemachineOrbitalFollow (orbit positioning — mouse driven)
///   - CinemachineInputAxisController (auto-connects mouse to orbit axes)
///   - CinemachineRotationComposer (smooth look-at)
///   - CinemachineDeoccluder (wall/obstacle collision)
///
/// Controls (handled by CinemachineInputAxisController):
///   - Mouse X/Y: Orbit around the car
///   - Recentering: Auto-returns behind car when idle
///
/// Requirements:
///   - com.unity.cinemachine (v3.x, ships with Unity 6)
///   - com.unity.inputsystem
///   - Mirror
/// </summary>
public class ThirdPersonCameraController : NetworkBehaviour
{
    // ──────────────────────────────────────────────
    //  ORBIT SETTINGS
    // ──────────────────────────────────────────────

    [Header("Orbit")]
    [Tooltip("Distance from the car")]
    [SerializeField] private float orbitRadius = 8f;

    [Tooltip("Offset above the car pivot that the orbit centers on")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 2f, 0f);

    [Header("Recentering")]
    [Tooltip("Camera auto-returns behind the car when mouse is idle")]
    [SerializeField] private bool enableRecentering = true;

    [Tooltip("Seconds of idle before recentering starts")]
    [SerializeField] private float recenterWait = 1.5f;

    [Tooltip("Time in seconds to recenter")]
    [SerializeField] private float recenterTime = 1.0f;

    [Header("Vertical Limits")]
    [Tooltip("Minimum vertical angle (looking down)")]
    [SerializeField] private float minVerticalAngle = -20f;

    [Tooltip("Maximum vertical angle (looking up)")]
    [SerializeField] private float maxVerticalAngle = 50f;

    [Header("Damping")]
    [Tooltip("Position damping — smaller = more responsive, larger = smoother")]
    [SerializeField] private Vector3 positionDamping = new Vector3(1f, 1f, 1f);

    [Header("Look At")]
    [Tooltip("Offset above the car that the camera looks at")]
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Collision")]
    [Tooltip("Enable obstacle avoidance")]
    [SerializeField] private bool enableCollision = true;

    [Tooltip("Layers the camera collides with")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Tooltip("Tag to ignore for collision (set to your car's tag)")]
    [SerializeField] private string ignoreCollisionTag = "";

    [Header("Speed Effects")]
    [SerializeField] private bool enableSpeedFOV = true;
    [SerializeField] private float baseFOV = 60f;
    [SerializeField] private float maxFOVBoost = 15f;
    [SerializeField] private float referenceSpeed = 40f;
    [SerializeField] private float fovSmoothSpeed = 5f;

    [Header("Mouse Input")]
    [Tooltip("Mouse sensitivity for orbiting. Increase if the camera feels sluggish.")]
    [SerializeField] private float mouseSensitivity = 0.15f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    [Header("Camera Distance")]
    [Tooltip("Distance presets cycled with the cycle key (near → far). Press cycles to the next and wraps back to the first.")]
    [SerializeField] private float[] distancePresets = { 6f, 10f, 14f };

    [Tooltip("Key that cycles to the next distance preset")]
    [SerializeField] private UnityEngine.InputSystem.Key cycleDistanceKey = UnityEngine.InputSystem.Key.V;

    [Tooltip("How quickly the camera eases to a newly selected distance")]
    [SerializeField] private float distanceLerpSpeed = 8f;

    // ──────────────────────────────────────────────
    //  RUNTIME REFERENCES
    // ──────────────────────────────────────────────

    private GameObject cameraGO;
    private Camera playerCamera;
    private GameObject vcamGO;
    private CinemachineCamera cinemachineCamera;
    private CinemachineOrbitalFollow orbitalFollow;

    private Rigidbody carRigidbody;
    private float currentFOV;
    private bool cameraCreated;

    // Camera distance (cycled with V) — persisted between sessions
    public const string PrefCamDistanceIndex = "CamDistanceIndex";
    private int   _distanceIndex;
    private float _targetRadius;

    // Manually tracked orbit values (so we can layer the look-back offset on top)
    private float _hValue    = 0f;
    private float _vValue    = 15f;   // matches VerticalAxis initial value in CreateCameraRig
    private bool  _lookBack  = false;

    // ──────────────────────────────────────────────
    //  PUBLIC API
    // ──────────────────────────────────────────────

    public Camera Camera => playerCamera;

    // ──────────────────────────────────────────────
    //  MIRROR LIFECYCLE
    // ──────────────────────────────────────────────

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CreateCameraRig();
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        DestroyCameraRig();
    }

    // ──────────────────────────────────────────────
    //  CAMERA RIG CREATION
    // ──────────────────────────────────────────────

    private void CreateCameraRig()
    {
        if (cameraCreated) return;

        carRigidbody = GetComponent<Rigidbody>();

        // ── Kill all existing cameras ──
        DisableAllExistingCameras();

        // ── 1. Real Camera + CinemachineBrain ──
        cameraGO = new GameObject($"PlayerCamera [netId={netId}]");

        playerCamera = cameraGO.AddComponent<Camera>();
        playerCamera.fieldOfView = baseFOV;
        playerCamera.nearClipPlane = 0.1f;
        playerCamera.farClipPlane = 1000f;
        playerCamera.tag = "MainCamera";

        cameraGO.AddComponent<AudioListener>();
        cameraGO.AddComponent<CinemachineBrain>();

        // ── 2. Virtual Camera ──
        vcamGO = new GameObject($"VCam_Player [netId={netId}]");

        cinemachineCamera = vcamGO.AddComponent<CinemachineCamera>();
        cinemachineCamera.Follow = transform;
        cinemachineCamera.LookAt = transform;

        // ── 3. Orbital Follow (Position Control) ──
        orbitalFollow = vcamGO.AddComponent<CinemachineOrbitalFollow>();
        orbitalFollow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        _distanceIndex = LoadDistanceIndex();
        _targetRadius = ResolveRadius(_distanceIndex);
        orbitalFollow.Radius = _targetRadius;
        orbitalFollow.TargetOffset = targetOffset;
        orbitalFollow.TrackerSettings.PositionDamping = positionDamping;

        // Horizontal axis: full 360° wrap
        orbitalFollow.HorizontalAxis.Value = 0f;
        orbitalFollow.HorizontalAxis.Center = 0f;
        orbitalFollow.HorizontalAxis.Range = new Vector2(-180f, 180f);
        orbitalFollow.HorizontalAxis.Wrap = true;
        orbitalFollow.HorizontalAxis.Recentering.Enabled = enableRecentering;
        orbitalFollow.HorizontalAxis.Recentering.Wait = recenterWait;
        orbitalFollow.HorizontalAxis.Recentering.Time = recenterTime;

        // Vertical axis: clamped pitch
        orbitalFollow.VerticalAxis.Value = 15f;
        orbitalFollow.VerticalAxis.Center = 15f;
        orbitalFollow.VerticalAxis.Range = new Vector2(minVerticalAngle, maxVerticalAngle);
        orbitalFollow.VerticalAxis.Wrap = false;
        orbitalFollow.VerticalAxis.Recentering.Enabled = enableRecentering;
        orbitalFollow.VerticalAxis.Recentering.Wait = recenterWait;
        orbitalFollow.VerticalAxis.Recentering.Time = recenterTime;

        // ── 4. Input Axis Controller ──
        // We intentionally do NOT add CinemachineInputAxisController here.
        // That component has multi-instance input issues (Ctrl+B / two clients).
        // We drive the axes manually in LateUpdate using Mouse.current.delta instead.

        // ── 5. Rotation Composer (Aim) ──
        var rotationComposer = vcamGO.AddComponent<CinemachineRotationComposer>();
        rotationComposer.TargetOffset = lookAtOffset;

        // ── 6. Deoccluder (Collision) ──
        if (enableCollision)
        {
            var deoccluder = vcamGO.AddComponent<CinemachineDeoccluder>();
            deoccluder.CollideAgainst = collisionLayers;
            deoccluder.MinimumDistanceFromTarget = 0.3f;

            if (!string.IsNullOrEmpty(ignoreCollisionTag))
            {
                deoccluder.IgnoreTag = ignoreCollisionTag;
            }

            // AvoidObstacles is a struct with no field initializer, so a runtime
            // AddComponent leaves it zeroed (Enabled=false → no collision at all).
            // ObstacleAvoidance.Default is internal, so configure it explicitly.
            // This is what keeps the camera out of walls AND from dipping underground,
            // especially important now that the far distance preset sits well behind the car.
            var avoid = deoccluder.AvoidObstacles;
            avoid.Enabled              = true;
            avoid.CameraRadius         = 0.3f;
            avoid.Strategy             = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PreserveCameraDistance;
            avoid.MaximumEffort        = 4;
            avoid.SmoothingTime        = 0f;
            avoid.Damping              = 0.5f;
            avoid.DampingWhenOccluded  = 0.1f;
            deoccluder.AvoidObstacles  = avoid;
        }

        // ── 7. Finalize ──
        currentFOV = baseFOV;
        cameraCreated = true;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log($"[ThirdPersonCamera] Cinemachine 3 orbit camera created for local player (netId={netId})");
    }

    private void DestroyCameraRig()
    {
        if (vcamGO != null) Destroy(vcamGO);
        if (cameraGO != null) Destroy(cameraGO);

        playerCamera = null;
        cinemachineCamera = null;
        orbitalFollow = null;
        cameraCreated = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void DisableAllExistingCameras()
    {
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            Destroy(listener);
        }

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            cam.enabled = false;
            if (cam.transform.childCount == 0 && cam.GetComponents<Component>().Length <= 3)
            {
                cam.gameObject.SetActive(false);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  UPDATE — only FOV + cursor toggle
    // ──────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!isLocalPlayer || playerCamera == null) return;

        // ESC is handled by InGameOptionsPanel — do not toggle cursor here.

        // Mouse orbit — driven manually so it works in every multiplayer instance
        if (orbitalFollow != null && Cursor.lockState == CursorLockMode.Locked)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                // Look-back (middle mouse button) — GTA:SA style
                _lookBack = mouse.middleButton.isPressed;

                // Read per-player camera settings from PlayerPrefs
                float horizSens = PlayerPrefs.GetFloat(LobbyOptionsPanel.PrefHorizSens, mouseSensitivity);
                float vertSens  = PlayerPrefs.GetFloat(LobbyOptionsPanel.PrefVertSens,  mouseSensitivity);
                bool  invertY   = PlayerPrefs.GetInt(LobbyOptionsPanel.PrefInvertY, 0) == 1;

                // Normal orbit input (always accumulate into the base values)
                Vector2 delta = mouse.delta.ReadValue();
                _hValue += delta.x * horizSens;
                float vertDelta = delta.y * vertSens * (invertY ? 1f : -1f);
                _vValue += vertDelta;
                _vValue  = Mathf.Clamp(_vValue, minVerticalAngle, maxVerticalAngle);

                // Apply to Cinemachine: add 180° offset when looking back
                orbitalFollow.HorizontalAxis.Value = _hValue + (_lookBack ? 180f : 0f);
                orbitalFollow.VerticalAxis.Value   = _vValue;
            }
        }

        // Camera distance cycling (V by default) — wraps around, persisted between sessions
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard != null && distancePresets != null && distancePresets.Length > 0)
        {
            var keyControl = keyboard[cycleDistanceKey];
            if (keyControl != null && keyControl.wasPressedThisFrame)
            {
                _distanceIndex = (_distanceIndex + 1) % distancePresets.Length;
                _targetRadius  = distancePresets[_distanceIndex];
                SaveDistanceIndex(_distanceIndex);
            }
        }

        // Ease the orbit radius toward the selected distance (Deoccluder still
        // pulls it back in if the new distance would clip a wall or the ground)
        if (orbitalFollow != null)
        {
            orbitalFollow.Radius = Mathf.Lerp(orbitalFollow.Radius, _targetRadius, distanceLerpSpeed * Time.deltaTime);
        }

        // Speed FOV
        if (enableSpeedFOV && carRigidbody != null)
        {
            float speed = carRigidbody.linearVelocity.magnitude;
            float speedNormalized = Mathf.Clamp01(speed / referenceSpeed);
            float targetFOV = baseFOV + (maxFOVBoost * speedNormalized);
            currentFOV = Mathf.Lerp(currentFOV, targetFOV, fovSmoothSpeed * Time.deltaTime);
            playerCamera.fieldOfView = currentFOV;
        }
    }

    // ──────────────────────────────────────────────
    //  CAMERA DISTANCE HELPERS
    // ──────────────────────────────────────────────

    private int LoadDistanceIndex()
    {
        if (distancePresets == null || distancePresets.Length == 0) return 0;
        int idx = PlayerPrefs.GetInt(PrefCamDistanceIndex, 0);
        return Mathf.Clamp(idx, 0, distancePresets.Length - 1);
    }

    private void SaveDistanceIndex(int idx)
    {
        PlayerPrefs.SetInt(PrefCamDistanceIndex, idx);
        PlayerPrefs.Save();
    }

    private float ResolveRadius(int idx)
    {
        if (distancePresets != null && idx >= 0 && idx < distancePresets.Length)
            return distancePresets[idx];
        return orbitRadius;
    }
}